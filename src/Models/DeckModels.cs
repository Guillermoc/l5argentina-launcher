using System;
using System.Collections.Generic;

namespace L5ArgentinaLauncher.Models
{
    /// <summary>
    /// Mazo del deckbuilder — espejo de la tabla `decks` de la app Android
    /// (docs/functional/deckbuilder-launcher/MODELO-DATOS-MAZO.md §1), con dos diferencias
    /// deliberadas del launcher: la persistencia es un .txt por mazo en la carpeta Decks de
    /// Sun and Moon (no Room), y las líneas del txt que no matchean el catálogo se conservan
    /// crudas en <see cref="Unknown"/> (equivalente launcher de las "huérfanas" ADR-013:
    /// nunca se pierde lo que había en el archivo).
    /// </summary>
    public class Deck
    {
        /// <summary>Id estable durante la sesión (UUID). La identidad en disco es el archivo.</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>Epoch ms (System.currentTimeMillis() de la app).</summary>
        public long CreatedAt { get; set; }

        /// <summary>Notas libres; null si no hay (el import de texto las acumula acá).</summary>
        public string Notes { get; set; }

        /// <summary>"deck" | "list" — en listas el tope de copias se fuerza a 1.</summary>
        public string Type { get; set; } = "deck";

        public int SortOrder { get; set; }

        /// <summary>null | "default" | "by_type" | "custom" (lo setea `# sort_mode:` del import).</summary>
        public string SortMode { get; set; }

        /// <summary>
        /// Header "Legality: X" del txt de Sun and Moon. Se preserva tal cual (el modelo de la
        /// app no tiene formato objetivo; esto es solo fidelidad de archivo).
        /// </summary>
        public string Legality { get; set; }

        /// <summary>Una entrada por carta distinta; la multiplicidad va en Quantity.</summary>
        public List<DeckCardEntry> Cards { get; } = new List<DeckCardEntry>();

        /// <summary>Líneas del txt que no matchearon el catálogo (se reescriben verbatim al guardar).</summary>
        public List<UnknownDeckLine> Unknown { get; } = new List<UnknownDeckLine>();

        /// <summary>
        /// Vocabulario de etiquetas de ESTE mazo (ADR-057). Extra del launcher: la app tenía
        /// `deck_card_groups` y quedó en desuso. El orden es el del usuario y define el orden de
        /// las secciones al agrupar por etiqueta.
        /// </summary>
        public List<DeckTag> Tags { get; } = new List<DeckTag>();

        /// <summary>
        /// Qué etiquetas tiene cada carta. En memoria va por CardId; al disco, por NOMBRE de carta
        /// (ADR-044). Se manipula desde <c>Services.Deck.DeckTags</c>, no a mano.
        /// </summary>
        public Dictionary<string, List<string>> CardTags { get; } =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Modo "ajuste de oro" de ESTE mazo (ADR-059): el coste que se ordena y se muestra es el
        /// que se paga en la mesa, no el impreso. Apagado, nada de esto se aplica (los ajustes
        /// puestos a mano se conservan igual).
        /// </summary>
        public bool GoldAdjustOn { get; set; }

        /// <summary>
        /// Ajustes de oro puestos A MANO, por CardId — no los que salen de la regla, que se
        /// calculan al vuelo contra el clan del stronghold. Al disco van por NOMBRE de carta
        /// (ADR-044). Se manipula desde <c>Services.Deck.DeckGold</c>, no a mano.
        /// </summary>
        public Dictionary<string, int> GoldAdjust { get; } =
            new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Etiqueta de usuario dentro de un mazo: un nombre libre y un color de la paleta. La
    /// identidad es el nombre (único sin distinguir mayúsculas dentro del mazo), que es también
    /// lo que se escribe al sidecar.
    /// </summary>
    public class DeckTag
    {
        public string Name { get; set; }

        /// <summary>"#RRGGBB" de <c>DeckTags.Palette</c>.</summary>
        public string Color { get; set; }
    }

    /// <summary>Fila de `deck_cards`: quantity=0 = carta "recortada" (la fila persiste).</summary>
    public class DeckCardEntry
    {
        public string CardId { get; set; }
        public int Quantity { get; set; }
        public int SortOrder { get; set; }

