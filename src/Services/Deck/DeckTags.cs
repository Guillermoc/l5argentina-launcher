using System;
using System.Collections.Generic;
using System.Linq;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services.Deck
{
    /// <summary>
    /// Etiquetas de usuario sobre las cartas de un mazo (ADR-057). Extra del launcher: replican el
    /// gesto de armar en la mesa —apilar las cartas por lo que hacen— sobre un mazo que en el
    /// archivo es una lista plana. No existen en la app Android, así que ningún golden las cubre.
    ///
    /// Todo lo de acá opera sobre un <see cref="Models.Deck"/> en memoria y por CardId; quien
    /// persiste es <see cref="DeckLibrary"/>, que traduce a NOMBRE de carta en el borde (ADR-044).
    /// El nombre de la etiqueta ES su identidad: renombrarla reescribe las asignaciones.
    /// </summary>
    public static class DeckTags
    {
        /// <summary>Un nombre más largo no entra en el chip ni en la cabecera de sección.</summary>
        public const int MaxNameLength = 24;

        /// <summary>
        /// Colores de los puntitos y los chips. Al crear una etiqueta se asigna uno por orden
        /// (round-robin) para no frenar el armado con una decisión de color; después se cambia
        /// desde el menú de la etiqueta. La paleta es cerrada a propósito: son los colores que se
        /// leen sobre el fondo oscuro y como punto de 8 px encima del arte de una carta.
        /// </summary>
        public static readonly string[] Palette =
        {
            "#C0392B", // rojo
            "#2980B9", // azul
            "#27AE60", // verde
            "#8E44AD", // violeta
            "#D68910", // ámbar
            "#16A085", // turquesa
            "#C2185B", // magenta
            "#D35400", // naranja
            "#5D8AA8", // acero
            "#7F8C8D", // gris
        };

        private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

        // ------------------------------------------------------------------ vocabulario

        /// <summary>
        /// Crea la etiqueta si el nombre es válido y no está repetido; si ya existe, devuelve la
        /// que había (crear dos veces "Deshonor" es querer la misma pila, no una segunda).
        /// </summary>
        public static DeckTag Add(Models.Deck deck, string name)
        {
            if (deck == null) return null;
            name = Normalize(name);
            if (name.Length == 0) return null;
            var existing = Find(deck, name);
            if (existing != null) return existing;
            var tag = new DeckTag { Name = name, Color = Palette[deck.Tags.Count % Palette.Length] };
            deck.Tags.Add(tag);
            return tag;
        }

        public static DeckTag Find(Models.Deck deck, string name) =>
            deck?.Tags.FirstOrDefault(t => Cmp.Equals(t.Name, name));

        /// <summary>Renombra la etiqueta y arrastra las asignaciones. false = nombre inválido o ya usado.</summary>
        public static bool Rename(Models.Deck deck, string oldName, string newName)
        {
            var tag = Find(deck, oldName);
            if (tag == null) return false;
            newName = Normalize(newName);
            if (newName.Length == 0) return false;
            if (Cmp.Equals(tag.Name, newName)) { tag.Name = newName; return true; } // solo cambió el case
            if (Find(deck, newName) != null) return false;

            var previous = tag.Name;
            tag.Name = newName;
            foreach (var tags in deck.CardTags.Values)
                for (var i = 0; i < tags.Count; i++)
                    if (Cmp.Equals(tags[i], previous)) tags[i] = newName;
            return true;
        }

        /// <summary>
        /// Cambia el color de la etiqueta. Solo colores de <see cref="Palette"/>: un color libre
        /// puede quedar ilegible como punto sobre el arte, que es donde más chico se dibuja.
        /// </summary>
        public static bool SetColor(Models.Deck deck, string name, string color)
        {
            var tag = Find(deck, name);
            if (tag == null) return false;
            var known = Palette.FirstOrDefault(c => Cmp.Equals(c, color));
            if (known == null) return false;
            tag.Color = known;
            return true;
        }

        /// <summary>Borra la etiqueta del vocabulario y de todas las cartas que la tenían.</summary>
        public static void Delete(Models.Deck deck, string name)
        {
            var tag = Find(deck, name);
            if (tag == null) return;
            deck.Tags.Remove(tag);
            foreach (var cardId in deck.CardTags.Keys.ToList())
            {
                deck.CardTags[cardId].RemoveAll(t => Cmp.Equals(t, tag.Name));
                if (deck.CardTags[cardId].Count == 0) deck.CardTags.Remove(cardId);
            }
        }

        // ------------------------------------------------------------------ asignación

        /// <summary>Etiquetas de una carta, en el orden del vocabulario del mazo.</summary>
        public static IReadOnlyList<string> Of(Models.Deck deck, string cardId)
        {
            if (deck == null || cardId == null) return Empty;
            if (!deck.CardTags.TryGetValue(cardId, out var tags) || tags.Count == 0) return Empty;
            return tags.OrderBy(t => IndexOf(deck, t)).ToList();
        }

        public static bool Has(Models.Deck deck, string cardId, string tag) =>
            deck != null && cardId != null &&
            deck.CardTags.TryGetValue(cardId, out var tags) && tags.Any(t => Cmp.Equals(t, tag));

        /// <summary>Pone o saca una etiqueta de una carta. Solo etiquetas del vocabulario del mazo.</summary>
        public static void Set(Models.Deck deck, string cardId, string tagName, bool on)
        {
            var tag = Find(deck, tagName);
            if (tag == null || cardId == null) return;
            if (on)
            {
                if (!deck.CardTags.TryGetValue(cardId, out var tags))
                    deck.CardTags[cardId] = tags = new List<string>();
                if (!tags.Any(t => Cmp.Equals(t, tag.Name))) tags.Add(tag.Name);
            }
            else if (deck.CardTags.TryGetValue(cardId, out var tags))
            {
                tags.RemoveAll(t => Cmp.Equals(t, tag.Name));
                if (tags.Count == 0) deck.CardTags.Remove(cardId);
            }
        }

        public static bool Toggle(Models.Deck deck, string cardId, string tagName)
        {
            var on = !Has(deck, cardId, tagName);
            Set(deck, cardId, tagName, on);
            return on;
        }

        /// <summary>Cuántas cartas DEL MAZO tienen la etiqueta (las recortadas también están).</summary>
        public static int CardCount(Models.Deck deck, string tagName) =>
            deck == null ? 0 : deck.Cards.Count(e => Has(deck, e.CardId, tagName));

        /// <summary>
        /// Saca las asignaciones de cartas que ya no están en el mazo y las de etiquetas borradas.
        /// La etiqueta acompaña a la carta mientras esté en el mazo: sacarla con ✕ se lleva su
        /// clasificación, igual que levantar la carta de la mesa.
        /// </summary>
        public static void Prune(Models.Deck deck)
        {
            if (deck == null) return;
            var present = new HashSet<string>(deck.Cards.Select(e => e.CardId), StringComparer.Ordinal);
            foreach (var cardId in deck.CardTags.Keys.ToList())
            {
                if (!present.Contains(cardId)) { deck.CardTags.Remove(cardId); continue; }
                deck.CardTags[cardId].RemoveAll(t => Find(deck, t) == null);
                if (deck.CardTags[cardId].Count == 0) deck.CardTags.Remove(cardId);
            }
        }

        /// <summary>Copia vocabulario y asignaciones a otro mazo (duplicar). Solo las cartas que llegaron.</summary>
        public static void CopyTo(Models.Deck source, Models.Deck target)
        {
            if (source == null || target == null) return;
            foreach (var tag in source.Tags)
                if (Find(target, tag.Name) == null)
                    target.Tags.Add(new DeckTag { Name = tag.Name, Color = tag.Color });
            foreach (var entry in target.Cards)
                foreach (var tag in Of(source, entry.CardId))
                    Set(target, entry.CardId, tag, true);
        }

        // ------------------------------------------------------------------ filtro

        /// <summary>
        /// ¿La carta pasa el filtro de etiquetas? Sin nada seleccionado pasan todas.
        /// <paramref name="all"/> false = tiene alguna de las marcadas (la unión, para explorar);
        /// true = las tiene todas (la intersección, para aislar el solapamiento).
        /// </summary>
        public static bool Matches(Models.Deck deck, string cardId, ICollection<string> selected, bool all)
        {
            if (selected == null || selected.Count == 0) return true;
            var tags = Of(deck, cardId);
            return all
                ? selected.All(s => tags.Any(t => Cmp.Equals(t, s)))
                : selected.Any(s => tags.Any(t => Cmp.Equals(t, s)));
        }

        // ------------------------------------------------------------------ agrupación

        /// <summary>
        /// Una sección por etiqueta, en el orden del vocabulario, y las cartas sin etiquetar al
        /// final. **Una carta aparece en TODAS sus pilas**: la que quita honor y pide magistrados
        /// está en las dos. Al armar, la pregunta es "cuánto deshonor tengo", y una pila que se
        /// saltea las cartas compartidas —porque viven en una sección aparte— contesta de menos.
        ///
        /// Consecuencia buscada: los conteos por sección **no** suman el mazo, porque una carta de
        /// dos etiquetas cuenta en las dos. Son conjuntos que se solapan, no cajas.
        /// <see cref="TagSection{T}.Shared"/> dice cuántas de las cartas de la pila están también
        /// en otra. Una etiqueta sin cartas no genera sección.
        ///
        /// Preserva el orden de entrada dentro de cada sección, o sea que el criterio de orden del
        /// editor sigue valiendo.
        /// </summary>
        public static List<TagSection<T>> Sections<T>(
            Models.Deck deck, IEnumerable<T> items, Func<T, string> cardIdOf)
        {
            var sections = new List<TagSection<T>>();
            if (items == null) return sections;
            var list = items as IList<T> ?? items.ToList();

            if (deck != null)
                foreach (var tag in deck.Tags)
                {
                    var section = new TagSection<T> { Tag = tag.Name };
                    foreach (var item in list)
                    {
                        var tags = Of(deck, cardIdOf(item));
                        if (!tags.Any(t => Cmp.Equals(t, tag.Name))) continue;
                        section.Items.Add(item);
                        if (tags.Count > 1) section.Shared++;
                    }
                    if (section.Items.Count > 0) sections.Add(section);
                }

            var untagged = new TagSection<T>();
            foreach (var item in list)
                if (Of(deck, cardIdOf(item)).Count == 0) untagged.Items.Add(item);
            if (untagged.Items.Count > 0) sections.Add(untagged);

            return sections;
        }

        // ------------------------------------------------------------------ interno

        private static readonly string[] Empty = new string[0];

        /// <summary>Posición en el vocabulario; las desconocidas al final (no debería haberlas).</summary>
        private static int IndexOf(Models.Deck deck, string tagName)
        {
            if (deck == null) return int.MaxValue;
            for (var i = 0; i < deck.Tags.Count; i++)
                if (Cmp.Equals(deck.Tags[i].Name, tagName)) return i;
            return int.MaxValue;
        }

        private static string Normalize(string name)
        {
            name = (name ?? "").Trim();
            return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength).Trim() : name;
        }
    }

    /// <summary>Una pila: una etiqueta (null = las cartas sin ninguna) y las cartas que la llevan.</summary>
    public class TagSection<T>
    {
        public string Tag { get; set; }

        public List<T> Items { get; } = new List<T>();

        /// <summary>
        /// Cuántas de <see cref="Items"/> están también en otra pila. Es el dato que la partición
        /// daba gratis y esta forma tiene que decir explícitamente: al armar, saber que 2 de tus 7
        /// cartas de deshonor además piden magistrados es media decisión tomada.
        /// </summary>
        public int Shared { get; set; }
    }
}
