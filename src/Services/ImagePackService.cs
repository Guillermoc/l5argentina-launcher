using System;
using System.IO;
using System.IO.Compression;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>
    /// Extrae el pack de imágenes propio sobre &lt;SunAndMoon&gt;\images\ con protección
    /// zip-slip obligatoria (spec §8 regla 5, §11). Se fusiona con los packs clásicos
    /// existentes (agrega/actualiza; no reemplaza la carpeta).
    /// </summary>
    public class ImagePackService
    {
        /// <summary>
        /// Extrae <paramref name="zipPath"/> dentro de images\. Por cada entrada resuelve la
        /// ruta destino final y verifica que quede DENTRO de images\ antes de escribir;
        /// rechaza entradas con ..\, rutas absolutas o que escapen del directorio.
        /// </summary>
        public void ApplyImagePack(string installPath, string zipPath)
        {
            var imagesRoot = InstallationService.ImagesDir(installPath);
            Directory.CreateDirectory(imagesRoot);

            // Raíz canónica con separador final, para comparar prefijos sin falsos positivos
            // (p. ej. "images" vs "images-evil").
            var rootFull = Path.GetFullPath(imagesRoot);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                rootFull += Path.DirectorySeparatorChar;

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    // Entradas de directorio (terminan en / y sin nombre) se omiten.
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    // Resolución de la ruta destino y verificación zip-slip.
                    var destPath = Path.GetFullPath(Path.Combine(rootFull, entry.FullName));
                    if (!destPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                        throw new SecurityExceptionZipSlip(entry.FullName);

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }
        }
    }

    /// <summary>Se lanza cuando una entrada del zip intentaría escribir fuera de images\.</summary>
    public class SecurityExceptionZipSlip : System.Security.SecurityException
    {
        public SecurityExceptionZipSlip(string entryName)
            : base("Entrada de zip rechazada por intentar escapar de images\\ (zip-slip): " + entryName) { }
    }
}
