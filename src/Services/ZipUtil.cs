using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>Utilidades de zip para la base comunidad distribuida comprimida.</summary>
    public static class ZipUtil
    {
        /// <summary>
        /// Extrae el único archivo <c>.xml</c> de un zip (la base comunidad viene como
        /// <c>database-X.Y.Z.zip</c> con un <c>database.xml</c> adentro) a <paramref name="destPath"/>.
        /// El SHA-256 se verifica sobre el ZIP descargado (aguas arriba), antes de llegar acá.
        /// </summary>
        public static void ExtractSingleXml(string zipPath, string destPath)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var xml = archive.Entries.FirstOrDefault(e =>
                    !string.IsNullOrEmpty(e.Name) &&
                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

                if (xml == null)
                    throw new InvalidOperationException("El zip de la base no contiene ningún .xml.");

                Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                xml.ExtractToFile(destPath, overwrite: true);
            }
        }
    }
}
