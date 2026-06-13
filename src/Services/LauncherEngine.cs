using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>
    /// Coordina el flujo del launcher (spec §4.1): chequeo de manifest, selección y aplicación
    /// de base (con backups), sincronización de imágenes y lanzamiento. Mantiene el estado
    /// (config + manifest cargado) para que la UI sólo orqueste y muestre.
    ///
    /// "Original" NO se hostea: es el backup local capturado en el primer arranque (spec §7).
    /// "Comunidad" (s_extended) se descarga del bucket y se verifica por SHA-256.
    /// </summary>
    public class LauncherEngine
    {
        public const string CommunityId = "community";
        public const string OriginalId = "original";

        private readonly ConfigService _configService = new ConfigService();
        private readonly ManifestService _manifestService = new ManifestService();
        private readonly DownloadService _downloadService = new DownloadService();
        private readonly DatabaseService _databaseService = new DatabaseService();
        private readonly ImagePackService _imagePackService = new ImagePackService();
        private readonly GameLauncher _gameLauncher = new GameLauncher();

        public UserConfig Config { get; }
        public Manifest Manifest { get; private set; }

        public LauncherEngine(UserConfig config)
        {
            Config = config;
        }

        // ---------------- Manifest ----------------

        public async Task LoadManifestAsync(CancellationToken ct = default)
        {
            Manifest = await _manifestService.FetchAsync(Config.ManifestUrl, ct).ConfigureAwait(false);
        }

        public bool LauncherUpdateAvailable()
        {
            var latest = Manifest?.Launcher?.LatestVersion;
            return !string.IsNullOrWhiteSpace(latest) &&
                   VersionUtil.IsNewer(latest, AppConstants.LauncherVersion);
        }

        public DatabaseEntry CommunityEntry =>
            Manifest?.Databases?.FirstOrDefault(d =>
                string.Equals(d.Id, CommunityId, StringComparison.OrdinalIgnoreCase));

        // ---------------- Base: Comunidad (descarga del bucket) ----------------

        private string CachedDatabasePath(DatabaseEntry entry) =>
            Path.Combine(AppConstants.CacheDir, Path.GetFileName(entry.File));

        /// <summary>True si la base comunidad hay que (re)descargarla (falta o cambió el hash).</summary>
        public bool CommunityNeedsDownload()
        {
            var entry = CommunityEntry;
            if (entry == null) return false;
            var cachedPath = CachedDatabasePath(entry);
            if (!File.Exists(cachedPath)) return true;
            return !Config.CachedDatabases.TryGetValue(CommunityId, out var cached)
                   || !HashUtil.HashesEqual(cached.Sha256, entry.Sha256);
        }

        private async Task<string> EnsureCommunityDownloadedAsync(
            IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            var entry = CommunityEntry
                ?? throw new InvalidOperationException("El manifest no incluye la base comunidad.");
            var cachedPath = CachedDatabasePath(entry);

            if (CommunityNeedsDownload())
            {
                var uri = ManifestService.ResolveFileUri(Config.ManifestUrl, entry.File);
                await _downloadService.DownloadAndVerifyAsync(uri, entry.Sha256, cachedPath, progress, ct)
                    .ConfigureAwait(false);
                Config.CachedDatabases[CommunityId] = new CachedFile { Version = entry.Version, Sha256 = entry.Sha256 };
                _configService.Save(Config);
            }

            // La base puede venir como .zip (con el database.xml adentro) o como .xml crudo.
            // El SHA-256 se verificó sobre lo descargado; si es zip, extraemos el xml para aplicarlo.
            if (cachedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var xmlPath = Path.Combine(AppConstants.CacheDir, "community.applied.xml");
                ZipUtil.ExtractSingleXml(cachedPath, xmlPath);
                return xmlPath;
            }
            return cachedPath;
        }

        // ---------------- Aplicar base seleccionada ----------------

        public bool HasOriginalBackup() => _databaseService.HasOriginalBackup();

        /// <summary>
        /// True si la base seleccionada todavía no está aplicada (o cambió de versión / falta el
        /// database.xml), de modo que JUGAR deba aplicarla antes de lanzar.
        /// </summary>
        public bool NeedsApply(string baseId)
        {
            var dbXml = InstallationService.DatabaseXmlPath(Config.SunAndMoonPath);
            if (!File.Exists(dbXml)) return true;
            if (!string.Equals(Config.AppliedBaseId, baseId, StringComparison.OrdinalIgnoreCase)) return true;

            if (string.Equals(baseId, CommunityId, StringComparison.OrdinalIgnoreCase))
            {
                var entry = CommunityEntry;
                if (entry != null && !string.Equals(Config.AppliedBaseVersion, entry.Version, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Aplica la base indicada (descargando comunidad si hace falta) con backups previos.</summary>
        public async Task ApplyBaseAsync(string baseId, IProgress<DownloadProgress> progress, CancellationToken ct = default)
        {
            string sourceXml;
            string version;

            if (string.Equals(baseId, OriginalId, StringComparison.OrdinalIgnoreCase))
            {
                if (!_databaseService.HasOriginalBackup())
                    throw new InvalidOperationException(
                        "No hay backup de la base original. Se captura en el primer arranque sobre una instalación limpia.");
                sourceXml = AppConstants.OriginalBackupPath;
                version = "original";
            }
            else
            {
                sourceXml = await EnsureCommunityDownloadedAsync(progress, ct).ConfigureAwait(false);
                version = CommunityEntry?.Version;
            }

            _databaseService.ApplyDatabase(Config.SunAndMoonPath, sourceXml);
            Config.AppliedBaseId = baseId;
            Config.AppliedBaseVersion = version;
            Config.SelectedBaseId = baseId;
            _configService.Save(Config);
        }

        // ---------------- Pack de imágenes ----------------

        public bool ImagesNeedSync()
        {
            var img = Manifest?.Images;
            if (img == null || string.IsNullOrWhiteSpace(img.File)) return false;
            return !HashUtil.HashesEqual(Config.AppliedImagePackSha256, img.Sha256);
        }

        public async Task SyncImagesAsync(IProgress<DownloadProgress> progress, CancellationToken ct = default)
        {
            var img = Manifest?.Images;
            if (img == null || !ImagesNeedSync()) return;

            var cachedZip = Path.Combine(AppConstants.CacheDir, Path.GetFileName(img.File));
            var uri = ManifestService.ResolveFileUri(Config.ManifestUrl, img.File);
            await _downloadService.DownloadAndVerifyAsync(uri, img.Sha256, cachedZip, progress, ct)
                .ConfigureAwait(false);

            _imagePackService.ApplyImagePack(Config.SunAndMoonPath, cachedZip);

            Config.AppliedImagePackVersion = img.Version;
            Config.AppliedImagePackSha256 = img.Sha256;
            _configService.Save(Config);
        }

        // ---------------- Lanzar ----------------

        public void Play() => _gameLauncher.Launch(Config.SunAndMoonPath);
    }
}
