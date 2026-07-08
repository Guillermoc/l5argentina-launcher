using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services.Search
{
    /// <summary>
    /// Port fiel del pipeline de importación de la app Android (CardImporter + RulesImporter +
    /// FiltersImporter; ver docs/handoff/buscador-launcher/). Produce el mismo estado final que
    /// un sync completo de la app: import con mapa de rules → applyRules (reset + apply).
    /// Los comportamientos "accidentales" documentados con ⚠️ en las specs se portan tal cual:
    /// la paridad la verifican los 53 golden tests (SelfTest).
    /// </summary>
    public static class CatalogImporter
    {
        private static readonly HashSet<string> ActionKwLegal =
            new HashSet<string>(StringComparer.Ordinal) { "ivory", "TwentyFestivals", "samurai" };

        private static readonly HashSet<string> ActionDesignators =
            new HashSet<string>(StringComparer.Ordinal)
            { "Open", "Limited", "Engage", "Battle", "Dynasty", "Reaction", "Interrupt" };

        private static readonly HashSet<string> CompoundActionKeywords =
            new HashSet<string>(StringComparer.Ordinal) { "Bushido Virtue", "Dark Virtue" };

        // <b>Term:</b> o <b>Term</b>:  (labels de action keywords en el texto)
        private static readonly Regex BoldColonRe = new Regex("<b>([^<]+):</b>|<b>([^<]+)</b>:");
        private static readonly Regex FirstBoldRe = new Regex("<b>([^<]+)</b>");
        private static readonly string[] BulletSeparators = { "&#8226;", "•" };

        public static CardCatalog Import(string cardsJson, string rulesJson, string filtersJson)
        {
            var catalog = new CardCatalog();

            // ----- filters.json: sets/formats/keywords + elegibles de holdings iniciales -----
            var initialHoldingByCard = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(filtersJson))
            {
                var filters = JsonNode.Parse(StripBom(filtersJson));
                foreach (var s in AsArray(filters?["sets"]))
                    catalog.Sets.Add(new SetInfo { Code = Str(s?["code"]), Name = Str(s?["name"]) });
                foreach (var f in AsArray(filters?["formats"]))
                {
                    catalog.Formats.Add(new FormatInfo { Code = Str(f?["code"]), Name = Str(f?["name"]) });
                    foreach (var slot in AsArray(f?["initialHoldingSlots"]))
                        foreach (var cardName in AsArray(slot?["eligibleCards"]))
                        {
                            var n = Str(cardName);
                            if (n != null) initialHoldingByCard[n] = Str(slot?["id"]);
                        }
                }
                foreach (var k in AsArray(filters?["keywords"]))
                {
                    var code = Str(k?["code"]);
                    if (code != null) catalog.KeywordCodes.Add(code);
                }
            }

            // ----- rules.json: mapa título → regla (para preferredPrinting en el import) -----
            var rules = ParseRules(rulesJson);

            // ----- cards.json → entidades (CardImporter.toEntity) -----
            var root = JsonNode.Parse(StripBom(cardsJson));
            var array = root as JsonArray ?? root?["cards"] as JsonArray
                ?? throw new InvalidOperationException("Estructura inesperada del cards.json");

            foreach (var node in array)
            {
                var card = ToCard(node as JsonObject, rules, initialHoldingByCard);
                if (card != null) catalog.Cards.Add(card);
            }

            // ----- applyRules (reset + apply): el estado final del sync real es este -----
            ApplyRules(catalog.Cards, rulesJson);

            return catalog;
        }

        // ------------------------------------------------------------------ cards

        private static Card ToCard(JsonObject obj, Dictionary<string, CardRule> rules,
                                   Dictionary<string, string> initialHoldingByCard)
        {
            if (obj == null) return null;
            var type = Str(obj["type"]);
            if (type == null) return null; // carta sin type → descartada (CardImporter:117)

            try
            {
                var id = Str(obj["id"]);
                var name = Str(obj["name"]);

                var printings = ReadPrintings(obj["printings"]);
                var editions = StrList(obj["editions"]);
                var legalitiesTop = StrListLenient(obj["legalities"]);

                var allSets = editions.Count > 0
                    ? editions
                    : NonEmpty(printings.Select(p => p.Edition).Where(e => e != null).Distinct().ToList(),
                               ListOfNotNull(Str(obj["edition"])));
                var allLegalities = legalitiesTop.Count > 0
                    ? legalitiesTop
                    : NonEmpty(printings.SelectMany(p => p.Legalities).Distinct().ToList(),
                               ListOfNotNull(Str(obj["legal"])));

                // keywords: ausente → null (isUnique cae al texto); no-array → lista vacía
                List<string> keywords = null;
                var kwNode = obj["keywords"];
                if (kwNode != null)
                    keywords = kwNode is JsonArray ? StrList(kwNode) : new List<string>();

                CardRule rule;
                rules.TryGetValue(name ?? "", out rule);

                var currentPrinting = printings.FirstOrDefault(p => p.Current) ?? printings.LastOrDefault();
                string imagePath;
                if (rule?.PreferredPrinting == null)
                    imagePath = currentPrinting?.Image ?? Str(obj["image"]);
                else
                    imagePath = printings.FirstOrDefault(p => p.Edition == rule.PreferredPrinting)?.Image
                        ?? $"images/cards/{rule.PreferredPrinting}/{id}.jpg";

                var banned = (rule?.Banned ?? false) || rule?.MaxCopies == 0;
                int maxCopies;
                if (banned) maxCopies = 0;
                else if (rule?.MaxCopies != null) maxCopies = rule.MaxCopies.Value;
                else maxCopies = CalculateMaxCopies(type, keywords, Str(obj["text"]));

                string initialHoldingSlot = null;
                if (name != null) initialHoldingByCard.TryGetValue(name, out initialHoldingSlot);

                var text = Str(obj["text"]);

                return new Card
                {
                    Id = id,
                    Name = name,
                    Type = type,
                    Clan = Str(obj["clan"]),
                    DeckGroup = initialHoldingSlot != null ? "starter" : DeriveDeckGroup(type),
                    Rarity = Str(obj["rarity"]) ?? "",
                    Sets = SerializeList(allSets),
                    Legality = SerializeList(allLegalities),
                    Keywords = SerializeList(keywords ?? new List<string>()),
                    Text = text,
                    Flavor = Str(obj["flavor"]),
                    Force = ParseStatInt(Str(obj["force"])),
                    ForceSpecial = IsSpecialStat(Str(obj["force"])),
                    Chi = ToIntOrNull(Str(obj["chi"])),
                    Cost = ParseStatInt(Str(obj["cost"])),
                    CostSpecial = IsSpecialStat(Str(obj["cost"])),
                    Honor = ParseHonorInt(Str(obj["honor_req"])),
                    HonorReqNegative = IsDashStat(Str(obj["honor_req"])),
                    PersonalHonor = ParseHonorInt(Str(obj["personal_honor"])),
                    PersonalHonorNegative = IsDashStat(Str(obj["personal_honor"])),
                    Focus = ToIntOrNull(Str(obj["focus"])),
                    GoldProduction = ParseStatInt(Str(obj["gold_production"])),
                    GoldProductionSpecial = IsSpecialStat(Str(obj["gold_production"])),
                    ProvinceStrength = ToIntOrNull(Str(obj["province_strength"])),
                    StartingHonor = ToIntOrNull(Str(obj["starting_honor"])),
                    Artist = Str(obj["artist"]),
                    ImagePath = imagePath,
                    PrintingImages = SerializeList(printings.Select(p => p.Image).Where(i => i != null).Distinct().ToList()),
                    MaxCopies = maxCopies,
                    Banned = banned,
                    Note = rule?.Note,
                    LabelText = rule?.LabelText,
                    LabelColor = rule?.LabelColor,
                    HighlightColor = rule?.HighlightColor,
                    InitialHoldingSlot = initialHoldingSlot,
                    Subtitle = ExtractSubtitle(name),
                    ActionKeywords = ExtractActionKeywords(text, allLegalities),
                    NormalizedText = CardSearchEngine.NormalizeTextForWordSearch(text),
                };
            }
            catch
            {
                return null; // cualquier excepción por carta la descarta (CardImporter:180-182)
            }
        }

        private class Printing
        {
            public string Edition;
            public string Image;
            public List<string> Legalities = new List<string>();
            public bool Current;
        }

        private static List<Printing> ReadPrintings(JsonNode node)
        {
            var result = new List<Printing>();
            if (node == null) return result;
            // objeto suelto → array de uno (normalizePrintings)
            var arr = node is JsonArray ja ? ja : new JsonArray(JsonNode.Parse(node.ToJsonString()));
            foreach (var p in arr)
            {
                if (!(p is JsonObject po)) continue;
                result.Add(new Printing
                {
                    Edition = Str(po["edition"]),
                    Image = Str(po["image"]),
                    Legalities = StrListLenient(po["legalities"]),
                    Current = (po["current"] as JsonValue)?.TryGetValue<bool>(out var b) == true && b,
                });
            }
            return result;
        }

        // ------------------------------------------------------------------ rules

        private class CardRule
        {
            public string Title;
            public int? MaxCopies;
            public bool? Banned;
            public string PreferredPrinting;
            public string Note;
            public string LabelText;
            public string LabelColor;
            public string HighlightColor;
        }

        private static Dictionary<string, CardRule> ParseRules(string rulesJson)
        {
            var map = new Dictionary<string, CardRule>(StringComparer.Ordinal);
            foreach (var r in ReadRulesList(rulesJson))
                map[r.Title] = r; // associateBy: la última gana
            return map;
        }

        private static List<CardRule> ReadRulesList(string rulesJson)
        {
            var list = new List<CardRule>();
            if (string.IsNullOrEmpty(rulesJson)) return list;
            var root = JsonNode.Parse(StripBom(rulesJson));
            foreach (var r in AsArray(root?["rules"]))
            {
                var title = Str(r?["title"]);
                if (title == null) continue;
                int? maxCopies = null;
                if (r["maxCopies"] is JsonValue mv && mv.TryGetValue<int>(out var mc)) maxCopies = mc;
                bool? banned = null;
                if (r["banned"] is JsonValue bv && bv.TryGetValue<bool>(out var bb)) banned = bb;
                list.Add(new CardRule
                {
                    Title = title,
                    MaxCopies = maxCopies,
                    Banned = banned,
                    PreferredPrinting = Str(r["preferredPrinting"]),
                    Note = Str(r["note"]),
                    LabelText = Str(r["label"]?["text"]),
                    LabelColor = r["label"] != null ? (Str(r["label"]?["color"]) ?? "#FFFFFF") : null,
                    HighlightColor = Str(r["highlight"]?["color"]),
                });
            }
            return list;
        }

        /// <summary>
        /// Port de RulesImporter.applyRules + CardDao.resetAllRules/applyCardRule*: resetea el
        /// estado de reglas de TODAS las cartas y re-aplica cada regla por título (match exacto,
        /// case-sensitive como el `=` de SQLite). Idempotente.
        /// </summary>
        private static void ApplyRules(List<Card> cards, string rulesJson)
        {
            // resetAllRules
            foreach (var c in cards)
            {
                c.Banned = false;
                c.MaxCopies = (c.Type == "stronghold" || c.Type == "sensei") ? 1
                    : CardSearchEngine.LikeContains(c.Keywords, "|Unique|") ? 1
                    : 3;
                c.Note = null;
                c.LabelText = null;
                c.LabelColor = null;
                c.HighlightColor = null;
            }

            foreach (var rule in ReadRulesList(rulesJson))
            {
                var banned = rule.Banned == true || rule.MaxCopies == 0;
                foreach (var c in cards.Where(c => string.Equals(c.Name, rule.Title, StringComparison.Ordinal)))
                {
                    c.Banned = banned;
                    if (rule.MaxCopies != null) c.MaxCopies = Math.Max(0, rule.MaxCopies.Value);
                    c.Note = rule.Note;
                    c.LabelText = rule.LabelText;
                    c.LabelColor = rule.LabelColor;
                    c.HighlightColor = rule.HighlightColor;
                }
            }
        }

        // ------------------------------------------------------------------ derivaciones

        /// <summary>"4" → 4; "+2" → 2; con '*' → null (special); no parseable → null.</summary>
        private static int? ParseStatInt(string value)
        {
            if (value == null || value.IndexOf('*') >= 0) return null;
            return ToIntOrNull(value);
        }

        private static bool IsSpecialStat(string value) => value != null && value.IndexOf('*') >= 0;

        private static int? ParseHonorInt(string value)
        {
            if (value == null || value == "-") return null;
            return ToIntOrNull(value);
        }

        private static bool IsDashStat(string value) => value == "-";

        /// <summary>Equivalente de Kotlin toIntOrNull(): signo opcional, sin trim de whitespace.</summary>
        private static int? ToIntOrNull(string value)
        {
            if (value == null) return null;
            return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var r)
                ? r : (int?)null;
        }

        private static string ExtractSubtitle(string name)
        {
            if (name == null) return null;
            var idx = name.IndexOf(',');
            return idx == -1 ? null : name.Substring(idx + 1).Trim();
        }

        private static string ExtractActionKeywords(string text, List<string> legalities)
        {
            if (text == null || !legalities.Any(l => ActionKwLegal.Contains(l))) return "";
            var found = new List<string>();
            foreach (Match match in BoldColonRe.Matches(text))
            {
                var raw = (match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : match.Groups[2].Value).Trim();
                foreach (var part in raw.Split(BulletSeparators, StringSplitOptions.None))
                {
                    var words = part.Trim().TrimEnd(':').Trim()
                        .Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 0 || !ActionDesignators.Contains(words[words.Length - 1])) continue;
                    var remaining = words.Take(words.Length - 1).ToList();
                    int i = 0;
                    while (i < remaining.Count)
                    {
                        var twoWord = i + 1 < remaining.Count ? remaining[i] + " " + remaining[i + 1] : "";
                        if (CompoundActionKeywords.Contains(twoWord)) { AddDistinct(found, twoWord); i += 2; }
                        else { AddDistinct(found, remaining[i]); i++; }
                    }
                }
            }
            return SerializeList(found);
        }

        private static void AddDistinct(List<string> list, string value)
        {
            if (!list.Contains(value)) list.Add(value);
        }

        private static int CalculateMaxCopies(string type, List<string> keywords, string text)
        {
            var t = type.ToLowerInvariant();
            if (t == "stronghold" || t == "sensei") return 1;
            if (IsUnique(keywords, text)) return 1;
            return 3;
        }

        private static bool IsUnique(List<string> keywords, string text)
        {
            if (keywords != null)
                return keywords.Any(kw => string.Equals(kw, "Unique", StringComparison.OrdinalIgnoreCase));
            // Fallback: primer bloque <b> del texto, para cartas sin campo keywords
            if (text == null) return false;
            var firstBold = FirstBoldRe.Match(text);
            if (!firstBold.Success) return false;
            return firstBold.Groups[1].Value.Split(BulletSeparators, StringSplitOptions.None)
                .Any(p => string.Equals(p.Trim(), "Unique", StringComparison.OrdinalIgnoreCase));
        }

        private static string DeriveDeckGroup(string type)
        {
            switch (type.ToLowerInvariant())
            {
                case "stronghold": case "sensei": case "wind":
                    return "starter";
                case "celestial": case "event": case "holding": case "region": case "personality":
                    return "dynasty";
                case "ancestor": case "follower": case "ring": case "item": case "spell": case "strategy":
                    return "fate";
                default:
                    return "";
            }
        }

        // ------------------------------------------------------------------ helpers JSON

        private static string StripBom(string raw) =>
            raw.Length > 0 && raw[0] == '\uFEFF' ? raw.Substring(1) : raw;

        private static IEnumerable<JsonNode> AsArray(JsonNode node) =>
            node is JsonArray arr ? arr.Where(n => n != null) : Enumerable.Empty<JsonNode>();

        /// <summary>String laxo: valores no-string (número/bool) se leen como su texto crudo.</summary>
        private static string Str(JsonNode node)
        {
            if (node is JsonValue v)
                return v.TryGetValue<string>(out var s) ? s : v.ToJsonString();
            return null;
        }

        private static List<string> StrList(JsonNode node) =>
            AsArray(node).Select(Str).Where(s => s != null).ToList();

        /// <summary>legalities: string suelto → lista de uno (normalizePrintings).</summary>
        private static List<string> StrListLenient(JsonNode node)
        {
            if (node == null) return new List<string>();
            if (node is JsonArray) return StrList(node);
            var s = Str(node);
            return s != null ? new List<string> { s } : new List<string>();
        }

        private static List<string> NonEmpty(List<string> primary, List<string> fallback) =>
            primary.Count > 0 ? primary : fallback;

        private static List<string> ListOfNotNull(string value) =>
            value != null ? new List<string> { value } : new List<string>();

        private static string SerializeList(List<string> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new StringBuilder("|");
            foreach (var item in list) sb.Append(item).Append('|');
            return sb.ToString();
        }
    }
}
