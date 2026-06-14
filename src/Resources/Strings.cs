using System.Globalization;
using System.Resources;

namespace L5ArgentinaLauncher.Resources
{
    /// <summary>
    /// Acceso central a los textos de UI (i18n). Hoy: español, en Strings.resx (cultura neutral).
    /// Para agregar otro idioma: crear Strings.&lt;cultura&gt;.resx (p. ej. Strings.en.resx) con las
    /// mismas claves y setear Thread.CurrentThread.CurrentUICulture al arrancar. Nada de código cambia.
    /// </summary>
    public static class Strings
    {
        private static readonly ResourceManager Rm =
            new ResourceManager("L5ArgentinaLauncher.Resources.Strings", typeof(Strings).Assembly);

        /// <summary>Devuelve el texto de la clave (o la clave misma si falta, para detectar olvidos).</summary>
        public static string Get(string key) =>
            Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;

        /// <summary>Texto con formato: Format("Fmt_Version", "2.0.1").</summary>
        public static string Format(string key, params object[] args) =>
            string.Format(Get(key), args);
    }
}
