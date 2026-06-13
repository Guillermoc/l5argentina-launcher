using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>
    /// Descarga archivos a la caché local verificando su SHA-256 DESPUÉS de bajar y ANTES de
    /// usarlos (spec §8 regla 2). Si el hash no coincide, aborta sin dejar el archivo final.
    /// Soporta file:// (para testing local en DEBUG; el esquema se valida aguas arriba).
    /// </summary>
    public class DownloadService
    {
        /// <summary>
        /// Descarga <paramref name="uri"/> a <paramref name="destPath"/> verificando el hash.
        /// Devuelve la ruta final. Lanza si el hash no coincide.
        /// </summary>
        public async Task DownloadAndVerifyAsync(
            Uri uri, string expectedSha256, string destPath,
            IProgress<DownloadProgress> progress = null, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
            var tmp = destPath + ".part";

            try
            {
                string actualHash;
                if (uri.IsFile)
                {
                    File.Copy(uri.LocalPath, tmp, overwrite: true);
                    actualHash = HashUtil.Sha256OfFile(tmp);
                }
                else
                {
                    actualHash = await DownloadHttpAsync(uri, tmp, progress, ct).ConfigureAwait(false);
                }

                if (!HashUtil.HashesEqual(actualHash, expectedSha256))
                {
                    TryDelete(tmp);
                    throw new SecurityException(
                        $"El SHA-256 del archivo descargado no coincide con el del manifest.\n" +
                        $"Esperado: {expectedSha256}\nObtenido: {actualHash}");
                }

                // Reemplazo atómico del destino final.
                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(tmp, destPath);
            }
            finally
            {
                TryDelete(tmp);
            }
        }

        private static async Task<string> DownloadHttpAsync(
            Uri uri, string tmpPath, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            using (var resp = await Http.Client.GetAsync(
                       uri, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                long read = 0;

                using (var sha = SHA256.Create())
                using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var dst = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                                81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    int n;
                    while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
                        sha.TransformBlock(buffer, 0, n, null, 0);
                        read += n;
                        progress?.Report(new DownloadProgress(read, total));
                    }
                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    return HashUtil.ToHex(sha.Hash);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }

    public struct DownloadProgress
    {
        public long BytesRead { get; }
        public long? TotalBytes { get; }
        public DownloadProgress(long read, long? total) { BytesRead = read; TotalBytes = total; }

        public double? Fraction => TotalBytes.HasValue && TotalBytes.Value > 0
            ? (double?)BytesRead / TotalBytes.Value
            : null;
    }
}
