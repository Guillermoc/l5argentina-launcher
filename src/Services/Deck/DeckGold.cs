using System;
using System.Collections.Generic;
using System.Linq;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services.Deck
{
    /// <summary>
    /// Ajuste de oro por carta (ADR-059). Extra del launcher: el coste impreso del catálogo es
    /// el que la carta dice, no el que se paga. Las reglas del formato le dan descuento a las
    /// personalidades del clan del stronghold y le cobran de más a las de otro clan, así que
    /// ordenar por coste impreso mezcla cartas que en la mesa cuestan cosas distintas.
    ///
    /// **La regla es viva, no materializada.** Al prender el modo no se escriben valores: se
    /// calculan contra el clan del stronghold del momento. Cambiar el stronghold re-ajusta el
    /// mazo entero solo, y una personalidad agregada después ya entra con su ajuste. Al sidecar
    /// van únicamente los <em>overrides</em> que puso el usuario a mano.
    ///
    /// Todo opera sobre un <see cref="Models.Deck"/> en memoria y por CardId; quien persiste es
    /// <see cref="DeckLibrary"/>, que traduce a NOMBRE de carta en el borde (ADR-044).
    /// No existe en la app Android: ningún golden lo cubre.
    /// </summary>
    public static class DeckGold
    {
        /// <summary>Tope del ajuste manual, en las dos direcciones (−3 … +3).</summary>
        public const int MaxAdjust = 3;

        /// <summary>Descuento de las personalidades del clan del stronghold.</summary>
        public const int OwnClanDiscount = -2;

        /// <summary>Sobrecosto de las personalidades de otro clan grande.</summary>
        public const int OffClanSurcharge = 2;

        /// <summary>
        /// Los nueve clanes grandes — los mismos que expone el filtro de clan del Buscador.
        /// El sobrecosto es solo entre ellos: shadowlands, ratling, naga, ronin, hare, spirit,
        /// monk y ninja aparecen en el campo `clan` del catálogo pero no son un clan al que un
        /// stronghold pueda pertenecer del otro lado de la regla, así que se tratan como una
        /// personalidad sin clan y no pagan de más. El descuento sí es literal: si la carta lleva
        /// el clan del stronghold —sea cual sea— es tu personalidad.
        /// </summary>
        public static readonly string[] GreatClans =
        {
            "crab", "crane", "dragon", "lion", "mantis", "phoenix", "scorpion", "spider", "unicorn",
        };

        private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

        // ------------------------------------------------------------------ la regla

        /// <summary>
        /// Lo que las dos reglas del formato le hacen a esta carta. Sin stronghold no hay contra
        /// qué compararla y todo da 0: la regla entera es relativa al clan propio.
        /// </summary>
        public static int RuleFor(Card card, string deckClan)
        {
            if (card == null || string.IsNullOrEmpty(deckClan)) return 0;
            if (!Cmp.Equals(card.Type, "personality")) return 0;
            if (string.IsNullOrEmpty(card.Clan)) return 0;              // unaligned: paga lo impreso
            if (Cmp.Equals(card.Clan, deckClan)) return OwnClanDiscount;
            return IsGreatClan(card.Clan) ? OffClanSurcharge : 0;
        }

        public static bool IsGreatClan(string clan) =>
            clan != null && GreatClans.Any(c => Cmp.Equals(c, clan));

        // ------------------------------------------------------------------ ajuste efectivo

        /// <summary>
        /// El ajuste que corre para esta carta: el override si el usuario puso uno, si no la
        /// regla. Con el modo apagado no ajusta nada — el flag es del mazo, así que se mira acá
        /// y no en cada llamador.
        /// </summary>
        public static int Effective(Models.Deck deck, Card card, string deckClan)
        {
            if (deck == null || card == null || !deck.GoldAdjustOn) return 0;
            var manual = OverrideOf(deck, card.Id);
            return manual ?? RuleFor(card, deckClan);
        }

        /// <summary>
        /// Coste que se paga de verdad: el impreso más el ajuste, con piso en 0 (no se paga
        /// menos que nada). null cuando la carta no tiene coste numérico —el `*` de coste
        /// variable— para que siga ordenando al final, como sin ajustes.
        /// </summary>
        public static int? EffectiveCost(Models.Deck deck, Card card, string deckClan)
        {
            if (card?.Cost == null) return null;
            return Math.Max(0, card.Cost.Value + Effective(deck, card, deckClan));
        }

        // ------------------------------------------------------------------ overrides

        /// <summary>El ajuste puesto a mano, o null si esta carta sigue la regla.</summary>
        public static int? OverrideOf(Models.Deck deck, string cardId)
        {
            if (deck == null || cardId == null) return null;
            return deck.GoldAdjust.TryGetValue(cardId, out var value) ? value : (int?)null;
        }

        public static bool HasOverride(Models.Deck deck, string cardId) =>
            OverrideOf(deck, cardId) != null;

        public static int OverrideCount(Models.Deck deck) => deck?.GoldAdjust.Count ?? 0;

        /// <summary>
        /// Fija el ajuste de una carta a mano, acotado a ±<see cref="MaxAdjust"/>. null la
        /// devuelve a la regla. Elegir a mano el mismo número que dice la regla igual queda como
        /// override: "lo puse yo" y "lo dice la regla" son estados distintos, y solo el segundo
        /// se mueve si cambia el stronghold.
        /// </summary>
        public static void Set(Models.Deck deck, string cardId, int? value)
        {
            if (deck == null || cardId == null) return;
            if (value == null) { deck.GoldAdjust.Remove(cardId); return; }
            deck.GoldAdjust[cardId] = Math.Max(-MaxAdjust, Math.Min(MaxAdjust, value.Value));
        }

        /// <summary>Saca todos los ajustes manuales: el mazo vuelve a lo que dice la regla.</summary>
        public static void ClearOverrides(Models.Deck deck) => deck?.GoldAdjust.Clear();

        // ------------------------------------------------------------------ mantenimiento

        /// <summary>
        /// Saca los ajustes de cartas que ya no están en el mazo. El ajuste acompaña a la carta,
        /// igual que las etiquetas: sacarla con ✕ se lo lleva.
        /// </summary>
        public static void Prune(Models.Deck deck)
        {
            if (deck == null) return;
            var present = new HashSet<string>(deck.Cards.Select(e => e.CardId), StringComparer.Ordinal);
            foreach (var cardId in deck.GoldAdjust.Keys.ToList())
                if (!present.Contains(cardId)) deck.GoldAdjust.Remove(cardId);
        }

        /// <summary>Copia el modo y los ajustes manuales a otro mazo (duplicar). Solo las cartas que llegaron.</summary>
        public static void CopyTo(Models.Deck source, Models.Deck target)
        {
            if (source == null || target == null) return;
            target.GoldAdjustOn = source.GoldAdjustOn;
            foreach (var entry in target.Cards)
            {
                var value = OverrideOf(source, entry.CardId);
                if (value != null) Set(target, entry.CardId, value);
            }
        }

        // ------------------------------------------------------------------ agrupación

        /// <summary>
        /// Una sección por coste EFECTIVO, para partir el bloque de personalities cuando el modo
        /// está prendido. Es lo que vuelve legible el ajuste: con la regla corriendo, "las de 6"
        /// es un grupo real del mazo —lo que sale una compra— y no un tramo cualquiera de una
        /// lista ordenada.
        ///
        /// A diferencia de las pilas de etiquetas (<c>DeckTags.Sections</c>), esto **sí es una
        /// partición**: cada carta cae en exactamente una sección, porque coste efectivo hay uno
        /// solo. Los conteos por sección, entonces, suman el bloque.
        ///
        /// Las de coste variable (★) van siempre al final, apunte donde apunte el orden: no tienen
        /// lugar en la escala. Dentro de cada sección se preserva el orden de entrada, así que el
        /// criterio de orden del editor sigue valiendo.
        /// </summary>
        public static List<CostSection<T>> CostSections<T>(
            Models.Deck deck, IEnumerable<T> items, Func<T, Card> cardOf, string deckClan,
            bool descending)
        {
            var sections = new List<CostSection<T>>();
            if (items == null) return sections;
            foreach (var item in items)
            {
                var cost = EffectiveCost(deck, cardOf(item), deckClan);
                var section = sections.FirstOrDefault(s => s.Cost == cost);
                if (section == null) sections.Add(section = new CostSection<T> { Cost = cost });
                section.Items.Add(item);
            }
            return descending
                ? sections.OrderBy(s => s.Cost == null).ThenByDescending(s => s.Cost ?? 0).ToList()
                : sections.OrderBy(s => s.Cost == null).ThenBy(s => s.Cost ?? 0).ToList();
        }

        // ------------------------------------------------------------------ presentación

        /// <summary>"+2" / "−2" con el mismo signo menos (U+2212) que usan los botones de la UI.</summary>
        public static string Format(int adjust) =>
            adjust > 0 ? "+" + adjust : adjust < 0 ? "−" + (-adjust) : "0";
    }

    /// <summary>Un escalón de la escala de coste efectivo. <c>Cost</c> null = coste variable (★).</summary>
    public class CostSection<T>
    {
        public int? Cost { get; set; }

        public List<T> Items { get; } = new List<T>();
    }
}
