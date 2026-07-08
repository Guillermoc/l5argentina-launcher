using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace L5ArgentinaLauncher.Models
{
    /// <summary>
    /// Manifest del CDN de la app Android (canal production): lista plana de packages con URL
    /// absoluta y SHA-256. Es la fuente de los packs de imágenes (y a futuro del catálogo).
    /// Las URLs se validan contra el MISMO origen del manifest antes de descargar
    /// (<see cref="Services.ManifestService.ResolveSameOriginUrl"/>) — mismo modelo de
    /// confianza que el manifest del launcher, adaptado a URLs absolutas.
    /// </summary>
    public class PoolManifest
    {
        [JsonPropertyName("packages")]
        public List<PoolPackage> Packages { get; set; } = new List<PoolPackage>();
    }

    public class PoolPackage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>"database" | "rules" | "filters" | "images" | "changelog"</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }
    }
}
