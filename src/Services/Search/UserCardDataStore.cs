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
    }
}
