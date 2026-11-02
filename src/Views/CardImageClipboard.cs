using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Copiar el arte de una carta al portapapeles desde una grilla (click derecho).
    ///
    /// **No** se copia la miniatura de la grilla: <see cref="ThumbnailCache"/> decodifica a 340 px
    /// de ancho para que la grilla no reviente la memoria, y pegar eso en el Discord daría una
    /// imagen borrosa. Acá se vuelve a decodificar el archivo a tamaño completo, una sola vez y
    /// solo cuando el usuario lo pide.
    ///
    /// El portapapeles de Windows falla de forma intermitente si otro proceso lo tiene tomado
    /// (mismo caso que el botón del panel de detalle), así que el error se devuelve como false y
    /// lo reporta quien llama, con su propia UI.
    /// </summary>
    internal static class CardImageClipboard
    {
        /// <summary>
        /// Copia el arte a tamaño completo del primer candidato que exista en disco.
        /// Devuelve false si no hay imagen o si el portapapeles rechazó la operación.
        /// </summary>
        public static bool TryCopy(IEnumerable<string> candidatePaths)
        {
            var source = LoadFull(candidatePaths);
            if (source == null) return false;
            try
            {
                Clipboard.SetImage(source);
                return true;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                       || ex is InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// Arte a tamaño completo del primer candidato que exista, o null. Lo usa también la
        /// previsualización con Alt: ahí sí conviene el original, que es lo que se quiere mirar.
        /// </summary>
        public static BitmapImage LoadFull(IEnumerable<string> candidatePaths)
        {
            foreach (var path in candidatePaths ?? new string[0])
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path);
                    bmp.CacheOption = BitmapCacheOption.OnLoad; // libera el archivo enseguida
                    bmp.EndInit();
                    var fixedBmp = CardImageDpiFix.Apply(bmp);
                    fixedBmp.Freeze();
                    return fixedBmp;
                }
                catch
                {
                    // archivo corrupto → probar el siguiente candidato, como hace la grilla
                }
            }
            return null;
        }
    }
}
