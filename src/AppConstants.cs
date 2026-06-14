using System;
using System.IO;

namespace L5ArgentinaLauncher
{
    /// <summary>
    /// Constantes globales y rutas de la aplicación. Las URLs marcadas con TODO son
    /// placeholders que hay que reemplazar por las definitivas (spec §15):
    ///   - DefaultManifestUrl: prefijo del bucket R2 (origen del manifest por defecto).
    ///   - ReleasesPageUrl: página de GitHub Releases (link de update, HARDCODEADO a propósito).
    /// </summary>
    public static class AppConstants
    {
        public const string AppName = "L5Argentina Launcher";

        /// <summary>Versión embebida del launcher; se compara contra manifest.launcher.latest_version.</summary>
        public const string LauncherVersion = "0.1.1";

        // URL del manifest en el bucket R2 público (origen de datos por defecto).
        public const string DefaultManifestUrl = "https://pub-4ab8e43f10604d7fa0f9402a8259a855.r2.dev/sunandmoon/manifest.json";

        // Página de GitHub Releases del launcher (link del aviso de update).
        // HARDCODEADO a propósito (spec §9): NUNCA se toma del manifest (evita phishing).
        public const string ReleasesPageUrl = "https://github.com/Guillermoc/l5argentina-launcher/releases";

        // ----- Rutas de estado/caché del usuario (spec §13) -----

        /// <summary>
        /// Hook de testing/portabilidad: si la variable de entorno L5A_DATA_ROOT está seteada,
        /// toda la config/caché/backups cuelgan de ahí en vez de %APPDATA%/%LOCALAPPDATA%.
        /// En producción no se setea (comportamiento normal).
        /// </summary>
        private static string OverrideRoot => Environment.GetEnvironmentVariable("L5A_DATA_ROOT");

        /// <summary>%APPDATA%\L5Argentina Launcher\ — config del usuario.</summary>
        public static string AppDataDir =>
            string.IsNullOrEmpty(OverrideRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName)
                : Path.Combine(OverrideRoot, "appdata", AppName);

        /// <summary>%LOCALAPPDATA%\L5Argentina Launcher\ — caché y backups.</summary>
        public static string LocalAppDataDir =>
            string.IsNullOrEmpty(OverrideRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName)
                : Path.Combine(OverrideRoot, "localappdata", AppName);

        public static string ConfigPath => Path.Combine(AppDataDir, "config.json");
        public static string CacheDir => Path.Combine(LocalAppDataDir, "cache");
        public static string BackupsDir => Path.Combine(LocalAppDataDir, "backups");

        /// <summary>Backup de la base "Original" capturada en el primer arranque (spec §7).</summary>
        public static string OriginalBackupPath => Path.Combine(BackupsDir, "database.original.xml");
    }
}
