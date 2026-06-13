using System;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>
    /// Descarga y parseo del manifest, más la resolución segura de las rutas de archivos
    /// referenciadas (spec §6, §8.2).
    ///
    /// Reglas de seguridad:
    ///  - HTTPS obligatorio (se rechaza http://). En build DEBUG se permite file:// y
    ///    http://localhost SOLO para testear sin el bucket real.
    ///  - Los <c>file</c> del manifest son relativos y se resuelven contra el origen del
    ///    manifest. El launcher NUNCA sigue URLs absolutas a otros dominios ni rutas que
    ///    escapen del directorio del manifest.
    /// </summary>
    public class ManifestService
    {
        public async Task<Manifest> FetchAsync(string manifestUrl, CancellationToken ct = default)
        {
            if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("La URL del manifest no es válida.");

            EnsureAllowedScheme(uri);

            string json;
            if (uri.IsFile)
            {
                json = File.ReadAllText(uri.LocalPath);
            }
            else
            {
                using (var resp = await Http.Client.GetAsync(uri, ct).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var manifest = JsonSerializer.Deserialize<Manifest>(json, opts);
            if (manifest == null)
                throw new InvalidOperationException("El manifest está vacío o no se pudo parsear.");
            return manifest;
        }

        /// <summary>
        /// Resuelve un <c>file</c> relativo del manifest contra el origen del manifest,
        /// confinándolo al directorio del manifest (mismo esquema+host+puerto y bajo su path).
        /// Rechaza URLs absolutas a otros orígenes y escapes de directorio.
        /// </summary>
        public static Uri ResolveFileUri(string manifestUrl, string relativeFile)
        {
            if (string.IsNullOrWhiteSpace(relativeFile))
                throw new InvalidOperationException("El manifest referencia un archivo vacío.");

            var baseUri = new Uri(manifestUrl, UriKind.Absolute);
            var resolved = new Uri(baseUri, relativeFile);

            // Mismo origen (esquema + host + puerto).
            bool sameOrigin =
                string.Equals(resolved.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(resolved.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) &&
                resolved.Port == baseUri.Port;

            // Confinado al directorio del manifest (no puede subir por encima con ../).
            string baseDir = baseUri.AbsolutePath;
            int slash = baseDir.LastIndexOf('/');
            baseDir = slash >= 0 ? baseDir.Substring(0, slash + 1) : "/";
            bool underBaseDir = resolved.AbsolutePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);

            if (!sameOrigin || !underBaseDir)
                throw new SecurityException(
                    "El manifest referencia un origen o ruta fuera de su propio bucket: " + relativeFile);

            return resolved;
        }

        private static void EnsureAllowedScheme(Uri uri)
        {
            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return;
#if DEBUG
            // Escape hatch SOLO en DEBUG para testear contra un bucket local.
            if (uri.IsFile) return;
            if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback)
                return;
#endif
            throw new SecurityException(
                "La URL del manifest debe ser HTTPS. Se rechazó: " + uri.Scheme + "://");
        }
    }

    /// <summary>HttpClient compartido (spec §12).</summary>
    internal static class Http
    {
        internal static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient();
            c.DefaultRequestHeaders.UserAgent.ParseAdd("L5ArgentinaLauncher/" + AppConstants.LauncherVersion);
            c.Timeout = TimeSpan.FromMinutes(5);
            return c;
        }
    }
}
