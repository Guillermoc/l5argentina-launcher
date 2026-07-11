using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services.Deck
{
    /// <summary>
    /// Lectura/escritura del formato .txt de mazos de Sun and Moon (carpeta &lt;juego&gt;\Decks\):
    ///   Legality: Ivory
    ///   (línea en blanco)
    ///   The Seventh Tower            ← starter sin prefijo (stronghold/sensei)
    ///   (bloques separados por línea en blanco)
    ///   2x Jade Works                ← "Nx Nombre"
    /// El parser además tolera el formato de export de la app Android ("N Nombre" y líneas
    /// "# ..."), así se puede tirar un export del celu dentro de la carpeta y funciona.
    /// Las líneas que no matchean el catálogo se conservan crudas (Deck.Unknown) y se
    /// reescriben verbatim: el launcher nunca destruye contenido de un txt ajeno.
    /// </summary>
    public static class SunAndMoonDeckFile
    {
        private static readonly Regex QtyXRe = new Regex(@"^(\d+)\s*[xX]\s+(.+)$");
        private static readonly Regex QtyPlainRe = new Regex(@"^(\d+)\s+(.+)$");
        private static readonly Regex SortModeRe = new Regex(@"^#\s*sort_mode:\s*(\S+)$");
        private static readonly Regex HeaderRe = new Regex(@"^.*\s*-\s*\(\d+\)\s*\[\d+\]$");
        private static readonly Regex LegalityRe = new Regex(@"^Legality:\s*(.*)$", RegexOptions.IgnoreCase);
        private static readonly Regex NewlineRe = new Regex("\r\n|\n|\r");

        /// <summary>
        /// Parsea el contenido de un txt sobre un Deck nuevo (sin Id/Name — los pone el caller).
        /// Los nombres se resuelven contra el catálogo con el mismo criterio que el import de la
        /// app (match exacto Ordinal, con fallback de trim porque los txt a mano traen espacios).
        /// </summary>
        public static Models.Deck Parse(string text, DeckRepository repo)
        {
            var deck = new Models.Deck();
            var noteLines = new List<string>();
            var sortOrder = 0;

            foreach (var line in NewlineRe.Split(text ?? ""))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                var legality = LegalityRe.Match(trimmed);
                if (legality.Success && deck.Legality == null)
                {
                    deck.Legality = legality.Groups[1].Value.Trim();
                    continue;
                }

                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    var sortMode = SortModeRe.Match(trimmed);
                    if (sortMode.Success) deck.SortMode = sortMode.Groups[1].Value;
                    else
                    {
                        var content = trimmed.Substring(1).Trim();
                        if (!HeaderRe.IsMatch(content)) noteLines.Add(content);
                    }
                    continue;
                }

                int qty;
                string name;
                var m = QtyXRe.Match(trimmed);
                if (!m.Success) m = QtyPlainRe.Match(trimmed);
                if (m.Success && int.TryParse(m.Groups[1].Value, out qty))
                    name = m.Groups[2].Value.Trim();
                else
                {
                    qty = 1;
                    name = trimmed;
                }

                var card = repo.GetCardByNameRelaxed(name);
                if (card == null)
                {
                    deck.Unknown.Add(new UnknownDeckLine { Raw = trimmed });
                    continue;
                }

                var existing = deck.Cards
                    .FirstOrDefault(e => string.Equals(e.CardId, card.Id, StringComparison.Ordinal));
                if (existing != null) existing.Quantity += qty;
                else deck.Cards.Add(new DeckCardEntry
                {
                    CardId = card.Id,
                    Quantity = qty,
                    SortOrder = sortOrder++,
                    // si el txt usaba otro nombre (sufijo "(N)", case, "- exp"), se conserva para
                    // reescribirlo idéntico: el juego tiene que seguir reconociendo su mazo
                    SourceName = string.Equals(name, card.Name, StringComparison.Ordinal) ? null : name,
                });
            }

            if (noteLines.Count > 0) deck.Notes = string.Join("\n", noteLines);
            return deck;
        }

        /// <summary>
        /// Serializa el mazo al formato de Sun and Moon. Starter (stronghold/sensei/wind) va sin
        /// prefijo como en los txt que trae el juego; el resto "Nx Nombre" agrupado por tipo en
        /// el orden canónico. Las recortadas (quantity=0) no se escriben; las líneas desconocidas
        /// se anexan verbatim al final.
        /// </summary>
        public static string Write(Models.Deck deck, DeckRepository repo)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(deck.Legality))
                sb.Append("Legality: ").Append(deck.Legality.Trim()).Append("\r\n\r\n");

            var entries = deck.Cards
                .Where(e => e.Quantity > 0)
                .Select(e => (Entry: e, Card: repo.GetCardById(e.CardId)))
                .Where(x => x.Card != null)
                .ToList();

            // starter primero, en el orden del área starter del juego
            var starter = entries
                .Where(x => x.Card.DeckGroup == "starter")
                .OrderBy(x => DeckGroups.StarterRank(x.Card.Type))
                .ThenBy(x => x.Card.InitialHoldingSlot ?? "zzz", StringComparer.Ordinal)
                .ThenBy(x => x.Card.Name, StringComparer.Ordinal)
                .ToList();
            if (starter.Count > 0)
            {
                foreach (var x in starter)
                {
                    var n = WriteName(x, repo);
                    sb.Append(x.Entry.Quantity == 1 ? n : x.Entry.Quantity + "x " + n).Append("\r\n");
                }
                sb.Append("\r\n");
            }

            foreach (var group in new[] { "dynasty", "fate" })
            {
                var groupEntries = entries
                    .Where(x => string.Equals(x.Card.DeckGroup, group, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => DeckGroups.TypeOrderInGroup(group, x.Card.Type))
                    .ThenBy(x => x.Card.Name, StringComparer.Ordinal)
                    .ToList();
                foreach (var typeGroup in groupEntries.GroupBy(x => x.Card.Type))
                {
                    foreach (var x in typeGroup)
                        sb.Append(x.Entry.Quantity).Append("x ").Append(WriteName(x, repo)).Append("\r\n");
                    sb.Append("\r\n");
                }
            }

            // resto de grupos (deckGroup vacío/raro) para no perder nada
            var leftovers = entries.Where(x =>
                x.Card.DeckGroup != "starter" &&
                !string.Equals(x.Card.DeckGroup, "dynasty", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x.Card.DeckGroup, "fate", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var x in leftovers)
                sb.Append(x.Entry.Quantity).Append("x ").Append(WriteName(x, repo)).Append("\r\n");
            if (leftovers.Count > 0) sb.Append("\r\n");

            foreach (var unknown in deck.Unknown)
                sb.Append(unknown.Raw).Append("\r\n");

            return sb.ToString().TrimEnd('\r', '\n') + "\r\n";
        }

        /// <summary>
        /// Nombre para el txt: el original del archivo si difería del catálogo, o el nombre del
        /// catálogo con el sufijo "(N)" quitado (Sun and Moon usa el nombre pelado).
        /// </summary>
        private static string WriteName((DeckCardEntry Entry, Card Card) x, DeckRepository repo) =>
            x.Entry.SourceName ?? repo.SunAndMoonNameOf(x.Card);
    }
}
