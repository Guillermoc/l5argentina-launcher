using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace L5ArgentinaLauncher.Services.Search
{
    /// <summary>
    /// Presets de filtros del Buscador con nombre, en %APPDATA%\...\presets.json (respeta
    /// L5A_DATA_ROOT). Superset del preset único de la app: el criteria serializado usa los
    /// mismos nombres JSON que los golden tests, así que el formato queda documentado de facto.
    /// </summary>
    public class FilterPresetStore
    {
        private class Dto
        {
            [JsonPropertyName("presets")]
            public Dictionary<string, CardSearchCriteria> Presets { get; set; } =
                new Dictionary<string, CardSearchCriteria>();
        }

        private readonly Dictionary<string, CardSearchCriteria> _presets =
            new Dictionary<string, CardSearchCriteria>();

        public List<string> Names => _presets.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        public static FilterPresetStore Load()
        {
            var store = new FilterPresetStore();
            try
            {
                if (File.Exists(AppConstants.PresetsPath))
                {
                    var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(AppConstants.PresetsPath));
                    foreach (var kv in dto?.Presets ?? new Dictionary<string, CardSearchCriteria>())
                        if (kv.Value != null) store._presets[kv.Key] = kv.Value;
                }
            }
            catch
            {
                // archivo corrupto → se arranca vacío; el próximo guardado lo regenera
            }
            return store;
        }

        public CardSearchCriteria Get(string name) =>
            _presets.TryGetValue(name, out var c) ? c : null;

        /// <summary>Guarda (o pisa) el preset y persiste. Nombre vacío → false.</summary>
        public bool Save(string name, CardSearchCriteria criteria)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || criteria == null) return false;
            _presets[name] = criteria;
            Persist();
            return true;
        }

        public void Delete(string name)
        {
            if (_presets.Remove(name)) Persist();
        }

        private void Persist()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppConstants.PresetsPath));
            File.WriteAllText(AppConstants.PresetsPath,
                JsonSerializer.Serialize(new Dto { Presets = _presets },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
