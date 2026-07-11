using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services.Deck
{
    /// <summary>
    /// Biblioteca de mazos del launcher: cada mazo es UN .txt en la carpeta Decks de Sun and
    /// Moon (los mismos archivos que carga el juego), formato de <see cref="SunAndMoonDeckFile"/>.
    /// Lo que el txt no puede representar (notas, sort_mode, fecha de creación, mazo activo)
    /// vive en un sidecar decksmeta.json en %APPDATA% (respeta L5A_DATA_ROOT). Sin instalación
    /// configurada usa %APPDATA%\...\Decks para no perder la funcionalidad.
    /// Guardado inmediato en cada mutación (los txt son chicos y el evento, esporádico).
    /// </summary>
    public class DeckLibrary
    {
        private class DeckMeta
        {
            [JsonPropertyName("notes")] public string Notes { get; set; }
            [JsonPropertyName("sortMode")] public string SortMode { get; set; }
            [JsonPropertyName("createdAt")] public long CreatedAt { get; set; }
            [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }
        }

        private class MetaDto
        {
            [JsonPropertyName("activeDeck")] public string ActiveDeck { get; set; }
            [JsonPropertyName("decks")]
            public Dictionary<string, DeckMeta> Decks { get; set; } = new Dictionary<string, DeckMeta>();
        }

        private readonly Dictionary<string, string> _fileByDeckId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly string _metaPath;
        private bool _loading;
        private string _activeDeckId;

        public DeckRepository Repo { get; }
        public string DecksDir { get; }

        /// <summary>Cambió el mazo activo (o dejó de haber uno).</summary>
        public event Action ActiveDeckChanged;

        private static DeckLibrary _current;

        /// <summary>
        /// Instancia compartida del proceso (Buscador y editor operan sobre los mismos mazos).
        /// Si cambia la carpeta o el catálogo se recrea.
        /// </summary>
        public static DeckLibrary GetOrLoad(CardCatalog catalog, string installPath)
        {
            var dir = ResolveDecksDir(installPath);
            if (_current == null || _current.DecksDir != dir || _current.Repo.Catalog != catalog)
                _current = new DeckLibrary(catalog, dir);
            return _current;
        }

        public static string ResolveDecksDir(string installPath) =>
            string.IsNullOrEmpty(installPath)
                ? Path.Combine(AppConstants.AppDataDir, "Decks")
                : Path.Combine(installPath, "Decks");

        public DeckLibrary(CardCatalog catalog, string decksDir, string metaPath = null)
        {
            DecksDir = decksDir;
            _metaPath = metaPath ?? AppConstants.DecksMetaPath;
            Repo = new DeckRepository(catalog);
            Load();
            Repo.DeckChanged += OnDeckChanged;
        }

        // ------------------------------------------------------------------ carga

        private void Load()
        {
            _loading = true;
            try
            {
                var meta = LoadMeta();
                if (Directory.Exists(DecksDir))
                {
                    foreach (var file in Directory.EnumerateFiles(DecksDir, "*.txt")
                                                  .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var baseName = Path.GetFileNameWithoutExtension(file);
                            var deck = SunAndMoonDeckFile.Parse(File.ReadAllText(file), Repo);
                            deck.Id = Guid.NewGuid().ToString();
                            deck.Name = baseName;
                            DeckMeta m = null;
                            meta.Decks?.TryGetValue(baseName, out m);
                            // meta manda (el txt no lleva notas); sin meta, lo parseado del txt
                            if (m != null)
                            {
                                deck.Notes = string.IsNullOrWhiteSpace(m.Notes) ? deck.Notes : m.Notes;
                                deck.SortMode = m.SortMode ?? deck.SortMode;
                                deck.CreatedAt = m.CreatedAt;
                                deck.SortOrder = m.SortOrder;
                            }
                            if (deck.CreatedAt == 0)
                                deck.CreatedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(file))
                                    .ToUnixTimeMilliseconds();
                            Repo.AttachLoadedDeck(deck);
                            _fileByDeckId[deck.Id] = baseName;
                        }
                        catch
                        {
                            // txt ilegible → se ignora (no se pisa: solo se guardan mazos abiertos)
                        }
                    }
                }
                if (meta.ActiveDeck != null)
                    _activeDeckId = Repo.Decks.FirstOrDefault(d =>
                        _fileByDeckId.TryGetValue(d.Id, out var f) && f == meta.ActiveDeck)?.Id;
            }
            finally
            {
                _loading = false;
            }
        }

        private MetaDto LoadMeta()
        {
            try
            {
                if (File.Exists(_metaPath))
                    return JsonSerializer.Deserialize<MetaDto>(File.ReadAllText(_metaPath)) ?? new MetaDto();
            }
            catch
            {
                // sidecar corrupto → se regenera en el próximo guardado
            }
            return new MetaDto();
        }

        // ------------------------------------------------------------------ mazo activo

        /// <summary>Mazo activo global (destino de los +/− del Buscador). Puede ser null.</summary>
        public string ActiveDeckId
        {
            get => _activeDeckId;
            set
            {
                if (_activeDeckId == value) return;
                _activeDeckId = value;
                PersistMeta();
                ActiveDeckChanged?.Invoke();
            }
        }

        public Models.Deck ActiveDeck => _activeDeckId == null ? null : Repo.GetDeckById(_activeDeckId);

        // ------------------------------------------------------------------ operaciones con archivo

        /// <summary>Crea un mazo con nombre único y lo activa (como la app). Devuelve el id.</summary>
        public string CreateDeck(string name)
        {
            var id = Repo.CreateDeck(UniqueName(name));
            ActiveDeckId = id;
            return id;
        }

        public void RenameDeck(string id, string newName)
        {
            var deck = Repo.GetDeckById(id);
            if (deck == null) return;
            newName = (newName ?? "").Trim();
            if (newName.Length == 0 || newName == deck.Name) return;
            Repo.RenameDeck(id, UniqueName(newName)); // dispara DeckChanged → guarda y renombra el txt
        }

        public void DeleteDeck(string id)
        {
            var deck = Repo.GetDeckById(id);
            if (deck == null) return;
            if (_fileByDeckId.TryGetValue(id, out var baseName))
            {
                TryDelete(PathFor(baseName));
                _fileByDeckId.Remove(id);
            }
            Repo.DeleteDeck(id);
            if (_activeDeckId == id) _activeDeckId = null;
            PersistMeta();
            ActiveDeckChanged?.Invoke();
        }

        /// <summary>Duplicar (no existe en la app; acá es export → mazo nuevo → import).</summary>
        public string DuplicateDeck(string id)
        {
            var deck = Repo.GetDeckById(id);
            if (deck == null) return null;
            var text = Repo.ExportDeck(id);
            var copyId = Repo.CreateDeck(UniqueName(deck.Name));
            Repo.ImportCards(copyId, text);
            return copyId;
        }

        /// <summary>Nombre libre: agrega " (2)", " (3)"… si ya existe mazo o archivo.</summary>
        public string UniqueName(string name)
        {
            name = SanitizeFileName((name ?? "").Trim());
            if (name.Length == 0) name = "Mazo";
            var candidate = name;
            var n = 2;
            while (Repo.Decks.Any(d => string.Equals(d.Name, candidate, StringComparison.OrdinalIgnoreCase))
                   || File.Exists(PathFor(candidate)))
                candidate = name + " (" + n++ + ")";
            return candidate;
        }

        // ------------------------------------------------------------------ persistencia

        private void OnDeckChanged(Models.Deck deck)
        {
            if (_loading) return;
            SaveDeck(deck);
        }

        /// <summary>Escribe el txt del mazo (renombrando el archivo si cambió el nombre) + sidecar.</summary>
        public void SaveDeck(Models.Deck deck)
        {
            Directory.CreateDirectory(DecksDir);
            var baseName = SanitizeFileName(deck.Name);
            if (_fileByDeckId.TryGetValue(deck.Id, out var oldBase) && oldBase != baseName)
                TryDelete(PathFor(oldBase));
            File.WriteAllText(PathFor(baseName), SunAndMoonDeckFile.Write(deck, Repo));
            _fileByDeckId[deck.Id] = baseName;
            PersistMeta();
        }

        private void PersistMeta()
        {
            var dto = new MetaDto
            {
                ActiveDeck = _activeDeckId != null && _fileByDeckId.TryGetValue(_activeDeckId, out var f)
                    ? f : null,
            };
            foreach (var deck in Repo.Decks)
            {
                if (!_fileByDeckId.TryGetValue(deck.Id, out var baseName)) continue;
                dto.Decks[baseName] = new DeckMeta
                {
                    Notes = deck.Notes,
                    SortMode = deck.SortMode,
                    CreatedAt = deck.CreatedAt,
                    SortOrder = deck.SortOrder,
                };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_metaPath));
            File.WriteAllText(_metaPath,
                JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }

        private string PathFor(string baseName) => Path.Combine(DecksDir, baseName + ".txt");

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>Caracteres inválidos de nombre de archivo → '_' (el nombre del mazo ES el archivo).</summary>
        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            return sb.ToString().TrimEnd('.', ' ');
        }
    }
}
