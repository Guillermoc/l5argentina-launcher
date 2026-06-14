using System;
using System.Windows.Markup;

namespace L5ArgentinaLauncher.Resources
{
    /// <summary>
    /// Extensión de marcado para textos localizados desde XAML: <c>Text="{loc:Loc Clave}"</c>.
    /// Resuelve la clave contra <see cref="Strings"/> al cargar la vista.
    /// </summary>
    [MarkupExtensionReturnType(typeof(string))]
    public class LocExtension : MarkupExtension
    {
        public string Key { get; set; }

        public LocExtension() { }
        public LocExtension(string key) { Key = key; }

        public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
    }
}
