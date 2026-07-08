using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Convierte el HTML acotado del texto de carta a Inlines de WPF. El inventario exhaustivo
    /// del dataset (MODELO-DATOS.md §8) es SOLO &lt;b&gt;, &lt;i&gt;, &lt;br&gt; y bullets
    /// literales; el formato histórico usaba la entidad &amp;#8226;. Cualquier tag desconocido
    /// se muestra como texto literal (nunca se interpreta — regla §8: sin WebView ni HTML real).
    /// </summary>
    public static class CardTextRenderer
    {
        private static readonly Regex NumericEntity = new Regex(@"&#(\d{2,6});");

        public static IEnumerable<Inline> ToInlines(string html)
        {
            var inlines = new List<Inline>();
            if (string.IsNullOrEmpty(html)) return inlines;

            // Entidades numéricas del formato histórico (&#8226; → •)
            var text = NumericEntity.Replace(html, m =>
            {
                int code;
                return int.TryParse(m.Groups[1].Value, out code) && code > 0 && code <= 0x10FFFF
                    ? char.ConvertFromUtf32(code) : m.Value;
            });

            int bold = 0, italic = 0;
            var buffer = new StringBuilder();

            void Flush()
            {
                if (buffer.Length == 0) return;
                var run = new Run(buffer.ToString());
                if (bold > 0) run.FontWeight = FontWeights.Bold;
                if (italic > 0) run.FontStyle = FontStyles.Italic;
                inlines.Add(run);
                buffer.Clear();
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '<')
                {
                    if (Matches(text, i, "<b>")) { Flush(); bold++; i += 2; continue; }
                    if (Matches(text, i, "</b>")) { Flush(); if (bold > 0) bold--; i += 3; continue; }
                    if (Matches(text, i, "<i>")) { Flush(); italic++; i += 2; continue; }
                    if (Matches(text, i, "</i>")) { Flush(); if (italic > 0) italic--; i += 3; continue; }
                    if (Matches(text, i, "<br>")) { Flush(); inlines.Add(new LineBreak()); i += 3; continue; }
                    if (Matches(text, i, "<br/>")) { Flush(); inlines.Add(new LineBreak()); i += 4; continue; }
                    if (Matches(text, i, "<br />")) { Flush(); inlines.Add(new LineBreak()); i += 5; continue; }
                }
                buffer.Append(text[i]);
            }
            Flush();
            return inlines;
        }

        private static bool Matches(string s, int i, string token) =>
            i + token.Length <= s.Length &&
            string.Compare(s, i, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
