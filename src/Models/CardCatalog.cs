using System.Collections.Generic;
using System.Linq;

namespace L5ArgentinaLauncher.Models
{
    /// <summary>
    /// Carta del catálogo del Buscador, espejo 1:1 de la tabla `cards` de la app Android
    /// (docs/functional/buscador-launcher/MODELO-DATOS.md §2/§5). Las listas se guardan en el
    /// formato pipe-delimited ("|a|b|") igual que en la app: la semántica de los filtros
    /// (match exacto de elemento vía LIKE '%|x|%') depende de conservar ese formato.
    /// Propiedades (no fields) porque la UI las bindea directo.
    /// </summary>
    public class Card
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Clan { get; set; }            // null si no tiene
        public string DeckGroup { get; set; }
        public string Rarity { get; set; }          // "" si falta; case SIN normalizar (paridad con la app)
        public string Sets { get; set; }            // pipe
        public string Legality { get; set; }        // pipe
        public string Keywords { get; set; }        // pipe
        public string Text { get; set; }            // HTML crudo (b/i/br + bullets literales); null si falta
        public string Flavor { get; set; }
        public int? Force { get; set; }
        public bool ForceSpecial { get; set; }
        public int? Chi { get; set; }
        public int? Cost { get; set; }
        public bool CostSpecial { get; set; }
        public int? Honor { get; set; }             // honor requirement
        public bool HonorReqNegative { get; set; }
        public int? PersonalHonor { get; set; }
        public bool PersonalHonorNegative { get; set; }
        public int? Focus { get; set; }
        public int? GoldProduction { get; set; }
        public bool GoldProductionSpecial { get; set; }
        public int? ProvinceStrength { get; set; }
        public int? StartingHonor { get; set; }
        public string Artist { get; set; }
        public string ImagePath { get; set; }       // relativo a la instalación: images/cards/<ED>/<printingId>.jpg
        public string PrintingImages { get; set; }  // pipe con todos los paths de impresión
        public int MaxCopies { get; set; }
        public bool Banned { get; set; }
        public string Note { get; set; }
        public string LabelText { get; set; }
        public string LabelColor { get; set; }
        public string HighlightColor { get; set; }
        public string InitialHoldingSlot { get; set; }
        public string Subtitle { get; set; }        // null si el nombre no tiene coma
        public string ActionKeywords { get; set; }  // pipe

        /// <summary>
        /// Texto pre-normalizado para el filtro de palabra completa: ' ' + text con la
        /// puntuación/tags reemplazados por espacio + ' ' (SPEC-BUSQUEDA-TEXTO.md §4b).
        /// null si Text es null (la carta queda excluida del filtro, igual que en SQL).
        /// </summary>
        public string NormalizedText { get; set; }

        // ----- Presentación (solo lectura, para binding de la grilla/detalle) -----

        public string DisplayForce => ForceSpecial ? "+*" : Force?.ToString() ?? "";
        public string DisplayChi => Chi?.ToString() ?? "";
        public string DisplayCost => CostSpecial ? "*" : Cost?.ToString() ?? "";
        public string DisplayHonor => HonorReqNegative ? "-" : Honor?.ToString() ?? "";
        public string DisplayPersonalHonor => PersonalHonorNegative ? "-" : PersonalHonor?.ToString() ?? "";
        public string DisplayFocus => Focus?.ToString() ?? "";
        public string DisplaySets => JoinPipe(Sets);
        public string DisplayKeywords => JoinPipe(Keywords, " • ");
        public string DisplayLegality => JoinPipe(Legality);

        private static string JoinPipe(string pipe, string sep = ", ")
        {
            if (string.IsNullOrEmpty(pipe)) return "";
            return string.Join(sep, pipe.Split('|').Where(p => p.Length > 0));
        }
    }

    public class SetInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string DisplayName => string.IsNullOrEmpty(Name) ? Code : Name;
    }

    public class FormatInfo
    {
        public string Code { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Slot de holding inicial de un formato (tabla format_initial_holding_slots de la app;
    /// filters.json → formats[].initialHoldingSlots[]). Los slots con Required alimentan el
    /// aviso "Falta: {label}" del deckbuilder; hoy solo el formato samurai define slots.
    /// </summary>
    public class InitialHoldingSlot
    {
        public string FormatId { get; set; }
        public string SlotId { get; set; }
        public string Label { get; set; }
        public bool Required { get; set; }
    }

    /// <summary>
    /// Catálogo cargado en memoria: cartas (en el orden del cards.json — ese orden es el
    /// desempate implícito "rowid" de los ordenamientos) + tablas auxiliares de filters.json.
    /// </summary>
    public class CardCatalog
    {
        /// <summary>Cartas en el orden de inserción del cards.json.</summary>
        public List<Card> Cards = new List<Card>();

        /// <summary>Sets en el orden del filters.json (orden canónico del selector de Edición).</summary>
        public List<SetInfo> Sets = new List<SetInfo>();

        public List<FormatInfo> Formats = new List<FormatInfo>();

        /// <summary>Slots de holding inicial por formato, en el orden del filters.json.</summary>
        public List<InitialHoldingSlot> InitialHoldingSlots = new List<InitialHoldingSlot>();

        /// <summary>Códigos de keyword para el autocomplete (838 en filters 2.3.0).</summary>
        public List<string> KeywordCodes = new List<string>();

        public string CardsVersion;
        public string RulesVersion;
        public string FiltersVersion;
    }
}
