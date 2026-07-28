using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace L5ArgentinaLauncher.Services.Search
{
    /// <summary>
    /// Campos de ordenamiento. Los primeros seis son el port de SortField de la app (mismos
    /// nombres, mismas semánticas golden); los de abajo son extras del launcher para el orden
    /// por cabecera de columna de la tabla.
    /// </summary>
    public enum SortField
    {
        NAME,
        COST,
        FORCE,
        CHI,
        PERSONAL_HONOR,
        SET,
        // ----- extras del launcher (orden por cabecera; no existen en la app) -----
        TYPE,
        CLAN,
        RARITY,
        HONOR_REQ,
        FOCUS,
    }

    /// <summary>
    /// Criterios de búsqueda — port 1:1 de CardSearchCriteria.kt. Los nombres JSON coinciden
    /// con la serialización de la app para poder deserializar los golden tests tal cual.
    /// </summary>
    public class CardSearchCriteria
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";

        // Keywords
        [JsonPropertyName("keywords")] public List<string> Keywords { get; set; } = new List<string>();
        [JsonPropertyName("negativeKeywords")] public List<string> NegativeKeywords { get; set; } = new List<string>();
        [JsonPropertyName("keywordMatchAll")] public bool KeywordMatchAll { get; set; } = true;
        [JsonPropertyName("includeActionKeywords")] public bool IncludeActionKeywords { get; set; } = true;
        [JsonPropertyName("includeSubtitleKeywords")] public bool IncludeSubtitleKeywords { get; set; } = true;
        [JsonPropertyName("text")] public string Text { get; set; } = "";

        // Categóricos
        [JsonPropertyName("types")] public List<string> Types { get; set; } = new List<string>();
        [JsonPropertyName("clans")] public List<string> Clans { get; set; } = new List<string>();
        [JsonPropertyName("rarities")] public List<string> Rarities { get; set; } = new List<string>();
        [JsonPropertyName("sets")] public List<string> Sets { get; set; } = new List<string>();
        [JsonPropertyName("formats")] public List<string> Formats { get; set; } = new List<string>();

        // Stats — Fuerza
        [JsonPropertyName("forceMin")] public int? ForceMin { get; set; }
        [JsonPropertyName("forceMax")] public int? ForceMax { get; set; }
        [JsonPropertyName("includeSpecialForce")] public bool IncludeSpecialForce { get; set; }

        // Stats — Chi
        [JsonPropertyName("chiMin")] public int? ChiMin { get; set; }
        [JsonPropertyName("chiMax")] public int? ChiMax { get; set; }

        // Stats — Costo
        [JsonPropertyName("costMin")] public int? CostMin { get; set; }
        [JsonPropertyName("costMax")] public int? CostMax { get; set; }
        [JsonPropertyName("includeSpecialCost")] public bool IncludeSpecialCost { get; set; }

        // Stats — Honor requerido
        [JsonPropertyName("honorReqMin")] public int? HonorReqMin { get; set; }
        [JsonPropertyName("honorReqMax")] public int? HonorReqMax { get; set; }
        [JsonPropertyName("includeNegativeHonorReq")] public bool IncludeNegativeHonorReq { get; set; }

        // Stats — Honor personal
        [JsonPropertyName("personalHonorMin")] public int? PersonalHonorMin { get; set; }
        [JsonPropertyName("personalHonorMax")] public int? PersonalHonorMax { get; set; }
        [JsonPropertyName("includeNegativePersonalHonor")] public bool IncludeNegativePersonalHonor { get; set; }

        // Stats — Focus
        [JsonPropertyName("focusMin")] public int? FocusMin { get; set; }
        [JsonPropertyName("focusMax")] public int? FocusMax { get; set; }

        // Designadores (Open, Limited, Battle, …)
        [JsonPropertyName("designators")] public List<string> Designators { get; set; } = new List<string>();

        // Otros
        [JsonPropertyName("includeBanned")] public bool IncludeBanned { get; set; }
        [JsonPropertyName("onlyInitialHoldings")] public bool OnlyInitialHoldings { get; set; }
        [JsonPropertyName("onlyHidden")] public bool OnlyHidden { get; set; }

        // Quick filters (datos de usuario; sin UI en el launcher por ahora — los usa el selftest)
        [JsonPropertyName("onlyFavorites")] public bool OnlyFavorites { get; set; }
        [JsonPropertyName("includeHidden")] public bool IncludeHidden { get; set; }
        [JsonPropertyName("listId")] public string ListId { get; set; }

        /// <summary>
        /// De qué conjunto es <see cref="ListId"/>: "list" (o null, default histórico), "deck",
        /// "favorites" u "hidden". El motor no lo mira —resuelve siempre contra
        /// <see cref="UserCardData.Lists"/>, que es lo que verifican los golden—; lo usa la UI
        /// para volver a resolver el conjunto al recargar un preset. Campo agregado, no
        /// renombrado: los golden serializados sin él siguen deserializando igual.
        /// </summary>
        [JsonPropertyName("listKind")] public string ListKind { get; set; }

        // Orden
        [JsonPropertyName("sortField")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SortField SortField { get; set; } = SortField.NAME;
        [JsonPropertyName("sortAscending")] public bool SortAscending { get; set; } = true;

        /// <summary>true solo con todo en default; excluye sortField/sortAscending (como la app).</summary>
        [JsonIgnore]
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Name) && !Keywords.Any() && !NegativeKeywords.Any() &&
            string.IsNullOrWhiteSpace(Text) &&
            !Types.Any() && !Clans.Any() && !Rarities.Any() && !Sets.Any() && !Formats.Any() &&
            ForceMin == null && ForceMax == null && !IncludeSpecialForce &&
            ChiMin == null && ChiMax == null &&
            CostMin == null && CostMax == null && !IncludeSpecialCost &&
            HonorReqMin == null && HonorReqMax == null && !IncludeNegativeHonorReq &&
            PersonalHonorMin == null && PersonalHonorMax == null && !IncludeNegativePersonalHonor &&
            FocusMin == null && FocusMax == null &&
            !OnlyInitialHoldings && !OnlyHidden && !OnlyFavorites && !IncludeHidden &&
            ListId == null && !IncludeBanned && !Designators.Any();
    }

    /// <summary>
    /// Datos de usuario que el motor consulta. Favoritas y ocultas se persisten en
    /// usercards.json; <see cref="Lists"/> es la entrada del filtro "solo el conjunto X" y se
    /// arma en el momento de buscar: la UI mete ahí las cartas del conjunto elegido, venga de
    /// una lista, de un mazo o de las propias favoritas/ocultas. El motor no sabe de dónde salió
    /// —es la forma de sumar conjuntos nuevos sin tocar lo que verifican los golden—.
    /// </summary>
    public class UserCardData
    {
        public HashSet<string> Favorites = new HashSet<string>();
        public HashSet<string> Hidden = new HashSet<string>();
        public Dictionary<string, HashSet<string>> Lists = new Dictionary<string, HashSet<string>>();

        public static readonly UserCardData Empty = new UserCardData();

        /// <summary>
        /// Copia que comparte favoritas/ocultas pero con su propio diccionario de conjuntos, para
        /// materializar el conjunto activo sin ensuciar (ni persistir) el estado real.
        /// </summary>
        public UserCardData WithList(string id, IEnumerable<string> cardIds)
        {
            var copy = new UserCardData { Favorites = Favorites, Hidden = Hidden };
            foreach (var kv in Lists) copy.Lists[kv.Key] = kv.Value;
            if (id != null) copy.Lists[id] = new HashSet<string>(cardIds ?? Enumerable.Empty<string>());
            return copy;
        }
    }
}
