using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services.Deck
{
    /// <summary>
    /// Única capa de mutación de mazos: port 1:1 de DeckRepository.kt de la app Android
    /// (docs/functional/deckbuilder-launcher/). En vez de Room/SQLite opera sobre las listas en
    /// memoria de <see cref="Models.Deck"/> con las mismas semánticas (= de SQLite → Ordinal,
    /// getByName → primera carta en orden de inserción del cards.json). La paridad la
    /// verifican los 25 golden tests del deckbuilder en el SelfTest.
    /// Los ⚠️ de las specs se portan a propósito (NoActiveDeck = "carta o mazo inexistente",
    /// dos strongholds permitidos, banned cuenta como notFound en import, etc.).
    /// </summary>
    public class DeckRepository
    {
        private static readonly Regex VersionSuffixRe = new Regex(@"^(.*) \(\d+\)$");

        private readonly List<Models.Deck> _decks = new List<Models.Deck>();
        private readonly Dictionary<string, Card> _cardsById;
        private readonly Dictionary<string, Card> _cardsByName;
        private readonly Dictionary<string, Card> _cardsByNameCI;
        private readonly Dictionary<string, Card> _cardsByBaseName;
        private readonly CardCatalog _catalog;

        /// <summary>Un mazo cambió de contenido/metadata (para persistir su archivo).</summary>
        public event Action<Models.Deck> DeckChanged;

        /// <summary>La lista de mazos cambió (alta/baja/rename).</summary>
        public event Action DeckListChanged;

        public DeckRepository(CardCatalog catalog)
        {
            _catalog = catalog;
            _cardsById = new Dictionary<string, Card>(StringComparer.Ordinal);
            _cardsByName = new Dictionary<string, Card>(StringComparer.Ordinal);
            _cardsByNameCI = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);
            _cardsByBaseName = new Dictionary<string, Card>(StringComparer.Ordinal);
            foreach (var card in catalog.Cards)
            {
                if (card.Id != null && !_cardsById.ContainsKey(card.Id)) _cardsById[card.Id] = card;
                if (card.Name == null) continue;
                // getByName = "WHERE name = ? LIMIT 1" → primera en orden de inserción (rowid)
                if (!_cardsByName.ContainsKey(card.Name)) _cardsByName[card.Name] = card;
                if (!_cardsByNameCI.ContainsKey(card.Name)) _cardsByNameCI[card.Name] = card;
                // "The Impregnable Fortress of the Crab (1)" → base sin sufijo, si el catálogo no
                // tiene una carta que se llame exactamente así (los txt del juego usan la base)
                var m = VersionSuffixRe.Match(card.Name);
                if (m.Success && !_cardsByBaseName.ContainsKey(m.Groups[1].Value))
                    _cardsByBaseName[m.Groups[1].Value] = card;
            }
            foreach (var name in _cardsByName.Keys)
                _cardsByBaseName.Remove(name); // la base existe como carta propia → sin alias
        }

        public CardCatalog Catalog => _catalog;

        // ------------------------------------------------------------------ catálogo

        public Card GetCardById(string id) =>
            id != null && _cardsById.TryGetValue(id, out var c) ? c : null;

        public Card GetCardByName(string name) =>
            name != null && _cardsByName.TryGetValue(name, out var c) ? c : null;

        /// <summary>
        /// Resolución tolerante para los .txt de Sun and Moon (NO para importCards, que porta el
        /// match exacto de la app): exacto → trim → stronghold sin sufijo "(N)" → case-insensitive
        /// → "- exp" omitido. Los txt que trae el juego resuelven completos con esta cadena.
        /// </summary>
        public Card GetCardByNameRelaxed(string name)
        {
            if (name == null) return null;
            var exact = GetCardByName(name) ?? GetCardByName(name.Trim());
            if (exact != null) return exact;
            name = name.Trim();
            if (_cardsByBaseName.TryGetValue(name, out var byBase)) return byBase;
            if (_cardsByNameCI.TryGetValue(name, out var ci)) return ci;
            if (_cardsByName.TryGetValue(name + " - exp", out var exp)) return exp;
            return null;
        }

        /// <summary>
        /// Nombre a escribir en un txt de Sun and Moon para una carta del catálogo: el sufijo de
        /// versión "(N)" se quita si la base no es otra carta (el juego usa el nombre pelado).
        /// </summary>
        public string SunAndMoonNameOf(Card card)
        {
            var m = VersionSuffixRe.Match(card.Name ?? "");
            if (m.Success && !_cardsByName.ContainsKey(m.Groups[1].Value)) return m.Groups[1].Value;
            return card.Name;
        }

        /// <summary>Slots requeridos (observeRequiredSlots: required=1, orden del filters.json).</summary>
        public List<InitialHoldingSlot> RequiredSlots =>
            _catalog.InitialHoldingSlots.Where(s => s.Required).ToList();

        // ------------------------------------------------------------------ deck CRUD

        public IReadOnlyList<Models.Deck> Decks => _decks;

        public Models.Deck GetDeckById(string id) =>
            _decks.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));

        public string CreateDeck(string name, string type = "deck")
        {
            var deck = new Models.Deck
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Type = type,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SortOrder = _decks.Where(d => d.Type == type)
                                  .Select(d => (int?)d.SortOrder).Max() ?? -1,
            };
            deck.SortOrder += 1;
            _decks.Add(deck);
            DeckListChanged?.Invoke();
            DeckChanged?.Invoke(deck);
            return deck.Id;
        }

        /// <summary>Registra un mazo ya cargado desde disco (no dispara persistencia).</summary>
        public void AttachLoadedDeck(Models.Deck deck) => _decks.Add(deck);

        public void DeleteDeck(string id)
        {
            var deck = GetDeckById(id);
            if (deck == null) return;
            _decks.Remove(deck);
            DeckListChanged?.Invoke();
        }

        public void RenameDeck(string id, string name)
        {
            var deck = GetDeckById(id);
            if (deck == null) return;
            deck.Name = name;
            DeckListChanged?.Invoke();
            DeckChanged?.Invoke(deck);
        }

        public void UpdateNotes(string deckId, string notes)
        {
            var deck = GetDeckById(deckId);
            if (deck == null) return;
            SetNotes(deck, notes);
            DeckChanged?.Invoke(deck);
        }

        public void UpdateSortMode(string deckId, string sortMode)
        {
            var deck = GetDeckById(deckId);
            if (deck == null) return;
            deck.SortMode = sortMode;
            DeckChanged?.Invoke(deck);
        }

        // ------------------------------------------------------------------ lectura de cartas

        public DeckCardEntry GetDeckCard(string deckId, string cardId) =>
            GetDeckById(deckId)?.Cards
                .FirstOrDefault(e => string.Equals(e.CardId, cardId, StringComparison.Ordinal));

        /// <summary>
        /// Entradas del mazo con su carta resuelta (getDeckCardsWithCards): las que no están
        /// en el catálogo quedan fuera, como el filtrado de huérfanas del repo original.
        /// </summary>
        public List<(DeckCardEntry Entry, Card Card)> GetCardsWithCards(string deckId)
        {
            var deck = GetDeckById(deckId);
            if (deck == null) return new List<(DeckCardEntry, Card)>();
            return deck.Cards
                .Select(e => (Entry: e, Card: GetCardById(e.CardId)))
                .Where(x => x.Card != null)
                .ToList();
        }

        public int TotalCardCount(string deckId) =>
            GetDeckById(deckId)?.Cards.Sum(e => e.Quantity) ?? 0;

        /// <summary>SQL countCardsInSlot: cartas del mazo con ese slot y quantity>0.</summary>
        public int CountCardsInSlot(string deckId, string slot)
        {
            var deck = GetDeckById(deckId);
            if (deck == null) return 0;
            return deck.Cards.Count(e =>
            {
                if (e.Quantity <= 0) return false;
                var card = GetCardById(e.CardId);
                return card != null &&
                       string.Equals(card.InitialHoldingSlot, slot, StringComparison.Ordinal);
            });
        }

        // ------------------------------------------------------------------ mutación de cartas

        public AddCardResult AddCard(string deckId, string cardId)
        {
            var card = GetCardById(cardId);
            if (card == null) return AddCardResult.NoActiveDeck;
            if (card.Banned) return AddCardResult.Banned;

            var deck = GetDeckById(deckId);
            if (deck == null) return AddCardResult.NoActiveDeck;

            if (card.InitialHoldingSlot != null && CountCardsInSlot(deckId, card.InitialHoldingSlot) > 0)
                return AddCardResult.SlotAlreadyCovered;

            var maxCopies = deck.Type == "list" ? 1 : card.MaxCopies;
            var existing = GetDeckCard(deckId, cardId);
            var current = existing?.Quantity ?? 0;
            if (current >= maxCopies) return AddCardResult.LimitReached(maxCopies);

            var sortOrder = existing == null ? MaxCardSortOrder(deck) + 1 : existing.SortOrder;
            Upsert(deck, cardId, current + 1, sortOrder);
            DeckChanged?.Invoke(deck);
            return AddCardResult.Success;
        }

        /// <summary>
        /// keepRowAtZero = true (pantalla de mazo de la app): quantity 0 = "recortada", la fila
        /// persiste. false (Buscador): la fila se borra al llegar a 0. El launcher usa false en
        /// toda la UI (sin UX de recorte), pero la semántica se porta completa.
        /// </summary>
        public void RemoveCard(string deckId, string cardId, bool keepRowAtZero = false)
        {
            var deck = GetDeckById(deckId);
            var existing = GetDeckCard(deckId, cardId);
            if (deck == null || existing == null) return;
            if (existing.Quantity <= 0) return;
            if (existing.Quantity == 1 && !keepRowAtZero) deck.Cards.Remove(existing);
            else existing.Quantity -= 1;
            DeckChanged?.Invoke(deck);
        }

        /// <summary>Eliminación dura de la entrada (distinto de recortar).</summary>
        public void DeleteCardEntry(string deckId, string cardId)
        {
            var deck = GetDeckById(deckId);
            var existing = GetDeckCard(deckId, cardId);
            if (deck == null || existing == null) return;
            deck.Cards.Remove(existing);
            DeckChanged?.Invoke(deck);
        }

        public void RemoveAllTrimmed(string deckId)
        {
            var deck = GetDeckById(deckId);
            if (deck == null) return;
            deck.Cards.RemoveAll(e => e.Quantity == 0);
            DeckChanged?.Invoke(deck);
        }

        public void SetCardQuantity(string deckId, string cardId, int n, bool keepRowAtZero = false)
        {
            if (n < 0 || n > 3)
                throw new ArgumentOutOfRangeException(nameof(n)); // require(n in 0..3)
            var deck = GetDeckById(deckId);
            if (deck == null) return;
            var existing = GetDeckCard(deckId, cardId);
            if (n == 0 && keepRowAtZero)
            {
                if (existing != null && existing.Quantity != 0) existing.Quantity = 0;
                else return;
            }
            else if (n == 0)
            {
                if (existing == null) return;
                deck.Cards.Remove(existing);
            }
            else if (existing == null)
            {
                Upsert(deck, cardId, n, MaxCardSortOrder(deck) + 1);
            }
            else existing.Quantity = n;
            DeckChanged?.Invoke(deck);
        }

        private static int MaxCardSortOrder(Models.Deck deck) =>
            deck.Cards.Count == 0 ? -1 : deck.Cards.Max(e => e.SortOrder);

        /// <summary>INSERT OR REPLACE de deck_cards: pisa quantity Y sort_order.</summary>
        private void Upsert(Models.Deck deck, string cardId, int quantity, int sortOrder)
        {
            var existing = deck.Cards
                .FirstOrDefault(e => string.Equals(e.CardId, cardId, StringComparison.Ordinal));
            if (existing == null)
                deck.Cards.Add(new DeckCardEntry { CardId = cardId, Quantity = quantity, SortOrder = sortOrder });
            else
            {
                existing.Quantity = quantity;
                existing.SortOrder = sortOrder;
            }
        }

        // ------------------------------------------------------------------ import

        private static readonly Regex LineRe = new Regex(@"^(\d+)\s+(.+)$");
        private static readonly Regex SortModeRe = new Regex(@"^#\s*sort_mode:\s*(\S+)$");
        private static readonly Regex HeaderRe = new Regex(@"^.*\s*-\s*\(\d+\)\s*\[\d+\]$");
        private static readonly Regex NewlineRe = new Regex("\r\n|\n|\r"); // Kotlin lines()

        public ImportResult ImportCards(string deckId, string text)
        {
            var deck = GetDeckById(deckId);
            if (deck == null) return new ImportResult();
            var isList = deck.Type == "list";
            var result = new ImportResult();
            var noteLines = new List<string>();
            var coveredSlots = new HashSet<string>(StringComparer.Ordinal);

            foreach (var line in NewlineRe.Split(text ?? ""))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    var sortModeMatch = SortModeRe.Match(trimmed);
                    if (sortModeMatch.Success)
                        deck.SortMode = sortModeMatch.Groups[1].Value;
                    else
                    {
                        var content = trimmed.Substring(1).Trim(); // removePrefix("#")
                        if (!HeaderRe.IsMatch(content)) noteLines.Add(content);
                    }
                    continue;
                }

                int qty;
                string name;
                var match = LineRe.Match(trimmed);
                if (match.Success)
                {
                    if (!int.TryParse(match.Groups[1].Value, out qty)) continue;
                    name = match.Groups[2].Value.Trim();
                }
                else
                {
                    qty = 1;
                    name = trimmed;
                }

                var card = GetCardByName(name);
                if (card == null || card.Banned)
                {
                    result.NotFound++;
                    result.NotFoundNames.Add(name);
                    continue;
                }

                if (card.InitialHoldingSlot != null)
                {
                    var alreadyInDeck = CountCardsInSlot(deckId, card.InitialHoldingSlot) > 0;
                    if (alreadyInDeck || coveredSlots.Contains(card.InitialHoldingSlot))
                    {
                        result.NotFound++;
                        result.NotFoundNames.Add(name);
                        continue;
                    }
                    coveredSlots.Add(card.InitialHoldingSlot);
                }

                var maxCopies = isList ? 1 : card.MaxCopies;
                var existing = GetDeckCard(deckId, card.Id);
                var current = existing?.Quantity ?? 0;
                var allowed = Math.Min(qty, maxCopies - current);

                if (allowed > 0)
                {
                    // el upsert del import de la app resetea sort_order a 0 (REPLACE de la fila)
                    Upsert(deck, card.Id, current + allowed, 0);
                    result.Imported += allowed;
                }
            }

            if (noteLines.Count > 0)
            {
                var combined = string.Join("\n", noteLines);
                SetNotes(deck, string.IsNullOrWhiteSpace(deck.Notes) ? combined : deck.Notes + "\n" + combined);
            }
            DeckChanged?.Invoke(deck);
            return result;
        }

        private static void SetNotes(Models.Deck deck, string notes) =>
            deck.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;

        // ------------------------------------------------------------------ export

        /// <summary>
        /// Formato de texto propio de la app (portapapeles). Las recortadas (quantity=0) y las
        /// líneas desconocidas no se exportan. Golden `export-roundtrip` verifica el string exacto.
        /// </summary>
        public string ExportDeck(string deckId)
        {
            var deck = GetDeckById(deckId);
            if (deck == null) return "";
            var entries = GetCardsWithCards(deckId).Where(x => x.Entry.Quantity > 0).ToList();
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(deck.Notes))
                foreach (var line in NewlineRe.Split(deck.Notes))
                    sb.Append("# ").Append(line).Append('\n');

            var totalCards = entries.Sum(x => x.Entry.Quantity);
            var totalDistinct = entries.Count;
            sb.Append("# ").Append(deck.Name).Append(" - (").Append(totalCards)
              .Append(") [").Append(totalDistinct).Append("]\n");

            foreach (var group in DeckGroups.Order)
            {
                var groupEntries = entries
                    .Where(x => string.Equals(x.Card.DeckGroup, group, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Card.Type, StringComparer.Ordinal)
                    .ThenBy(x => x.Card.Name, StringComparer.Ordinal)
                    .ToList();
                if (groupEntries.Count == 0) continue;
                sb.Append("# ").Append(Capitalize(group)).Append(" - (")
                  .Append(groupEntries.Sum(x => x.Entry.Quantity)).Append(") [")
                  .Append(groupEntries.Count).Append("]\n");
                foreach (var typeGroup in groupEntries.GroupBy(x => x.Card.Type))
                {
                    sb.Append("# ").Append(Capitalize(typeGroup.Key)).Append(" - (")
                      .Append(typeGroup.Sum(x => x.Entry.Quantity)).Append(") [")
                      .Append(typeGroup.Count()).Append("]\n");
                    foreach (var entry in typeGroup)
                    {
                        if (entry.Entry.Quantity == 1) sb.Append(entry.Card.Name).Append('\n');
                        else sb.Append(entry.Entry.Quantity).Append(' ').Append(entry.Card.Name).Append('\n');
                    }
                }
            }
            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------ espejos de UI (specs)

        /// <summary>Conteo Dynasty (DeckDetailViewModel: solo quantity>0; listas → 0).</summary>
        public int DynastyCount(string deckId) => SideCount(deckId, "dynasty");

        public int FateCount(string deckId) => SideCount(deckId, "fate");

        /// <summary>SIDE_TARGET de la app: objetivo blando (rojo si menor). No bloquea nada.</summary>
        public const int SideTarget = 40;

        private int SideCount(string deckId, string group)
        {
            var deck = GetDeckById(deckId);
            if (deck == null || deck.Type == "list") return 0;
            return GetCardsWithCards(deckId)
                .Where(x => string.Equals(x.Card.DeckGroup, group, StringComparison.Ordinal))
                .Sum(x => x.Entry.Quantity);
        }

        /// <summary>Slots requeridos sin carta elegible en el mazo (aviso blando; listas → vacío).</summary>
        public List<InitialHoldingSlot> UncoveredRequiredSlots(string deckId)
        {
            var deck = GetDeckById(deckId);
            if (deck == null || deck.Type == "list") return new List<InitialHoldingSlot>();
            var covered = new HashSet<string>(
                GetCardsWithCards(deckId)
                    .Where(x => x.Entry.Quantity > 0 && x.Card.InitialHoldingSlot != null)
                    .Select(x => x.Card.InitialHoldingSlot),
                StringComparer.Ordinal);
            return RequiredSlots.Where(s => !covered.Contains(s.SlotId)).ToList();
        }

        /// <summary>Clan del mazo = clan del primer stronghold (MainViewModel/DecksViewModel).</summary>
        public string DeckClan(string deckId) =>
            GetCardsWithCards(deckId)
                .FirstOrDefault(x => x.Card.Type == "stronghold").Card?.Clan;

        /// <summary>
        /// Honor inicial (GoldfishViewModel.buildInitialState): startingHonor del primer
        /// stronghold del área starter ordenada por (starterRank, slot, name); sin stronghold → 0.
        /// </summary>
        public int InitialHonor(string deckId)
        {
            var starter = GetCardsWithCards(deckId)
                .Where(x => x.Entry.Quantity > 0 && x.Card.DeckGroup == "starter")
                .OrderBy(x => DeckGroups.StarterRank(x.Card.Type))
                .ThenBy(x => x.Card.InitialHoldingSlot ?? "zzz", StringComparer.Ordinal)
                .ThenBy(x => x.Card.Name, StringComparer.Ordinal);
            var stronghold = starter.FirstOrDefault(x => x.Card.Type == "stronghold");
            return stronghold.Card?.StartingHonor ?? 0;
        }

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
