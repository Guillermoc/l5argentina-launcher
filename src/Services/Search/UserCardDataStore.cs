using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using L5ArgentinaLauncher.Services.Deck;

namespace L5ArgentinaLauncher.Services.Search
{
    /// <summary>
    /// Persistencia de favoritas/ocultas del Buscador en %APPDATA%\...\usercards.json
    /// (respeta L5A_DATA_ROOT, como el resto de la config). Equivale a la tabla
    /// user_card_data de la app Android.
    ///
    /// **En disco se guardan NOMBRES de carta, no ids** (ADR-044): el id sale del cards_db.json
    /// y lo reasigna quien genera la base, así que sobrevivir a una actualización de la base
    /// exige la única clave que el juego y el launcher acuerdan. En memoria se sigue trabajando
    /// con ids, que es lo que matchea <see cref="CardSearchEngine"/>: la traducción ocurre acá,
    /// al cargar y al guardar.
    ///
    /// Las listas ya no viven acá: son .txt en la carpeta Lists (ADR-045). El archivo v1 las
    /// tenía, y <see cref="Load"/> las migra una sola vez.
    /// Guardado inmediato en cada toggle: el archivo es chico y el evento, esporádico.
    /// </summary>
    public class UserCardDataStore
    {
        /// <summary>v1 = ids y listas adentro; v2 = nombres y sin listas.</summary>
        private const int CurrentVersion = 2;

        private class Dto
        {
            [JsonPropertyName("version")] public int Version { get; set; }
            [JsonPropertyName("favorites")] public List<string> Favorites { get; set; } = new List<string>();
            [JsonPropertyName("hidden")] public List<string> Hidden { get; set; } = new List<string>();
            // solo v1: se leen para migrarlas a .txt y no se vuelven a escribir (el v2 no las
            // tiene, así que en el guardado quedan en null y no salen al archivo)
            [JsonPropertyName("lists")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public Dictionary<string, List<string>> Lists { get; set; }
        }

        private readonly DeckRepository _repo;

        public UserCardData Data { get; } = new UserCardData();

        private UserCardDataStore(DeckRepository repo)
        {
            _repo = repo;
        }

        /// <summary>
        /// Carga las favoritas/ocultas resolviendo los nombres contra el catálogo. Si el archivo
        /// es v1 (ids + listas), lo migra: respalda el original en usercards.v1.json, traduce los
        /// ids que todavía resuelven y convierte cada lista en un .txt de la carpeta Lists.
        /// </summary>
        public static UserCardDataStore Load(DeckLibrary library)
        {
            var store = new UserCardDataStore(library?.Repo);
            Dto dto = null;
            string raw = null;
            try
            {
                if (File.Exists(AppConstants.UserCardsPath))
                {
                    raw = File.ReadAllText(AppConstants.UserCardsPath);
                    dto = JsonSerializer.Deserialize<Dto>(raw);
                }
            }
            catch
            {
                // archivo corrupto → se arranca vacío; el próximo guardado lo regenera
            }
            if (dto == null) return store;

            if (dto.Version >= CurrentVersion)
            {
                foreach (var id in store.ResolveNames(dto.Favorites)) store.Data.Favorites.Add(id);
                foreach (var id in store.ResolveNames(dto.Hidden)) store.Data.Hidden.Add(id);
                return store;
            }

            // ---- migración v1 → v2 -------------------------------------------------
            // El v1 guardaba ids: los que ya no existen en el catálogo se pierden (estaban rotos
            // igual). El archivo viejo se conserva al lado, nunca se destruye lo del usuario.
            BackupV1(raw);
            foreach (var id in dto.Favorites ?? new List<string>())
                if (store.ResolveId(id) != null) store.Data.Favorites.Add(id);
            foreach (var id in dto.Hidden ?? new List<string>())
                if (store.ResolveId(id) != null) store.Data.Hidden.Add(id);
            MigrateLists(dto.Lists, library);
            store.Save();
            return store;
        }

        private static void BackupV1(string raw)
        {
            if (raw == null) return;
            try
            {
                var backup = Path.Combine(Path.GetDirectoryName(AppConstants.UserCardsPath) ?? "",
                                          "usercards.v1.json");
                if (!File.Exists(backup)) File.WriteAllText(backup, raw);
            }
            catch
            {
                // sin respaldo no se aborta la migración: el archivo nuevo es mejor que el viejo
            }
        }

        /// <summary>Cada lista del v1 pasa a ser un .txt de la carpeta Lists, con el mismo nombre.</summary>
        private static void MigrateLists(Dictionary<string, List<string>> lists, DeckLibrary library)
        {
            if (lists == null || library == null) return;
            foreach (var list in lists)
            {
                if (string.IsNullOrWhiteSpace(list.Key)) continue;
                // una lista vacía también se migra: el usuario la creó a propósito
                var id = library.CreateList(list.Key);
                foreach (var cardId in list.Value ?? new List<string>())
                    library.Repo.SetInList(id, cardId, true); // cada mutación ya escribe el txt
            }
            library.ActiveListId = null;
        }

        public void Save()
        {
            var dto = new Dto
            {
                Version = CurrentVersion,
                Favorites = NamesOf(Data.Favorites),
                Hidden = NamesOf(Data.Hidden),
            };
            Directory.CreateDirectory(Path.GetDirectoryName(AppConstants.UserCardsPath));
            File.WriteAllText(AppConstants.UserCardsPath,
                JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }

        // ------------------------------------------------------------------ nombres ⇄ ids

        /// <summary>Ids → nombres del catálogo, ordenados. Los ids sin carta no se escriben.</summary>
        private List<string> NamesOf(IEnumerable<string> ids) =>
            ids.Select(id => _repo?.GetCardById(id)?.Name)
               .Where(n => n != null)
               .OrderBy(n => n, StringComparer.Ordinal)
               .ToList();

        /// <summary>
        /// Nombres → ids. Usa la resolución tolerante del parser de txt, así un usercards.json
        /// editado a mano (o escrito con el nombre que usa el juego) resuelve igual que un mazo.
        /// </summary>
        private IEnumerable<string> ResolveNames(IEnumerable<string> names) =>
            (names ?? Enumerable.Empty<string>())
                .Select(n => _repo?.GetCardByNameRelaxed(n)?.Id)
                .Where(id => id != null);

        private string ResolveId(string id) => _repo?.GetCardById(id)?.Id;

        // ------------------------------------------------------------------ toggles

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
