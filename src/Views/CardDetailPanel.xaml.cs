using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Resources;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Detalle de carta compartido (Buscador + editor de mazos): imagen con cadena de fallback,
    /// pastilla de estado data-driven de rules.json, texto renderizado sin WebView (regla §8) y
    /// línea de stats. Solo lectura de archivos locales.
    /// </summary>
    public partial class CardDetailPanel : UserControl
    {
        public Card Card { get; private set; }

        /// <summary>Arte resuelto de la carta actual, o null. Fuente de verdad del panel: el
        /// XAML solo lo pinta como <see cref="ImageBrush"/> sobre un Border redondeado.</summary>
        private BitmapSource _art;

        public CardDetailPanel()
        {
            InitializeComponent();
        }

        public void Clear()
        {
            Card = null;
            DetailScroll.Visibility = Visibility.Collapsed;
            DetailEmpty.Visibility = Visibility.Visible;
        }

        /// <summary>Muestra la carta; candidates = rutas absolutas donde buscar la imagen, en orden.</summary>
        public void ShowCard(Card card, List<string> candidates)
        {
            Card = card;
            if (card == null) { Clear(); return; }
            DetailEmpty.Visibility = Visibility.Collapsed;
            DetailScroll.Visibility = Visibility.Visible;

            candidates = candidates ?? new List<string>();
            SetArt(LoadFromCandidates(candidates) as BitmapSource);
            UpdateImageDiagnostic(card, candidates);

            DetailName.Text = card.Name;
            DetailMeta.Text = JoinNonEmpty(" · ", Capitalize(card.Type), Capitalize(card.Clan),
                card.Rarity.Length > 0 ? Strings.Get("Rarity_" + card.Rarity.ToUpperInvariant()) : null);
            DetailKeywords.Text = card.DisplayKeywords;
            DetailKeywords.Visibility = card.DisplayKeywords.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            DetailText.Inlines.Clear();
            foreach (var inline in CardTextRenderer.ToInlines(card.Text))
                DetailText.Inlines.Add(inline);
            DetailText.Visibility = string.IsNullOrEmpty(card.Text) ? Visibility.Collapsed : Visibility.Visible;

            DetailFlavor.Inlines.Clear();
            foreach (var inline in CardTextRenderer.ToInlines(card.Flavor))
                DetailFlavor.Inlines.Add(inline);
            DetailFlavor.Visibility = string.IsNullOrEmpty(card.Flavor) ? Visibility.Collapsed : Visibility.Visible;

            DetailNote.Text = card.Note ?? "";
            DetailNoteBox.Visibility = string.IsNullOrEmpty(card.Note) ? Visibility.Collapsed : Visibility.Visible;

            DetailStats.Text = BuildStatsLine(card);
            DetailStats.Visibility = DetailStats.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            DetailSets.Text = Strings.Format("Fmt_DetailSets", card.DisplaySets);
            DetailLegality.Text = card.DisplayLegality.Length > 0
                ? Strings.Format("Fmt_DetailLegality", card.DisplayLegality) : "";
            DetailArtist.Text = string.IsNullOrEmpty(card.Artist)
                ? "" : Strings.Format("Fmt_DetailArtist", card.Artist);

            // Pastilla de estado (BANNED/UNIQUE etc.), data-driven desde rules.json: borde,
            // rombito y texto del color de la regla sobre fondo claro fijo (LabelPillBg) —
            // divergencia deliberada del 15% de alfa de la app: sobre el arte no se leía.
            if (!string.IsNullOrEmpty(card.LabelText))
            {
                var labelColor = ColorUtil.ParseColor(card.LabelColor) ?? Colors.White;
                var solid = ColorUtil.SolidBrush(labelColor);
                BannedPillText.Text = card.LabelText;
                BannedPillText.Foreground = solid;
                BannedPillDiamond.Foreground = solid;
                BannedPill.BorderBrush = solid;
                BannedPill.Visibility = Visibility.Visible;
            }
            else BannedPill.Visibility = Visibility.Collapsed;

            // Marco alrededor de la imagen (highlightColor): relleno al 12% + borde sólido de 2
            // + padding 3 — mismo patrón que CardListItem/CardViewerModal de la app.
            var highlightColor = ColorUtil.ParseColor(card.HighlightColor);
            if (highlightColor != null && _art == null)
            {
                CardImageFrame.BorderBrush = ColorUtil.SolidBrush(highlightColor.Value);
                CardImageFrame.Background = ColorUtil.SolidBrush(ColorUtil.WithAlpha(highlightColor.Value, 0.12));
                CardImageFrame.BorderThickness = new Thickness(2);
                CardImageFrame.Padding = new Thickness(3);
            }
            else
            {
                CardImageFrame.BorderThickness = new Thickness(0);
                CardImageFrame.Background = null;
                CardImageFrame.Padding = new Thickness(0);
            }

            CopyImageBtn.IsEnabled = _art != null;
            CopyImageFeedback.Visibility = Visibility.Collapsed;

            DetailScroll.ScrollToTop();
        }

        /// <summary>
        /// Copia el arte de la carta al portapapeles. Se copia el bitmap ya cargado (no el archivo):
        /// el panel es solo lectura y no vuelve a tocar el disco. El portapapeles de Windows falla
        /// de forma intermitente si otro proceso lo tiene tomado, así que el error se reporta en el
        /// panel y no se propaga.
        /// </summary>
        private void CopyImageBtn_Click(object sender, RoutedEventArgs e)
        {
            var source = _art;
            if (source == null) return;
            try
            {
                Clipboard.SetImage(source);
                ShowCopyFeedback("Detail_CopyImageOk", (Brush)FindResource("Gold"));
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                       || ex is InvalidOperationException)
            {
                ShowCopyFeedback("Detail_CopyImageFail", (Brush)FindResource("ErrorText"));
            }
        }

        private void ShowCopyFeedback(string key, Brush brush)
        {
            CopyImageFeedback.Text = Strings.Get(key);
            CopyImageFeedback.Foreground = brush;
            CopyImageFeedback.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Pinta el arte como <see cref="ImageBrush"/> sobre el Border redondeado. El alto sale del
        /// aspecto real del bitmap: con el alto correcto, UniformToFill no recorta nada y las
        /// esquinas redondeadas caen justo sobre las del escaneo.
        /// </summary>
        private void SetArt(BitmapSource art)
        {
            _art = art;
            if (art == null || art.PixelWidth <= 0)
            {
                CardArt.Background = null;
                CardArt.Height = double.NaN;
                return;
            }
            CardArt.Background = new ImageBrush(art) { Stretch = Stretch.UniformToFill };
            CardArt.Height = CardArt.Width * art.PixelHeight / art.PixelWidth;
        }

        private static string BuildStatsLine(Card card)
        {
            var parts = new List<string>();
            void Add(string key, string value)
            {
                if (!string.IsNullOrEmpty(value)) parts.Add(Strings.Get(key) + " " + value);
            }
            Add("Search_StatForce", card.DisplayForce);
            Add("Search_StatChi", card.DisplayChi);
            Add("Search_StatCost", card.DisplayCost);
            Add("Search_StatHonor", card.DisplayHonor);
            Add("Search_StatPHonor", card.DisplayPersonalHonor);
            Add("Search_StatFocus", card.DisplayFocus);
            Add("Search_StatGold", card.GoldProductionSpecial ? "*" : card.GoldProduction?.ToString());
            Add("Search_StatProvince", card.ProvinceStrength?.ToString());
            Add("Search_StatStartHonor", card.StartingHonor?.ToString());
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// Carga la primera imagen existente de la lista de candidatos (cadena de fallback de la
        /// app: impresión pedida → imagePath preferido → cada printing_images). Sin imagen queda
        /// el detalle en texto como placeholder.
        /// </summary>
        internal static ImageSource LoadFromCandidates(List<string> candidates)
        {
            foreach (var full in candidates)
            {
                try
                {
                    if (!File.Exists(full)) continue;
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(full);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch
                {
                    // archivo corrupto/no imagen → probar el siguiente printing
                }
            }
            return null;
        }

        /// <summary>
        /// Si la imagen no apareció, muestra dónde se la buscó — sin esto, "no se ven las
        /// imágenes" es indebuggeable a distancia.
        /// </summary>
        private void UpdateImageDiagnostic(Card card, List<string> candidates)
        {
            if (_art != null)
            {
                DetailImageMiss.Visibility = Visibility.Collapsed;
                return;
            }
            string message;
            if (string.IsNullOrEmpty(card.ImagePath) && string.IsNullOrEmpty(card.PrintingImages))
                message = Strings.Get("Search_NoImagePath");
            else if (candidates.Count == 0)
                message = Strings.Get("Search_NoImageRoots");
            else
                message = Strings.Format("Fmt_ImageMissing", candidates[0]);
            DetailImageMiss.Text = message;
            DetailImageMiss.Visibility = Visibility.Visible;
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static string JoinNonEmpty(string sep, params string[] parts) =>
            string.Join(sep, parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>Helpers de color compartidos (parseHexColor de la app: hex inválido → null).</summary>
    public static class ColorUtil
    {
        internal static Color? ParseColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }

        internal static Color WithAlpha(Color color, double alpha) =>
            Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);

        internal static Brush SolidBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// Resuelve las rutas absolutas candidatas de la imagen de una carta contra las raíces de
    /// instalación (Sun and Moon + carpeta del exe), con los alias de carpeta P/Promo.
    /// </summary>
    public class CardImageResolver
    {
        private readonly List<string> _roots = new List<string>();

        public CardImageResolver(string installPath)
        {
            if (!string.IsNullOrEmpty(installPath)) _roots.Add(installPath);
            try
            {
                var exeDir = AppContext.BaseDirectory;
                if (!string.IsNullOrEmpty(exeDir) && !_roots.Contains(exeDir)) _roots.Add(exeDir);
            }
            catch { /* sin fallback de exe */ }
        }

        /// <summary>
        /// Candidatos en orden: impresión pedida y, si fallbackChain, imagePath preferido +
        /// todos los printing_images; cada uno probado con alias P/Promo en cada raíz.
        /// </summary>
        public List<string> ResolveCandidates(Card card, string preferredPath, bool fallbackChain)
        {
            var result = new List<string>();
            if (_roots.Count == 0) return result;

            var relatives = new List<string>();
            if (!string.IsNullOrEmpty(preferredPath)) relatives.Add(preferredPath);
            if (fallbackChain)
            {
                if (!string.IsNullOrEmpty(card.ImagePath)) relatives.Add(card.ImagePath);
                relatives.AddRange(Services.Search.CardSearchEngine.SplitPipe(card.PrintingImages));
            }

            foreach (var rel in relatives.Distinct())
                foreach (var alias in WithSetFolderAliases(rel))
                    foreach (var root in _roots)
                    {
                        try { result.Add(Path.GetFullPath(Path.Combine(root, alias.Replace('/', '\\')))); }
                        catch { /* ruta inválida en datos → se ignora */ }
                    }
            return result;
        }

        /// <summary>
        /// Alias de carpeta de set: la DB referencia las promos como images/cards/P/… pero
        /// Sun and Moon las guarda en images/cards/Promo/…. Se prueban ambas (y a la inversa,
        /// por si algún pack usa la otra convención).
        /// </summary>
        private static IEnumerable<string> WithSetFolderAliases(string rel)
        {
            yield return rel;
            const string prefix = "images/cards/";
            var i = rel.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (i < 0) yield break;
            var start = i + prefix.Length;
            var j = rel.IndexOf('/', start);
            if (j < 0) yield break;
            var folder = rel.Substring(start, j - start);
            if (folder == "P")
                yield return rel.Substring(0, start) + "Promo" + rel.Substring(j);
            else if (folder == "Promo")
                yield return rel.Substring(0, start) + "P" + rel.Substring(j);
        }
    }
}