        /// <summary>
        /// Nombre EXACTO con el que la carta aparecía en el .txt de Sun and Moon, cuando difiere
        /// del nombre del catálogo (strongholds sin sufijo "(1)", case distinto, "- exp" omitido).
        /// Al guardar el txt se reescribe este nombre para que el juego lo siga reconociendo.
        /// null = la entrada nació en el launcher (se escribe el nombre del catálogo).
        /// </summary>
        public string SourceName { get; set; }

        /// <summary>
        /// Tipo del prefijo "Proxy &lt;tipo&gt;: " que traía la línea del .txt, sin el "Proxy " ni
        /// los dos puntos ("Holding"). En Sun and Moon esa carta NO va al mazo: se apoya boca
        /// arriba en la mesa. El launcher no hace nada con el dato — lo lee para entender la línea
        /// y lo reescribe igual ([ADR-060](../../docs/DECISIONS.md#adr-060)). null = línea normal.
        /// </summary>
        public string ProxyType { get; set; }

        /// <summary>
        /// Edición entre paréntesis al final de la línea ("Productive Mine (GoC)"), sin los
        /// paréntesis. El juego la usa para desempatar entre impresiones de la misma carta; el
        /// launcher solo la conserva para reescribirla igual. null = la línea no traía ninguna.
        /// </summary>
        public string Edition { get; set; }
    }

    /// <summary>Línea cruda no resuelta contra el catálogo (nombre desconocido).</summary>
    public class UnknownDeckLine
    {
        /// <summary>La línea original del archivo, sin tocar.</summary>
        public string Raw { get; set; }
    }

    /// <summary>Resultado de DeckRepository.AddCard (port de AddCardResult.kt).</summary>
    public enum AddCardResultKind
    {
        Success,
        Banned,
        /// <summary>⚠️ Nombre engañoso heredado de la app: carta O mazo inexistente.</summary>
        NoActiveDeck,
        LimitReached,
        SlotAlreadyCovered,
    }

    public struct AddCardResult
    {
        public AddCardResultKind Kind;
        /// <summary>Solo para LimitReached: el máximo alcanzado.</summary>
        public int Max;

        public static readonly AddCardResult Success = new AddCardResult { Kind = AddCardResultKind.Success };
        public static readonly AddCardResult Banned = new AddCardResult { Kind = AddCardResultKind.Banned };
        public static readonly AddCardResult NoActiveDeck = new AddCardResult { Kind = AddCardResultKind.NoActiveDeck };
        public static readonly AddCardResult SlotAlreadyCovered = new AddCardResult { Kind = AddCardResultKind.SlotAlreadyCovered };
        public static AddCardResult LimitReached(int max) =>
            new AddCardResult { Kind = AddCardResultKind.LimitReached, Max = max };
    }

    /// <summary>Resultado de ImportCards (port de ImportResult.kt).</summary>
    public class ImportResult
    {
        public int Imported;
        public int NotFound;
        public List<string> NotFoundNames = new List<string>();
    }

    /// <summary>
    /// Grupos del mazo y orden canónico de tipos dentro de cada uno (DeckGroup.kt +
    /// TYPE_ORDER_MAP de DeckDetailViewModel.kt). Base de los conteos, el export y la grilla.
    /// </summary>
    public static class DeckGroups
    {
        public static readonly string[] Order = { "starter", "dynasty", "fate" };

        private static readonly Dictionary<string, string[]> TypeOrderMap =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["starter"] = new[] { "stronghold", "sensei", "wind", "holding" },
                ["dynasty"] = new[] { "celestial", "event", "region", "holding", "personality" },
                ["fate"] = new[] { "follower", "item", "spell", "strategy" },
            };

        public static int GroupOrder(string group)
        {
            switch (group)
            {
                case "starter": return 0;
                case "dynasty": return 1;
                case "fate": return 2;
                default: return 3;
            }
        }

        public static int TypeOrderInGroup(string deckGroup, string type)
        {
            string[] order;
            if (deckGroup != null && TypeOrderMap.TryGetValue(deckGroup, out order))
            {
                var idx = Array.IndexOf(order, type);
                if (idx >= 0) return idx;
            }
            return int.MaxValue;
        }

        /// <summary>starterRank del Goldfish (orden del área starter y del honor inicial).</summary>
        public static int StarterRank(string type)
        {
            switch (type)
            {
                case "stronghold": return 0;
                case "sensei": return 1;
                case "holding": return 2;
                default: return 3;
            }
        }
    }
}
