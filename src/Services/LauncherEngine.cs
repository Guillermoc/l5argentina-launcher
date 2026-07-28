using System;
using System.Collections.Generic;
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

        // ---------------- Catálogo del Buscador ----------------

        /// <summary>True si el manifest trae catálogo y algún archivo falta o cambió de hash.</summary>
        public bool CatalogNeedsSync()
        {
            var cat = Manifest?.Catalog;
            if (cat == null) return false;
            return CatalogFileNeedsSync("cards", cat.Cards)
                || CatalogFileNeedsSync("rules", cat.Rules)
                || CatalogFileNeedsSync("filters", cat.Filters);
        }

        private bool CatalogFileNeedsSync(string key, CatalogFileEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.File)) return false;
            var path = Path.Combine(AppConstants.CatalogDir, Path.GetFileName(entry.File));
            if (!File.Exists(path)) return true;
            return !Config.CachedCatalogFiles.TryGetValue(key, out var cached)
                   || !HashUtil.HashesEqual(cached.Sha256, entry.Sha256);
        }

        /// <summary>
        /// Descarga (con verificación SHA-256 y confinamiento al origen del manifest, igual que
        /// bases/imágenes) los archivos del catálogo que falten o hayan cambiado, a
        /// cache/catalog/. Devuelve true si descargó algo (la UI invalida el catálogo cargado).
        /// </summary>
        public async Task<bool> SyncCatalogAsync(IProgress<DownloadProgress> progress, CancellationToken ct = default)
        {
            var cat = Manifest?.Catalog;
            if (cat == null) return false;

            var downloaded = false;
            foreach (var pair in new[]
            {
                new KeyValuePair<string, CatalogFileEntry>("cards", cat.Cards),
                new KeyValuePair<string, CatalogFileEntry>("rules", cat.Rules),
                new KeyValuePair<string, CatalogFileEntry>("filters", cat.Filters),
            })
            {
                var entry = pair.Value;
                if (!CatalogFileNeedsSync(pair.Key, entry)) continue;

                var destPath = Path.Combine(AppConstants.CatalogDir, Path.GetFileName(entry.File));
                var uri = ManifestService.ResolveFileUri(Config.ManifestUrl, entry.File);
                await _downloadService.DownloadAndVerifyAsync(uri, entry.Sha256, destPath, progress, ct)
                    .ConfigureAwait(false);

                Config.CachedCatalogFiles[pair.Key] = new CachedFile { Version = entry.Version, Sha256 = entry.Sha256 };
                downloaded = true;
            }
            if (downloaded) _configService.Save(Config);
            return downloaded;
        }

        // ---------------- Packs de imágenes del pool (manifest de la app) ----------------

        private PoolManifest _poolManifest;
        private string _poolManifestUrl;

        /// <summary>Carga (o recarga) el manifest del pool. URL alternativa solo para tests.</summary>
        public async Task LoadPoolManifestAsync(string url = null, CancellationToken ct = default)
        {
            _poolManifestUrl = url ?? AppConstants.PoolManifestUrl;
            _poolManifest = await _manifestService.FetchPoolAsync(_poolManifestUrl, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Packs de imágenes del pool, en el orden del manifest. Vacío si no se cargó.
        /// **Se excluye el pack propio de la comunidad** (ver <see cref="IsOwnImagePack"/>).
        /// </summary>
        public IReadOnlyList<PoolPackage> ImagePacks =>
            _poolManifest?.Packages?
                .Where(p => string.Equals(p.Type, "images", StringComparison.OrdinalIgnoreCase))
                .Where(p => !IsOwnImagePack(p))
                .ToList() ?? (IReadOnlyList<PoolPackage>)new List<PoolPackage>();

        /// <summary>
        /// El pack de imágenes propio (sección `images` del manifest del launcher, el que muestra
        /// la barra "Imágenes Samurai Extended") **también está publicado en el pool**. Ofrecerlo
        /// en las dos partes hacía que el usuario lo descargara dos veces y que cada mitad llevara
        /// su propio registro de "aplicado" —`appliedImagePackSha256` por un lado y
        /// `appliedImagePacks[id]` por el otro—, así que una decía "al día" y la otra
        /// "actualización disponible" sobre el mismo archivo.
        ///
        /// Se identifica **por hash** y no por id: si algún día los contenidos se separan de
        /// verdad, los hashes dejan de coincidir y el pack del pool vuelve a aparecer solo.
        /// </summary>
        private bool IsOwnImagePack(PoolPackage pack) =>
            Manifest?.Images?.Sha256 != null && pack?.Sha256 != null &&
            HashUtil.HashesEqual(Manifest.Images.Sha256, pack.Sha256);

        /// <summary>True si el pack ya está aplicado en esta versión exacta (por hash).</summary>
        public bool PackIsCurrent(PoolPackage pack) =>
            Config.AppliedImagePacks.TryGetValue(pack.Id, out var cached)
            && HashUtil.HashesEqual(cached.Sha256, pack.Sha256);

        public bool PackWasApplied(PoolPackage pack) =>
            Config.AppliedImagePacks.ContainsKey(pack.Id);

        /// <summary>
        /// Descarga un pack del pool (URL validada contra el origen del manifest, SHA-256
        /// verificado), lo extrae sobre &lt;SunAndMoon&gt;\images\ con la protección zip-slip de
        /// ImagePackService, borra el zip (pesan hasta ~280MB) y registra la versión aplicada.
        /// </summary>
        public async Task ApplyPoolImagePackAsync(PoolPackage pack, IProgress<DownloadProgress> progress, CancellationToken ct = default)
        {
            if (!InstallationService.IsValidInstall(Config.SunAndMoonPath))
                throw new InvalidOperationException("No hay una instalación válida de Sun and Moon configurada.");
            if (_poolManifestUrl == null)
                throw new InvalidOperationException("El manifest del pool no está cargado.");

            var uri = ManifestService.ResolveSameOriginUrl(_poolManifestUrl, pack.Url);
            var zipPath = Path.Combine(AppConstants.CacheDir, "packs", $"{pack.Id}-{pack.Version}.zip");

            await _downloadService.DownloadAndVerifyAsync(uri, pack.Sha256, zipPath, progress, ct)
                .ConfigureAwait(false);
            try
            {
                _imagePackService.ApplyImagePack(Config.SunAndMoonPath, zipPath);
            }
            finally
            {
                try { File.Delete(zipPath); } catch { /* limpieza best-effort */ }
            }

            Config.AppliedImagePacks[pack.Id] = new CachedFile { Version = pack.Version, Sha256 = pack.Sha256 };
            _configService.Save(Config);
        }

        // ---------------- Catálogo del Buscador desde el pool ----------------

        // Mapeo package del pool → clave de config y nombre base local. El nombre local lleva
        // la versión (cards_db-2.3.1.zip) porque CatalogStore elige la versión más alta por
        // nombre de archivo; la URL del pool termina sin versión (cards_db.zip).
        private static readonly Tuple<string, string, string>[] CatalogPoolParts =
        {
            Tuple.Create("cards", "database", "cards_db"),
            Tuple.Create("rules", "rules", "rules"),
            Tuple.Create("filters", "filters", "filters"),
        };

        private PoolPackage FindPoolPackage(string type) =>
            _poolManifest?.Packages?.FirstOrDefault(p =>
                string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase));

        /// <summary>True si el pool (ya cargado) trae catálogo con algo para (re)descargar.</summary>
        public bool CatalogNeedsSyncFromPool()
        {
            if (_poolManifest == null) return false;
            foreach (var part in CatalogPoolParts)
            {
                var pkg = FindPoolPackage(part.Item2);
                if (pkg != null && CatalogPoolFileNeedsSync(part.Item1, part.Item3, pkg)) return true;
            }
            return false;
        }

        private bool CatalogPoolFileNeedsSync(string key, string baseName, PoolPackage pkg)
        {
            var path = Path.Combine(AppConstants.CatalogDir, CatalogLocalName(baseName, pkg));
            if (!File.Exists(path)) return true;
            return !Config.CachedCatalogFiles.TryGetValue(key, out var cached)
                   || !HashUtil.HashesEqual(cached.Sha256, pkg.Sha256);
        }

        private static string CatalogLocalName(string baseName, PoolPackage pkg)
        {
            var ext = Path.GetExtension(new Uri(pkg.Url, UriKind.Absolute).AbsolutePath);
            if (string.IsNullOrEmpty(ext)) ext = ".json";
            return baseName + "-" + pkg.Version + ext;
        }

        /// <summary>
        /// Sincroniza cards_db/rules/filters desde el pool a cache/catalog/ (URL same-origin +
        /// SHA-256, como los packs). Borra las versiones viejas de cada archivo para que la
        /// caché no acumule. Devuelve true si descargó algo.
        /// </summary>
        public async Task<bool> SyncCatalogFromPoolAsync(IProgress<DownloadProgress> progress, CancellationToken ct = default)
        {
            if (_poolManifest == null) return false;

            var downloaded = false;
            foreach (var part in CatalogPoolParts)
            {
                var pkg = FindPoolPackage(part.Item2);
                if (pkg == null || !CatalogPoolFileNeedsSync(part.Item1, part.Item3, pkg)) continue;

                var uri = ManifestService.ResolveSameOriginUrl(_poolManifestUrl, pkg.Url);
                var localName = CatalogLocalName(part.Item3, pkg);
                var destPath = Path.Combine(AppConstants.CatalogDir, localName);
                await _downloadService.DownloadAndVerifyAsync(uri, pkg.Sha256, destPath, progress, ct)
                    .ConfigureAwait(false);

                CleanupOldCatalogVersions(part.Item3, localName);
                Config.CachedCatalogFiles[part.Item1] = new CachedFile { Version = pkg.Version, Sha256 = pkg.Sha256 };
                downloaded = true;
            }
            if (downloaded) _configService.Save(Config);
            return downloaded;
        }

        private static void CleanupOldCatalogVersions(string baseName, string keepFileName)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(AppConstants.CatalogDir, baseName + "-*"))
                    if (!string.Equals(Path.GetFileName(file), keepFileName, StringComparison.OrdinalIgnoreCase))
                        File.Delete(file);
            }
            catch
            {
                // limpieza best-effort: una versión vieja no molesta (se elige la más alta)
            }
        }

        // ---------------- Lanzar ----------------

        public void Play() => _gameLauncher.Launch(Config.SunAndMoonPath);
    }
}
