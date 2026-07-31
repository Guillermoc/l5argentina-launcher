using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace L5ArgentinaLauncher.Models
{
    /// <summary>
    /// Estado/config local del usuario en %APPDATA%\L5Argentina Launcher\config.json (spec §13).
    /// </summary>
    public class UserConfig
    {
        /// <summary>Carpeta de instalación de Sun and Moon (la que contiene Sun and Moon.exe).</summary>
        [JsonPropertyName("sunAndMoonPath")]
        public string SunAndMoonPath { get; set; }

        /// <summary>URL del manifest. Default hardcodeado; editable por el usuario (spec §8.2).</summary>
        [JsonPropertyName("manifestUrl")]
        public string ManifestUrl { get; set; }

        /// <summary>Última base seleccionada por el usuario en la UI: "community" | "original".</summary>
        [JsonPropertyName("selectedBaseId")]
        public string SelectedBaseId { get; set; }

        /// <summary>Base actualmente escrita en database.xml (para no recopiar en cada arranque).</summary>
        [JsonPropertyName("appliedBaseId")]
        public string AppliedBaseId { get; set; }

        /// <summary>Versión de la base actualmente aplicada.</summary>
        [JsonPropertyName("appliedBaseVersion")]
        public string AppliedBaseVersion { get; set; }

        /// <summary>Versiones/hashes de las bases en caché, por id, para saber si re-descargar.</summary>
        [JsonPropertyName("cachedDatabases")]
        public Dictionary<string, CachedFile> CachedDatabases { get; set; } = new Dictionary<string, CachedFile>();

        /// <summary>Hashes de los archivos del catálogo del Buscador ya descargados, por clave
        /// ("cards" | "rules" | "filters"), para saber si re-descargar.</summary>
        [JsonPropertyName("cachedCatalogFiles")]
        public Dictionary<string, CachedFile> CachedCatalogFiles { get; set; } = new Dictionary<string, CachedFile>();

        /// <summary>Packs de imágenes del pool ya aplicados sobre la instalación, por id
        /// ("lotus", "celestial", …), para mostrar estado y detectar actualizaciones.</summary>
        [JsonPropertyName("appliedImagePacks")]
        public Dictionary<string, CachedFile> AppliedImagePacks { get; set; } = new Dictionary<string, CachedFile>();

        /// <summary>
        /// Ids de packs de imágenes que este launcher **ya vio** en el manifest del pool. Sirve para
        /// una sola cosa: distinguir un pack **nuevo** de uno viejo que nunca bajaste
        /// ([ADR-056](../../docs/DECISIONS.md)). Se siembra con todo lo que había la primera vez, así
        /// que estrenar esta versión no marca los ~20 arcos existentes como novedades.
        ///
        /// **`null` ≠ lista vacía**: null es "todavía no se sembró" y vacío sería "no había ningún
        /// pack". Por eso no se inicializa acá.
        /// </summary>
        [JsonPropertyName("knownImagePacks")]
        public List<string> KnownImagePacks { get; set; }

        /// <summary>Versión del pack de imágenes ya aplicado (para no re-extraer si no cambió).</summary>
        [JsonPropertyName("appliedImagePackVersion")]
        public string AppliedImagePackVersion { get; set; }

        [JsonPropertyName("appliedImagePackSha256")]
        public string AppliedImagePackSha256 { get; set; }

        /// <summary>True una vez capturado el backup de la base "original" (primer arranque, spec §7).</summary>
        [JsonPropertyName("originalBackupCaptured")]
        public bool OriginalBackupCaptured { get; set; }
    }

    public class CachedFile
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }
    }
}
