using System;
using System.IO;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>
    /// Aplica bases y gestiona backups del database.xml (spec §7).
    ///  - Primer arranque: respalda la base "Original" del usuario antes de tocar nada.
    ///  - Antes de cada sobrescritura: copia con timestamp del database.xml vigente.
    /// El XML se trata como blob opaco; sólo se copia/respalda, nunca se parsea para algo crítico.
    /// </summary>
    public class DatabaseService
    {
        /// <summary>
        /// Captura la base "Original" en %LOCALAPPDATA%\...\backups\database.original.xml la
        /// primera vez. Devuelve true si la capturó en esta llamada.
        /// </summary>
        public bool EnsureOriginalBackup(string installPath)
        {
            var dbXml = InstallationService.DatabaseXmlPath(installPath);
            if (!File.Exists(dbXml)) return false;
            if (File.Exists(AppConstants.OriginalBackupPath)) return false;

            Directory.CreateDirectory(AppConstants.BackupsDir);
            File.Copy(dbXml, AppConstants.OriginalBackupPath, overwrite: false);
            return true;
        }

        /// <summary>
        /// Heurística (sin parsear): el database.xml actual parece una base de comunidad/modificada
        /// si contiene el tag de formato "s_extended". Útil para advertir en el primer arranque que
        /// el backup "original" podría no ser limpio (spec §7, §15).
        /// </summary>
        public bool LooksLikeCommunityBase(string installPath)
        {
            var dbXml = InstallationService.DatabaseXmlPath(installPath);
            if (!File.Exists(dbXml)) return false;
            try
            {
                // Stream-scan en vez de cargar 8MB en memoria.
                using (var reader = new StreamReader(dbXml))
                {
                    var window = new char[4096];
                    string carry = "";
                    int n;
                    while ((n = reader.Read(window, 0, window.Length)) > 0)
                    {
                        var chunk = carry + new string(window, 0, n);
                        if (chunk.IndexOf("s_extended", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                        // Conservar el final por si el tag queda partido entre chunks.
                        carry = chunk.Length > 16 ? chunk.Substring(chunk.Length - 16) : chunk;
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }

        public bool HasOriginalBackup() => File.Exists(AppConstants.OriginalBackupPath);

        /// <summary>
        /// Respalda el database.xml vigente con timestamp y luego copia <paramref name="sourceXmlPath"/>
        /// como nueva base activa.
        /// </summary>
        public void ApplyDatabase(string installPath, string sourceXmlPath)
        {
            var dbXml = InstallationService.DatabaseXmlPath(installPath);
            Directory.CreateDirectory(InstallationService.DatabaseDir(installPath));

            if (File.Exists(dbXml))
            {
                Directory.CreateDirectory(AppConstants.BackupsDir);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var backup = Path.Combine(AppConstants.BackupsDir, $"database.{stamp}.xml");
                File.Copy(dbXml, backup, overwrite: false);
            }

            File.Copy(sourceXmlPath, dbXml, overwrite: true);
        }
    }
}
