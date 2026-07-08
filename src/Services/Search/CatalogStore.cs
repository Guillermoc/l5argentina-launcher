using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services.Search
{
    /// <summary>
    /// Localiza y carga el catálogo del Buscador desde una carpeta local
    /// (por defecto <see cref="AppConstants.CatalogDir"/>): el trío
    /// cards_db-X.Y.Z.zip (o cards-X.Y.Z.json) + rules-X.Y.Z.json + filters-X.Y.Z.json,
    /// tomando la versión más alta de cada uno — mismo esquema de artefactos que consume
    /// la app Android. Los archivos llegan verificados por SHA-256 (sync del manifest)
    /// o se colocan a mano para desarrollo.
    /// </summary>
    public static class CatalogStore
    {
        private static readonly Regex VersionedFile =
            new Regex(@"^(?<name>[a-z_]+)-(?<ver>[0-9][0-9.]*)\.(?<ext>zip|json)$", RegexOptions.IgnoreCase);

        /// <summary>True si la carpeta tiene al menos un cards_db/cards utilizable.</summary>
        public static bool IsAvailable(string dir = null)
        {
            dir = dir ?? AppConstants.CatalogDir;
            return FindLatest(dir, "cards_db", "zip") != null || FindLatest(dir, "cards", "json") != null;
        }

        /// <summary>
        /// Carga el catálogo completo. rules/filters son opcionales (sin rules no hay banlist;
        /// sin filters no hay orden canónico de sets ni autocomplete de keywords).
        /// </summary>
        public static CardCatalog Load(string dir = null)
        {
            dir = dir ?? AppConstants.CatalogDir;

            string cardsJson, cardsVersion;
            var cardsZip = FindLatest(dir, "cards_db", "zip");
            if (cardsZip != null)
            {
                cardsJson = ReadSingleJsonFromZip(cardsZip.Path);
                cardsVersion = cardsZip.Version;
            }
            else
            {
                var cardsFile = FindLatest(dir, "cards", "json")
                    ?? throw new FileNotFoundException(
                        "No hay catálogo de cartas (cards_db-X.Y.Z.zip o cards-X.Y.Z.json) en " + dir);
                cardsJson = File.ReadAllText(cardsFile.Path);
                cardsVersion = cardsFile.Version;
            }

            var rules = FindLatest(dir, "rules", "json");
            var filters = FindLatest(dir, "filters", "json");

            var catalog = CatalogImporter.Import(
                cardsJson,
                rules != null ? File.ReadAllText(rules.Path) : null,
                filters != null ? File.ReadAllText(filters.Path) : null);

            catalog.CardsVersion = cardsVersion;
            catalog.RulesVersion = rules?.Version;
            catalog.FiltersVersion = filters?.Version;
            return catalog;
        }

        // ------------------------------------------------------------------

        private class VersionedMatch
        {
            public string Path;
            public string Version;
        }

        private static VersionedMatch FindLatest(string dir, string baseName, string ext)
        {
            if (!Directory.Exists(dir)) return null;
            VersionedMatch best = null;
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var m = VersionedFile.Match(Path.GetFileName(file));
                if (!m.Success) continue;
                if (!string.Equals(m.Groups["name"].Value, baseName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(m.Groups["ext"].Value, ext, StringComparison.OrdinalIgnoreCase)) continue;
                var ver = m.Groups["ver"].Value;
                if (best == null || VersionUtil.IsNewer(ver, best.Version))
                    best = new VersionedMatch { Path = file, Version = ver };
            }
            return best;
        }

        /// <summary>
        /// Lee el único .json de un cards_db-X.Y.Z.zip. Igual criterio que ZipUtil.ExtractSingleXml:
        /// exactamente uno, si no se rechaza. Se lee en memoria, no se extrae a disco.
        /// </summary>
        private static string ReadSingleJsonFromZip(string zipPath)
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var jsons = archive.Entries.Where(e =>
                    !string.IsNullOrEmpty(e.Name) &&
                    e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
                if (jsons.Count != 1)
                    throw new InvalidOperationException(
                        $"El cards_db.zip debe contener exactamente un .json (encontrados: {jsons.Count}).");
                using (var stream = jsons[0].Open())
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }
    }
}
