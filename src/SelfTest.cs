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

            // ---------- Buscador: paridad con la app Android (golden tests) ----------
            RunGoldenSearchTests();

            // ---------- Opcional: e2e con archivos REALES del bucket ----------
            // Se activa pasando L5A_SELFTEST_BUCKET=<carpeta con manifest.json + zips reales>.
            var realBucket = Environment.GetEnvironmentVariable("L5A_SELFTEST_BUCKET");
            if (!string.IsNullOrEmpty(realBucket) && File.Exists(Path.Combine(realBucket, "manifest.json")))
                RunRealBucket(tempRoot, realBucket);
        }

        /// <summary>
        /// Paridad del motor del Buscador contra los 53 golden tests generados con la lógica
        /// real de la app Android (docs/handoff/buscador-launcher/fixtures/). Cada caso es un
        /// criteria serializado + los ids esperados; orderMatters decide secuencia vs conjunto.
        /// Si los fixtures no están (exe distribuido fuera del repo), se omite sin fallar.
        /// </summary>
        private static void RunGoldenSearchTests()
        {
            var fixturesDir = FindRepoRelative(Path.Combine("docs", "handoff", "buscador-launcher", "fixtures"));
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
