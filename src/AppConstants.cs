using System;
using System.IO;

namespace L5ArgentinaLauncher
{
    /// <summary>
    /// Constantes globales y rutas de la aplicación. Las tres URLs apuntan a producción:
    ///   - DefaultManifestUrl: manifest del bucket R2 (editable por el usuario, default acá).
    ///   - PoolManifestUrl: manifest del CDN de la app Android (HARDCODEADO).
    ///   - ReleasesPageUrl: página de GitHub Releases (link de update, HARDCODEADO a propósito
    ///     para que un manifest comprometido no pueda apuntar a un exe malicioso).
    /// Las rutas de estado respetan L5A_DATA_ROOT; las del juego (database.xml, images, Decks, Lists)
    /// NO — cuelgan de la instalación configurada. Ver docs/design/persistencia.md.
    /// </summary>
    public static class AppConstants
    {
        public const string AppName = "L5Argentina Launcher";

        /// <summary>Versión embebida del launcher; se compara contra manifest.launcher.latest_version.</summary>
        public const string LauncherVersion = "0.6.0";

        // URL del manifest en el bucket R2 público (origen de datos por defecto).
        public const string DefaultManifestUrl = "https://pub-4ab8e43f10604d7fa0f9402a8259a855.r2.dev/sunandmoon/manifest.json";

        // Página de GitHub Releases del launcher (link del aviso de update).
        // HARDCODEADO a propósito (spec §9): NUNCA se toma del manifest (evita phishing).
        public const string ReleasesPageUrl = "https://github.com/Guillermoc/l5argentina-launcher/releases";

        // Manifest del CDN de la app Android (canal production): fuente de los packs de
        // imágenes (y del catálogo del Buscador a futuro). Mismo bucket que el manifest del
        // launcher; hardcodeado como ReleasesPageUrl.
        public const string PoolManifestUrl =
            "https://pub-4ab8e43f10604d7fa0f9402a8259a855.r2.dev/production/manifest.json";

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

        /// <summary>Favoritas/ocultas del Buscador, por NOMBRE de carta (ADR-044). Las listas
        /// ya no viven acá: son .txt de la carpeta Lists (ADR-045).</summary>
        public static string UserCardsPath => Path.Combine(AppDataDir, "usercards.json");

        /// <summary>Presets de filtros del Buscador (criterios guardados con nombre).</summary>
        public static string PresetsPath => Path.Combine(AppDataDir, "presets.json");

        /// <summary>
        /// Sidecar de mazos y listas: lo que el .txt de Sun and Moon no puede representar
        /// (notas, sort_mode, fecha, mazo y lista activos, cartas recortadas). Los conjuntos en sí
        /// son .txt en las carpetas Decks y Lists de la instalación (ver DeckLibrary).
        /// </summary>
        public static string DecksMetaPath => Path.Combine(AppDataDir, "decksmeta.json");
        public static string CacheDir => Path.Combine(LocalAppDataDir, "cache");
        public static string BackupsDir => Path.Combine(LocalAppDataDir, "backups");

        /// <summary>Catálogo del Buscador (cards_db/rules/filters, mismos artefactos que la app Android).</summary>
        public static string CatalogDir => Path.Combine(CacheDir, "catalog");

        /// <summary>Backup de la base "Original" capturada en el primer arranque (spec §7).</summary>
        public static string OriginalBackupPath => Path.Combine(BackupsDir, "database.original.xml");
    }
}
