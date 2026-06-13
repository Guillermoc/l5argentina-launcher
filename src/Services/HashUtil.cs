using System;
using System.IO;
using System.Security.Cryptography;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>Cálculo y comparación de SHA-256 (spec §8 regla 2).</summary>
    public static class HashUtil
    {
        public static string Sha256OfFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                return ToHex(sha.ComputeHash(fs));
            }
        }

        public static string ToHex(byte[] bytes)
        {
            var c = new char[bytes.Length * 2];
            const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                c[i * 2] = hex[bytes[i] >> 4];
                c[i * 2 + 1] = hex[bytes[i] & 0xF];
            }
            return new string(c);
        }

        /// <summary>Comparación case-insensitive de dos hashes hex.</summary>
        public static bool HashesEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
