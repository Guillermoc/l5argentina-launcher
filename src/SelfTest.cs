#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using L5ArgentinaLauncher.Models;
using L5ArgentinaLauncher.Services;
using L5ArgentinaLauncher.Services.Deck;

namespace L5ArgentinaLauncher
{
    /// <summary>
    /// Harness de verificación (SOLO build DEBUG). Monta un bucket y una instalación falsos en
    /// %TEMP% y ejercita el pipeline real (manifest, hash, confinamiento de origen, zip-slip,
    /// backups). Se invoca con: L5ArgentinaLauncher.exe --selftest [reportPath].
    /// Aísla todo seteando L5A_DATA_ROOT a una carpeta temporal (no toca %APPDATA%).
    /// </summary>
    internal static class SelfTest
    {
        private static readonly List<string> Log = new List<string>();
        private static int _pass, _fail;

        public static int Run(string[] args)
        {
            var reportPath = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "l5a_selftest.txt");
            var tempRoot = Path.Combine(Path.GetTempPath(), "l5a-selftest-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("L5A_DATA_ROOT", tempRoot);

            try
            {
                RunAll(tempRoot);
            }
            catch (Exception ex)
            {
                Line("FATAL: " + ex);
            }
            finally
            {
                Line("");
                Line($"RESULTADO: {_pass} PASS, {_fail} FAIL");
                File.WriteAllText(reportPath, string.Join(Environment.NewLine, Log), Encoding.UTF8);
                TryDeleteDir(tempRoot);
            }
            return _fail == 0 ? 0 : 1;
        }

