using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace L5ArgentinaLauncher.Models
{
    /// <summary>
    /// Modelo del manifest.json del bucket (spec §6). El launcher lo trata como datos:
    /// nunca sigue URLs absolutas de terceros; los <c>file</c> son rutas relativas que se
    /// resuelven contra el origen del manifest (ver <see cref="Services.ManifestService"/>).
    /// </summary>
    public class Manifest
    {
        [JsonPropertyName("schema")]
        public int Schema { get; set; }

        [JsonPropertyName("launcher")]
        public LauncherInfo Launcher { get; set; }

        [JsonPropertyName("databases")]
        public List<DatabaseEntry> Databases { get; set; } = new List<DatabaseEntry>();

        [JsonPropertyName("images")]
        public ImagePackEntry Images { get; set; }

        // v2: en v1 puede venir ausente o vacío. Placeholder hasta definir el formato.
        [JsonPropertyName("news")]
        public List<NewsEntry> News { get; set; } = new List<NewsEntry>();
    }

    public class LauncherInfo
    {
        [JsonPropertyName("latest_version")]
        public string LatestVersion { get; set; }

        [JsonPropertyName("notes")]
        public string Notes { get; set; }
    }

    public class DatabaseEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("file")]
        public string File { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class ImagePackEntry
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("file")]
        public string File { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    /// <summary>Entrada de noticias (v2). Modelada pero no usada todavía.</summary>
    public class NewsEntry
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("date")]
        public string Date { get; set; }

        [JsonPropertyName("file")]
        public string File { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }
    }
}
