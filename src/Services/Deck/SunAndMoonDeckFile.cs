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
    ///   Proxy holding: Border Keep   ← prefijo de proxy: el juego la apoya en la mesa, no en el mazo
    ///   Productive Mine (GoC)        ← edición entre paréntesis
    /// El parser además tolera el formato de export de la app Android ("N Nombre" y líneas
    /// "# ..."), así se puede tirar un export del celu dentro de la carpeta y funciona.
    /// Las líneas que no matchean el catálogo se conservan crudas (Deck.Unknown) y se
    /// reescriben verbatim: el launcher nunca destruye contenido de un txt ajeno.
    /// El formato entero, leído del código del juego, está en
    /// docs/technical/contratos/formato-txt-sun-and-moon.md.
    /// </summary>
    public static class SunAndMoonDeckFile
    {
        /// <summary>
        /// Los doce tipos de proxy que el juego reconoce (`Persistent.TrimProxyType`). Cualquier
        /// otro texto hace que la línea se lea como carta común — "wind" NO está en la lista,
        /// aunque exista como tipo de carta. Se comparan sin distinguir mayúsculas.
        /// </summary>
        public static readonly string[] ProxyTypes =
        {
            "holding", "personality", "ring", "spell", "strategy", "follower",
            "item", "sensei", "stronghold", "celestial", "event", "region",
        };

        /// <summary>
        /// Tipo con el que se marcan los starter holdings al escribir el txt. En minúscula porque
        /// el juego pisa `card.type` con este texto y después lo compara de forma ordinal contra
        /// literales en minúscula ("Holding" no matchearía nada).
        /// </summary>
        private const string StarterProxyType = "holding";

        private static readonly Regex QtyXRe = new Regex(@"^(\d+)\s*[xX]\s+(.+)$");
        private static readonly Regex QtyPlainRe = new Regex(@"^(\d+)\s+(.+)$");
        private static readonly Regex SortModeRe = new Regex(@"^#\s*sort_mode:\s*(\S+)$");
        private static readonly Regex HeaderRe = new Regex(@"^.*\s*-\s*\(\d+\)\s*\[\d+\]$");
        private static readonly Regex LegalityRe = new Regex(@"^Legality:\s*(.*)$", RegexOptions.IgnoreCase);
        private static readonly Regex NewlineRe = new Regex("\r\n|\n|\r");
        /// <summary>Paréntesis final de una línea: "Productive Mine (GoC)" → nombre + edición.</summary>
        private static readonly Regex EditionRe = new Regex(@"^(.*?)\s*\(([^()]*)\)$");

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

                // "Proxy <tipo>: " va PRIMERO, como en el juego: se saca antes de mirar cantidad
                // y nombre. El launcher no hace nada con el tipo, solo lo conserva para
                // reescribir la línea igual (ADR-060).
                string body;
                var proxyType = SplitProxyPrefix(trimmed, out body);

                int qty;
                string name;
                var m = QtyXRe.Match(body);
                if (!m.Success) m = QtyPlainRe.Match(body);
                if (m.Success && int.TryParse(m.Groups[1].Value, out qty))
                    name = m.Groups[2].Value.Trim();
                else
                {
                    qty = 1;
                    name = body.Trim();
                }

                string edition = null;
                var card = repo.GetCardByNameRelaxed(name);
                if (card == null)
                {
                    // "(GoC)" al final es la EDICIÓN, no parte del nombre. Se prueba después del
                    // nombre entero, igual que el juego, para no romper los strongholds cuyo
                    // nombre de catálogo sí lleva un "(1)".
                    var ed = EditionRe.Match(name);
                    if (ed.Success)
                    {
                        var bare = ed.Groups[1].Value.Trim();
                        card = repo.GetCardByNameRelaxed(bare);
                        if (card != null)
                        {
                            edition = ed.Groups[2].Value.Trim();
                            name = bare;
                        }
                    }
                }

                if (card == null)
                {
                    deck.Unknown.Add(new UnknownDeckLine { Raw = trimmed });
                    continue;
                }

                var existing = deck.Cards.FirstOrDefault(e =>
                    string.Equals(e.CardId, card.Id, StringComparison.Ordinal) &&
                    string.Equals(e.ProxyType ?? "", proxyType ?? "", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Edition ?? "", edition ?? "", StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Quantity += qty;
                    continue;
                }

                // La misma carta con otro prefijo u otra edición NO se fusiona: en el juego
                // "2x Productive Mine" + "Proxy holding: Productive Mine" son dos copias en el
                // mazo y una en la mesa, no tres de lo mismo. El modelo tiene una entrada por
                // carta, así que la segunda línea se conserva cruda y se reescribe verbatim
                // (ADR-024: nunca cambiarle el sentido a un archivo ajeno).
                if (deck.Cards.Any(e => string.Equals(e.CardId, card.Id, StringComparison.Ordinal)))
                {
                    deck.Unknown.Add(new UnknownDeckLine { Raw = trimmed });
                    continue;
                }

                deck.Cards.Add(new DeckCardEntry
                {
                    CardId = card.Id,
                    Quantity = qty,
                    SortOrder = sortOrder++,
                    // si el txt usaba otro nombre (sufijo "(N)", case, "- exp"), se conserva para
                    // reescribirlo idéntico: el juego tiene que seguir reconociendo su mazo
                    SourceName = string.Equals(name, card.Name, StringComparison.Ordinal) ? null : name,
                    ProxyType = proxyType,
                    Edition = edition,
                });
            }

            if (noteLines.Count > 0) deck.Notes = string.Join("\n", noteLines);
            return deck;
        }

        /// <summary>
        /// Serializa el mazo al formato de Sun and Moon. Starter (stronghold/sensei/wind) va sin
        /// cantidad como en los txt que trae el juego; el resto "Nx Nombre" agrupado por tipo en
        /// el orden canónico. Las recortadas (quantity=0) no se escriben; las líneas desconocidas
        /// se anexan verbatim al final.
        /// En los MAZOS (no en las listas, que el juego no lee) los starter holdings salen con
        /// "Proxy holding: " adelante, para que el juego los apoye en la mesa (ADR-060).
        /// </summary>
        public static string Write(Models.Deck deck, DeckRepository repo)
        {
            var markStarterHoldings = !string.Equals(deck.Type, "list", StringComparison.Ordinal);
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
                    sb.Append(CardLine(x, repo, bareWhenSingle: true, markStarterHoldings)).Append("\r\n");
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
                        sb.Append(CardLine(x, repo, bareWhenSingle: false, markStarterHoldings)).Append("\r\n");
                    sb.Append("\r\n");
                }
            }

            // resto de grupos (deckGroup vacío/raro) para no perder nada
            var leftovers = entries.Where(x =>
                x.Card.DeckGroup != "starter" &&
                !string.Equals(x.Card.DeckGroup, "dynasty", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(x.Card.DeckGroup, "fate", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var x in leftovers)
                sb.Append(CardLine(x, repo, bareWhenSingle: false, markStarterHoldings)).Append("\r\n");
            if (leftovers.Count > 0) sb.Append("\r\n");

            foreach (var unknown in deck.Unknown)
                sb.Append(unknown.Raw).Append("\r\n");

            return sb.ToString().TrimEnd('\r', '\n') + "\r\n";
        }

        /// <summary>
        /// Una línea de carta completa: "[Proxy tipo: ][Nx ]Nombre[ (edición)]".
        /// El prefijo sale del que traía el archivo, salvo en los starter holdings, donde se
        /// fuerza "Proxy holding: " (en minúscula, que es lo único que el juego reconoce después).
        /// </summary>
        private static string CardLine(
            (DeckCardEntry Entry, Card Card) x, DeckRepository repo,
            bool bareWhenSingle, bool markStarterHoldings)
        {
            var proxy = ProxyMarkerFor(x.Entry, x.Card, markStarterHoldings);
            var sb = new StringBuilder();
            if (proxy != null) sb.Append("Proxy ").Append(proxy).Append(": ");
            if (!bareWhenSingle || x.Entry.Quantity != 1) sb.Append(x.Entry.Quantity).Append("x ");
            sb.Append(WriteName(x, repo));
            if (!string.IsNullOrEmpty(x.Entry.Edition)) sb.Append(" (").Append(x.Entry.Edition).Append(")");
            return sb.ToString();
        }

        /// <summary>
        /// Nombre para el txt: el original del archivo si difería del catálogo, o el nombre del
        /// catálogo con el sufijo "(N)" quitado (Sun and Moon usa el nombre pelado).
        /// </summary>
        private static string WriteName((DeckCardEntry Entry, Card Card) x, DeckRepository repo) =>
            x.Entry.SourceName ?? repo.SunAndMoonNameOf(x.Card);

        /// <summary>
        /// Con qué tipo de proxy se marca una carta al escribirla: "holding" forzado en los
        /// **starter holdings** ([ADR-060](../../../docs/DECISIONS.md#adr-060)) o el que traía el
        /// archivo. null = sin prefijo. Es la regla única que comparten el `.txt` del juego y el
        /// export al portapapeles.
        /// </summary>
        public static string ProxyMarkerFor(DeckCardEntry entry, Card card, bool markStarterHoldings)
        {
            if (markStarterHoldings
                && string.Equals(card?.DeckGroup, "starter", StringComparison.Ordinal)
                && string.Equals(card?.Type, "holding", StringComparison.Ordinal))
                return StarterProxyType;
            return entry?.ProxyType;
        }

        /// <summary>
        /// Traduce un texto escrito en el formato del juego al que entiende `ImportCards`. Va en el
        /// BORDE: `ImportCards` porta el match exacto de nombre de la app
        /// ([ADR-023](../../../docs/DECISIONS.md#adr-023)) y no se toca.
        /// Dos ajustes, línea por línea:
        /// <list type="bullet">
        ///   <item>saca el prefijo "Proxy &lt;tipo&gt;: " — así pegar en el launcher lo que el
        ///   launcher copió cierra el círculo;</item>
        ///   <item>pasa "Nx Nombre" a "N Nombre", que es la **única** forma de cantidad que el
        ///   import de la app reconoce (su regex pide un espacio después del número). Sin esto,
        ///   pegar un .txt de la carpeta Decks rebota en todas las líneas con "2x".</item>
        /// </list>
        /// Las líneas "#" y las que no matchean quedan como estaban.
        /// </summary>
        public static string NormalizeForImport(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var lines = NewlineRe.Split(text);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                string rest;
                if (SplitProxyPrefix(line, out rest) != null) line = rest.Trim();

                var m = QtyXRe.Match(line);
                if (m.Success) line = m.Groups[1].Value + " " + m.Groups[2].Value.Trim();

                lines[i] = line;
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Port de `Persistent.TrimProxyType`: "Proxy &lt;tipo&gt;: resto". "Proxy " se busca sin
        /// distinguir mayúsculas y el separador es el PRIMER ": " de la línea; el texto entre
        /// ambos tiene que ser uno de <see cref="ProxyTypes"/> o no hay prefijo.
        /// Devuelve el tipo tal cual venía escrito (o null) y deja en <paramref name="rest"/> la
        /// línea sin el prefijo.
        /// </summary>
        private static string SplitProxyPrefix(string line, out string rest)
        {
            rest = line;
            var start = line.IndexOf("Proxy ", StringComparison.OrdinalIgnoreCase);
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            // el juego no valida que el ": " esté DESPUÉS del "Proxy " y revienta con un
            // Substring de largo negativo; acá esa línea simplemente no es un proxy
            if (start < 0 || colon < start + 6) return null;

            var type = line.Substring(start + 6, colon - start - 6);
            if (!ProxyTypes.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase)))
                return null;

            rest = line.Remove(start, 6 + type.Length + 2);
            return type;
        }
    }
}
