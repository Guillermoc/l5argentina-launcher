using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace L5ArgentinaLauncher.Services.Search
{
    /// <summary>
    /// Persistencia de favoritas/ocultas del Buscador en %APPDATA%\...\usercards.json
    /// (respeta L5A_DATA_ROOT, como el resto de la config). Equivale a la tabla
    /// user_card_data de la app Android; "listas" quedan fuera (van con Mazos).
    /// Guardado inmediato en cada toggle: el archivo es chico y el evento, esporádico.
    /// </summary>
    public class UserCardDataStore
    {
        private class Dto
        {
            [JsonPropertyName("favorites")] public List<string> Favorites { get; set; } = new List<string>();
            [JsonPropertyName("hidden")] public List<string> Hidden { get; set; } = new List<string>();
            [JsonPropertyName("lists")] public Dictionary<string, List<string>> Lists { get; set; } = new Dictionary<string, List<string>>();
        }

        public UserCardData Data { get; } = new UserCardData();

        public static UserCardDataStore Load()
        {
            var store = new UserCardDataStore();
            try
            {
                if (File.Exists(AppConstants.UserCardsPath))
                {
                    var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(AppConstants.UserCardsPath));
                    foreach (var id in dto?.Favorites ?? new List<string>()) store.Data.Favorites.Add(id);
                    foreach (var id in dto?.Hidden ?? new List<string>()) store.Data.Hidden.Add(id);
                    foreach (var list in dto?.Lists ?? new Dictionary<string, List<string>>())
                        store.Data.Lists[list.Key] = new HashSet<string>(list.Value ?? new List<string>());
                }
            }
            catch
            {
                // archivo corrupto → se arranca vacío; el próximo guardado lo regenera
            }
            return store;
        }

        public void Save()
        {
            var dto = new Dto
            {
                Favorites = Data.Favorites.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Hidden = Data.Hidden.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Lists = Data.Lists.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.OrderBy(x => x, StringComparer.Ordinal).ToList()),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(AppConstants.UserCardsPath));
            File.WriteAllText(AppConstants.UserCardsPath,
                JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }

        /// <summary>Alterna favorita y persiste. Devuelve el estado nuevo.</summary>
        public bool ToggleFavorite(string cardId)
        {
            var added = Data.Favorites.Add(cardId);
            if (!added) Data.Favorites.Remove(cardId);
            Save();
            return added;
        }

        /// <summary>Alterna oculta y persiste. Devuelve el estado nuevo.</summary>
        public bool ToggleHidden(string cardId)
        {
            var added = Data.Hidden.Add(cardId);
            if (!added) Data.Hidden.Remove(cardId);
            Save();
            return added;
        }

        // ------------------------------------------------------------------ listas de cartas

        /// <summary>Nombres de lista ordenados (el nombre ES el id, como el seed de los golden).</summary>
        public List<string> ListNames =>
            Data.Lists.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Crea una lista vacía. Devuelve false si ya existía o el nombre está vacío.</summary>
        public bool CreateList(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || Data.Lists.ContainsKey(name)) return false;
            Data.Lists[name] = new HashSet<string>();
            Save();
            return true;
        }

        public void DeleteList(string name)
        {
            if (Data.Lists.Remove(name)) Save();
        }

        public bool ListContains(string name, string cardId) =>
            Data.Lists.TryGetValue(name, out var ids) && ids.Contains(cardId);

        /// <summary>Agrega/quita la carta de la lista y persiste. Devuelve el estado nuevo (está en la lista).</summary>
        public bool ToggleInList(string name, string cardId)
        {
            if (!Data.Lists.TryGetValue(name, out var ids)) return false;
            var added = ids.Add(cardId);
            if (!added) ids.Remove(cardId);
            Save();
            return added;
        }
    }
}
