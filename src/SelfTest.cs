#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
