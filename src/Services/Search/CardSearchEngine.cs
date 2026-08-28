using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services.Search
{
    /// <summary>Entrada de visualización: una carta mostrando una impresión concreta.</summary>
    public class CardPrintingView
    {
        public Card Card { get; set; }
        public string ImagePath { get; set; }
    }

    /// <summary>
    /// Motor de filtrado en memoria: port semántico 1:1 del SQL que genera
    /// CardQueryCompiler.kt de la app Android (docs/functional/buscador-launcher/SPEC-FILTROS.md).
    /// En vez de SQLite (dependencia nativa) replica sus semánticas exactas:
    ///   - LIKE  → contains case-insensitive SOLO ASCII (LikeContains)
    ///   - IN/=  → igualdad Ordinal (case-sensitive, collation BINARY)
    ///   - ORDER BY name → comparación Ordinal; NULLs de stats siempre al final
    ///   - empates exactos → orden de inserción del cards.json (sort estable = rowid)
    /// La paridad la verifican los 53 golden tests en el SelfTest.
    /// Comportamientos accidentales documentados (rareza case-sensitive, subtitle substring)
    /// se portan tal cual; los ajustes de UX se hacen en la capa UI (p. ej. rareza manda P y p).
    /// </summary>
    public class CardSearchEngine
    {
        private static readonly string[] MinorFactions =
            { "shadowlands", "ratling", "naga", "hare", "ronin", "ninja", "monk", "spirit" };

        /// <summary>Sets de la era Loto que el orden canónico fuerza antes de "Samurai".</summary>
        private static readonly HashSet<string> ForceBeforeSamurai =
            new HashSet<string>(StringComparer.Ordinal) { "Filters", "Tomorrow", "TTT", "CotM", "KD" };

        private readonly CardCatalog _catalog;
        private readonly Dictionary<string, int> _setOrder;

        public CardSearchEngine(CardCatalog catalog)
        {
            _catalog = catalog;
            _setOrder = BuildSetOrderMap(catalog.Sets);
        }

        public CardCatalog Catalog => _catalog;

        // ------------------------------------------------------------------ búsqueda

        public List<Card> Search(CardSearchCriteria c, UserCardData user = null)
        {
            user = user ?? UserCardData.Empty;
            var result = _catalog.Cards.Where(card => Matches(card, c, user));
            return Sort(result, c).ToList();
        }

        private bool Matches(Card card, CardSearchCriteria c, UserCardData user)
        {
            // Nombre: AND de substrings por palabra
            foreach (var word in SplitWords(c.Name))
                if (!LikeContains(card.Name, word)) return false;

            // Keywords positivas: AND/OR según keywordMatchAll
            if (c.Keywords.Count > 0)
            {
                bool KwMatch(string kw) =>
                    LikeContains(card.Keywords, "|" + kw + "|")
                    || (c.IncludeActionKeywords && LikeContains(card.ActionKeywords, "|" + kw + "|"))
                    || (c.IncludeSubtitleKeywords && LikeContains(card.Subtitle, kw)); // ⚠️ substring sin pipes

                var ok = c.KeywordMatchAll ? c.Keywords.All(KwMatch) : c.Keywords.Any(KwMatch);
                if (!ok) return false;
            }

            // Keywords negativas: cada una excluye (sin subtitle; hereda includeActionKeywords)
            foreach (var kw in c.NegativeKeywords)
            {
                if (LikeContains(card.Keywords, "|" + kw + "|")
                    || (c.IncludeActionKeywords && LikeContains(card.ActionKeywords, "|" + kw + "|")))
                    return false;
            }

            // Designadores: 3 patrones anclados al cierre del bold
            if (c.Designators.Count > 0)
            {
                var ok = c.Designators.Any(d =>
                    LikeContains(card.Text, d + ":</b>")
                    || LikeContains(card.Text, d + "</b>:")
                    || LikeContains(card.Text, d + "</b>,"));
                if (!ok) return false;
            }

            // Texto de carta: frase entre comillas = substring del HTML crudo;
            // sin comillas = AND de palabras completas sobre el texto normalizado
            var rawText = (c.Text ?? "").Trim();
            if (rawText.Length > 0)
            {
                if (rawText.Length > 2 && rawText.StartsWith("\"") && rawText.EndsWith("\""))
                {
                    if (!LikeContains(card.Text, rawText.Substring(1, rawText.Length - 2))) return false;
                }
                else
                {
                    foreach (var word in SplitWords(rawText))
                        if (!LikeContains(card.NormalizedText, " " + word + " ")) return false;
                }
            }

            // Tipo: IN case-sensitive
            if (c.Types.Count > 0 && !c.Types.Contains(card.Type, StringComparer.Ordinal)) return false;

            // Clan: nombrados + rama sensei-por-keywords + noclan (incluye facciones menores)
            if (c.Clans.Count > 0)
            {
                var hasNoClan = c.Clans.Any(x => string.Equals(x, "noclan", StringComparison.OrdinalIgnoreCase));
                var named = c.Clans.Where(x => !string.Equals(x, "noclan", StringComparison.OrdinalIgnoreCase)).ToList();

                var ok = false;
                if (named.Count > 0)
                {
                    if (card.Clan != null && named.Contains(card.Clan, StringComparer.Ordinal))
                        ok = true;
                    if (!ok && card.Type == "sensei")
                    {
                        foreach (var clanKey in named)
                        {
                            var disp = Capitalize(clanKey);
                            if (LikeContains(card.Keywords, "|" + disp + "|")
                                || LikeContains(card.Keywords, "|" + disp + " Clan|"))
                            { ok = true; break; }
                        }
                        if (!ok && (LikeContains(card.Keywords, "|All Clans|")
                                    || LikeContains(card.Keywords, "|Any|")
                                    || LikeContains(card.Keywords, "|Any Clan|")))
                            ok = true;
                    }
                }
                if (!ok && hasNoClan)
                    ok = card.Clan == null || MinorFactions.Contains(card.Clan, StringComparer.Ordinal);
                if (!ok) return false;
            }

            // Rareza: IN case-sensitive (⚠️ bug portado a propósito; la UI compensa)
            if (c.Rarities.Count > 0 && !c.Rarities.Contains(card.Rarity, StringComparer.Ordinal)) return false;

            // Edición y Formato: LIKE sobre columnas pipe
            if (c.Sets.Count > 0 && !c.Sets.Any(s => LikeContains(card.Sets, "|" + s + "|"))) return false;
            if (c.Formats.Count > 0 && !c.Formats.Any(f => LikeContains(card.Legality, "|" + f + "|"))) return false;

            // Stats
            if (!SpecialOrRange(card.Force, card.ForceSpecial, c.ForceMin, c.ForceMax, c.IncludeSpecialForce)) return false;
            if (c.ChiMin.HasValue && !(card.Chi.HasValue && card.Chi.Value >= c.ChiMin.Value)) return false;
            if (c.ChiMax.HasValue && !(card.Chi.HasValue && card.Chi.Value <= c.ChiMax.Value)) return false;
            if (!SpecialOrRange(card.Cost, card.CostSpecial, c.CostMin, c.CostMax, c.IncludeSpecialCost)) return false;
            if (!SpecialOrRange(card.Honor, card.HonorReqNegative, c.HonorReqMin, c.HonorReqMax, c.IncludeNegativeHonorReq)) return false;
            if (!SpecialOrRange(card.PersonalHonor, card.PersonalHonorNegative, c.PersonalHonorMin, c.PersonalHonorMax, c.IncludeNegativePersonalHonor)) return false;
            if (c.FocusMin.HasValue && !(card.Focus.HasValue && card.Focus.Value >= c.FocusMin.Value)) return false;
            if (c.FocusMax.HasValue && !(card.Focus.HasValue && card.Focus.Value <= c.FocusMax.Value)) return false;

            // Lista
            if (c.ListId != null)
            {
                HashSet<string> ids;
                if (!user.Lists.TryGetValue(c.ListId, out ids) || !ids.Contains(card.Id)) return false;
            }

            // Baneadas / favoritas / ocultas / holdings iniciales
            if (!c.IncludeBanned && card.Banned) return false;
            if (c.OnlyFavorites && !user.Favorites.Contains(card.Id)) return false;
            if (c.OnlyHidden)
            {
                if (!user.Hidden.Contains(card.Id)) return false;
            }
            else if (!c.IncludeHidden && user.Hidden.Contains(card.Id)) return false;
            if (c.OnlyInitialHoldings && card.InitialHoldingSlot == null) return false;

            return true;
        }

        /// <summary>AND (special=1 OR (col IS NOT NULL AND col&gt;=min AND col&lt;=max)) — solo si hay algo activo.</summary>
        private static bool SpecialOrRange(int? value, bool specialFlag, int? min, int? max, bool includeSpecial)
        {
            if (min == null && max == null && !includeSpecial) return true;
            if (includeSpecial && specialFlag) return true;
            if (min == null && max == null) return false;
            return value.HasValue
                && (min == null || value.Value >= min.Value)
                && (max == null || value.Value <= max.Value);
        }

        // ------------------------------------------------------------------ orden

        private IEnumerable<Card> Sort(IEnumerable<Card> cards, CardSearchCriteria c)
        {
            var asc = c.SortAscending;
            if (c.SortField == SortField.NAME || c.SortField == SortField.SET)
            {
                var byName = asc
                    ? cards.OrderBy(x => x.Name, StringComparer.Ordinal)
                    : cards.OrderByDescending(x => x.Name, StringComparer.Ordinal);
                // SET: el orden real se aplica en memoria (el SQL de la app tampoco lo resuelve)
                return c.SortField == SortField.SET ? SortBySet(byName, asc) : byName;
            }

            // Extras del launcher (cabeceras de columna): texto Ordinal, vacíos al final,
            // desempate por nombre ASC. No existen en la app, no los cubren los golden.
            Func<Card, string> strCol = null;
            switch (c.SortField)
            {
                case SortField.TYPE: strCol = x => x.Type; break;
                case SortField.CLAN: strCol = x => x.Clan; break;
                case SortField.RARITY: strCol = x => x.Rarity; break;
            }
            if (strCol != null)
            {
                var ordered2 = cards.OrderBy(x => string.IsNullOrEmpty(strCol(x)) ? 1 : 0);
                var byStr = asc
                    ? ordered2.ThenBy(x => strCol(x) ?? "", StringComparer.OrdinalIgnoreCase)
                    : ordered2.ThenByDescending(x => strCol(x) ?? "", StringComparer.OrdinalIgnoreCase);
                return byStr.ThenBy(x => x.Name, StringComparer.Ordinal);
            }

            Func<Card, int?> col;
            switch (c.SortField)
            {
                case SortField.COST: col = x => x.Cost; break;
                case SortField.FORCE: col = x => x.Force; break;
                case SortField.CHI: col = x => x.Chi; break;
                case SortField.PERSONAL_HONOR: col = x => x.PersonalHonor; break;
                case SortField.HONOR_REQ: col = x => x.Honor; break;
                case SortField.FOCUS: col = x => x.Focus; break;
                default: col = x => null; break;
            }

            // NULLs siempre al final (en ambas direcciones) + desempate por name ASC
            var ordered = cards.OrderBy(x => col(x).HasValue ? 0 : 1);
            var byCol = asc
                ? ordered.ThenBy(x => col(x) ?? 0)
                : ordered.ThenByDescending(x => col(x) ?? 0);
            return byCol.ThenBy(x => x.Name, StringComparer.Ordinal);
        }

        /// <summary>
        /// Orden por Set (port de SearchViewModel.displayCards): rank = posición de la edición
        /// de la impresión mostrada en el orden canónico; fallback al set más temprano de la
        /// carta; desconocidos SIEMPRE al final; desempate por printingId ascendente.
        /// </summary>
        private IEnumerable<Card> SortBySet(IEnumerable<Card> cards, bool asc)
        {
            int Rank(Card card)
            {
                var ed = EditionOf(card.ImagePath);
                if (ed != null && _setOrder.TryGetValue(ed, out var r)) return r;
                return CardSetRank(card);
            }

            var ranked = cards.Select(card => new { Card = card, Rank = Rank(card) });
            var known = ranked.OrderBy(x => x.Rank == int.MaxValue ? 1 : 0);
            var dir = asc ? known.ThenBy(x => x.Rank) : known.ThenByDescending(x => x.Rank);
            return dir.ThenBy(x => PrintingIdOf(x.Card.ImagePath), StringComparer.Ordinal)
                      .Select(x => x.Card);
        }

        /// <summary>Set más temprano (menor índice canónico) de la columna sets; MAX si ninguno.</summary>
        private int CardSetRank(Card card)
        {
            var best = int.MaxValue;
            foreach (var s in SplitPipe(card.Sets))
                if (_setOrder.TryGetValue(s, out var r) && r < best) best = r;
            return best;
        }

        // ------------------------------------------------------------------ expansión por impresión

        /// <summary>
        /// Port de displayCards con expandPrintings (SearchViewModel.kt:155-201, SPEC-FILTROS §15):
        /// cada carta se expande en una entrada por impresión (imagePath preferida + cada
        /// printing_images, sin duplicados). Con filtro de Edición activo, solo las impresiones
        /// de esos sets; si ninguna carpeta coincide, la impresión preferida (no se pierde la
        /// carta). Orden: con SET, bySet global sobre las entradas; con otro orden, se preserva
        /// el orden de las cartas y dentro de cada una (rank de set, printingId).
        /// Capa de visualización pura: no altera el resultado de Search.
        /// </summary>
        public List<CardPrintingView> ExpandForDisplay(List<Card> results, CardSearchCriteria c, bool expandPrintings)
        {
            if (!expandPrintings)
                return results.Select(x => new CardPrintingView { Card = x, ImagePath = x.ImagePath }).ToList();

            var selected = new HashSet<string>(c.Sets, StringComparer.Ordinal);

            List<CardPrintingView> PrintsOf(Card card)
            {
                var prints = ExpandCard(card);
                if (selected.Count == 0) return prints;
                var filtered = prints.Where(p =>
                {
                    var ed = EditionOf(p.ImagePath);
                    return ed != null && selected.Contains(ed);
                }).ToList();
                return filtered.Count > 0
                    ? filtered
                    : new List<CardPrintingView> { new CardPrintingView { Card = card, ImagePath = card.ImagePath } };
            }

            int RankOf(CardPrintingView v)
            {
                var ed = EditionOf(v.ImagePath);
                if (ed != null && _setOrder.TryGetValue(ed, out var r)) return r;
                return CardSetRank(v.Card);
            }

            if (c.SortField == SortField.SET)
            {
                var ranked = results.SelectMany(PrintsOf).Select(v => new { V = v, Rank = RankOf(v) });
                var known = ranked.OrderBy(x => x.Rank == int.MaxValue ? 1 : 0);
                var dir = c.SortAscending ? known.ThenBy(x => x.Rank) : known.ThenByDescending(x => x.Rank);
                return dir.ThenBy(x => PrintingIdOf(x.V.ImagePath), StringComparer.Ordinal)
                          .Select(x => x.V).ToList();
            }

            return results.SelectMany(card => PrintsOf(card)
                    .OrderBy(RankOf)
                    .ThenBy(v => PrintingIdOf(v.ImagePath), StringComparer.Ordinal))
                .ToList();
        }

        /// <summary>Una entrada por impresión: imagePath preferida + printing_images, dedup en orden.</summary>
        private static List<CardPrintingView> ExpandCard(Card card)
        {
            var paths = new List<string>();
            if (!string.IsNullOrEmpty(card.ImagePath)) paths.Add(card.ImagePath);
            foreach (var p in SplitPipe(card.PrintingImages))
                if (!paths.Contains(p)) paths.Add(p);
            if (paths.Count == 0)
                return new List<CardPrintingView> { new CardPrintingView { Card = card, ImagePath = null } };
            return paths.Select(p => new CardPrintingView { Card = card, ImagePath = p }).ToList();
        }

        // ------------------------------------------------------------------ orden canónico de sets

        /// <summary>
        /// presortSets: orden del filters.json, adelantando los códigos de FORCE_BEFORE_SAMURAI
        /// que aparezcan después de "Samurai" a justo antes (resiliencia; hoy es no-op).
        /// </summary>
        public static List<SetInfo> PresortSets(List<SetInfo> sets)
        {
            var samuraiIdx = sets.FindIndex(s => s.Code == "Samurai");
            if (samuraiIdx < 0) return sets;
            var fromSamurai = sets.Skip(samuraiIdx).ToList();
            var misplaced = fromSamurai.Where(s => ForceBeforeSamurai.Contains(s.Code)).ToList();
            if (misplaced.Count == 0) return sets;
            return sets.Take(samuraiIdx)
                .Concat(misplaced)
                .Concat(fromSamurai.Where(s => !ForceBeforeSamurai.Contains(s.Code)))
                .ToList();
        }

        private static Dictionary<string, int> BuildSetOrderMap(List<SetInfo> sets)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            var i = 0;
            foreach (var s in PresortSets(sets))
                if (s.Code != null) map[s.Code] = i++;
            return map;
        }

        // ------------------------------------------------------------------ helpers de semántica SQL

        /// <summary>
        /// Equivalente de <c>haystack LIKE '%needle%'</c> de SQLite: contains case-insensitive
        /// SOLO para ASCII (sin folding Unicode, sin normalización de acentos).
        /// haystack null → false (como NULL en SQL). Sin semántica de comodines.
        /// </summary>
        public static bool LikeContains(string haystack, string needle)
        {
            if (haystack == null || needle == null) return false;
            int n = haystack.Length, m = needle.Length;
            if (m == 0) return true;
            for (int i = 0; i + m <= n; i++)
            {
                int j = 0;
                while (j < m && Fold(haystack[i + j]) == Fold(needle[j])) j++;
                if (j == m) return true;
            }
            return false;
        }

        private static char Fold(char c) => c >= 'A' && c <= 'Z' ? (char)(c + 32) : c;

        private static readonly char[] TextNormChars =
            { '\'', '.', ',', ':', ';', '!', '?', '(', ')', '<', '>', '[', ']', '/', '"' };

        /// <summary>
        /// Pre-normalización del filtro de texto (SPEC-BUSQUEDA-TEXTO.md §4b): esos 15 caracteres
        /// se vuelven espacio y se agrega padding, para matchear '% palabra %' = palabra completa.
        /// null → null (la carta sin texto queda excluida, como en SQL).
        /// </summary>
        public static string NormalizeTextForWordSearch(string text)
        {
            if (text == null) return null;
            var sb = new StringBuilder(text.Length + 2);
            sb.Append(' ');
            foreach (var ch in text)
                sb.Append(Array.IndexOf(TextNormChars, ch) >= 0 ? ' ' : ch);
            sb.Append(' ');
            return sb.ToString();
        }

        private static IEnumerable<string> SplitWords(string s) =>
            string.IsNullOrEmpty(s)
                ? Enumerable.Empty<string>()
                : s.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        public static IEnumerable<string> SplitPipe(string pipeList)
        {
            if (string.IsNullOrEmpty(pipeList)) yield break;
            foreach (var part in pipeList.Split('|'))
                if (part.Length > 0) yield return part;
        }

        /// <summary>Capitaliza el primer char (port de replaceFirstChar { it.uppercase() }).</summary>
        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>Edición de la impresión mostrada: segmento entre "images/cards/" y "/".</summary>
        public static string EditionOf(string imagePath)
        {
            if (imagePath == null) return null;
            const string prefix = "images/cards/";
            var i = imagePath.IndexOf(prefix, StringComparison.Ordinal);
            if (i < 0) return null;
            var start = i + prefix.Length;
            var j = imagePath.IndexOf('/', start);
            return j < 0 ? null : imagePath.Substring(start, j - start);
        }

        /// <summary>
        /// Nombre de archivo sin extensión (id del printing). No asume ".jpg": el pack de la
        /// comunidad puede traer otros formatos de imagen.
        /// </summary>
        public static string PrintingIdOf(string imagePath)
        {
            if (imagePath == null) return "";
            var slash = imagePath.LastIndexOf('/');
            var file = slash < 0 ? imagePath : imagePath.Substring(slash + 1);
            return Path.GetFileNameWithoutExtension(file);
        }
    }
}
