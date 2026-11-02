using System.IO;
using System.Windows.Media.Imaging;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Corrige bitmaps de carta con DPI horizontal/vertical asimétrico (algún jpg de scan trae
    /// XResolution=1 en vez de 300 en su metadata EXIF/JFIF, p. ej. TSE006.jpg "Star-Crossed
    /// Lovers"). WPF usa esa densidad para calcular el tamaño "natural" del bitmap en cada eje por
    /// separado, y una relación tan extrema entre DpiX y DpiY hace que <c>ImageBrush</c>/<c>Image</c>
    /// compongan la imagen en franjas horizontales en vez de mostrarla entera — visto y confirmado
    /// re-renderizando ese archivo fuera de la app. Es un bug puramente de composición: los píxeles
    /// decodificados son correctos (por eso copiar al portapapeles siempre se veía bien, ya que
    /// <c>Clipboard.SetImage</c> lee los píxeles crudos y no pasa por ese camino), así que alcanza
    /// con reempaquetar el mismo bitmap bajo una densidad 96×96 uniforme.
    /// </summary>
    internal static class CardImageDpiFix
    {
        /// <summary>Devuelve <paramref name="source"/> sin tocar si DpiX y DpiY ya coinciden.</summary>
        public static BitmapImage Apply(BitmapImage source)
        {
            if (source == null || source.DpiX == source.DpiY) return source;

            var stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);
            var normalized = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96,
                source.Format, source.Palette, pixels, stride);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(normalized));
            using (var memory = new MemoryStream())
            {
                encoder.Save(memory);
                memory.Position = 0;
                var fixedImage = new BitmapImage();
                fixedImage.BeginInit();
                fixedImage.CacheOption = BitmapCacheOption.OnLoad;
                fixedImage.StreamSource = memory;
                fixedImage.EndInit();
                return fixedImage;
            }
        }
    }
}
