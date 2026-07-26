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

            // ---------- Stores del Buscador: listas y presets (roundtrip en disco) ----------
            var ucs = Services.Search.UserCardDataStore.Load();
            Assert(ucs.CreateList("mazo-prueba"), "Stores: crea lista");
            Assert(!ucs.CreateList("mazo-prueba"), "Stores: no duplica lista");
            Assert(ucs.ToggleInList("mazo-prueba", "X1"), "Stores: agrega carta a la lista");
            var ucs2 = Services.Search.UserCardDataStore.Load();
            Assert(ucs2.ListContains("mazo-prueba", "X1"), "Stores: lista persiste en disco");
            Assert(!ucs2.ToggleInList("mazo-prueba", "X1"), "Stores: toggle quita la carta");

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
