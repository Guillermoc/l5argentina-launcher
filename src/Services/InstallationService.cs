using System.IO;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>
    /// Resolución y validación de rutas dentro de la instalación de Sun and Moon (spec §2.1).
    /// Centraliza los paths para que el resto de servicios no los reconstruya a mano.
    /// </summary>
    public static class InstallationService
    {
        public const string GameExeName = "Sun and Moon.exe";

        public static string GameExePath(string installPath) =>
            Path.Combine(installPath, GameExeName);

        public static string DatabaseDir(string installPath) =>
            Path.Combine(installPath, "Sun and Moon_Data", "StreamingAssets", "Database");

        public static string DatabaseXmlPath(string installPath) =>
            Path.Combine(DatabaseDir(installPath), "database.xml");

        /// <summary>Carpeta de imágenes donde se fusiona el pack propio (spec §11).</summary>
        public static string ImagesDir(string installPath) =>
            Path.Combine(installPath, "images");

        /// <summary>
        /// Una instalación válida tiene el exe y la carpeta Database (spec §4.1.1).
        /// </summary>
        public static bool IsValidInstall(string installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath)) return false;
            return File.Exists(GameExePath(installPath))
                && Directory.Exists(DatabaseDir(installPath));
        }
    }
}
