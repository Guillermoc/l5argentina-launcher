using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace L5ArgentinaLauncher.Services.Deck
{
    /// <summary>Tamaño de hoja del PDF de impresión (los dos de PdfExportViewModel.kt).</summary>
    public enum PdfPageSize
    {
        A4,
        Letter,
    }

    /// <summary>Una carta a imprimir: sus rutas candidatas de imagen y cuántas copias.</summary>
    public class PdfExportItem
    {
        public string CardName { get; set; }

        /// <summary>Rutas absolutas en orden de preferencia (CardImageResolver.ResolveCandidates).</summary>
        public IList<string> CandidatePaths { get; set; }

        public int Copies { get; set; }
    }

    /// <summary>Geometría de la hoja, en puntos PDF (1 pt = 1/72"). Todo entero, como la app.</summary>
    public struct PdfLayout
    {
        public int PageWidth;
        public int PageHeight;
        public int CardWidth;
        public int CardHeight;
        public int Gap;
        public int OriginX;
        public int OriginY;
    }

    /// <summary>
    /// Escribe el PDF de impresión de un mazo: grilla 3×3 de cartas a tamaño real (63×88 mm),
    /// centrada en la hoja. Port de <c>PdfExportViewModel.buildPdf()</c> de la app Android, que allá
    /// usa <c>android.graphics.pdf.PdfDocument</c>; en net48 no hay equivalente, así que se emite el
    /// PDF a mano (ADR-049).
    ///
    /// Los JPEG de las cartas se embeben **tal cual** con el filtro <c>/DCTDecode</c>: el PDF es un
    /// contenedor de JPEG, así que no hay que decodificar ni recomprimir nada y la impresión sale con
    /// la calidad exacta del archivo original. Solo se recodifica lo que <c>/DCTDecode</c> no acepta
    /// (JPEG progresivo, CMYK, o directamente otro formato).
    /// </summary>
    public static class PdfExporter
    {
        // Medidas de una carta de L5R en mm → puntos, redondeadas como la app.
        public const double CardWidthMm = 63.0;
        public const double CardHeightMm = 88.0;

        public const int Columns = 3;
        public const int Rows = 3;
        public const int CardsPerPage = Columns * Rows;

        /// <summary>Color del placeholder cuando la carta no tiene imagen descargada (0xFF2A1008).</summary>
        private const string PlaceholderColorOp = "0.164706 0.062745 0.031373 rg\n";

        private static int MmToPt(double mm) => (int)Math.Round(mm / 25.4 * 72.0, MidpointRounding.AwayFromZero);

        /// <summary>Geometría de la hoja. Aislada del escritor para poder verificarla sola.</summary>
        public static PdfLayout Layout(PdfPageSize size, bool margin1mm)
        {
            var layout = new PdfLayout();
            if (size == PdfPageSize.Letter) { layout.PageWidth = 612; layout.PageHeight = 792; }
            else { layout.PageWidth = 595; layout.PageHeight = 842; }

            layout.CardWidth = MmToPt(CardWidthMm);    // 179
            layout.CardHeight = MmToPt(CardHeightMm);  // 249
            layout.Gap = margin1mm ? MmToPt(1.0) : 0;  // 3

            var gridW = Columns * layout.CardWidth + (Columns - 1) * layout.Gap;
            var gridH = Rows * layout.CardHeight + (Rows - 1) * layout.Gap;
            layout.OriginX = (layout.PageWidth - gridW) / 2;
            layout.OriginY = (layout.PageHeight - gridH) / 2;
            return layout;
        }

        /// <summary>
        /// Nombre sugerido: <c>L5A-{mazo}-{yyyyMMdd-HHmm}.pdf</c>, con el mismo saneado que la app
        /// (todo lo que no sea ASCII alfanumérico, guion bajo o guion pasa a "-").
        /// </summary>
        public static string SuggestFileName(string deckName, DateTime now)
        {
            var safe = Regex.Replace(deckName ?? "", "[^a-zA-Z0-9_\\-]", "-").TrimEnd('-');
            return "L5A-" + safe + "-" + now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".pdf";
        }

        /// <summary>Cuántas hojas salen para N cartas (la última va incompleta).</summary>
        public static int PageCount(int totalCards) =>
            totalCards <= 0 ? 0 : (totalCards + CardsPerPage - 1) / CardsPerPage;

        /// <summary>
        /// Expande las copias en la secuencia de impresión: cada ítem aparece <c>Copies</c> veces
        /// seguidas, en el orden en que vino. Se queda con la primera ruta candidata que exista;
        /// null = sin imagen (va el placeholder).
        /// </summary>
        public static List<string> BuildPrintSequence(IEnumerable<PdfExportItem> items)
        {
            var sequence = new List<string>();
            if (items == null) return sequence;
            foreach (var item in items)
            {
                if (item == null || item.Copies <= 0) continue;
                var path = FirstExisting(item.CandidatePaths);
                for (var i = 0; i < item.Copies; i++) sequence.Add(path);
            }
            return sequence;
        }

        private static string FirstExisting(IEnumerable<string> candidates)
        {
            if (candidates == null) return null;
            foreach (var path in candidates)
            {
                try { if (File.Exists(path)) return path; }
                catch { /* ruta inválida en datos → siguiente candidato */ }
            }
            return null;
        }

        /// <summary>
        /// Genera el PDF en <paramref name="outputPath"/>. Devuelve la cantidad de hojas escritas.
        /// Escribe primero a un archivo temporal y recién al final reemplaza el destino: si algo
        /// falla a mitad no queda un PDF truncado con nombre bueno.
        /// </summary>
        public static int Write(string outputPath, IEnumerable<PdfExportItem> items,
                                PdfPageSize size, bool margin1mm)
        {
            var sequence = BuildPrintSequence(items);
            if (sequence.Count == 0) throw new InvalidOperationException("Nada para imprimir.");

            var layout = Layout(size, margin1mm);

            // Una carta repetida embebe su JPEG UNA sola vez y lo referencia N veces: sin esto un
            // mazo de 40 cartas con 3 copias pesaría el triple.
            var images = new Dictionary<string, PdfImage>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in sequence.Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var image = PdfImage.TryLoad(path);
                if (image != null) images[path] = image;
            }

            var temp = outputPath + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    WriteDocument(stream, sequence, images, layout);

                if (File.Exists(outputPath)) File.Delete(outputPath);
                File.Move(temp, outputPath);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
            return PageCount(sequence.Count);
        }

        // ------------------------------------------------------------------ emisión del PDF

        private static void WriteDocument(Stream stream, List<string> sequence,
                                          Dictionary<string, PdfImage> images, PdfLayout layout)
        {
            var offsets = new Dictionary<int, long>();
            var pages = ChunkPages(sequence);

            // Numeración: 1 catálogo, 2 árbol de páginas, después las imágenes y por último cada
            // hoja con su content stream.
            var next = 3;
            foreach (var image in images.Values) image.ObjectNumber = next++;
            var pageObjects = new int[pages.Count];
            var contentObjects = new int[pages.Count];
            for (var i = 0; i < pages.Count; i++) { pageObjects[i] = next++; contentObjects[i] = next++; }
            var totalObjects = next - 1;

            Ascii(stream, "%PDF-1.4\n");
            stream.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 6); // marca de binario

            BeginObject(stream, offsets, 1);
            Ascii(stream, "<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            BeginObject(stream, offsets, 2);
            Ascii(stream, "<< /Type /Pages /Count " + pages.Count + " /Kids [" +
                          string.Join(" ", pageObjects.Select(n => n + " 0 R")) + "] >>\nendobj\n");

            foreach (var image in images.Values)
            {
                BeginObject(stream, offsets, image.ObjectNumber);
                Ascii(stream, image.Dictionary());
                Ascii(stream, "stream\n");
                stream.Write(image.Data, 0, image.Data.Length);
                Ascii(stream, "\nendstream\nendobj\n");
            }

            for (var i = 0; i < pages.Count; i++)
            {
                var used = pages[i].Where(p => p != null && images.ContainsKey(p))
                                   .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var resources = used.Count == 0
                    ? "<< >>"
                    : "<< /XObject << " + string.Join(" ",
                          used.Select(p => "/Im" + images[p].ObjectNumber + " " + images[p].ObjectNumber + " 0 R")) + " >> >>";

                BeginObject(stream, offsets, pageObjects[i]);
                Ascii(stream, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " +
                              layout.PageWidth + " " + layout.PageHeight + "] /Resources " + resources +
                              " /Contents " + contentObjects[i] + " 0 R >>\nendobj\n");

                var content = Encoding.ASCII.GetBytes(PageContent(pages[i], images, layout));
                BeginObject(stream, offsets, contentObjects[i]);
                Ascii(stream, "<< /Length " + content.Length + " >>\nstream\n");
                stream.Write(content, 0, content.Length);
                Ascii(stream, "endstream\nendobj\n");
            }

            var xref = stream.Position;
            Ascii(stream, "xref\n0 " + (totalObjects + 1) + "\n");
            Ascii(stream, "0000000000 65535 f \n");
            for (var n = 1; n <= totalObjects; n++)
            {
                long at;
                offsets.TryGetValue(n, out at);
                Ascii(stream, at.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
            }
            Ascii(stream, "trailer\n<< /Size " + (totalObjects + 1) + " /Root 1 0 R >>\nstartxref\n" +
                          xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        }

        /// <summary>Reparte la secuencia en hojas de 9 (3×3), en orden.</summary>
        internal static List<List<string>> ChunkPages(List<string> sequence)
        {
            var pages = new List<List<string>>();
            for (var i = 0; i < sequence.Count; i += CardsPerPage)
                pages.Add(sequence.Skip(i).Take(CardsPerPage).ToList());
            return pages;
        }

        /// <summary>
        /// Content stream de una hoja. El origen del PDF es abajo-izquierda y el de la app es
        /// arriba-izquierda, así que la fila se espeja en Y; sin eso la primera fila del mazo se
        /// imprimiría al pie de la hoja.
        /// </summary>
        private static string PageContent(List<string> cards, Dictionary<string, PdfImage> images, PdfLayout layout)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < cards.Count; i++)
            {
                var col = i % Columns;
                var row = i / Columns;
                var x = layout.OriginX + col * (layout.CardWidth + layout.Gap);
                var top = layout.OriginY + row * (layout.CardHeight + layout.Gap);
                var y = layout.PageHeight - top - layout.CardHeight;

                PdfImage image;
                if (cards[i] != null && images.TryGetValue(cards[i], out image))
                {
                    sb.Append("q\n")
                      .Append(layout.CardWidth).Append(" 0 0 ").Append(layout.CardHeight).Append(' ')
                      .Append(x).Append(' ').Append(y).Append(" cm\n")
                      .Append("/Im").Append(image.ObjectNumber).Append(" Do\nQ\n");
                }
                else
                {
                    sb.Append("q\n").Append(PlaceholderColorOp)
                      .Append(x).Append(' ').Append(y).Append(' ')
                      .Append(layout.CardWidth).Append(' ').Append(layout.CardHeight).Append(" re f\nQ\n");
                }
            }
            return sb.ToString();
        }

        private static void BeginObject(Stream stream, Dictionary<int, long> offsets, int number)
        {
            offsets[number] = stream.Position;
            Ascii(stream, number + " 0 obj\n");
        }

        private static void Ascii(Stream stream, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>
    /// Una imagen ya lista para embeber: bytes JPEG baseline + sus dimensiones y espacio de color.
    /// </summary>
    internal class PdfImage
    {
        public int ObjectNumber;
        public byte[] Data;
        public int Width;
        public int Height;
        public int Components;

        public string Dictionary()
        {
            var colorSpace = Components == 1 ? "/DeviceGray" : "/DeviceRGB";
            return "<< /Type /XObject /Subtype /Image /Width " + Width + " /Height " + Height +
                   " /ColorSpace " + colorSpace + " /BitsPerComponent 8 /Filter /DCTDecode /Length " +
                   Data.Length + " >>\n";
        }

        /// <summary>
        /// Carga el archivo listo para <c>/DCTDecode</c>. Camino rápido: si ya es un JPEG baseline
        /// gris o YCbCr, se embebe sin tocar un byte. Si no (progresivo, CMYK, PNG…), se recodifica
        /// a JPEG baseline. Devuelve null si el archivo no es una imagen legible.
        /// </summary>
        public static PdfImage TryLoad(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                int w, h, comps;
                bool baseline;
                if (TryReadJpegHeader(bytes, out w, out h, out comps, out baseline) &&
                    baseline && (comps == 1 || comps == 3))
                {
                    return new PdfImage { Data = bytes, Width = w, Height = h, Components = comps };
                }
                return Reencode(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Recodifica cualquier formato que WPF sepa leer a JPEG baseline RGB.</summary>
        private static PdfImage Reencode(string path)
        {
            try
            {
                var source = new BitmapImage();
                source.BeginInit();
                source.UriSource = new Uri(path);
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.EndInit();

                var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
                encoder.Frames.Add(BitmapFrame.Create(source));
                using (var memory = new MemoryStream())
                {
                    encoder.Save(memory);
                    var bytes = memory.ToArray();
                    int w, h, comps;
                    bool baseline;
                    if (!TryReadJpegHeader(bytes, out w, out h, out comps, out baseline)) return null;
                    return new PdfImage { Data = bytes, Width = w, Height = h, Components = comps };
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Lee el SOF del JPEG: dimensiones, componentes y si es baseline. <c>/DCTDecode</c> solo
        /// garantiza baseline (SOF0/SOF1); un progresivo (SOF2) lo abren algunos visores y otros no,
        /// así que se trata como "hay que recodificar".
        /// </summary>
        internal static bool TryReadJpegHeader(byte[] data, out int width, out int height,
                                               out int components, out bool baseline)
        {
            width = height = components = 0;
            baseline = false;
            if (data == null || data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return false;

            var i = 2;
            while (i < data.Length - 1)
            {
                if (data[i] != 0xFF) return false;
                while (i < data.Length && data[i] == 0xFF) i++;   // bytes de relleno
                if (i >= data.Length) return false;
                var marker = data[i++];

                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;
                if (marker == 0xD9 || marker == 0xDA) return false;  // fin, o datos: no hubo SOF
                if (i + 1 >= data.Length) return false;
                var length = (data[i] << 8) | data[i + 1];
                if (length < 2) return false;

                var isSof = marker >= 0xC0 && marker <= 0xCF &&
                            marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isSof)
                {
                    if (i + 7 >= data.Length) return false;
                    height = (data[i + 3] << 8) | data[i + 4];
                    width = (data[i + 5] << 8) | data[i + 6];
                    components = data[i + 7];
                    baseline = marker == 0xC0 || marker == 0xC1;
                    return width > 0 && height > 0 && components > 0;
                }
                i += length;
            }
            return false;
        }
    }
}
