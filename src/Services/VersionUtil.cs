using System;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>Comparación de versiones tipo "1.2.0" (numérica por componente).</summary>
    public static class VersionUtil
    {
        /// <summary>True si <paramref name="candidate"/> es estrictamente más nueva que <paramref name="current"/>.</summary>
        public static bool IsNewer(string candidate, string current)
        {
            return Compare(candidate, current) > 0;
        }

        public static int Compare(string a, string b)
        {
            int[] pa = Parse(a), pb = Parse(b);
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va = i < pa.Length ? pa[i] : 0;
                int vb = i < pb.Length ? pb[i] : 0;
                if (va != vb) return va.CompareTo(vb);
            }
            return 0;
        }

        private static int[] Parse(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return new[] { 0 };
            var parts = v.Trim().Split('.');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                // Tolerar sufijos no numéricos ("1.2.0-rc1" → 1,2,0).
                var digits = new string(System.Linq.Enumerable.ToArray(
                    System.Linq.Enumerable.TakeWhile(parts[i], char.IsDigit)));
                int.TryParse(digits, out result[i]);
            }
            return result;
        }
    }
}