        private static void RunAll(string tempRoot)
        {
            // ---------- VersionUtil ----------
            Assert(VersionUtil.IsNewer("1.2.0", "1.0.0"), "VersionUtil: 1.2.0 > 1.0.0");
            Assert(!VersionUtil.IsNewer("1.0.0", "1.0.0"), "VersionUtil: 1.0.0 == 1.0.0");
            Assert(!VersionUtil.IsNewer("1.0.0", "1.2.0"), "VersionUtil: 1.0.0 < 1.2.0");
            Assert(VersionUtil.IsNewer("1.0.10", "1.0.9"), "VersionUtil: numérico (10 > 9)");

            // ---------- Bucket falso ----------
            var bucket = Path.Combine(tempRoot, "bucket");
            Directory.CreateDirectory(Path.Combine(bucket, "bases"));
            Directory.CreateDirectory(Path.Combine(bucket, "images"));

            var communityXml = Path.Combine(bucket, "bases", "community.xml");
            File.WriteAllText(communityXml, "<cards><card><legal>s_extended</legal></card></cards>");
            var communitySha = HashUtil.Sha256OfFile(communityXml);

            var packZip = Path.Combine(bucket, "images", "pack.zip");
            CreateZip(packZip, new Dictionary<string, string> { { "cards/TEST/card1.jpg", "fake-jpg-bytes" } });
            var packSha = HashUtil.Sha256OfFile(packZip);

            var manifest = new Manifest
            {
                Schema = 1,
                Launcher = new LauncherInfo { LatestVersion = "9.9.9", Notes = "test" },
                Databases = new List<DatabaseEntry>
                {
                    new DatabaseEntry { Id="community", Label="Comunidad", Version="2.0.1",
                                        File="bases/community.xml", Sha256=communitySha,
                                        Size=new FileInfo(communityXml).Length }
                },
                Images = new ImagePackEntry { Version="2.0.1", File="images/pack.zip",
                                              Sha256=packSha, Size=new FileInfo(packZip).Length }
            };
            var manifestPath = Path.Combine(bucket, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
            var manifestUrl = new Uri(manifestPath).AbsoluteUri; // file:///...

            // ---------- ManifestService: fetch + parseo ----------
            var ms = new ManifestService();
            var m = ms.FetchAsync(manifestUrl).GetAwaiter().GetResult();
            Assert(m.Schema == 1, "Manifest: schema");
            Assert(m.Databases.Count == 1 && m.Databases[0].Version == "2.0.1", "Manifest: base comunidad parseada");
            Assert(m.Launcher.LatestVersion == "9.9.9", "Manifest: launcher.latest_version (snake_case)");
            Assert(m.Images != null && m.Images.Sha256 == packSha, "Manifest: images.sha256");

            // ---------- ResolveFileUri: confinamiento al origen ----------
            var good = ManifestService.ResolveFileUri(manifestUrl, "bases/community.xml");
            Assert(good.IsFile && File.Exists(good.LocalPath), "Resolve: ruta relativa válida");
            AssertThrows<SecurityException>(() => ManifestService.ResolveFileUri(manifestUrl, "https://evil.example.com/x.xml"),
                "Resolve: rechaza URL absoluta a otro origen");
            AssertThrows<SecurityException>(() => ManifestService.ResolveFileUri(manifestUrl, "../escape.xml"),
                "Resolve: rechaza escape de directorio (../)");

            // ---------- DownloadService: verificación SHA-256 ----------
            var ds = new DownloadService();
            var cacheOk = Path.Combine(AppConstants.CacheDir, "community.xml");
            ds.DownloadAndVerifyAsync(good, communitySha, cacheOk).GetAwaiter().GetResult();
            Assert(File.Exists(cacheOk) && HashUtil.HashesEqual(HashUtil.Sha256OfFile(cacheOk), communitySha),
                "Download: hash correcto → archivo en caché");

            var cacheBad = Path.Combine(AppConstants.CacheDir, "community-bad.xml");
            AssertThrows<SecurityException>(
                () => ds.DownloadAndVerifyAsync(good, "00" + communitySha.Substring(2), cacheBad).GetAwaiter().GetResult(),
                "Download: hash incorrecto → aborta");
            Assert(!File.Exists(cacheBad), "Download: no deja archivo final si el hash falla");

            // ---------- LauncherEngine: estado desde manifest ----------
            var config = new UserConfig { ManifestUrl = manifestUrl };
            var engine = new LauncherEngine(config);
            engine.LoadManifestAsync().GetAwaiter().GetResult();
            Assert(engine.LauncherUpdateAvailable(), "Engine: detecta update del launcher (9.9.9 > 1.0.0)");
            Assert(engine.CommunityNeedsDownload(), "Engine: comunidad necesita descarga (sin caché previa)");

            // ---------- Instalación falsa ----------
            var install = Path.Combine(tempRoot, "install");
            var dbDir = Path.Combine(install, "Sun and Moon_Data", "StreamingAssets", "Database");
            Directory.CreateDirectory(dbDir);
            Directory.CreateDirectory(Path.Combine(install, "images"));
            File.WriteAllText(Path.Combine(install, "Sun and Moon.exe"), "dummy");
            var dbXml = Path.Combine(dbDir, "database.xml");
            File.WriteAllText(dbXml, "ORIGINAL-BASE-CONTENT");

            Assert(InstallationService.IsValidInstall(install), "Install: detectada como válida");

            // La pantalla baja la base sola al abrir (ADR-064); pisar el database.xml sigue siendo de JUGAR.
            var preEngine = new LauncherEngine(new UserConfig { ManifestUrl = manifestUrl, SunAndMoonPath = install });
            preEngine.LoadManifestAsync().GetAwaiter().GetResult();
            preEngine.DownloadCommunityAsync(null).GetAwaiter().GetResult();
            Assert(!preEngine.CommunityNeedsDownload() && preEngine.NeedsApply(LauncherEngine.CommunityId) &&
                   File.ReadAllText(dbXml) == "ORIGINAL-BASE-CONTENT",
                "Engine: la predescarga deja la base en caché sin tocar el database.xml");

            // ---------- ImagePackService: extracción normal ----------
            var ips = new ImagePackService();
            ips.ApplyImagePack(install, packZip);
            Assert(File.Exists(Path.Combine(install, "images", "cards", "TEST", "card1.jpg")),
                "ImagePack: entrada normal extraída a images/cards/...");

            // ---------- ImagePackService: zip-slip rechazado ----------
            var evilZip = Path.Combine(tempRoot, "evil.zip");
            CreateZip(evilZip, new Dictionary<string, string> { { "../../l5a-evil.txt", "pwned" } });
            AssertThrows<SecurityException>(() => ips.ApplyImagePack(install, evilZip), "ImagePack: zip-slip rechazado");
            Assert(!File.Exists(Path.Combine(tempRoot, "l5a-evil.txt")), "ImagePack: no escribió fuera de images/");

            // ---------- DatabaseService: backup original + aplicar con backup timestamped ----------
            var dbs = new DatabaseService();
            var captured = dbs.EnsureOriginalBackup(install);
            Assert(captured && File.Exists(AppConstants.OriginalBackupPath), "Database: capturó backup original (1er arranque)");
            Assert(File.ReadAllText(AppConstants.OriginalBackupPath) == "ORIGINAL-BASE-CONTENT", "Database: backup original = contenido original");
            Assert(!dbs.EnsureOriginalBackup(install), "Database: no recaptura si ya existe");
            Assert(dbs.LooksLikeCommunityBase(install) == false, "Database: original no parece comunidad");

            int backupsBefore = CountFiles(AppConstants.BackupsDir);
            dbs.ApplyDatabase(install, cacheOk); // pisa con la comunidad descargada
            Assert(File.ReadAllText(dbXml).Contains("s_extended"), "Database: database.xml pisado con la base comunidad");
            Assert(CountFiles(AppConstants.BackupsDir) > backupsBefore, "Database: dejó backup timestamped antes de pisar");
            Assert(dbs.LooksLikeCommunityBase(install), "Database: ahora sí parece comunidad (s_extended)");

            // ---------- ZipUtil: base comunidad comprimida (.zip con database.xml adentro) ----------
            var dbZip = Path.Combine(tempRoot, "db.zip");
            CreateZip(dbZip, new Dictionary<string, string> { { "database.xml", "<cards><card><legal>s_extended</legal></card></cards>" } });
            var outXml = Path.Combine(tempRoot, "out.xml");
            ZipUtil.ExtractSingleXml(dbZip, outXml);
            Assert(File.Exists(outXml) && File.ReadAllText(outXml).Contains("s_extended"), "ZipUtil: extrae database.xml del zip");

            // ---------- Catálogo del Buscador: sync desde manifest (sección opcional) ----------
            var catalogDirBucket = Path.Combine(bucket, "catalog");
            Directory.CreateDirectory(catalogDirBucket);
            var miniCards = "[{\"id\":\"T1\",\"type\":\"personality\",\"name\":\"Prueba Uno\",\"rarity\":\"C\"," +
                            "\"printings\":[{\"id\":\"T1a\",\"edition\":\"OLD\",\"image\":\"images/cards/OLD/T1a.jpg\"," +
                            "\"legalities\":[\"samurai\"],\"current\":false}," +
                            "{\"id\":\"T1\",\"edition\":\"TEST\",\"image\":\"images/cards/TEST/T1.jpg\"," +
                            "\"legalities\":[\"samurai\"],\"current\":true}]}," +
                            "{\"id\":\"T2\",\"type\":\"holding\",\"name\":\"Prueba Dos\",\"rarity\":\"C\"," +
                            "\"printings\":[{\"id\":\"T2\",\"edition\":\"TEST\",\"legalities\":[\"samurai\"],\"current\":true}]}]";
            var cardsZipPath = Path.Combine(catalogDirBucket, "cards_db-1.0.0.zip");
            CreateZip(cardsZipPath, new Dictionary<string, string> { { "cards-1.0.0.json", miniCards } });
            var rulesPath = Path.Combine(catalogDirBucket, "rules-1.0.0.json");
            File.WriteAllText(rulesPath, "{\"rules\":[{\"title\":\"Prueba Dos\",\"banned\":true,\"label\":{\"text\":\"BANNED\",\"color\":\"#FF0000\"}}]}");
            var filtersPath = Path.Combine(catalogDirBucket, "filters-1.0.0.json");
            File.WriteAllText(filtersPath, "{\"sets\":[{\"code\":\"TEST\",\"name\":\"Set de prueba\"}],\"formats\":[],\"keywords\":[]}");

            manifest.Catalog = new CatalogSection
            {
                Cards = new CatalogFileEntry { Version = "1.0.0", File = "catalog/cards_db-1.0.0.zip", Sha256 = HashUtil.Sha256OfFile(cardsZipPath), Size = new FileInfo(cardsZipPath).Length },
                Rules = new CatalogFileEntry { Version = "1.0.0", File = "catalog/rules-1.0.0.json", Sha256 = HashUtil.Sha256OfFile(rulesPath), Size = new FileInfo(rulesPath).Length },
                Filters = new CatalogFileEntry { Version = "1.0.0", File = "catalog/filters-1.0.0.json", Sha256 = HashUtil.Sha256OfFile(filtersPath), Size = new FileInfo(filtersPath).Length },
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

            var engineCat = new LauncherEngine(new UserConfig { ManifestUrl = manifestUrl });
            engineCat.LoadManifestAsync().GetAwaiter().GetResult();
            Assert(engineCat.Manifest.Catalog?.Cards?.Version == "1.0.0", "Catálogo: manifest parsea la sección catalog");
            Assert(engineCat.CatalogNeedsSync(), "Catálogo: necesita sync (caché vacía)");
            engineCat.SyncCatalogAsync(null).GetAwaiter().GetResult();
            Assert(File.Exists(Path.Combine(AppConstants.CatalogDir, "cards_db-1.0.0.zip")), "Catálogo: cards_db descargado a cache/catalog");
            Assert(!engineCat.CatalogNeedsSync(), "Catálogo: al día tras el sync");
            Assert(Services.Search.CatalogStore.IsAvailable(), "Catálogo: CatalogStore lo detecta");
            var miniCatalog = Services.Search.CatalogStore.Load();
            Assert(miniCatalog.Cards.Count == 2 && miniCatalog.CardsVersion == "1.0.0", "Catálogo: carga 2 cartas v1.0.0");
            var miniEngine = new Services.Search.CardSearchEngine(miniCatalog);
            var miniResult = miniEngine.Search(new Services.Search.CardSearchCriteria { Name = "prueba" });
            Assert(miniResult.Count == 1 && miniResult[0].Id == "T1", "Catálogo: buscador excluye la baneada por default");
            var miniBanned = miniEngine.Search(new Services.Search.CardSearchCriteria { Name = "prueba", IncludeBanned = true });
            Assert(miniBanned.Count == 2 && miniBanned.Any(x => x.LabelText == "BANNED"), "Catálogo: includeBanned la muestra con su label de rules.json");

            // Expansión por impresión (capa de visualización, SPEC-FILTROS §15)
            var noExpand = miniEngine.ExpandForDisplay(miniBanned, new Services.Search.CardSearchCriteria(), false);
            Assert(noExpand.Count == 2, "Expansión: sin expandir, una entrada por carta");
            var expanded = miniEngine.ExpandForDisplay(miniBanned, new Services.Search.CardSearchCriteria(), true);
            Assert(expanded.Count == 3 && expanded.Count(v => v.Card.Id == "T1") == 2,
                $"Expansión: T1 (2 printings) se expande a 2 entradas (total {expanded.Count})");
            var expandedFiltered = miniEngine.ExpandForDisplay(miniBanned,
                new Services.Search.CardSearchCriteria { Sets = new List<string> { "OLD" } }, true);
            Assert(expandedFiltered.Count(v => v.Card.Id == "T1") == 1
                   && expandedFiltered.First(v => v.Card.Id == "T1").ImagePath.Contains("OLD")
                   && expandedFiltered.Count(v => v.Card.Id == "T2") == 1,
                "Expansión: con filtro de set solo esa impresión; carta sin match conserva la preferida");

            // ---------- Pool manifest (CDN de la app): parse + same-origin + aplicar pack ----------
            var pool = Path.Combine(tempRoot, "pool");
            Directory.CreateDirectory(pool);
            var poolPackZip = Path.Combine(pool, "testpack.zip");
            CreateZip(poolPackZip, new Dictionary<string, string> { { "cards/POOLSET/img1.jpg", "fake-pool-jpg" } });
            var poolPackSha = HashUtil.Sha256OfFile(poolPackZip);
            // Catálogo del pool: cards_db.zip (con un cards.json válido adentro) + rules + filters
            var poolCardsZip = Path.Combine(pool, "cards_db.zip");
            CreateZip(poolCardsZip, new Dictionary<string, string>
            {
                { "cards-9.9.json", "[{\"id\":\"PX1\",\"type\":\"event\",\"name\":\"Pool Card\",\"rarity\":\"C\"," +
                  "\"printings\":[{\"id\":\"PX1\",\"edition\":\"POOLSET\",\"current\":true}]}]" },
            });
            var poolRulesPath = Path.Combine(pool, "rules.json");
            File.WriteAllText(poolRulesPath, "{\"rules\":[]}");
            var poolFiltersPath = Path.Combine(pool, "filters.json");
            File.WriteAllText(poolFiltersPath, "{\"sets\":[{\"code\":\"POOLSET\"}],\"formats\":[],\"keywords\":[]}");

            string PoolPkg(string id, string type, string ver, string path) =>
                "{\"id\":\"" + id + "\",\"type\":\"" + type + "\",\"version\":\"" + ver + "\",\"url\":\"" +
                new Uri(path).AbsoluteUri + "\",\"sizeBytes\":" + new FileInfo(path).Length +
                ",\"sha256\":\"" + HashUtil.Sha256OfFile(path) + "\"}";

            var poolManifestPath = Path.Combine(pool, "manifest.json");
            File.WriteAllText(poolManifestPath, "{\"packages\":[" +
                PoolPkg("testpack", "images", "1.0", poolPackZip) + "," +
                PoolPkg("cards_db", "database", "9.9", poolCardsZip) + "," +
                PoolPkg("rules", "rules", "9.9", poolRulesPath) + "," +
                PoolPkg("filters", "filters", "9.9", poolFiltersPath) + "]}");
            var poolUrl = new Uri(poolManifestPath).AbsoluteUri;

            // Confinamiento same-origin de URLs absolutas del pool
            var okUri = ManifestService.ResolveSameOriginUrl(
                "https://bucket.example/production/manifest.json", "https://bucket.example/pool/images/x/1.0/x.zip");
            Assert(okUri.AbsolutePath == "/pool/images/x/1.0/x.zip", "Pool: acepta URL absoluta del mismo origen");
            AssertThrows<SecurityException>(() => ManifestService.ResolveSameOriginUrl(
                "https://bucket.example/production/manifest.json", "https://evil.example/pool/x.zip"),
                "Pool: rechaza URL de otro origen");
            AssertThrows<SecurityException>(() => ManifestService.ResolveSameOriginUrl(
                "https://bucket.example/production/manifest.json", "http://bucket.example/pool/x.zip"),
                "Pool: rechaza degradación de esquema (http)");

            var enginePool = new LauncherEngine(new UserConfig { ManifestUrl = manifestUrl, SunAndMoonPath = install });
            enginePool.LoadPoolManifestAsync(poolUrl).GetAwaiter().GetResult();
            Assert(enginePool.ImagePacks.Count == 1 && enginePool.ImagePacks[0].Id == "testpack",
                "Pool: solo los packages type=images son packs");
            Assert(!enginePool.PackIsCurrent(enginePool.ImagePacks[0]), "Pool: pack no aplicado inicialmente");

            // El pack que el manifest del bucket marca como incluido también está publicado en el
            // pool: mismo archivo, mismo hash. Ya NO se lo saca de la lista (ADR-055): es un pack de
            // imágenes como cualquier otro y el pool es quien dice cuáles hay. Lo que sí cambia es
            // que se lo marca, y que los dos registros de "aplicado" se mantienen en sincronía —eso
            // era el problema real de ADR-048, no que apareciera en la lista—.
            var dupPoolPath = Path.Combine(pool, "manifest-dup.json");
            File.WriteAllText(dupPoolPath, "{\"packages\":[" +
                PoolPkg("testpack", "images", "1.0", poolPackZip) + "," +
                PoolPkg("samuraiEx", "images", "1.0", packZip) + "]}");
            var dupEngine = new LauncherEngine(new UserConfig { ManifestUrl = manifestUrl, SunAndMoonPath = install });
            dupEngine.LoadPoolManifestAsync(new Uri(dupPoolPath).AbsoluteUri).GetAwaiter().GetResult();
            Assert(dupEngine.ImagePacks.Count == 2, "Pool: los dos packages type=images están en la lista");
            var bundledPack = dupEngine.ImagePacks.First(p => p.Id == "samuraiEx");
            Assert(!dupEngine.IsBundledPack(bundledPack),
                "Pool: sin el manifest del launcher cargado, ningún pack es el incluido");
            dupEngine.LoadManifestAsync().GetAwaiter().GetResult();
            Assert(dupEngine.ImagePacks.Count == 2,
                "Pool: el pack incluido sigue en la lista, no se esconde");
            Assert(dupEngine.IsBundledPack(bundledPack) &&
                   !dupEngine.IsBundledPack(dupEngine.ImagePacks.First(p => p.Id == "testpack")),
                "Pool: el incluido se reconoce por sha256 contra manifest.images, y solo ese");

            // Los dos caminos (JUGAR y el listado) escriben los dos registros: si no, el mismo
            // archivo quedaba "al día" por un lado y "actualización disponible" por el otro.
            Assert(!dupEngine.PackIsCurrent(bundledPack), "Pool: el incluido arranca sin aplicar");
            // JUGAR ya no lo aplica (ADR-064): el aviso es lo único que dice que falta, así que el
            // recomendado cuenta aunque esté en 1.0, y un 1.0 común sigue sin contar.
            Assert(dupEngine.PackIsPending(bundledPack) &&
                   !dupEngine.PackIsPending(dupEngine.ImagePacks.First(p => p.Id == "testpack")),
                "Pendientes: el recomendado sin bajar avisa aunque esté en 1.0; un 1.0 común no");
            dupEngine.SyncImagesAsync(null).GetAwaiter().GetResult();
            Assert(dupEngine.PackIsCurrent(bundledPack),
                "Pool: aplicado por JUGAR, el listado lo ve al día (no lo ofrece de nuevo)");
            Assert(dupEngine.Config.AppliedImagePacks.ContainsKey("samuraiEx"),
                "Pool: JUGAR deja el registro por id, no solo el suelto");

            // ---------- Qué cuenta como "pendiente" ----------
            // Un pack en versión inicial (1.0) es catálogo, no novedad: se puede bajar, pero no
            // aparece como pendiente. Cualquier otra versión sí, porque significa republicado.
            Assert(LauncherEngine.IsInitialPackVersion("1.0") && LauncherEngine.IsInitialPackVersion("1")
                   && LauncherEngine.IsInitialPackVersion("1.0.0"),
                "Pendientes: 1, 1.0 y 1.0.0 son la misma versión inicial");
            Assert(!LauncherEngine.IsInitialPackVersion("1.1") && !LauncherEngine.IsInitialPackVersion("2.0")
                   && !LauncherEngine.IsInitialPackVersion("0.9"),
                "Pendientes: cualquier otra versión no es la inicial");
            Assert(!enginePool.PackIsPending(enginePool.ImagePacks[0]),
                "Pendientes: un pack 1.0 sin descargar no es pendiente");
            Assert(enginePool.GetPendingSummary().ImagePacks == 0,
                "Pendientes: no se cuentan los packs en versión inicial");

            var mixedPoolPath = Path.Combine(pool, "manifest-mixed.json");
            File.WriteAllText(mixedPoolPath, "{\"packages\":[" +
                PoolPkg("packInicial", "images", "1.0", poolPackZip) + "," +
                PoolPkg("packNuevo", "images", "1.1", poolPackZip) + "]}");
            var mixedEngine = new LauncherEngine(new UserConfig { ManifestUrl = manifestUrl, SunAndMoonPath = install });
            mixedEngine.LoadPoolManifestAsync(new Uri(mixedPoolPath).AbsoluteUri).GetAwaiter().GetResult();
            Assert(mixedEngine.PackIsPending(mixedEngine.ImagePacks.First(p => p.Id == "packNuevo")),
                "Pendientes: un pack con versión distinta de 1.0 sin descargar sí es pendiente");
            var mixedSummary = mixedEngine.GetPendingSummary();
            Assert(mixedSummary.ImagePacks == 1 && mixedSummary.AnyData,
                "Pendientes: se cuenta solo el pack republicado, y el resumen lo reporta");
            Assert(!enginePool.GetPendingSummary().AnyData,
                "Pendientes: sin nada que bajar, el resumen dice que está todo al día");

            // ---------- Un pack NUEVO avisa aunque salga en 1.0 (ADR-056) ----------
            // La siembra es lo que evita que estrenar esta versión marque como novedad los ~20 arcos
            // que existen desde siempre.
            var newCfg = new UserConfig { ManifestUrl = manifestUrl, SunAndMoonPath = install };
            var newEngine = new LauncherEngine(newCfg);
            newEngine.LoadPoolManifestAsync(new Uri(mixedPoolPath).AbsoluteUri).GetAwaiter().GetResult();
            Assert(newCfg.KnownImagePacks != null && newCfg.KnownImagePacks.Count == 2,
                "Nuevos: la primera vista del pool siembra todos los packs como conocidos");
            Assert(!newEngine.PackIsNew(newEngine.ImagePacks.First(p => p.Id == "packInicial")),
                "Nuevos: lo que ya estaba en la siembra no es nuevo");
            Assert(newEngine.GetPendingSummary().ImagePacks == 1,
                "Nuevos: tras sembrar, solo el republicado cuenta");

            var laterPoolPath = Path.Combine(pool, "manifest-later.json");
            File.WriteAllText(laterPoolPath, "{\"packages\":[" +
                PoolPkg("packInicial", "images", "1.0", poolPackZip) + "," +
                PoolPkg("packNuevo", "images", "1.1", poolPackZip) + "," +
                PoolPkg("expaComunidad", "images", "1.0", poolPackZip) + "]}");
            newEngine.LoadPoolManifestAsync(new Uri(laterPoolPath).AbsoluteUri).GetAwaiter().GetResult();
            var expa = newEngine.ImagePacks.First(p => p.Id == "expaComunidad");
            Assert(newEngine.PackIsNew(expa) && newEngine.PackIsPending(expa),
                "Nuevos: un pack que aparece después es novedad AUNQUE esté en 1.0");
            Assert(newEngine.GetPendingSummary().ImagePacks == 2,
                "Nuevos: el pack nuevo se suma al conteo de pendientes");
            newEngine.ApplyPoolImagePackAsync(expa, null).GetAwaiter().GetResult();
            Assert(!newEngine.PackIsNew(expa) && !newEngine.PackIsPending(expa),
                "Nuevos: descargarlo lo saca de novedades");

            enginePool.ApplyPoolImagePackAsync(enginePool.ImagePacks[0], null).GetAwaiter().GetResult();
            Assert(File.Exists(Path.Combine(install, "images", "cards", "POOLSET", "img1.jpg")),
                "Pool: pack extraído sobre images/cards/ de la instalación");
            Assert(enginePool.PackIsCurrent(enginePool.ImagePacks[0]), "Pool: pack registrado como aplicado");
            Assert(!Directory.Exists(Path.Combine(AppConstants.CacheDir, "packs")) ||
                   Directory.GetFiles(Path.Combine(AppConstants.CacheDir, "packs")).Length == 0,
                "Pool: el zip descargado se borra tras aplicar");

            // Catálogo del Buscador desde el pool (con nombre versionado + limpieza de viejos)
            Assert(enginePool.CatalogNeedsSyncFromPool(), "Pool: catálogo necesita sync (config fresca)");
            enginePool.SyncCatalogFromPoolAsync(null).GetAwaiter().GetResult();
            Assert(File.Exists(Path.Combine(AppConstants.CatalogDir, "cards_db-9.9.zip"))
                   && File.Exists(Path.Combine(AppConstants.CatalogDir, "rules-9.9.json"))
                   && File.Exists(Path.Combine(AppConstants.CatalogDir, "filters-9.9.json")),
                "Pool: catálogo descargado con nombres versionados");
            Assert(!File.Exists(Path.Combine(AppConstants.CatalogDir, "cards_db-1.0.0.zip")),
                "Pool: limpia versiones viejas del catálogo");
            Assert(!enginePool.CatalogNeedsSyncFromPool(), "Pool: catálogo al día tras el sync");
            var poolCatalog = Services.Search.CatalogStore.Load();
            Assert(poolCatalog.CardsVersion == "9.9" && poolCatalog.Cards.Count == 1
                   && poolCatalog.Cards[0].Id == "PX1",
                "Pool: CatalogStore carga la versión del pool");

            // ---------- Stores del Buscador: presets (roundtrip en disco) ----------
            // Las listas ya no viven en usercards.json (son .txt de la carpeta Lists): sus checks
            // se mudaron a RunDeckStoreTests, junto con los de favoritas/ocultas por nombre.
            var presets = Services.Search.FilterPresetStore.Load();
            presets.Save("agresivo", new Services.Search.CardSearchCriteria
            {
                Types = new List<string> { "personality" },
                ForceMin = 4,
                SortField = Services.Search.SortField.FORCE,
                SortAscending = false,
            });
            var presets2 = Services.Search.FilterPresetStore.Load();
            var preset = presets2.Get("agresivo");
            Assert(preset != null && preset.Types.Count == 1 && preset.Types[0] == "personality"
                   && preset.ForceMin == 4 && preset.SortField == Services.Search.SortField.FORCE
                   && !preset.SortAscending,
                "Stores: preset roundtrip (tipos, rango, orden)");

            // ---------- Buscador: paridad con la app Android (golden tests) ----------
            RunGoldenSearchTests();

            // ---------- Deckbuilder: paridad con la app Android (golden tests) ----------
            RunGoldenDeckTests();

            // ---------- Deckbuilder: persistencia txt de Sun and Moon ----------
            RunDeckStoreTests(tempRoot);

            // ---------- Proxies y ediciones del txt de Sun and Moon ----------
            RunDeckProxyTests(tempRoot);

            // ---------- Conjuntos de cartas: listas como txt + identidad por nombre ----------
            RunCardSetTests(tempRoot);

            // ---------- Recorte: quantity=0 conserva la carta en el mazo ----------
            RunTrimTests(tempRoot);

            // ---------- Estado de armado: mazo terminado vs pendiente ----------
            RunDeckStateTests(tempRoot);

            // ---------- Etiquetas: persistencia por nombre y partición por combinación ----------
            RunDeckTagTests(tempRoot);

            // ---------- Ajuste de oro por carta (el coste que se paga vs. el impreso) ----------
            RunDeckGoldTests(tempRoot);

            // ---------- Prefijos de la barra de filtro (los usan el Buscador y Mazos) ----------
            RunBarPrefixTests();

            // ---------- Edición e id de impresión derivados del path de imagen ----------
            RunPrintingIdTests();

            // ---------- Exportación a PDF: geometría de la hoja y estructura del archivo ----------
            RunPdfExportTests(tempRoot);

            // ---------- Ubicación de la previsualización con Alt (el cursor nunca queda adentro) ----------
            RunHoverPreviewTests();

            // ---------- Opcional: e2e con archivos REALES del bucket ----------
            // Se activa pasando L5A_SELFTEST_BUCKET=<carpeta con manifest.json + zips reales>.
            var realBucket = Environment.GetEnvironmentVariable("L5A_SELFTEST_BUCKET");
            if (!string.IsNullOrEmpty(realBucket) && File.Exists(Path.Combine(realBucket, "manifest.json")))
                RunRealBucket(tempRoot, realBucket);
        }

        /// <summary>
        /// Paridad del motor del Buscador contra los 53 golden tests generados con la lógica
        /// real de la app Android (tests/fixtures/buscador/). Cada caso es un
        /// criteria serializado + los ids esperados; orderMatters decide secuencia vs conjunto.
        /// Si los fixtures no están (exe distribuido fuera del repo), se omite sin fallar.
        /// </summary>
        private static void RunGoldenSearchTests()
        {
            var fixturesDir = FindRepoRelative(Path.Combine("tests", "fixtures", "buscador"));
            if (fixturesDir == null)
            {
                Line("SKIP: golden tests del Buscador (fixtures no encontrados junto al repo)");
                return;
            }

            Line("");
            Line("=== Buscador: golden tests de paridad (motor portado vs app Android) ===");

            var catalog = Services.Search.CatalogStore.Load(Path.Combine(fixturesDir, "data"));
            Assert(catalog.Cards.Count == 6394, $"Golden: catálogo importado (6394 cartas; hay {catalog.Cards.Count})");
            Assert(catalog.Sets.Count == 48, $"Golden: 48 sets de filters.json (hay {catalog.Sets.Count})");
            var engine = new Services.Search.CardSearchEngine(catalog);

            using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixturesDir, "golden-tests.json"))))
            {
                var root = doc.RootElement;

                // Datos de usuario sembrados (favoritas/ocultas/listas) para los casos que los usan
                var user = new Services.Search.UserCardData();
                var seed = root.GetProperty("userDataSeed");
                foreach (var f in seed.GetProperty("favorites").EnumerateArray()) user.Favorites.Add(f.GetString());
                foreach (var h in seed.GetProperty("hidden").EnumerateArray()) user.Hidden.Add(h.GetString());
                foreach (var list in seed.GetProperty("lists").EnumerateObject())
                {
                    var ids = new HashSet<string>();
                    foreach (var id in list.Value.EnumerateArray()) ids.Add(id.GetString());
                    user.Lists[list.Name] = ids;
                }

                foreach (var c in root.GetProperty("cases").EnumerateArray())
                {
                    var caseName = c.GetProperty("name").GetString();
                    var criteria = JsonSerializer.Deserialize<Services.Search.CardSearchCriteria>(
                        c.GetProperty("filters").GetRawText());
                    criteria.Name = c.GetProperty("query").GetString() ?? "";
                    var expected = c.GetProperty("expectedIds").EnumerateArray()
                        .Select(e => e.GetString()).ToList();
                    var orderMatters = c.TryGetProperty("orderMatters", out var om) && om.GetBoolean();

                    var actual = engine.Search(criteria, user).Select(x => x.Id).ToList();

                    bool ok;
                    string detail = "";
                    if (orderMatters)
                    {
                        ok = actual.SequenceEqual(expected);
                        if (!ok)
                        {
                            var firstDiff = Enumerable.Range(0, Math.Min(actual.Count, expected.Count))
                                .FirstOrDefault(i => actual[i] != expected[i]);
                            detail = $" (esperados {expected.Count}, obtenidos {actual.Count}; " +
                                     $"1ª diferencia en [{firstDiff}]: esperado={ElementAtOr(expected, firstDiff)} obtenido={ElementAtOr(actual, firstDiff)})";
                        }
                    }
                    else
                    {
                        var exp = new HashSet<string>(expected);
                        var act = new HashSet<string>(actual);
                        ok = exp.SetEquals(act);
                        if (!ok)
                        {
                            var missing = expected.Where(id => !act.Contains(id)).Take(5).ToList();
                            var extra = actual.Where(id => !exp.Contains(id)).Take(5).ToList();
                            detail = $" (esperados {expected.Count}, obtenidos {actual.Count}; " +
                                     $"faltan: [{string.Join(",", missing)}] sobran: [{string.Join(",", extra)}])";
                        }
                    }
                    Assert(ok, "Golden " + caseName + detail);
                }
            }
        }

        private static string ElementAtOr(List<string> list, int i) => i < list.Count ? list[i] : "<fin>";

        /// <summary>
        /// Paridad del DeckRepository portado contra los 25 golden tests del deckbuilder
        /// (tests/fixtures/deckbuilder/), generados ejecutando el
        /// DeckRepository REAL de la app sobre SQLite. Se replica la secuencia exacta del
        /// driver (DeckRunner.kt): mismo orden de casos, mismos nombres "Golden N" (el string
        /// del export los incluye). Si los fixtures no están, se omite sin fallar.
        /// </summary>
        private static void RunGoldenDeckTests()
        {
            var fixturesDir = FindRepoRelative(Path.Combine("tests", "fixtures", "deckbuilder"));
            if (fixturesDir == null)
            {
                Line("SKIP: golden tests del deckbuilder (fixtures no encontrados junto al repo)");
                return;
            }

            Line("");
            Line("=== Deckbuilder: golden tests de paridad (DeckRepository portado vs app Android) ===");

            var catalog = Services.Search.CatalogStore.Load(Path.Combine(fixturesDir, "data"));
            Assert(catalog.InitialHoldingSlots.Count == 2 && catalog.InitialHoldingSlots.All(s => s.Required),
                "Golden deck: filters.json trae 2 slots de holding inicial requeridos");
            var repo = new Services.Deck.DeckRepository(catalog);

            // Ids de cartas del harness (cardsUsed del golden-tests.json)
            const string STRONGHOLD_CRAB = "Samurai395";   // The Seventh Tower
            const string STRONGHOLD_ZERO = "Tomorrow054";  // Barracks of the Damned
            const string STRONGHOLD_LION = "Samurai401";   // The Hall of Ancestors
            const string PERSONALITY_MAX3 = "TDoW012";     // Hida Akeno
            const string UNIQUE_EVENT = "P232";            // Redrawing the Maps
            const string BANNED = "ALitS117";              // A Kensai's Art
            const string BORDER_KEEP = "Emperor028";
            const string BORDER_KEEP_EXP = "Ivory402";
            const string BAMBOO = "HaT001";

            using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixturesDir, "golden-tests.json"))))
            {
                var cases = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
                    cases[c.GetProperty("name").GetString()] = c.Clone();

                JsonElement Op(string name, int i) => cases[name].GetProperty("operations")[i];

                var deckSeq = 0;
                string NewDeck(string type = "deck") => repo.CreateDeck("Golden " + deckSeq++, type);

                void AssertAdd(string caseName, int opIdx, AddCardResult actual)
                {
                    var expect = Op(caseName, opIdx).GetProperty("expect");
                    var expected = expect.GetProperty("result").GetString();
                    var ok = actual.Kind.ToString() == expected;
                    if (ok && expect.TryGetProperty("max", out var max))
                        ok = max.GetInt32() == actual.Max;
                    Assert(ok, $"Golden deck {caseName}[{opIdx}] (esperado {expected}, obtenido {actual.Kind})");
                }

                string ImportText(string caseName, int opIdx) =>
                    Op(caseName, opIdx).GetProperty("import").GetString().Replace("\\n", "\n");

                void AssertImport(string caseName, int opIdx, ImportResult actual)
                {
                    var op = Op(caseName, opIdx);
                    var ok = actual.Imported == op.GetProperty("imported").GetInt32();
                    if (ok && op.TryGetProperty("notFound", out var nf))
                        ok = actual.NotFound == nf.GetInt32();
                    if (ok && op.TryGetProperty("notFoundNames", out var names))
                        ok = actual.NotFoundNames.SequenceEqual(
                            names.EnumerateArray().Select(n => n.GetString()), StringComparer.Ordinal);
                    Assert(ok, $"Golden deck {caseName} (imported={actual.Imported}, notFound={actual.NotFound})");
                }

                // ── addCard (misma secuencia y numeración de mazos que DeckRunner.kt) ──
                { var d = NewDeck(); AssertAdd("add-banned-rechazado", 0, repo.AddCard(d, BANNED)); }
                {
                    var d = NewDeck();
                    AssertAdd("add-normal-exito", 0, repo.AddCard(d, PERSONALITY_MAX3));
                    Assert(repo.GetDeckCard(d, PERSONALITY_MAX3)?.Quantity ==
                           Op("add-normal-exito", 0).GetProperty("quantityAfter").GetInt32(),
                        "Golden deck add-normal-exito: quantityAfter");
                }
                {
                    var d = NewDeck();
                    for (int i = 0; i < 4; i++) AssertAdd("add-max-copias-deck", i, repo.AddCard(d, PERSONALITY_MAX3));
                }
                {
                    var d = NewDeck();
                    AssertAdd("add-unique-max1", 0, repo.AddCard(d, UNIQUE_EVENT));
                    AssertAdd("add-unique-max1", 1, repo.AddCard(d, UNIQUE_EVENT));
                }
                {
                    var d = NewDeck("list");
                    AssertAdd("add-lista-tope-1", 1, repo.AddCard(d, PERSONALITY_MAX3));
                    AssertAdd("add-lista-tope-1", 2, repo.AddCard(d, PERSONALITY_MAX3));
                }
                { var d = NewDeck(); AssertAdd("add-card-inexistente", 0, repo.AddCard(d, "NO_EXISTE_999")); }
                AssertAdd("add-deck-inexistente", 0, repo.AddCard("deck-inexistente-xyz", PERSONALITY_MAX3));
                { var d = NewDeck(); AssertAdd("add-holding-inicial-ok", 0, repo.AddCard(d, BORDER_KEEP)); }
                {
                    var d = NewDeck();
                    AssertAdd("add-holding-slot-ocupado", 0, repo.AddCard(d, BORDER_KEEP));
                    AssertAdd("add-holding-slot-ocupado", 1, repo.AddCard(d, BORDER_KEEP_EXP));
                }
                {
                    var d = NewDeck();
                    AssertAdd("add-holding-slots-distintos", 0, repo.AddCard(d, BORDER_KEEP));
                    AssertAdd("add-holding-slots-distintos", 1, repo.AddCard(d, BAMBOO));
                }
                {
                    var d = NewDeck();
                    AssertAdd("add-dos-strongholds", 0, repo.AddCard(d, STRONGHOLD_CRAB));
                    AssertAdd("add-dos-strongholds", 1, repo.AddCard(d, STRONGHOLD_LION));
                }

                // ── importCards ──
                { var d = NewDeck(); AssertImport("import-basico", 0, repo.ImportCards(d, ImportText("import-basico", 0))); }
                { var d = NewDeck(); AssertImport("import-clamp-max", 0, repo.ImportCards(d, ImportText("import-clamp-max", 0))); }
                { var d = NewDeck(); AssertImport("import-baneada", 0, repo.ImportCards(d, ImportText("import-baneada", 0))); }
                { var d = NewDeck(); AssertImport("import-desconocida", 0, repo.ImportCards(d, ImportText("import-desconocida", 0))); }
                { var d = NewDeck("list"); AssertImport("import-lista-tope-1", 1, repo.ImportCards(d, ImportText("import-lista-tope-1", 1))); }
                { var d = NewDeck(); AssertImport("import-holding-conflicto", 0, repo.ImportCards(d, ImportText("import-holding-conflicto", 0))); }
                {
                    var d = NewDeck();
                    AssertImport("import-notas-headers-sortmode", 0,
                        repo.ImportCards(d, ImportText("import-notas-headers-sortmode", 0)));
                    var after = Op("import-notas-headers-sortmode", 1);
                    var deck = repo.GetDeckById(d);
                    Assert((deck.Notes ?? "") == after.GetProperty("notesAfter").GetString(),
                        $"Golden deck import-notas: notes='{deck.Notes}'");
                    Assert((deck.SortMode ?? "") == after.GetProperty("sortModeAfter").GetString(),
                        $"Golden deck import-notas: sort_mode='{deck.SortMode}'");
                }
                { var d = NewDeck(); AssertImport("import-qty-implicita", 0, repo.ImportCards(d, ImportText("import-qty-implicita", 0))); }

                // ── export round-trip (string EXACTO del mazo "Golden 18") ──
                {
                    var d = NewDeck();
                    repo.AddCard(d, STRONGHOLD_CRAB);
                    repo.ImportCards(d, "3 Hida Akeno\n2 Jade Works\n2 A Brave New World");
                    var exportText = repo.ExportDeck(d);
                    var expected = Op("export-roundtrip", 0).GetProperty("exportText").GetString();
                    Assert(exportText == expected,
                        "Golden deck export-roundtrip: string exacto" +
                        (exportText == expected ? "" : $" (obtenido:\n{exportText})"));
                    var d2 = NewDeck();
                    var reimport = repo.ImportCards(d2, exportText);
                    var totals = Op("export-roundtrip", 1);
                    Assert(repo.TotalCardCount(d) == totals.GetProperty("totalOriginal").GetInt32()
                           && reimport.Imported == totals.GetProperty("reimported").GetInt32()
                           && repo.TotalCardCount(d2) == totals.GetProperty("totalReimport").GetInt32(),
                        $"Golden deck export-roundtrip: totales {repo.TotalCardCount(d)}→{repo.TotalCardCount(d2)}");
                }

                // ── conteos Dynasty/Fate (espejo de DeckDetailViewModel) ──
                {
                    var d = NewDeck();
                    repo.AddCard(d, STRONGHOLD_CRAB);
                    repo.ImportCards(d, "3 Hida Akeno\n2 Jade Works\n3 A Brave New World");
                    var op = Op("conteos-dynasty-fate", 0);
                    Assert(repo.DynastyCount(d) == op.GetProperty("dynastyCount").GetInt32()
                           && repo.FateCount(d) == op.GetProperty("fateCount").GetInt32(),
                        $"Golden deck conteos: dynasty={repo.DynastyCount(d)} fate={repo.FateCount(d)}");
                    Assert(Services.Deck.DeckRepository.SideTarget ==
                           op.GetProperty("sideTarget").GetInt32(), "Golden deck conteos: SIDE_TARGET=40");
                }

                // ── slots requeridos sin cubrir ──
                {
                    var d = NewDeck();
                    var op0 = Op("slots-requeridos-sin-cubrir", 0);
                    var expectedRequired = op0.GetProperty("requiredSlots").EnumerateArray()
                        .Select(x => x.GetString()).ToList();
                    Assert(repo.RequiredSlots.Select(s => s.SlotId).SequenceEqual(expectedRequired),
                        "Golden deck slots: requeridos = [bamboo, border]");
                    Assert(repo.UncoveredRequiredSlots(d).Select(s => s.SlotId).SequenceEqual(
                            op0.GetProperty("uncoveredEmpty").EnumerateArray().Select(x => x.GetString())),
                        "Golden deck slots: mazo vacío → ambos sin cubrir");
                    repo.AddCard(d, BORDER_KEEP);
                    Assert(repo.UncoveredRequiredSlots(d).Select(s => s.SlotId).SequenceEqual(
                            Op("slots-requeridos-sin-cubrir", 1).GetProperty("afterAddBorderKeep")
                                .EnumerateArray().Select(x => x.GetString())),
                        "Golden deck slots: tras Border Keep queda bamboo");
                }

                // ── honor inicial (espejo de GoldfishViewModel) ──
                {
                    var op = Op("stat-inicial-honor-stronghold", 0);
                    var card = repo.GetCardById(STRONGHOLD_CRAB);
                    Assert(card.StartingHonor == op.GetProperty("startingHonor").GetInt32()
                           && card.ProvinceStrength == op.GetProperty("provinceStrength").GetInt32()
                           && card.GoldProduction == op.GetProperty("goldProduction").GetInt32(),
                        "Golden deck honor: The Seventh Tower (3 / prov 8 / oro 4)");
                    var d = NewDeck();
                    repo.AddCard(d, STRONGHOLD_CRAB);
                    Assert(repo.InitialHonor(d) == op.GetProperty("honorCounterInGoldfish").GetInt32(),
                        "Golden deck honor: honorCounter = 3");
                }
                {
                    var op = Op("stat-inicial-honor-cero", 0);
                    var d = NewDeck();
                    repo.AddCard(d, STRONGHOLD_ZERO);
                    Assert(repo.GetCardById(STRONGHOLD_ZERO).StartingHonor == op.GetProperty("startingHonor").GetInt32()
                           && repo.InitialHonor(d) == op.GetProperty("honorCounterInGoldfish").GetInt32(),
                        "Golden deck honor: starting_honor=0 legítimo → 0");
                }
                {
                    var d = NewDeck();
                    repo.AddCard(d, STRONGHOLD_CRAB);
                    repo.AddCard(d, STRONGHOLD_LION);
                    var pick = Op("stat-inicial-honor-multi-stronghold", 2)
                        .GetProperty("goldfishPicksByNameOrder").GetString();
                    var honorB = Op("stat-inicial-honor-multi-stronghold", 1).GetProperty("honorB").GetInt32();
                    Assert(pick == "The Hall of Ancestors" && repo.InitialHonor(d) == honorB,
                        $"Golden deck honor multi-stronghold: gana '{pick}' → {repo.InitialHonor(d)}");
                }
            }
        }

        /// <summary>
        /// Persistencia del deckbuilder propia del launcher: formato .txt de Sun and Moon
        /// (parse/write round-trip) y DeckLibrary (un txt por mazo en la carpeta Decks +
        /// sidecar de metadata, rename/delete/activo).
        /// </summary>
        private static void RunDeckStoreTests(string tempRoot)
        {
            var fixturesDir = FindRepoRelative(Path.Combine("tests", "fixtures", "deckbuilder"));
            if (fixturesDir == null) return;

            Line("");
            Line("=== Deckbuilder: persistencia txt (formato Sun and Moon) ===");
            var catalog = Services.Search.CatalogStore.Load(Path.Combine(fixturesDir, "data"));
            var decksDir = Path.Combine(tempRoot, "saminstall", "Decks");
            Directory.CreateDirectory(decksDir);

            // Un txt como los que trae el juego: Legality + starter sin prefijo + "Nx Nombre",
            // incluyendo los tres patrones de nombre de Sun and Moon que difieren del catálogo
            // (stronghold sin sufijo "(1)", case distinto, "- exp" omitido)
            File.WriteAllText(Path.Combine(decksDir, "Golden Crab.txt"),
                "Legality: s_extended\r\n\r\nThe Seventh Tower\r\n" +
                "The Impregnable Fortress of the Crab\r\n\r\n2x Jade Works\r\n\r\n" +
                "3 Hida Akeno\r\nCarta Inexistente Zzz\r\n" +
                "Strength of my Father\r\n1x Ancestral Armor of the Mantis Clan\r\n");

            var lib = new DeckLibrary(catalog, decksDir, Path.Combine(tempRoot, "decksmeta.json"));
            var loaded = lib.Repo.Decks.FirstOrDefault(d => d.Name == "Golden Crab");
            Assert(loaded != null, "DeckTxt: carga el txt existente de la carpeta Decks");
            Assert(loaded.Legality == "s_extended", "DeckTxt: preserva el header Legality");
            Assert(lib.Repo.GetDeckCard(loaded.Id, "Samurai395")?.Quantity == 1,
                "DeckTxt: stronghold sin prefijo → 1 copia");
            Assert(lib.Repo.GetDeckCard(loaded.Id, "Ivory032")?.Quantity == 2, "DeckTxt: '2x Nombre' → 2 copias");
            Assert(lib.Repo.GetDeckCard(loaded.Id, "TDoW012")?.Quantity == 3, "DeckTxt: '3 Nombre' (formato app) → 3 copias");
            Assert(loaded.Unknown.Count == 1 && loaded.Unknown[0].Raw == "Carta Inexistente Zzz",
                "DeckTxt: línea desconocida se conserva cruda");
            Assert(lib.Repo.GetDeckCard(loaded.Id, "AMoH053")?.Quantity == 1,
                "DeckTxt: stronghold del juego sin '(1)' resuelve a la primera versión del catálogo");
            Assert(lib.Repo.GetDeckCard(loaded.Id, "TCW054")?.Quantity == 1,
                "DeckTxt: nombre con case distinto ('my'/'My') resuelve");
            Assert(lib.Repo.GetDeckCard(loaded.Id, "Ivory223")?.Quantity == 1,
                "DeckTxt: nombre sin '- exp' resuelve a la versión exp");

            // Mutación → reescritura del txt; la línea desconocida sobrevive verbatim
            lib.Repo.AddCard(loaded.Id, "P410"); // A Brave New World (fate)
            var rewritten = File.ReadAllText(Path.Combine(decksDir, "Golden Crab.txt"));
            Assert(rewritten.Contains("1x A Brave New World") && rewritten.Contains("Carta Inexistente Zzz"),
                "DeckTxt: guardado con la carta nueva y la desconocida intacta");
            Assert(rewritten.Contains("Legality: s_extended") &&
                   rewritten.Contains("The Seventh Tower\r\n") && !rewritten.Contains("1x The Seventh Tower"),
                "DeckTxt: Legality + starter sin prefijo al reescribir");
            // Los nombres que el juego escribe distinto al catálogo se reescriben IGUAL que
            // estaban (para que Sun and Moon siga reconociendo su mazo)
            Assert(rewritten.Contains("The Impregnable Fortress of the Crab\r\n") && !rewritten.Contains("(1)"),
                "DeckTxt: el stronghold se reescribe sin sufijo '(1)'");
            Assert(rewritten.Contains("Strength of my Father") && !rewritten.Contains("Strength of My Father"),
                "DeckTxt: se reescribe el case original del juego");
            Assert(rewritten.Contains("1x Ancestral Armor of the Mantis Clan\r\n") && !rewritten.Contains("Mantis Clan - exp"),
                "DeckTxt: se reescribe sin '- exp' como estaba");

            // Round-trip completo por una segunda instancia (relee el disco)
            var lib2 = new DeckLibrary(catalog, decksDir, Path.Combine(tempRoot, "decksmeta.json"));
            var again = lib2.Repo.Decks.FirstOrDefault(d => d.Name == "Golden Crab");
            Assert(again != null
                   && lib2.Repo.GetDeckCard(again.Id, "Ivory032")?.Quantity == 2
                   && lib2.Repo.GetDeckCard(again.Id, "P410")?.Quantity == 1
                   && again.Unknown.Count == 1,
                "DeckTxt: round-trip por disco conserva cantidades y desconocidas");

            // Crear (activa) / renombrar (renombra el txt) / borrar (borra el txt) / activo persiste
            var newId = lib2.CreateDeck("Mi Mazo: ¿nuevo?"); // caracteres inválidos de archivo
            var deck = lib2.Repo.GetDeckById(newId);
            Assert(lib2.ActiveDeckId == newId, "DeckLibrary: el mazo nuevo queda activo");
            lib2.Repo.ImportCards(newId, "2 Jade Works");
            var newFile = Path.Combine(decksDir, deck.Name + ".txt");
            Assert(File.Exists(newFile) && deck.Name.IndexOf(':') < 0 && deck.Name.IndexOf('?') < 0,
                $"DeckLibrary: txt creado con nombre saneado ('{deck.Name}')");
            lib2.RenameDeck(newId, "Renombrado");
            Assert(!File.Exists(newFile) && File.Exists(Path.Combine(decksDir, "Renombrado.txt")),
                "DeckLibrary: renombrar mueve el txt");
            var lib3 = new DeckLibrary(catalog, decksDir, Path.Combine(tempRoot, "decksmeta.json"));
            Assert(lib3.ActiveDeck?.Name == "Renombrado", "DeckLibrary: el mazo activo persiste en el sidecar");
            lib3.DeleteDeck(lib3.ActiveDeckId);
            Assert(!File.Exists(Path.Combine(decksDir, "Renombrado.txt")) && lib3.ActiveDeckId == null,
                "DeckLibrary: borrar elimina el txt y limpia el activo");

            // Nombres duplicados → sufijo
            var dupA = lib3.CreateDeck("Duplicado");
            var dupB = lib3.CreateDeck("Duplicado");
            Assert(lib3.Repo.GetDeckById(dupB).Name == "Duplicado (2)", "DeckLibrary: nombre duplicado → ' (2)'");

            // Duplicar mazo (extra del launcher): copia composición
            lib3.Repo.ImportCards(dupA, "2 Jade Works\nThe Seventh Tower");
            var copyId = lib3.DuplicateDeck(dupA);
            Assert(copyId != null && lib3.Repo.TotalCardCount(copyId) == lib3.Repo.TotalCardCount(dupA),
                "DeckLibrary: duplicar copia la composición (export→import)");
        }

        /// <summary>
        /// Prefijo "Proxy &lt;tipo&gt;: " y edición entre paréntesis del .txt de Sun and Moon
        /// (ADR-060). El launcher los lee para entender la línea y los reescribe igual —no hace
        /// nada más con el dato— y marca los starter holdings con "Proxy holding: " al escribir.
        /// El formato está en docs/technical/contratos/formato-txt-sun-and-moon.md.
        /// </summary>
        private static void RunDeckProxyTests(string tempRoot)
        {
            var fixturesDir = FindRepoRelative(Path.Combine("tests", "fixtures", "deckbuilder"));
            if (fixturesDir == null) return;

            Line("");
            Line("=== Proxies y ediciones del .txt de Sun and Moon ===");
            var catalog = Services.Search.CatalogStore.Load(Path.Combine(fixturesDir, "data"));
            var root = Path.Combine(tempRoot, "proxyinstall");
            var decksDir = Path.Combine(root, "Decks");
            var listsDir = Path.Combine(root, "Lists");
            var metaPath = Path.Combine(tempRoot, "proxymeta.json");
            Directory.CreateDirectory(decksDir);
            Directory.CreateDirectory(listsDir);

            File.WriteAllText(Path.Combine(decksDir, "Proxies.txt"),
                "Legality: s_extended\r\n\r\n" +
                // "- exp2" es lo que escriben los mazos reales del juego: el slot inicial acepta
                // todas las versiones (filters.json → eligibleCards), no solo el nombre pelado
                "The Seventh Tower\r\nBorder Keep - exp2\r\nBamboo Harvesters\r\n\r\n" +
                "2x Jade Works\r\n" +
                "Proxy Holding: Jade Works\r\n" +        // colisiona con la línea de arriba
                "Proxy holding: Productive Mine\r\n" +   // la carta aparece SOLO como proxy
                "1x Copper Mine (Ivory)\r\n" +           // edición entre paréntesis
                "Proxy wind: Seat of Power\r\n");        // "wind" no es un tipo de proxy válido

            var lib = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var deck = lib.Repo.Decks.First(d => d.Name == "Proxies");

            Assert(lib.Repo.GetDeckCard(deck.Id, "CoM013")?.Quantity == 1
                   && lib.Repo.GetDeckCard(deck.Id, "HaT001")?.Quantity == 1,
                "Proxy: los starter holdings del txt cargan como cartas del mazo");
            var proxyOnly = lib.Repo.GetDeckCard(deck.Id, "GoC012");
            Assert(proxyOnly != null && proxyOnly.ProxyType == "holding",
                "Proxy: 'Proxy holding: X' resuelve la carta y conserva el tipo");
            var withEdition = lib.Repo.GetDeckCard(deck.Id, "Ivory014");
            Assert(withEdition != null && withEdition.Edition == "Ivory",
                "Proxy: la edición entre paréntesis se separa del nombre");
            Assert(lib.Repo.GetDeckCard(deck.Id, "Ivory032")?.Quantity == 2
                   && deck.Unknown.Any(u => u.Raw == "Proxy Holding: Jade Works"),
                "Proxy: la misma carta con y sin prefijo NO se fusiona (la segunda queda cruda)");
            Assert(lib.Repo.GetDeckCard(deck.Id, "Samurai039") == null
                   && deck.Unknown.Any(u => u.Raw == "Proxy wind: Seat of Power"),
                "Proxy: un tipo fuera de los doce no es prefijo, la línea queda cruda");

            // Reescritura: los starter holdings salen marcados, el stronghold no
            lib.Repo.AddCard(deck.Id, "TDoW012"); // Hida Akeno: fuerza el guardado
            var txt = File.ReadAllText(Path.Combine(decksDir, "Proxies.txt"));
            Assert(txt.Contains("Proxy holding: Border Keep - exp2\r\n")
                   && txt.Contains("Proxy holding: Bamboo Harvesters\r\n"),
                "Proxy: al escribir, los starter holdings llevan 'Proxy holding: '");
            Assert(txt.Contains("The Seventh Tower\r\n") && !txt.Contains("Proxy holding: The Seventh Tower"),
                "Proxy: el stronghold se escribe sin prefijo");
            Assert(txt.Contains("Proxy holding: 1x Productive Mine"),
                "Proxy: el prefijo que traía el archivo se reescribe igual");
            Assert(txt.Contains("1x Copper Mine (Ivory)"),
                "Proxy: la edición se reescribe entre paréntesis");
            Assert(txt.Contains("2x Jade Works\r\n") && txt.Contains("Proxy Holding: Jade Works"),
                "Proxy: la línea cruda que colisionaba sobrevive verbatim");

            var lib2 = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var again = lib2.Repo.Decks.First(d => d.Name == "Proxies");
            Assert(lib2.Repo.GetDeckCard(again.Id, "CoM013")?.ProxyType == "holding"
                   && lib2.Repo.GetDeckCard(again.Id, "Ivory014")?.Edition == "Ivory",
                "Proxy: round-trip por disco conserva prefijo y edición");

            // Las listas no las lee el juego: ahí la marca no tiene sentido y no se escribe
            var listId = lib2.CreateList("Material");
            lib2.Repo.SetInList(listId, "CoM013", true);
            var listTxt = File.ReadAllText(Path.Combine(listsDir, "Material.txt"));
            Assert(listTxt.Contains("Border Keep") && !listTxt.Contains("Proxy holding:"),
                "Proxy: una lista se escribe sin el prefijo (el juego no lee Lists)");

            // Portapapeles: los marcadores son OPT-IN. Sin ellos, ExportDeck sigue siendo el
            // string que fija el golden y la mitad de DuplicateDeck (ImportCards hace match
            // exacto de nombre y un prefijo se le comería la carta).
            var plain = lib2.Repo.ExportDeck(again.Id);
            Assert(plain.Contains("Border Keep - exp2") && !plain.Contains("Proxy "),
                "Proxy: el export sin marcadores queda igual (es el que usa duplicar mazo)");
            var marked = lib2.Repo.ExportDeck(again.Id, withProxyMarkers: true);
            Assert(marked.Contains("Proxy holding: Border Keep - exp2")
                   && marked.Contains("Proxy holding: Bamboo Harvesters"),
                "Proxy: el export al portapapeles marca los starter holdings");
            Assert(marked.Contains("Proxy holding: Productive Mine"),
                "Proxy: el portapapeles también lleva el prefijo que traía el archivo");

            var pasted = lib2.CreateDeck("Pegado");
            lib2.Repo.ImportCards(pasted, SunAndMoonDeckFile.NormalizeForImport(marked));
            Assert(lib2.Repo.GetDeckCard(pasted, "CoM013") != null
                   && lib2.Repo.GetDeckCard(pasted, "GoC012") != null,
                "Proxy: pegar de vuelta entra limpio (el prefijo se saca en el borde)");

            var dup = lib2.DuplicateDeck(again.Id);
            Assert(dup != null && lib2.Repo.GetDeckCard(dup, "CoM013") != null,
                "Proxy: duplicar un mazo con starter holdings no pierde cartas");

            // Pegar un .txt del juego: el import de la app solo entiende "N Nombre", así que el
            // borde traduce el "Nx". Sin esto rebotaban TODAS las líneas con cantidad.
            var normalized = SunAndMoonDeckFile.NormalizeForImport(
                "Legality: s_extended\r\n\r\nThe Seventh Tower\r\n" +
                "Proxy holding: Bamboo Harvesters\r\n2x Jade Works\r\n" +
                "# Dynasty - (2) [1]\r\n3 Hida Akeno\r\n");
            Assert(normalized.Contains("\n2 Jade Works") && !normalized.Contains("2x"),
                "Import: 'Nx Nombre' se traduce a 'N Nombre' en el borde");
            Assert(normalized.Contains("\n3 Hida Akeno") && normalized.Contains("# Dynasty - (2) [1]"),
                "Import: 'N Nombre' y las cabeceras '#' quedan intactas");

            var fromGame = lib2.CreateDeck("Del juego");
            var imported = lib2.Repo.ImportCards(fromGame, normalized);
            Assert(lib2.Repo.GetDeckCard(fromGame, "Ivory032")?.Quantity == 2
                   && lib2.Repo.GetDeckCard(fromGame, "TDoW012")?.Quantity == 3
                   && lib2.Repo.GetDeckCard(fromGame, "HaT001") != null
                   && lib2.Repo.GetDeckCard(fromGame, "Samurai395") != null,
                "Import: un .txt del juego pegado entra completo (cantidades, starter y proxy)");
            Assert(imported.NotFoundNames.Count == 1 && imported.NotFoundNames[0] == "Legality: s_extended",
                "Import: del .txt del juego solo rebota el header Legality (no es una carta)");
        }

        /// <summary>
        /// Conjuntos de cartas: las listas como .txt de la carpeta Lists (ADR-045) y la
        /// identidad persistida por NOMBRE de carta, no por id (ADR-044). Cubre lo que dejó de
        /// vivir en usercards.json y lo que ese archivo sigue guardando.
        /// </summary>
        private static void RunCardSetTests(string tempRoot)
        {
            var fixturesDir = FindRepoRelative(Path.Combine("tests", "fixtures", "deckbuilder"));
            if (fixturesDir == null) return;

            Line("");
            Line("=== Conjuntos de cartas: listas en txt + identidad por nombre ===");
            var dataDir = Path.Combine(fixturesDir, "data");
            var catalog = Services.Search.CatalogStore.Load(dataDir);
            var root = Path.Combine(tempRoot, "setsinstall");
            var decksDir = Path.Combine(root, "Decks");
            var listsDir = Path.Combine(root, "Lists");
            var metaPath = Path.Combine(tempRoot, "setsmeta.json");
            Directory.CreateDirectory(decksDir);

            var lib = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var listId = lib.CreateList("Ninjas");
            lib.Repo.SetInList(listId, "Ivory032", true); // Jade Works

            Assert(File.Exists(Path.Combine(listsDir, "Ninjas.txt")) &&
                   !File.Exists(Path.Combine(decksDir, "Ninjas.txt")),
                "Listas: el txt se escribe en Lists, nunca en Decks");
            Assert(File.ReadAllText(Path.Combine(listsDir, "Ninjas.txt")).Contains("Jade Works"),
                "Listas: el txt guarda el NOMBRE de la carta");

            // Tope 1: el segundo SetInList no acumula
            lib.Repo.SetInList(listId, "Ivory032", true);
            Assert(lib.Repo.GetDeckCard(listId, "Ivory032")?.Quantity == 1, "Listas: tope de 1 copia por carta");

            // SetInList NO es AddCard: una lista es material de armado, no un mazo legal
            Assert(lib.Repo.AddCard(listId, "ALitS117").Kind == AddCardResultKind.Banned,
                "Listas: AddCard sigue rechazando baneadas (semántica portada intacta)");
            Assert(lib.Repo.SetInList(listId, "ALitS117", true) &&
                   lib.Repo.GetDeckCard(listId, "ALitS117") != null,
                "Listas: SetInList acepta una baneada");
            lib.Repo.SetInList(listId, "HaT001", true);   // Bamboo Harvesters   } mismo slot
            lib.Repo.SetInList(listId, "Ivory401", true); // Bamboo Harvesters - exp
            Assert(lib.Repo.GetDeckCard(listId, "HaT001") != null &&
                   lib.Repo.GetDeckCard(listId, "Ivory401") != null,
                "Listas: SetInList acepta dos cartas del mismo slot inicial");
            Assert(lib.Repo.AddCard(listId, "Emperor028").Kind == AddCardResultKind.SlotAlreadyCovered ||
                   lib.Repo.AddCard(listId, "Ivory401").Kind == AddCardResultKind.SlotAlreadyCovered,
                "Listas: AddCard sigue aplicando el slot cubierto (semántica portada intacta)");

            // Un mazo y una lista pueden llamarse igual: son carpetas distintas
            var deckId = lib.CreateDeck("Ninjas");
            Assert(lib.Repo.GetDeckById(deckId).Name == "Ninjas" &&
                   File.Exists(Path.Combine(decksDir, "Ninjas.txt")),
                "Listas: un mazo y una lista pueden compartir nombre");

            // Round-trip por disco: tipo, tope y lista activa
            lib.ActiveListId = listId;
            var lib2 = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var reloaded = lib2.Lists.FirstOrDefault(d => d.Name == "Ninjas");
            Assert(reloaded != null && reloaded.Type == DeckLibrary.ListType &&
                   reloaded.Cards.Count(c => c.Quantity == 1) == reloaded.Cards.Count,
                "Listas: round-trip por disco (tipo lista, todo en 1 copia)");
            Assert(lib2.ActiveList?.Name == "Ninjas", "Listas: la lista activa persiste en el sidecar");
            Assert(lib2.Decks.Count() == 1 && lib2.Lists.Count() == 1,
                "Listas: las dos carpetas se cargan separadas");

            // Un txt de lista escrito a mano con cantidades se recorta a 1 al cargar
            File.WriteAllText(Path.Combine(listsDir, "A mano.txt"), "3x Jade Works\r\n");
            var lib3 = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var manual = lib3.Lists.FirstOrDefault(d => d.Name == "A mano");
            Assert(manual != null && manual.Cards.Single().Quantity == 1,
                "Listas: un txt con '3x' se recorta a 1 al cargar");

            // Duplicar una lista no puede pasar por ImportCards (se comería las del mismo slot)
            var dupId = lib3.DuplicateDeck(lib3.Lists.First(d => d.Name == "Ninjas").Id);
            Assert(dupId != null && lib3.Repo.GetDeckCard(dupId, "HaT001") != null &&
                   lib3.Repo.GetDeckCard(dupId, "Ivory401") != null &&
                   lib3.Repo.GetDeckCard(dupId, "ALitS117") != null,
                "Listas: duplicar conserva las del mismo slot y las baneadas");

            // Borrar la lista borra su txt y no toca el mazo homónimo
            lib3.DeleteDeck(lib3.Lists.First(d => d.Name == "Ninjas").Id);
            Assert(!File.Exists(Path.Combine(listsDir, "Ninjas.txt")) &&
                   File.Exists(Path.Combine(decksDir, "Ninjas.txt")),
                "Listas: borrar la lista no toca el mazo del mismo nombre");

            // ---- usercards.json: nombres, no ids (ADR-044) --------------------------------
            var ucPath = AppConstants.UserCardsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(ucPath));

            // v1: ids + listas adentro → se migra
            File.WriteAllText(ucPath,
                "{\"favorites\":[\"Ivory032\",\"NoExisteZZZ\"],\"hidden\":[\"Samurai395\"]," +
                "\"lists\":{\"Vieja\":[\"Ivory032\",\"TDoW012\"]}}");
            var libM = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var store = Services.Search.UserCardDataStore.Load(libM);
            Assert(store.Data.Favorites.Contains("Ivory032") && store.Data.Favorites.Count == 1,
                "usercards: migración v1 traduce los ids que existen y descarta los rotos");
            Assert(store.Data.Hidden.Contains("Samurai395"), "usercards: migración v1 conserva las ocultas");
            Assert(File.Exists(Path.Combine(Path.GetDirectoryName(ucPath), "usercards.v1.json")),
                "usercards: la migración respalda el archivo viejo");
            var migrated = libM.Lists.FirstOrDefault(d => d.Name == "Vieja");
            Assert(migrated != null && migrated.Cards.Count == 2,
                "usercards: las listas del v1 pasan a .txt de la carpeta Lists");

            var saved = File.ReadAllText(ucPath);
            Assert(saved.Contains("Jade Works") && !saved.Contains("Ivory032"),
                "usercards: se guardan NOMBRES de carta, no ids (ADR-044)");
            Assert(!saved.Contains("\"lists\""), "usercards: ya no guarda listas");

            // La prueba de fuego de ADR-044: los ids del catálogo cambian y las favoritas siguen
            // apuntando a la misma carta (es lo que pasa cuando entra una base nueva).
            var shuffled = Services.Search.CatalogStore.Load(dataDir);
            foreach (var card in shuffled.Cards) card.Id = "NEW-" + card.Id;
            var libS = new DeckLibrary(shuffled, decksDir, metaPath, listsDir);
            var storeS = Services.Search.UserCardDataStore.Load(libS);
            Assert(storeS.Data.Favorites.Contains("NEW-Ivory032"),
                "usercards: las favoritas sobreviven a un cambio de ids del catálogo");
        }

        /// <summary>
        /// Recorte (SPEC-DECK-CRUD §4): bajar una carta a 0 la conserva en el mazo en vez de
        /// borrarla. El motor ya lo tenía (`keepRowAtZero`); lo que se verifica acá es la mitad
        /// que la app resolvía con SQLite y el launcher no tenía: dónde se guarda. Va al sidecar
        /// y **no** al .txt, porque ese archivo lo lee el juego (ADR-046).
        /// </summary>
        private static void RunTrimTests(string tempRoot)
        {
            var fixturesDir = FindRepoRelative(Path.Combine("tests", "fixtures", "deckbuilder"));
            if (fixturesDir == null) return;

            Line("");
            Line("=== Recorte de cartas (quantity=0 persiste) ===");
            var catalog = Services.Search.CatalogStore.Load(Path.Combine(fixturesDir, "data"));
            var root = Path.Combine(tempRoot, "triminstall");
            var decksDir = Path.Combine(root, "Decks");
            var listsDir = Path.Combine(root, "Lists");
            var metaPath = Path.Combine(tempRoot, "trimmeta.json");
            Directory.CreateDirectory(decksDir);

            var lib = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var deckId = lib.CreateDeck("Recortes");
            lib.Repo.ImportCards(deckId, "2 Jade Works\n1 Hida Akeno");

            // −  sobre la última copia: la fila persiste con quantity 0
            lib.Repo.RemoveCard(deckId, "TDoW012", keepRowAtZero: true); // Hida Akeno, tenía 1
            var trimmedEntry = lib.Repo.GetDeckCard(deckId, "TDoW012");
            Assert(trimmedEntry != null && trimmedEntry.Quantity == 0,
                "Recorte: bajar a 0 conserva la entrada en el mazo");
            Assert(lib.Repo.TotalCardCount(deckId) == 2, "Recorte: la recortada no cuenta en el total");
            Assert(!lib.Repo.ExportDeck(deckId).Contains("Hida Akeno"),
                "Recorte: la recortada no sale en el export");

            // El .txt es del juego: no puede aparecer ahí de ninguna forma
            var txt = File.ReadAllText(Path.Combine(decksDir, "Recortes.txt"));
            Assert(!txt.Contains("Hida Akeno"), "Recorte: la recortada NO se escribe en el .txt del juego");
            Assert(txt.Contains("2x Jade Works"), "Recorte: el resto del mazo se escribe igual");

            // …pero sobrevive al reinicio, vía el sidecar
            var meta = File.ReadAllText(metaPath);
            Assert(meta.Contains("\"trimmed\"") && meta.Contains("Hida Akeno"),
                "Recorte: se guarda en el sidecar, por nombre de carta");
            var lib2 = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var reloaded = lib2.Repo.Decks.First(d => d.Name == "Recortes");
            Assert(lib2.Repo.GetDeckCard(reloaded.Id, "TDoW012")?.Quantity == 0,
                "Recorte: vuelve como recortada al recargar del disco");
            Assert(lib2.Repo.TotalCardCount(reloaded.Id) == 2,
                "Recorte: sigue sin contar después de recargar");

            // El + la rescata conservando su lugar
            var orderBefore = lib2.Repo.GetDeckCard(reloaded.Id, "TDoW012").SortOrder;
            Assert(lib2.Repo.AddCard(reloaded.Id, "TDoW012").Kind == AddCardResultKind.Success,
                "Recorte: el + rescata una recortada");
            var rescued = lib2.Repo.GetDeckCard(reloaded.Id, "TDoW012");
            Assert(rescued.Quantity == 1 && rescued.SortOrder == orderBefore,
                "Recorte: al rescatarla conserva su sortOrder");

            // Limpiar recortadas
            lib2.Repo.RemoveCard(reloaded.Id, "TDoW012", keepRowAtZero: true);
            lib2.Repo.RemoveAllTrimmed(reloaded.Id);
            Assert(lib2.Repo.GetDeckCard(reloaded.Id, "TDoW012") == null,
                "Recorte: 'limpiar recortadas' saca la entrada del todo");
            Assert(!File.ReadAllText(metaPath).Contains("Hida Akeno"),
                "Recorte: al limpiarlas desaparecen del sidecar");

            // El .txt manda: si alguien puso la carta a mano con cantidad, la marca de recorte
            // del sidecar se ignora (lo que se juega es lo que dice el archivo del juego)
            lib2.Repo.RemoveCard(reloaded.Id, "Ivory032", keepRowAtZero: true);
            lib2.Repo.RemoveCard(reloaded.Id, "Ivory032", keepRowAtZero: true); // 2 → 1 → 0
            File.WriteAllText(Path.Combine(decksDir, "Recortes.txt"), "3x Jade Works\r\n");
            var lib3 = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var conflict = lib3.Repo.Decks.First(d => d.Name == "Recortes");
            Assert(lib3.Repo.GetDeckCard(conflict.Id, "Ivory032")?.Quantity == 3,
                "Recorte: el .txt manda sobre la marca de recorte del sidecar");

            // Las listas no tienen recorte: bajar a 0 ahí es simplemente sacar la carta
            var listId = lib3.CreateList("Sin recortes");
            lib3.Repo.SetInList(listId, "Ivory032", true);
            lib3.Repo.RemoveCard(listId, "Ivory032", keepRowAtZero: true);
            var reloadedList = new DeckLibrary(catalog, decksDir, metaPath, listsDir)
                .Lists.First(d => d.Name == "Sin recortes");
            Assert(reloadedList.Cards.Count == 0, "Recorte: una lista nunca guarda recortadas");
        }

        /// <summary>
        /// "Mazo terminado" (`IsDeckComplete`): extra del launcher, no existe en la app, así que
        /// no hay golden que lo cubra. Criterio: los dos slots requeridos cubiertos (Border Keep y
        /// Bamboo Harvesters) + stronghold + cada lado entre 40 y 50.
        /// </summary>
        private static void RunDeckStateTests(string tempRoot)
        {
            var fixturesDir = FindRepoRelative(Path.Combine("tests", "fixtures", "deckbuilder"));
            if (fixturesDir == null) return;

            Line("");
            Line("=== Estado de armado del mazo (terminado / pendiente) ===");
            var catalog = Services.Search.CatalogStore.Load(Path.Combine(fixturesDir, "data"));
            var decksDir = Path.Combine(tempRoot, "stateinstall", "Decks");
            Directory.CreateDirectory(decksDir);
            var lib = new DeckLibrary(catalog, decksDir, Path.Combine(tempRoot, "statemeta.json"));
            var repo = lib.Repo;

            var deckId = lib.CreateDeck("Estado");
            Assert(!repo.IsDeckComplete(deckId), "Estado: un mazo vacío está pendiente");

            repo.SetCardQuantity(deckId, "Samurai395", 1);  // The Seventh Tower (stronghold)
            repo.SetCardQuantity(deckId, "HaT001", 1);      // Bamboo Harvesters (slot bamboo)
            repo.SetCardQuantity(deckId, "Emperor028", 1);  // Border Keep (slot border)
            Assert(repo.UncoveredRequiredSlots(deckId).Count == 0,
                "Estado: Bamboo + Border cubren los dos slots requeridos");
            Assert(!repo.IsDeckComplete(deckId), "Estado: con slots y stronghold pero sin cartas, pendiente");

            FillSide(repo, catalog, deckId, "dynasty", 40);
            Assert(!repo.IsDeckComplete(deckId), "Estado: con un solo lado en rango sigue pendiente");
            FillSide(repo, catalog, deckId, "fate", 40);
            Assert(repo.DynastyCount(deckId) >= 40 && repo.FateCount(deckId) >= 40,
                "Estado: los dos lados llegaron a 40");
            Assert(repo.IsDeckComplete(deckId), "Estado: slots + stronghold + 40/40 → terminado");

            // Sacar el stronghold lo devuelve a pendiente (aunque los conteos sigan bien)
            repo.SetCardQuantity(deckId, "Samurai395", 0);
            Assert(!repo.IsDeckComplete(deckId), "Estado: sin stronghold vuelve a pendiente");
            repo.SetCardQuantity(deckId, "Samurai395", 1);

            // Recortar el Border Keep descubre su slot → pendiente (una recortada no cubre nada)
            repo.RemoveCard(deckId, "Emperor028", keepRowAtZero: true);
            Assert(!repo.IsDeckComplete(deckId), "Estado: un slot cubierto por una recortada no cuenta");
            repo.SetCardQuantity(deckId, "Emperor028", 1);

            // Pasarse de 50 en un lado también lo saca del rango
            FillSide(repo, catalog, deckId, "dynasty", 51);
            Assert(repo.DynastyCount(deckId) > DeckRepository.SideMax && !repo.IsDeckComplete(deckId),
                "Estado: pasarse de 50 en un lado lo devuelve a pendiente");

            // Una lista nunca está terminada
            var listId = lib.CreateList("Lista estado");
            Assert(!repo.IsDeckComplete(listId), "Estado: una lista nunca cuenta como terminada");
        }

        /// <summary>
        /// Etiquetas de las cartas de un mazo (ADR-057): extra del launcher, así que no hay golden
        /// que las cubra. Lo que se verifica es lo que no atrapa el compilador: que van al sidecar
        /// **por nombre de carta** y nunca al .txt del juego, y que agrupar por combinación es una
        /// partición (cada carta en una sola pila, y el cruce con entidad propia).
        /// </summary>
        private static void RunDeckTagTests(string tempRoot)
        {
            var fixturesDir = FindRepoRelative(Path.Combine("tests", "fixtures", "deckbuilder"));
            if (fixturesDir == null) return;

            Line("");
            Line("=== Etiquetas de las cartas del mazo ===");
            var dataDir = Path.Combine(fixturesDir, "data");
            var catalog = Services.Search.CatalogStore.Load(dataDir);
            var root = Path.Combine(tempRoot, "taginstall");
            var decksDir = Path.Combine(root, "Decks");
            var listsDir = Path.Combine(root, "Lists");
            var metaPath = Path.Combine(tempRoot, "tagmeta.json");
            Directory.CreateDirectory(decksDir);

            var lib = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var deckId = lib.CreateDeck("Deshonor");
            lib.Repo.SetCardQuantity(deckId, "Ivory032", 2);    // Jade Works
            lib.Repo.SetCardQuantity(deckId, "TDoW012", 1);     // Hida Akeno
            lib.Repo.SetCardQuantity(deckId, "Samurai395", 1);  // The Seventh Tower
            var deck = lib.Repo.GetDeckById(deckId);

            DeckTags.Add(deck, "Deshonor");
            DeckTags.Add(deck, "Magistrados");
            DeckTags.Add(deck, "Sin uso"); // etiqueta creada y todavía sin cartas
            DeckTags.Set(deck, "Ivory032", "Deshonor", true);
            DeckTags.Set(deck, "TDoW012", "Deshonor", true);
            DeckTags.Set(deck, "TDoW012", "Magistrados", true);
            lib.SaveMeta();

            Assert(DeckTags.Add(deck, "deshonor") == DeckTags.Find(deck, "Deshonor") && deck.Tags.Count == 3,
                "Etiquetas: crear una que ya existe devuelve la misma (sin distinguir mayúsculas)");
            Assert(DeckTags.CardCount(deck, "Deshonor") == 2 && DeckTags.CardCount(deck, "Sin uso") == 0,
                "Etiquetas: el conteo por etiqueta cuenta cartas distintas del mazo");

            // Color: sale uno de la paleta sin preguntar y después se cambia, pero solo por otro de
            // la paleta (un color libre puede quedar ilegible como punto de 8 px sobre el arte)
            Assert(DeckTags.Palette.Contains(DeckTags.Find(deck, "Deshonor").Color) &&
                   DeckTags.Find(deck, "Deshonor").Color != DeckTags.Find(deck, "Magistrados").Color,
                "Etiquetas: el color por defecto sale de la paleta y dos seguidas no repiten");
            Assert(DeckTags.SetColor(deck, "Deshonor", DeckTags.Palette[4]) &&
                   DeckTags.Find(deck, "Deshonor").Color == DeckTags.Palette[4],
                "Etiquetas: se le puede cambiar el color");
            Assert(!DeckTags.SetColor(deck, "Deshonor", "#123456") &&
                   DeckTags.Find(deck, "Deshonor").Color == DeckTags.Palette[4],
                "Etiquetas: un color fuera de la paleta se rechaza sin tocar el que había");
            lib.SaveMeta(); // el color elegido tiene que llegar al disco antes de recargar

            // El .txt es del juego: las etiquetas no pueden asomar ahí de ninguna forma
            var txt = File.ReadAllText(Path.Combine(decksDir, "Deshonor.txt"));
            Assert(!txt.Contains("Magistrados") && !txt.Contains("Sin uso"),
                "Etiquetas: NO se escriben en el .txt del juego");
            var meta = File.ReadAllText(metaPath);
            Assert(meta.Contains("\"cardTags\"") && meta.Contains("Jade Works") && !meta.Contains("Ivory032"),
                "Etiquetas: van al sidecar por NOMBRE de carta, nunca por id (ADR-044)");

            // Round-trip por disco
            var lib2 = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var deck2 = lib2.Repo.Decks.First(d => d.Name == "Deshonor");
            Assert(deck2.Tags.Count == 3 && DeckTags.Find(deck2, "Sin uso") != null,
                "Etiquetas: una etiqueta sin cartas también sobrevive al reinicio");
            Assert(DeckTags.Find(deck2, "Deshonor").Color == DeckTags.Palette[4],
                "Etiquetas: el color elegido vuelve del sidecar");
            Assert(DeckTags.Has(deck2, "TDoW012", "Deshonor") && DeckTags.Has(deck2, "TDoW012", "Magistrados"),
                "Etiquetas: las asignaciones vuelven del sidecar");
            Assert(DeckTags.Of(deck2, "TDoW012").SequenceEqual(new[] { "Deshonor", "Magistrados" }),
                "Etiquetas: salen en el orden del vocabulario, no en el de asignación");

            // Agrupar = una pila por etiqueta, y la carta compartida está en TODAS las suyas: al
            // armar, la pila "Deshonor" tiene que mostrar todo lo que deshonra, incluida la carta
            // que además pide magistrados.
            var sections = DeckTags.Sections(deck2, deck2.Cards, e => e.CardId);
            Assert(sections.Count == 3, "Etiquetas: una sección por etiqueta con cartas, más las sin etiquetar");
            Assert(sections[0].Tag == "Deshonor" && sections[1].Tag == "Magistrados" &&
                   sections[2].Tag == null,
                "Etiquetas: las pilas van en el orden del vocabulario y sin etiquetar al final");
            Assert(sections[0].Items.Select(e => e.CardId).OrderBy(x => x, StringComparer.Ordinal)
                       .SequenceEqual(new[] { "Ivory032", "TDoW012" }.OrderBy(x => x, StringComparer.Ordinal)) &&
                   sections[1].Items.Single().CardId == "TDoW012",
                "Etiquetas: la carta de dos etiquetas aparece en LAS DOS pilas");
            Assert(sections[0].Shared == 1 && sections[1].Shared == 1 && sections[2].Shared == 0,
                "Etiquetas: cada pila dice cuántas de sus cartas están también en otra");
            Assert(sections.Sum(s => s.Items.Count) > deck2.Cards.Count,
                "Etiquetas: los conteos por pila NO suman el mazo (se solapan, no son cajas)");

            // Filtro: unión vs cruce
            var both = new[] { "Deshonor", "Magistrados" };
            Assert(DeckTags.Matches(deck2, "Ivory032", both, all: false) &&
                   !DeckTags.Matches(deck2, "Ivory032", both, all: true),
                "Etiquetas: 'alguna' incluye la carta de una sola pila; 'todas' la deja afuera");
            Assert(DeckTags.Matches(deck2, "Samurai395", new string[0], all: false),
                "Etiquetas: sin nada marcado pasan todas");

            // Renombrar arrastra las asignaciones; el nombre repetido se rechaza
            Assert(DeckTags.Rename(deck2, "Deshonor", "Deshonor total"), "Etiquetas: renombrar");
            Assert(DeckTags.Has(deck2, "TDoW012", "Deshonor total") &&
                   !DeckTags.Has(deck2, "TDoW012", "Deshonor"),
                "Etiquetas: renombrar arrastra las cartas que la tenían");
            Assert(!DeckTags.Rename(deck2, "Magistrados", "Deshonor total"),
                "Etiquetas: no se puede renombrar a un nombre ya usado");

            // Borrar la etiqueta la saca de las cartas, sin tocar el mazo
            DeckTags.Delete(deck2, "Magistrados");
            Assert(!DeckTags.Has(deck2, "TDoW012", "Magistrados") &&
                   lib2.Repo.GetDeckCard(deck2.Id, "TDoW012") != null,
                "Etiquetas: borrar una etiqueta no saca la carta del mazo");

            // Una recortada sigue clasificada (está en el mazo, no se juega)
            lib2.Repo.RemoveCard(deck2.Id, "Ivory032", keepRowAtZero: true);
            lib2.Repo.RemoveCard(deck2.Id, "Ivory032", keepRowAtZero: true); // 2 → 1 → 0
            var lib3 = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var deck3 = lib3.Repo.Decks.First(d => d.Name == "Deshonor");
            Assert(lib3.Repo.GetDeckCard(deck3.Id, "Ivory032")?.Quantity == 0 &&
                   DeckTags.Has(deck3, "Ivory032", "Deshonor total"),
                "Etiquetas: una carta recortada conserva las suyas");

            // Sacarla del todo se lleva su clasificación (levantar la carta de la mesa)
            lib3.Repo.DeleteCardEntry(deck3.Id, "Ivory032");
            Assert(!File.ReadAllText(metaPath).Contains("Jade Works"),
                "Etiquetas: sacar la carta del mazo limpia sus etiquetas del sidecar");

            // Duplicar el mazo se lleva la clasificación
            var copyId = lib3.DuplicateDeck(deck3.Id);
            var copy = lib3.Repo.GetDeckById(copyId);
            Assert(DeckTags.Find(copy, "Deshonor total") != null &&
                   DeckTags.Has(copy, "TDoW012", "Deshonor total"),
                "Etiquetas: duplicar el mazo copia vocabulario y asignaciones");

            // Las listas también se etiquetan (son el mismo objeto, ADR-045)
            var listId = lib3.CreateList("Para conseguir");
            lib3.Repo.SetInList(listId, "TDoW012", true);
            var list = lib3.Repo.GetDeckById(listId);
            DeckTags.Add(list, "Prioridad");
            DeckTags.Set(list, "TDoW012", "Prioridad", true);
            lib3.SaveMeta();
            var reloadedList = new DeckLibrary(catalog, decksDir, metaPath, listsDir)
                .Lists.First(d => d.Name == "Para conseguir");
            Assert(DeckTags.Has(reloadedList, "TDoW012", "Prioridad"),
                "Etiquetas: una lista también las guarda, con su propio vocabulario");

            // Identidad por nombre: una base nueva reasigna los ids y la etiqueta tiene que seguir
            // en la MISMA carta (el bug que ADR-044 evita)
            var shuffled = Services.Search.CatalogStore.Load(dataDir);
            foreach (var card in shuffled.Cards) card.Id = "NEW-" + card.Id;
            var shuffledDeck = new DeckLibrary(shuffled, decksDir, metaPath, listsDir)
                .Repo.Decks.First(d => d.Name == "Deshonor");
            Assert(DeckTags.Has(shuffledDeck, "NEW-TDoW012", "Deshonor total"),
                "Etiquetas: sobreviven a un cambio de ids del catálogo");
        }

        /// <summary>
        /// Ajuste de oro por carta (ADR-059). Lo que se verifica es la regla —lo único con
        /// semántica no obvia— y que el ajuste puesto a mano mande sobre ella y sobreviva al
        /// disco. La regla es VIVA: no se materializa al prender el modo, así que cambiar el
        /// stronghold tiene que re-ajustar el mazo entero sin que nadie toque una carta.
        /// Extra del launcher: ningún golden lo cubre.
        /// </summary>
        private static void RunDeckGoldTests(string tempRoot)
        {
            var fixturesDir = FindRepoRelative(Path.Combine("tests", "fixtures", "deckbuilder"));
            if (fixturesDir == null) return;

            Line("");
            Line("=== Ajuste de oro de las cartas del mazo ===");
            var dataDir = Path.Combine(fixturesDir, "data");
            var catalog = Services.Search.CatalogStore.Load(dataDir);
            var root = Path.Combine(tempRoot, "goldinstall");
            var decksDir = Path.Combine(root, "Decks");
            var listsDir = Path.Combine(root, "Lists");
            var metaPath = Path.Combine(tempRoot, "goldmeta.json");
            Directory.CreateDirectory(decksDir);

            // El ejemplo del pedido: mazo escorpión con tres personalidades de coste 8 impreso.
            const string strongholdSc = "Samurai407";   // Kyuden Wasuremono    · scorpion
            const string persScorpion = "TTT053";       // Shosuro Jimen        · scorpion    · 8
            const string persUnaligned = "IG1013";      // Hitofu               · sin clan    · 8
            const string persDragon = "Samurai091";     // Togashi Kazuki       · dragon      · 8
            const string persShadow = "Celestial179";   // Shio no Oni          · shadowlands · 8
            const string persCheap = "IG3018";          // Shosuro Nishu        · scorpion    · 2

            var lib = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var deckId = lib.CreateDeck("Escorpion");
            foreach (var id in new[] { strongholdSc, persScorpion, persUnaligned, persDragon,
                                       persShadow, persCheap })
                lib.Repo.SetCardQuantity(deckId, id, 1);
            var deck = lib.Repo.GetDeckById(deckId);
            var clan = lib.Repo.DeckClan(deckId);
            Func<string, Card> get = id => lib.Repo.GetCardById(id);

            Assert(clan == "scorpion", $"Ajuste de oro: el clan del mazo sale del stronghold (es '{clan}')");

            // Apagado no ajusta nada, aunque la regla tenga algo que decir
            Assert(DeckGold.Effective(deck, get(persDragon), clan) == 0 &&
                   DeckGold.EffectiveCost(deck, get(persDragon), clan) == 8,
                "Ajuste de oro: con el modo apagado el coste es el impreso");

            deck.GoldAdjustOn = true;

            // Las dos reglas del formato sobre el ejemplo: 8 impreso → 6 / 8 / 10 pagados
            Assert(DeckGold.EffectiveCost(deck, get(persScorpion), clan) == 6,
                "Ajuste de oro: una personalidad de tu clan cuesta 2 menos");
            Assert(DeckGold.EffectiveCost(deck, get(persUnaligned), clan) == 8,
                "Ajuste de oro: una personalidad sin clan paga lo impreso");
            Assert(DeckGold.EffectiveCost(deck, get(persDragon), clan) == 10,
                "Ajuste de oro: una personalidad de otro clan grande cuesta 2 más");
            Assert(DeckGold.EffectiveCost(deck, get(persShadow), clan) == 8,
                "Ajuste de oro: shadowlands y las facciones menores NO pagan el sobrecosto");
            Assert(DeckGold.Effective(deck, get(strongholdSc), clan) == 0,
                "Ajuste de oro: la regla es solo de personalities (el stronghold no se ajusta)");

            // Es el orden lo que se buscaba arreglar: tres cartas de 8 impreso, tres lugares
            var byCost = new[] { persDragon, persUnaligned, persScorpion }
                .OrderBy(id => DeckGold.EffectiveCost(deck, get(id), clan) ?? int.MaxValue)
                .ToList();
            Assert(byCost.SequenceEqual(new[] { persScorpion, persUnaligned, persDragon }),
                "Ajuste de oro: tres cartas de coste 8 impreso se ordenan 6 < 8 < 10");

            // El ajuste manual manda sobre la regla, y volver a null devuelve el automático
            DeckGold.Set(deck, persDragon, 1);
            Assert(DeckGold.EffectiveCost(deck, get(persDragon), clan) == 9 &&
                   DeckGold.HasOverride(deck, persDragon),
                "Ajuste de oro: el ajuste puesto a mano gana sobre la regla");
            DeckGold.Set(deck, persDragon, null);
            Assert(!DeckGold.HasOverride(deck, persDragon) &&
                   DeckGold.EffectiveCost(deck, get(persDragon), clan) == 10,
                "Ajuste de oro: volver a la regla devuelve el ajuste automático");

            // Topes: ±3 al fijarlo, y el coste nunca queda negativo
            DeckGold.Set(deck, persDragon, 7);
            Assert(DeckGold.OverrideOf(deck, persDragon) == DeckGold.MaxAdjust,
                "Ajuste de oro: el ajuste manual se acota a ±3");
            DeckGold.Set(deck, persCheap, -3);
            Assert(DeckGold.EffectiveCost(deck, get(persCheap), clan) == 0,
                "Ajuste de oro: el coste ajustado tiene piso en 0, no se paga negativo");
            DeckGold.Set(deck, persDragon, 1);
            lib.SaveMeta();

            // El .txt es del juego: esto no puede asomar ahí de ninguna forma
            var txt = File.ReadAllText(Path.Combine(decksDir, "Escorpion.txt"));
            Assert(!txt.Contains("cardGold") && !txt.Contains("+1"),
                "Ajuste de oro: NO se escribe en el .txt del juego");
            var meta = File.ReadAllText(metaPath);
            Assert(meta.Contains("cardGold") && meta.Contains("Togashi Kazuki") &&
                   !meta.Contains(persDragon),
                "Ajuste de oro: va al sidecar por NOMBRE de carta, nunca por id (ADR-044)");
            Assert(!meta.Contains("Shosuro Jimen"),
                "Ajuste de oro: lo que sale de la regla no se materializa en el sidecar");

            // Round-trip por disco
            var lib2 = new DeckLibrary(catalog, decksDir, metaPath, listsDir);
            var deck2 = lib2.Repo.Decks.First(d => d.Name == "Escorpion");
            Assert(deck2.GoldAdjustOn, "Ajuste de oro: el modo prendido vuelve del sidecar");
            Assert(DeckGold.OverrideOf(deck2, persDragon) == 1 && DeckGold.OverrideCount(deck2) == 2,
                "Ajuste de oro: los ajustes manuales vuelven del sidecar");
            Assert(DeckGold.EffectiveCost(deck2, get(persScorpion), clan) == 6,
                "Ajuste de oro: la regla se recalcula al abrir, no se guarda");

            // La regla es VIVA: cambiar el stronghold re-ajusta el mazo entero solo
            lib2.Repo.DeleteCardEntry(deck2.Id, strongholdSc);
            lib2.Repo.SetCardQuantity(deck2.Id, "Samurai400", 1); // stronghold de otro clan
            var newClan = lib2.Repo.DeckClan(deck2.Id);
            Assert(newClan != null && newClan != "scorpion" &&
                   DeckGold.EffectiveCost(deck2, get(persScorpion), newClan) == 10,
                "Ajuste de oro: cambiar el stronghold re-ajusta el mazo sin tocar carta por carta");

            // Sin stronghold no hay contra qué comparar
            Assert(DeckGold.RuleFor(get(persScorpion), null) == 0,
                "Ajuste de oro: sin stronghold la regla no ajusta nada");

            // Sacar la carta se lleva su ajuste (acompaña a la carta, como la etiqueta)
            lib2.Repo.DeleteCardEntry(deck2.Id, persDragon);
            Assert(!File.ReadAllText(metaPath).Contains("Togashi Kazuki"),
                "Ajuste de oro: sacar la carta del mazo limpia su ajuste del sidecar");

            // Duplicar se lleva el modo y los ajustes
            var copy = lib2.Repo.GetDeckById(lib2.DuplicateDeck(deck2.Id));
            Assert(copy.GoldAdjustOn && DeckGold.OverrideOf(copy, persCheap) == -3,
                "Ajuste de oro: duplicar el mazo copia el modo y los ajustes manuales");

            // ---- Escalones de coste dentro de Personality (partición, no pilas) ----
            var pers = new[] { persScorpion, persUnaligned, persDragon, persShadow, persCheap };
            // En este punto persDragon lleva ajuste manual +1 (9) y persCheap −3 (piso en 0)
            var sections = DeckGold.CostSections(deck, pers, id => get(id), clan, descending: false);
            Assert(sections.Select(s => s.Cost).SequenceEqual(new int?[] { 0, 6, 8, 9 }),
                "Escalones de coste: una sección por coste efectivo, de menor a mayor");
            Assert(sections.Sum(s => s.Items.Count) == pers.Length &&
                   sections.All(s => s.Items.Count == s.Items.Distinct().Count()),
                "Escalones de coste: es una PARTICIÓN — cada carta cae en una sola sección");
            Assert(sections.Single(s => s.Cost == 8).Items
                       .OrderBy(x => x, StringComparer.Ordinal)
                       .SequenceEqual(new[] { persUnaligned, persShadow }.OrderBy(x => x, StringComparer.Ordinal)),
                "Escalones de coste: las que terminan costando lo mismo caen juntas");

            var desc = DeckGold.CostSections(deck, pers, id => get(id), clan, descending: true);
            Assert(desc.Select(s => s.Cost).SequenceEqual(new int?[] { 9, 8, 6, 0 }),
                "Escalones de coste: con el orden por coste invertido, los escalones se dan vuelta");

            // El coste variable (★) no tiene lugar en la escala: va al final en las dos direcciones
            var withStar = pers.Concat(new[] { "Samurai406" }).ToArray(); // stronghold: coste null
            Assert(DeckGold.CostSections(deck, withStar, id => get(id), clan, false).Last().Cost == null &&
                   DeckGold.CostSections(deck, withStar, id => get(id), clan, true).Last().Cost == null,
                "Escalones de coste: las de coste variable quedan al final en los dos sentidos");
        }

        /// <summary>
        /// Prefijos de la barra de filtro (`parseBarText` de la app). Hasta que el panel de filtros
        /// se compartió con el editor de Mazos ([ADR-058](../docs/DECISIONS.md)) esto era privado
        /// del Buscador; ahora lo usan dos ventanas y es un parser, que es justo lo que conviene
        /// verificar de a un caso. Lo delicado es el orden: `tx.` **antes** que `t.`.
        /// </summary>
        private static void RunBarPrefixTests()
        {
            Line("");
            Line("=== Prefijos de la barra de filtro ===");

            string value;
            Assert(Views.CardFilterPanel.ParseBar("hida", out value) == Views.CardFilterPanel.BarMode.Name &&
                   value == "hida", "Barra: sin prefijo es búsqueda por nombre");
            Assert(Views.CardFilterPanel.ParseBar("tx.destruye", out value) == Views.CardFilterPanel.BarMode.Text &&
                   value == "destruye", "Barra: tx. es texto de carta");
            Assert(Views.CardFilterPanel.ParseBar("t.personality", out value) == Views.CardFilterPanel.BarMode.Type &&
                   value == "personality", "Barra: t. es tipo");
            Assert(Views.CardFilterPanel.ParseBar("tx.algo", out value) == Views.CardFilterPanel.BarMode.Text,
                "Barra: tx. gana sobre t. (si no, 'tx.algo' se leería como tipo 'x.algo')");
            Assert(Views.CardFilterPanel.ParseBar("k.bushi", out value) == Views.CardFilterPanel.BarMode.Keyword &&
                   value == "bushi", "Barra: k. es keyword");
            Assert(Views.CardFilterPanel.ParseBar("c.crab", out value) == Views.CardFilterPanel.BarMode.Clan &&
                   value == "crab", "Barra: c. es clan");
            Assert(Views.CardFilterPanel.ParseBar("TX.Algo", out value) == Views.CardFilterPanel.BarMode.Text &&
                   value == "Algo", "Barra: el prefijo no distingue mayúsculas y el valor se conserva tal cual");
            Assert(Views.CardFilterPanel.ParseBar("", out value) == Views.CardFilterPanel.BarMode.Name &&
                   value == "", "Barra: vacía es nombre vacío");
            Assert(Views.CardFilterPanel.ParseBar(null, out value) == Views.CardFilterPanel.BarMode.Name &&
                   value == "", "Barra: null no explota");
            Assert(Views.CardFilterPanel.ParseBar("Kolat", out value) == Views.CardFilterPanel.BarMode.Name,
                "Barra: un nombre que empieza con K no se confunde con k.");
        }

        /// <summary>
        /// PrintingIdOf desempata el orden por Set (SortField.SET, golden del Buscador): antes
        /// asumía ".jpg" a fuego, así que un pack con otro formato de imagen quedaba con la
        /// extensión pegada al id. Recorta la extensión que sea, sin cambiar el resultado para
        /// ".jpg" (los 53 golden no se mueven).
        /// </summary>
        private static void RunPrintingIdTests()
        {
            Line("");
            Line("=== Edición e id de impresión (path de imagen) ===");

            Assert(Services.Search.CardSearchEngine.PrintingIdOf("images/cards/WoH/WoH011.jpg") == "WoH011",
                "PrintingId: recorta .jpg igual que antes");
            Assert(Services.Search.CardSearchEngine.PrintingIdOf("images/cards/WoH/WoH011.png") == "WoH011",
                "PrintingId: también recorta .png, no solo .jpg");
            Assert(Services.Search.CardSearchEngine.PrintingIdOf("images/cards/P/P230.jpeg") == "P230",
                "PrintingId: cualquier extensión, no una lista cerrada");
            Assert(Services.Search.CardSearchEngine.PrintingIdOf("SinCarpeta.jpg") == "SinCarpeta",
                "PrintingId: sin '/' en el path también funciona");
            Assert(Services.Search.CardSearchEngine.PrintingIdOf(null) == "",
                "PrintingId: null no explota");

            Assert(Services.Search.CardSearchEngine.EditionOf("images/cards/WoH/WoH011.jpg") == "WoH",
                "Edition: segmento entre 'images/cards/' y '/'");
            Assert(Services.Search.CardSearchEngine.EditionOf(null) == null, "Edition: null no explota");
        }

        /// <summary>
        /// Exportación a PDF: la geometría de la hoja (que es lo que hace que la carta impresa mida
        /// 63×88 mm de verdad) y la estructura del archivo emitido. No necesita fixtures: el JPEG de
        /// prueba se genera acá mismo.
        /// </summary>
        private static void RunPdfExportTests(string tempRoot)
        {
            Line("");
            Line("=== Exportación del mazo a PDF ===");

            // ---- geometría ----
            var a4 = PdfExporter.Layout(PdfPageSize.A4, margin1mm: false);
            Assert(a4.PageWidth == 595 && a4.PageHeight == 842, "PDF: A4 mide 595×842 pt");
            Assert(a4.CardWidth == 179 && a4.CardHeight == 249,
                "PDF: la carta mide 179×249 pt (63×88 mm a tamaño real)");
            Assert(a4.Gap == 0, "PDF: sin margen las cartas van pegadas");
            Assert(a4.OriginX == 29 && a4.OriginY == 47, "PDF: la grilla 3×3 queda centrada en A4");

            var letter = PdfExporter.Layout(PdfPageSize.Letter, margin1mm: false);
            Assert(letter.PageWidth == 612 && letter.PageHeight == 792, "PDF: Carta mide 612×792 pt");
            Assert(letter.OriginX == 37 && letter.OriginY == 22, "PDF: la grilla queda centrada en Carta");
            Assert(letter.CardWidth == a4.CardWidth && letter.CardHeight == a4.CardHeight,
                "PDF: la carta mide lo mismo en las dos hojas (el tamaño real no depende del papel)");

            var a3 = PdfExporter.Layout(PdfPageSize.A3, margin1mm: false);
            Assert(a3.PageWidth == 1191 && a3.PageHeight == 842,
                "PDF: A3 sale apaisado, 1191×842 pt (dos A4 uno al lado del otro)");
            Assert(a3.Columns == 6 && a3.Rows == 3, "PDF: A3 entra 6×3 = 18, el doble que A4/Carta");
            Assert(a3.OriginX == 58 && a3.OriginY == 47, "PDF: la grilla 6×3 queda centrada en A3");

            var spaced = PdfExporter.Layout(PdfPageSize.A4, margin1mm: true);
            Assert(spaced.Gap == 3, "PDF: el margen de 1 mm son 3 pt");
            Assert(spaced.OriginX == 26 && spaced.OriginY == 44,
                "PDF: con margen la grilla crece y el origen se corre para seguir centrada");

            // ---- nombre de archivo (mismo saneado que la app) ----
            Assert(PdfExporter.SuggestFileName("Mazo de Prueba", new DateTime(2026, 7, 29, 15, 4, 0))
                       == "L5A-Mazo-de-Prueba-20260729-1504.pdf",
                "PDF: el nombre sugerido lleva el mazo saneado y la fecha");
            Assert(PdfExporter.SuggestFileName("Unicornio!!!", DateTime.Now).StartsWith("L5A-Unicornio-2"),
                "PDF: los símbolos del final no dejan guiones colgando");
            Assert(!PdfExporter.SuggestFileName("Mazo/con\\barras", DateTime.Now).Contains("/"),
                "PDF: el nombre sugerido nunca trae separadores de ruta");

            // ---- hojas ----
            Assert(PdfExporter.PageCount(0, PdfPageSize.A4) == 0, "PDF: sin cartas no hay hojas");
            Assert(PdfExporter.PageCount(9, PdfPageSize.A4) == 1, "PDF: 9 cartas entran en una hoja");
            Assert(PdfExporter.PageCount(10, PdfPageSize.A4) == 2, "PDF: la carta 10 abre una hoja nueva");
            Assert(PdfExporter.PageCount(40, PdfPageSize.A4) == 5, "PDF: 40 cartas son 5 hojas");
            Assert(PdfExporter.PageCount(18, PdfPageSize.A3) == 1, "PDF: 18 cartas entran en una hoja A3");
            Assert(PdfExporter.PageCount(19, PdfPageSize.A3) == 2, "PDF: la carta 19 abre una hoja A3 nueva");

            // ---- secuencia de impresión ----
            var pdfDir = Path.Combine(tempRoot, "pdf");
            Directory.CreateDirectory(pdfDir);
            var jpegPath = Path.Combine(pdfDir, "carta.jpg");
            File.WriteAllBytes(jpegPath, MakeTestJpeg(210, 300));
            var missingPath = Path.Combine(pdfDir, "no-existe.jpg");

            var items = new List<PdfExportItem>
            {
                new PdfExportItem { CardName = "A", CandidatePaths = new[] { jpegPath }, Copies = 3 },
                new PdfExportItem { CardName = "B", CandidatePaths = new[] { missingPath }, Copies = 1 },
                new PdfExportItem { CardName = "C", CandidatePaths = new[] { jpegPath }, Copies = 0 },
            };
            var sequence = PdfExporter.BuildPrintSequence(items);
            Assert(sequence.Count == 4, "PDF: las copias se expanden (3 + 1 + 0 = 4 cartas)");
            Assert(sequence.Take(3).All(p => p == jpegPath), "PDF: las copias de una carta van seguidas");
            Assert(sequence[3] == null, "PDF: una carta sin imagen entra en la secuencia como hueco");

            // ---- cabecera JPEG ----
            int w, h, comps;
            bool baseline;
            Assert(PdfImage.TryReadJpegHeader(File.ReadAllBytes(jpegPath), out w, out h, out comps, out baseline)
                       && w == 210 && h == 300 && comps == 3 && baseline,
                "PDF: lee el SOF del JPEG (dimensiones, componentes y baseline)");
            Assert(!PdfImage.TryReadJpegHeader(new byte[] { 1, 2, 3, 4 }, out w, out h, out comps, out baseline),
                "PDF: un archivo que no es JPEG no pasa por el camino de embeber directo");

            // ---- documento emitido ----
            var outPath = Path.Combine(pdfDir, "salida.pdf");
            var pages = PdfExporter.Write(outPath, items, PdfPageSize.A4, margin1mm: false);
            Assert(pages == 1 && File.Exists(outPath), "PDF: escribe el archivo y devuelve las hojas");
            Assert(!File.Exists(outPath + ".tmp"), "PDF: no queda el temporal de escritura");

            var raw = File.ReadAllBytes(outPath);
            var text = Encoding.ASCII.GetString(raw);
            Assert(text.StartsWith("%PDF-1.4"), "PDF: arranca con la cabecera %PDF");
            Assert(text.TrimEnd('\n', '\r').EndsWith("%%EOF"), "PDF: cierra con %%EOF");
            Assert(text.Contains("xref") && text.Contains("startxref") && text.Contains("/Root 1 0 R"),
                "PDF: trae tabla xref y trailer con el catálogo");
            // El error clásico de escribir un PDF a mano: los offsets del xref no caen justo en su
            // objeto. Algunos visores lo perdonan reconstruyendo la tabla y otros no abren nada.
            Assert(XrefOffsetsResolve(text), "PDF: cada offset del xref cae exacto en su objeto");
            Assert(text.Contains("/Type /Pages /Count 1"), "PDF: el árbol de páginas declara 1 hoja");
            Assert(text.Contains("/MediaBox [0 0 595 842]"), "PDF: la hoja sale del tamaño elegido");

            Assert(CountOccurrences(text, "/DCTDecode") == 1,
                "PDF: una carta repetida embebe su JPEG una sola vez");
            Assert(CountOccurrences(text, " Do\n") == 3, "PDF: la referencia a la imagen sí se repite por copia");

            // Origen del PDF abajo-izquierda: la primera carta va arriba de todo, o sea con la Y
            // alta (842 - 47 - 249 = 546). Sin espejar, el mazo se imprimiría al revés.
            Assert(text.Contains("179 0 0 249 29 546 cm"),
                "PDF: la primera carta se ubica arriba a la izquierda (Y espejada)");
            Assert(text.Contains("0.164706 0.062745 0.031373 rg") && text.Contains(" re f"),
                "PDF: la carta sin imagen sale como rectángulo placeholder");

            // Segunda hoja: 10 cartas tienen que abrirla de verdad, no solo contarla.
            var many = new List<PdfExportItem>
            {
                new PdfExportItem { CardName = "A", CandidatePaths = new[] { jpegPath }, Copies = 10 },
            };
            var multiPath = Path.Combine(pdfDir, "dos-hojas.pdf");
            Assert(PdfExporter.Write(multiPath, many, PdfPageSize.Letter, margin1mm: true) == 2,
                "PDF: 10 copias se reparten en dos hojas");
            var multiText = Encoding.ASCII.GetString(File.ReadAllBytes(multiPath));
            Assert(CountOccurrences(multiText, "/Type /Page ") == 2, "PDF: emite los dos objetos de hoja");
            Assert(multiText.Contains("/MediaBox [0 0 612 792]"), "PDF: respeta el tamaño Carta");

            // A3: 18 copias tienen que entrar en una sola hoja (el doble que A4/Carta).
            var eighteen = new List<PdfExportItem>
            {
                new PdfExportItem { CardName = "A", CandidatePaths = new[] { jpegPath }, Copies = 18 },
            };
            var a3Path = Path.Combine(pdfDir, "a3.pdf");
            Assert(PdfExporter.Write(a3Path, eighteen, PdfPageSize.A3, margin1mm: false) == 1,
                "PDF: 18 copias en A3 entran en una sola hoja");
            var a3Text = Encoding.ASCII.GetString(File.ReadAllBytes(a3Path));
            Assert(a3Text.Contains("/MediaBox [0 0 1191 842]"), "PDF: respeta el tamaño A3 apaisado");
            Assert(CountOccurrences(a3Text, " Do\n") == 18, "PDF: las 18 cartas se referencian en la hoja");
        }

        /// <summary>
        /// Recorre la tabla xref del PDF y comprueba que el offset del objeto N caiga justo sobre
        /// "N 0 obj". Cada entrada mide exactamente 20 bytes, así que se indexan por posición.
        /// </summary>
        private static bool XrefOffsetsResolve(string pdf)
        {
            var startxref = pdf.LastIndexOf("startxref", StringComparison.Ordinal);
            if (startxref < 0) return false;
            var xrefAt = 0;
            if (!int.TryParse(pdf.Substring(startxref + 9).Trim().Split('\n')[0].Trim(), out xrefAt)) return false;
            if (xrefAt <= 0 || xrefAt >= pdf.Length) return false;
            if (!pdf.Substring(xrefAt).StartsWith("xref\n", StringComparison.Ordinal)) return false;

            var headerEnd = pdf.IndexOf('\n', xrefAt + 5);
            if (headerEnd < 0) return false;
            var header = pdf.Substring(xrefAt + 5, headerEnd - (xrefAt + 5)).Trim().Split(' ');
            int count;
            if (header.Length != 2 || !int.TryParse(header[1], out count) || count < 2) return false;

            var entries = headerEnd + 1;   // la entrada 0 es la libre "0000000000 65535 f"
            for (var n = 1; n < count; n++)
            {
                var at = entries + n * 20;
                if (at + 20 > pdf.Length) return false;
                int offset;
                if (!int.TryParse(pdf.Substring(at, 10), out offset)) return false;
                var expected = n + " 0 obj";
                if (offset + expected.Length > pdf.Length) return false;
                if (pdf.Substring(offset, expected.Length) != expected) return false;
            }
            return true;
        }

        /// <summary>
        /// Ubicación de la previsualización con Alt. El invariante es uno y es el que causaba el bug:
        /// **el cursor nunca puede quedar adentro del popup**. Si queda, el popup tapa la celda, salta
        /// su MouseLeave, se cierra, el mouse vuelve a estar sobre la celda y se reabre — parpadeo
        /// infinito. Contra el borde derecho, `PlacementMode.Mouse` de WPF hacía exactamente eso.
        /// </summary>
        private static void RunHoverPreviewTests()
        {
            Line("");
            Line("=== Previsualización con Alt: no puede quedar debajo del cursor ===");

            var content = new System.Windows.Size(444, 634);
            var work = new System.Windows.Rect(0, 0, 1536, 824);

            System.Windows.Point At(double x, double y) =>
                Views.HoverPreviewPlacer.ComputePosition(new System.Windows.Point(x, y), work, content);
            System.Windows.Rect RectAt(double x, double y)
            {
                var p = At(x, y);
                return new System.Windows.Rect(p.X, p.Y, content.Width, content.Height);
            }

            // Caso cómodo: abajo a la derecha del cursor
            var easy = At(200, 100);
            Assert(easy.X > 200 && easy.Y > 100, "Alt: con lugar, la previsualización va abajo-derecha del cursor");

            // Borde derecho: se va al otro lado, y NO espejando el offset (eso lo metía bajo el mouse)
            var right = At(1500, 100);
            Assert(right.X < 1500 - content.Width,
                "Alt: contra el borde derecho salta a la izquierda del cursor, corrida su ancho entero");
            Assert(!RectAt(1500, 100).Contains(new System.Windows.Point(1500, 100)),
                "Alt: contra el borde derecho el cursor queda FUERA (era el bucle de parpadeo)");

            // Borde inferior y esquina
            Assert(!RectAt(200, 800).Contains(new System.Windows.Point(200, 800)),
                "Alt: contra el borde inferior el cursor queda fuera");
            Assert(!RectAt(1520, 810).Contains(new System.Windows.Point(1520, 810)),
                "Alt: en la esquina inferior derecha el cursor queda fuera");

            // Nunca se sale del área de trabajo
            var corner = RectAt(1530, 820);
            Assert(corner.Left >= work.Left && corner.Top >= work.Top && corner.Right <= work.Right + 0.01,
                "Alt: la previsualización no se dibuja fuera del área de trabajo");

            // El barrido es el check que de verdad cierra el caso: toda la pantalla, paso de 8 px.
            var inside = 0;
            var outside = 0;
            for (var x = work.Left; x <= work.Right; x += 8)
                for (var y = work.Top; y <= work.Bottom; y += 8)
                {
                    var cursor = new System.Windows.Point(x, y);
                    var p = Views.HoverPreviewPlacer.ComputePosition(cursor, work, content);
                    if (new System.Windows.Rect(p.X, p.Y, content.Width, content.Height).Contains(cursor)) inside++;
                    if (p.X < work.Left - 0.01 || p.Y < work.Top - 0.01) outside++;
                }
            Assert(inside == 0, "Alt: barriendo la pantalla entera, el cursor no cae adentro en ninguna posición");
            Assert(outside == 0, "Alt: barriendo la pantalla entera, nunca se posiciona fuera del área");

            // Una pantalla más chica que el popup es el peor caso: la red de seguridad tiene que actuar
            var tiny = new System.Windows.Rect(0, 0, 700, 500);
            var tinyCursor = new System.Windows.Point(350, 250);
            var tinyAt = Views.HoverPreviewPlacer.ComputePosition(tinyCursor, tiny, content);
            Assert(!new System.Windows.Rect(tinyAt.X, tinyAt.Y, content.Width, content.Height).Contains(tinyCursor),
                "Alt: incluso en un área más chica que la previsualización, el cursor queda fuera");
        }

        /// <summary>JPEG baseline RGB sintético, para no depender de imágenes de cartas reales.</summary>
        private static byte[] MakeTestJpeg(int width, int height)
        {
            var stride = width * 3;
            var pixels = new byte[stride * height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 251);
            var source = System.Windows.Media.Imaging.BitmapSource.Create(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Rgb24, null, pixels, stride);
            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            using (var memory = new MemoryStream())
            {
                encoder.Save(memory);
                return memory.ToArray();
            }
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            var count = 0;
            for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        /// <summary>Carga cartas del grupo pedido hasta llegar al total (para armar mazos de prueba).</summary>
        private static void FillSide(DeckRepository repo, Models.CardCatalog catalog,
                                     string deckId, string group, int target)
        {
            foreach (var card in catalog.Cards.Where(c =>
                         string.Equals(c.DeckGroup, group, StringComparison.Ordinal) && !c.Banned))
            {
                if ((group == "dynasty" ? repo.DynastyCount(deckId) : repo.FateCount(deckId)) >= target) return;
                repo.SetCardQuantity(deckId, card.Id, Math.Min(3, Math.Max(1, card.MaxCopies)));
            }
        }

        /// <summary>Busca una ruta relativa al repo subiendo desde el directorio del exe.</summary>
        private static string FindRepoRelative(string relative)
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir, relative);
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            return null;
        }

        /// <summary>
        /// e2e contra una copia local del bucket real (archivos de verdad): parsea el manifest,
        /// descarga+verifica+extrae la base comunidad zippeada y sincroniza el pack de imágenes
        /// sobre una instalación falsa.
        /// </summary>
        private static void RunRealBucket(string tempRoot, string bucketDir)
        {
            Line("");
            Line("=== e2e con archivos REALES del bucket ===");
            var manifestUrl = new Uri(Path.Combine(bucketDir, "manifest.json")).AbsoluteUri;

            var install = Path.Combine(tempRoot, "realinstall");
            var dbDir = Path.Combine(install, "Sun and Moon_Data", "StreamingAssets", "Database");
            Directory.CreateDirectory(dbDir);
            Directory.CreateDirectory(Path.Combine(install, "images"));
            File.WriteAllText(Path.Combine(install, "Sun and Moon.exe"), "dummy");
            File.WriteAllText(Path.Combine(dbDir, "database.xml"), "ORIGINAL-VANILLA");

            var config = new UserConfig { ManifestUrl = manifestUrl, SunAndMoonPath = install };
            var engine = new LauncherEngine(config);
            engine.LoadManifestAsync().GetAwaiter().GetResult();

            Assert(engine.CommunityEntry != null && engine.CommunityEntry.Version == "2.0.1",
                "Real: manifest parsea la base comunidad 2.0.1");
            Assert(engine.CommunityNeedsDownload(), "Real: comunidad necesita descarga");

            engine.ApplyBaseAsync(LauncherEngine.CommunityId, null).GetAwaiter().GetResult();
            var applied = File.ReadAllText(Path.Combine(dbDir, "database.xml"));
            Assert(applied.Length > 1000000, "Real: database.xml aplicado (>1MB, base real descomprimida)");
            Assert(applied.Contains("s_extended"), "Real: la base aplicada contiene s_extended");

            Assert(engine.ImagesNeedSync(), "Real: imágenes necesitan sync");
            engine.SyncImagesAsync(null).GetAwaiter().GetResult();
            Assert(File.Exists(Path.Combine(install, "images", "cards", "SExMRP", "SEx002.jpg")),
                "Real: imagen SEx002.jpg extraída a images/cards/SExMRP/");
        }

        // ---------- helpers ----------

        private static void Assert(bool cond, string name)
        {
            if (cond) { _pass++; Line("PASS: " + name); }
            else { _fail++; Line("FAIL: " + name); }
        }

        private static void AssertThrows<TEx>(Action action, string name) where TEx : Exception
        {
            try { action(); _fail++; Line("FAIL: " + name + " (no lanzó)"); }
            catch (TEx) { _pass++; Line("PASS: " + name); }
            catch (Exception ex) { _fail++; Line("FAIL: " + name + " (lanzó " + ex.GetType().Name + ")"); }
        }

        private static void CreateZip(string path, Dictionary<string, string> entries)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (var kv in entries)
                {
                    var entry = zip.CreateEntry(kv.Key);
                    using (var w = new StreamWriter(entry.Open())) w.Write(kv.Value);
                }
            }
        }

        private static int CountFiles(string dir) =>
            Directory.Exists(dir) ? Directory.GetFiles(dir).Length : 0;

        private static void Line(string s) => Log.Add(s);

        private static void TryDeleteDir(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }
    }
}
#endif
