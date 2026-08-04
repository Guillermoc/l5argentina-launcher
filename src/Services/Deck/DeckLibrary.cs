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
    /// Biblioteca de conjuntos de cartas del launcher. Dos clases, un solo modelo
    /// (<see cref="Models.Deck"/> con Type "deck"/"list", como la app Android) y un solo
    /// <see cref="DeckRepository"/>; lo que cambia es dónde vive el archivo:
    ///   · mazos  → &lt;juego&gt;\Decks\  — los que carga Sun and Moon;
    ///   · listas → &lt;juego&gt;\Lists\  — al lado, material de armado que el juego no ve.
    /// Ambos son .txt con el formato de <see cref="SunAndMoonDeckFile"/>, o sea que la
    /// identidad persistida de cada carta es su NOMBRE (ADR-044), no su id de catálogo.
    /// Lo que el txt no puede representar (notas, sort_mode, fecha de creación, conjunto activo)
    /// vive en un sidecar decksmeta.json en %APPDATA% (respeta L5A_DATA_ROOT). Sin instalación
    /// configurada ambas carpetas cuelgan de %APPDATA%\... para no perder la funcionalidad.
    /// Guardado inmediato en cada mutación (los txt son chicos y el evento, esporádico).
    /// </summary>
    public class DeckLibrary
    {
        /// <summary>Valor de <see cref="Models.Deck.Type"/> para las listas (el de la app).</summary>
        public const string ListType = "list";
        public const string DeckType = "deck";
        private class DeckMeta
        {
            [JsonPropertyName("notes")] public string Notes { get; set; }
            [JsonPropertyName("sortMode")] public string SortMode { get; set; }
            [JsonPropertyName("createdAt")] public long CreatedAt { get; set; }
            [JsonPropertyName("sortOrder")] public int SortOrder { get; set; }

            /// <summary>
            /// Cartas recortadas (quantity=0): están en el mazo pero no se juegan
            /// ([ADR-046](../../../docs/DECISIONS.md)). Van acá y no al .txt porque el juego lee
            /// ese archivo y no tiene forma de expresar "esta carta está pero no cuenta": el
            /// sidecar es exactamente para lo que el formato del juego no representa.
            /// Por NOMBRE de carta, como todo lo que cruza el disco (ADR-044).
            /// </summary>
            [JsonPropertyName("trimmed")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<string> Trimmed { get; set; }

            /// <summary>
            /// Vocabulario de etiquetas del mazo (ADR-057). Va aparte de las asignaciones para que
            /// una etiqueta recién creada, todavía sin cartas, sobreviva al reinicio.
            /// </summary>
            [JsonPropertyName("tags")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<TagMeta> Tags { get; set; }

            /// <summary>Qué etiquetas tiene cada carta, por NOMBRE de carta (ADR-044).</summary>
            [JsonPropertyName("cardTags")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public Dictionary<string, List<string>> CardTags { get; set; }
        }

        private class TagMeta
        {
            [JsonPropertyName("name")] public string Name { get; set; }
            [JsonPropertyName("color")] public string Color { get; set; }
        }

        private class MetaDto
        {
            [JsonPropertyName("activeDeck")] public string ActiveDeck { get; set; }
            [JsonPropertyName("activeList")] public string ActiveList { get; set; }
            [JsonPropertyName("decks")]
            public Dictionary<string, DeckMeta> Decks { get; set; } = new Dictionary<string, DeckMeta>();
        }

        private readonly Dictionary<string, string> _fileByDeckId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        // Última escritura del .txt = última vez que se tocó el mazo. Se usa para ordenar por
        // "más reciente" en el editor. No va al modelo (que es espejo del de la app) ni al
        // sidecar: el dato ya lo tiene el sistema de archivos.
        private readonly Dictionary<string, DateTime> _updatedAt =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly string _metaPath;
        private bool _loading;
        private string _activeDeckId;
        private string _activeListId;

        public DeckRepository Repo { get; }
        public string DecksDir { get; }
        public string ListsDir { get; }

        /// <summary>Cambió el mazo activo (o dejó de haber uno).</summary>
        public event Action ActiveDeckChanged;

        /// <summary>Cambió la lista activa (o dejó de haber una).</summary>
        public event Action ActiveListChanged;

        private static DeckLibrary _current;

        /// <summary>
        /// Instancia compartida del proceso (Buscador y editor operan sobre los mismos mazos).
        /// Si cambia la carpeta o el catálogo se recrea.
        /// </summary>
        public static DeckLibrary GetOrLoad(CardCatalog catalog, string installPath)
        {
            var dir = ResolveDecksDir(installPath);
            if (_current == null || _current.DecksDir != dir || _current.Repo.Catalog != catalog)
                _current = new DeckLibrary(catalog, dir, null, ResolveListsDir(installPath));
            return _current;
        }

        public static string ResolveDecksDir(string installPath) =>
            string.IsNullOrEmpty(installPath)
                ? Path.Combine(AppConstants.AppDataDir, "Decks")
                : Path.Combine(installPath, "Decks");

        /// <summary>
        /// Carpeta de listas: hermana de Decks, no adentro. Sun and Moon lee &lt;juego&gt;\Decks\
        /// y no hay que arriesgarse a que una lista le aparezca como mazo jugable.
        /// </summary>
        public static string ResolveListsDir(string installPath) =>
            string.IsNullOrEmpty(installPath)
                ? Path.Combine(AppConstants.AppDataDir, "Lists")
                : Path.Combine(installPath, "Lists");

        public DeckLibrary(CardCatalog catalog, string decksDir, string metaPath = null,
                           string listsDir = null)
        {
            DecksDir = decksDir;
            // por defecto, hermana de la carpeta de mazos (así el selftest y los tests manuales
            // que pasan un decksDir temporal se llevan las listas al mismo lado)
            ListsDir = listsDir ?? Path.Combine(
                Path.GetDirectoryName(decksDir.TrimEnd(Path.DirectorySeparatorChar)) ?? "", "Lists");
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
                LoadDir(DecksDir, DeckType, meta);
                LoadDir(ListsDir, ListType, meta);

                if (meta.ActiveDeck != null) _activeDeckId = FindByFile(meta.ActiveDeck, DeckType);
                if (meta.ActiveList != null) _activeListId = FindByFile(meta.ActiveList, ListType);
            }
            finally
            {
                _loading = false;
            }
        }

        private void LoadDir(string dir, string type, MetaDto meta)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*.txt")
                                          .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var baseName = Path.GetFileNameWithoutExtension(file);
                    var deck = SunAndMoonDeckFile.Parse(File.ReadAllText(file), Repo);
                    deck.Id = Guid.NewGuid().ToString();
                    deck.Name = baseName;
                    deck.Type = type;
                    // una lista es un conjunto: si el txt traía cantidades (editado a mano, o
                    // copiado de un mazo), se recortan a 1 en vez de rechazar el archivo
                    if (type == ListType)
                        foreach (var entry in deck.Cards) entry.Quantity = 1;
                    DeckMeta m = null;
                    meta.Decks?.TryGetValue(MetaKey(baseName, type), out m);
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
                    if (type != ListType) RestoreTrimmed(deck, m);
                    RestoreTags(deck, m); // después del recorte: una recortada también se etiqueta
                    Repo.AttachLoadedDeck(deck);
                    _fileByDeckId[deck.Id] = baseName;
                    _updatedAt[deck.Id] = File.GetLastWriteTimeUtc(file);
                }
                catch
                {
                    // txt ilegible → se ignora (no se pisa: solo se guardan los que se abren)
                }
            }
        }

        /// <summary>
        /// Reinyecta las cartas recortadas del sidecar como entradas con quantity 0. El .txt
        /// manda: si el nombre ya está en el mazo con cantidad, se ignora la marca de recorte
        /// (alguien editó el archivo a mano y esa es la verdad de lo que se juega).
        /// </summary>
        private void RestoreTrimmed(Models.Deck deck, DeckMeta meta)
        {
            if (meta?.Trimmed == null) return;
            var sortOrder = deck.Cards.Count == 0 ? 0 : deck.Cards.Max(e => e.SortOrder) + 1;
            foreach (var name in meta.Trimmed)
            {
                var card = Repo.GetCardByNameRelaxed(name);
                if (card == null) continue;
                if (deck.Cards.Any(e => string.Equals(e.CardId, card.Id, StringComparison.Ordinal))) continue;
                deck.Cards.Add(new DeckCardEntry
                {
                    CardId = card.Id,
                    Quantity = 0,
                    SortOrder = sortOrder++,
                });
            }
        }

        /// <summary>
        /// Reconstruye las etiquetas del sidecar. Las asignaciones vienen por nombre de carta y se
        /// resuelven con la misma tolerancia que el .txt; las de cartas que ya no están en el mazo
        /// se descartan (la etiqueta acompaña a la carta). Una etiqueta que aparezca solo en
        /// `cardTags` —sidecar editado a mano— se crea igual, para no perder la clasificación.
        /// </summary>
        private void RestoreTags(Models.Deck deck, DeckMeta meta)
        {
            if (meta == null) return;
            if (meta.Tags != null)
                foreach (var tag in meta.Tags)
                {
                    var created = DeckTags.Add(deck, tag?.Name);
                    if (created != null && !string.IsNullOrWhiteSpace(tag.Color)) created.Color = tag.Color;
                }
            if (meta.CardTags == null) return;
            foreach (var pair in meta.CardTags)
            {
                if (pair.Value == null) continue;
                var card = Repo.GetCardByNameRelaxed(pair.Key);
                if (card == null) continue;
                if (!deck.Cards.Any(e => string.Equals(e.CardId, card.Id, StringComparison.Ordinal))) continue;
                foreach (var name in pair.Value)
                {
                    var tag = DeckTags.Add(deck, name); // ya existe → devuelve la que había
                    if (tag != null) DeckTags.Set(deck, card.Id, tag.Name, true);
                }
            }
        }

        private string FindByFile(string baseName, string type) =>
            Repo.Decks.FirstOrDefault(d => d.Type == type &&
                _fileByDeckId.TryGetValue(d.Id, out var f) && f == baseName)?.Id;

        /// <summary>
        /// Clave del sidecar. Los mazos conservan el nombre pelado (compatibilidad con los
        /// decksmeta.json ya escritos); las listas van prefijadas, porque un mazo y una lista
        /// pueden llamarse igual (viven en carpetas distintas).
        /// </summary>
        private static string MetaKey(string baseName, string type) =>
            type == ListType ? ListType + ":" + baseName : baseName;

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

        // ------------------------------------------------------------------ vistas por tipo

        /// <summary>Solo los mazos (los .txt de la carpeta del juego).</summary>
        public IEnumerable<Models.Deck> Decks => Repo.Decks.Where(d => d.Type != ListType);

        /// <summary>Solo las listas (material de armado; el juego no las ve).</summary>
        public IEnumerable<Models.Deck> Lists => Repo.Decks.Where(d => d.Type == ListType);

        // ------------------------------------------------------------------ activos

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

        /// <summary>Lista activa global (destino del ≡ de las celdas del Buscador). Puede ser null.</summary>
        public string ActiveListId
        {
            get => _activeListId;
            set
            {
                if (_activeListId == value) return;
                _activeListId = value;
                PersistMeta();
                ActiveListChanged?.Invoke();
            }
        }

        public Models.Deck ActiveList => _activeListId == null ? null : Repo.GetDeckById(_activeListId);

        // ------------------------------------------------------------------ operaciones con archivo

        /// <summary>Crea un mazo con nombre único y lo activa (como la app). Devuelve el id.</summary>
        public string CreateDeck(string name) => Create(name, DeckType);

        /// <summary>Crea una lista con nombre único y la activa. Devuelve el id.</summary>
        public string CreateList(string name) => Create(name, ListType);

        private string Create(string name, string type)
        {
            var id = Repo.CreateDeck(UniqueName(name, type), type);
            if (type == ListType) ActiveListId = id;
            else ActiveDeckId = id;
            return id;
        }

        public void RenameDeck(string id, string newName)
        {
            var deck = Repo.GetDeckById(id);
            if (deck == null) return;
            newName = (newName ?? "").Trim();
            if (newName.Length == 0 || newName == deck.Name) return;
            // dispara DeckChanged → guarda y renombra el txt
            Repo.RenameDeck(id, UniqueName(newName, deck.Type));
        }

        public void DeleteDeck(string id)
        {
            var deck = Repo.GetDeckById(id);
            if (deck == null) return;
            var isList = deck.Type == ListType;
            if (_fileByDeckId.TryGetValue(id, out var baseName))
            {
                TryDelete(PathFor(baseName, deck.Type));
                _fileByDeckId.Remove(id);
            }
            Repo.DeleteDeck(id);
            if (_activeDeckId == id) _activeDeckId = null;
            if (_activeListId == id) _activeListId = null;
            PersistMeta();
            if (isList) ActiveListChanged?.Invoke();
            else ActiveDeckChanged?.Invoke();
        }

        /// <summary>Duplicar (no existe en la app; acá es export → conjunto nuevo → import).</summary>
        public string DuplicateDeck(string id)
        {
            var deck = Repo.GetDeckById(id);
            if (deck == null) return null;
            var copyId = Repo.CreateDeck(UniqueName(deck.Name, deck.Type), deck.Type);
            if (deck.Type == ListType)
            {
                // Una lista NO se duplica por export→import: ImportCards aplica la regla de slot
                // inicial y se comería todas las Bamboo Harvesters menos una. Se copia entrada
                // por entrada, que es lo que significa duplicar una lista.
                foreach (var entry in deck.Cards.Where(e => e.Quantity > 0))
                    Repo.SetInList(copyId, entry.CardId, true);
            }
            else Repo.ImportCards(copyId, Repo.ExportDeck(id));
            // Las etiquetas son del mazo, así que la copia se las lleva: duplicar un mazo para
            // probar una variante y perder la clasificación sería empezar de cero.
            DeckTags.CopyTo(deck, Repo.GetDeckById(copyId));
            PersistMeta();
            return copyId;
        }

        /// <summary>
        /// Nombre libre dentro de su tipo: agrega " (2)", " (3)"… si ya existe uno igual o el
        /// archivo está ocupado. Un mazo y una lista SÍ pueden llamarse igual: son carpetas
        /// distintas y significan cosas distintas.
        /// </summary>
        public string UniqueName(string name, string type = DeckType)
        {
            name = SanitizeFileName((name ?? "").Trim());
            if (name.Length == 0) name = type == ListType ? "Lista" : "Mazo";
            var candidate = name;
            var n = 2;
            while (Repo.Decks.Any(d => d.Type == type &&
                       string.Equals(d.Name, candidate, StringComparison.OrdinalIgnoreCase))
                   || File.Exists(PathFor(candidate, type)))
                candidate = name + " (" + n++ + ")";
            return candidate;
        }

        // ------------------------------------------------------------------ persistencia

        private void OnDeckChanged(Models.Deck deck)
        {
            if (_loading) return;
            SaveDeck(deck);
        }

        /// <summary>Escribe el txt del conjunto (renombrando el archivo si cambió el nombre) + sidecar.</summary>
        public void SaveDeck(Models.Deck deck)
        {
            var dir = DirFor(deck.Type);
            Directory.CreateDirectory(dir);
            var baseName = SanitizeFileName(deck.Name);
            if (_fileByDeckId.TryGetValue(deck.Id, out var oldBase) && oldBase != baseName)
                TryDelete(PathFor(oldBase, deck.Type));
            File.WriteAllText(PathFor(baseName, deck.Type), SunAndMoonDeckFile.Write(deck, Repo));
            _fileByDeckId[deck.Id] = baseName;
            _updatedAt[deck.Id] = DateTime.UtcNow;
            PersistMeta();
        }

        /// <summary>
        /// Guarda **solo** el sidecar. Para lo que el juego no ve —las etiquetas— reescribir el
        /// .txt no aporta nada y toca un archivo ajeno sin necesidad (ADR-024).
        /// </summary>
        public void SaveMeta() => PersistMeta();

        /// <summary>
        /// Última vez que se escribió el .txt del conjunto (para ordenar por "más reciente").
        /// Sin dato —conjunto recién creado que todavía no se guardó— cae en su fecha de creación.
        /// </summary>
        public DateTime UpdatedAt(Models.Deck deck)
        {
            if (deck == null) return DateTime.MinValue;
            if (_updatedAt.TryGetValue(deck.Id, out var t)) return t;
            return DateTimeOffset.FromUnixTimeMilliseconds(deck.CreatedAt).UtcDateTime;
        }

        private void PersistMeta()
        {
            var dto = new MetaDto
            {
                ActiveDeck = FileOf(_activeDeckId),
                ActiveList = FileOf(_activeListId),
            };
            foreach (var deck in Repo.Decks)
            {
                if (!_fileByDeckId.TryGetValue(deck.Id, out var baseName)) continue;
                var trimmed = deck.Type == ListType ? null : deck.Cards
                    .Where(e => e.Quantity == 0)
                    .Select(e => Repo.GetCardById(e.CardId)?.Name)
                    .Where(n => n != null)
                    .ToList();
                // Sacar una carta con ✕ se lleva su clasificación: acá se limpia lo que quedó
                // colgando, así memoria y disco dicen lo mismo.
                DeckTags.Prune(deck);
                dto.Decks[MetaKey(baseName, deck.Type)] = new DeckMeta
                {
                    Notes = deck.Notes,
                    SortMode = deck.SortMode,
                    CreatedAt = deck.CreatedAt,
                    SortOrder = deck.SortOrder,
                    Trimmed = trimmed != null && trimmed.Count > 0 ? trimmed : null,
                    Tags = deck.Tags.Count == 0 ? null : deck.Tags
                        .Select(t => new TagMeta { Name = t.Name, Color = t.Color })
                        .ToList(),
                    CardTags = CardTagsByName(deck),
                };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_metaPath));
            File.WriteAllText(_metaPath,
                JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>
        /// Asignaciones traducidas de CardId a NOMBRE de carta (ADR-044). null si no hay ninguna,
        /// para no ensuciar el sidecar de los mazos que no usan etiquetas.
        /// </summary>
        private Dictionary<string, List<string>> CardTagsByName(Models.Deck deck)
        {
            Dictionary<string, List<string>> result = null;
            foreach (var pair in deck.CardTags)
            {
                if (pair.Value == null || pair.Value.Count == 0) continue;
                var name = Repo.GetCardById(pair.Key)?.Name;
                if (name == null) continue;
                if (result == null) result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                result[name] = pair.Value.ToList();
            }
            return result;
        }

        private string FileOf(string deckId) =>
            deckId != null && _fileByDeckId.TryGetValue(deckId, out var f) ? f : null;

        private string DirFor(string type) => type == ListType ? ListsDir : DecksDir;

        private string PathFor(string baseName, string type) =>
            Path.Combine(DirFor(type), baseName + ".txt");

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
