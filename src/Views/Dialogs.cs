using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using L5ArgentinaLauncher.Resources;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Diálogos chicos con la estética de la app para las ventanas que no tienen overlay propio
    /// (Buscador y editor de Mazos). <see cref="MainWindow"/> no los usa: tiene su propio
    /// <c>DialogOverlay</c> dentro del lienzo escalado.
    ///
    /// Nada de <c>MessageBox</c> del sistema (ADR-013): rompe el tema y en una ventana con chrome
    /// propio se ve directamente como otra aplicación.
    /// </summary>
    internal static class Dialogs
    {
        /// <summary>Pide un texto de una línea. Devuelve null si se canceló.</summary>
        public static string Prompt(Window owner, string title, string label, string initial)
        {
            var box = new TextBox
            {
                Text = initial ?? "",
                Style = (Style)owner.FindResource("InvertedTextBox"),
                Height = 32,
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 14),
            };
            var body = new StackPanel();
            body.Children.Add(Label(owner, label));
            body.Children.Add(box);

            StackPanel buttons;
            var dialog = Shell(owner, title, body, out buttons);
            var ok = Action(owner, Strings.Get("Deck_PromptOk"), isDefault: true);
            ok.Click += (s, e) => dialog.DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(Cancel(owner, Strings.Get("Deck_PromptCancel")));

            dialog.Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
            return dialog.ShowDialog() == true ? box.Text : null;
        }

        /// <summary>
        /// Confirmación destructiva. El botón que confirma **no** es el default: para borrar hay que
        /// elegirlo, y un Enter de reflejo cancela.
        /// </summary>
        public static bool Confirm(Window owner, string title, string message, string confirmText)
        {
            var body = new StackPanel();
            body.Children.Add(Label(owner, message, wrap: true));

            StackPanel buttons;
            var dialog = Shell(owner, title, body, out buttons);
            var ok = Action(owner, confirmText, isDefault: false);
            ok.Foreground = (Brush)owner.FindResource("ErrorText");
            ok.Click += (s, e) => dialog.DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(Cancel(owner, Strings.Get("Deck_PromptCancel"), isDefault: true));

            return dialog.ShowDialog() == true;
        }

        /// <summary>Aviso de un solo botón (resultado de importar, portapapeles vacío…).</summary>
        public static void Info(Window owner, string title, string message)
        {
            var body = new StackPanel();
            body.Children.Add(Label(owner, message, wrap: true));

            StackPanel buttons;
            var dialog = Shell(owner, title, body, out buttons);
            var ok = Action(owner, Strings.Get("Btn_Close"), isDefault: true);
            ok.Click += (s, e) => dialog.DialogResult = true;
            buttons.Children.Add(ok);
            dialog.ShowDialog();
        }

        // ------------------------------------------------------------------ piezas comunes

        private static TextBlock Label(Window owner, string text, bool wrap = false) => new TextBlock
        {
            Text = text,
            FontFamily = (FontFamily)owner.FindResource("BodyFont"),
            FontSize = 14,
            Foreground = (Brush)owner.FindResource("Text"),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Margin = wrap ? new Thickness(0, 0, 0, 14) : new Thickness(0),
        };

        private static Button Action(Window owner, string text, bool isDefault) => new Button
        {
            Content = text,
            Style = (Style)owner.FindResource("GhostButton"),
            Height = 30,
            Padding = new Thickness(14, 0, 14, 0),
            IsDefault = isDefault,
        };

        private static Button Cancel(Window owner, string text, bool isDefault = false)
        {
            var button = Action(owner, text, isDefault);
            button.Margin = new Thickness(8, 0, 0, 0);
            button.IsCancel = true;
            return button;
        }

        /// <summary>Ventana modal sin chrome, con el borde y el fondo del tema.</summary>
        private static Window Shell(Window owner, string title, StackPanel body, out StackPanel buttons)
        {
            buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            body.Children.Add(buttons);
            body.Margin = new Thickness(18);

            var dialog = new Window
            {
                Title = title,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 340,
                MaxWidth = 460,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = (Brush)owner.FindResource("Surface"),
                Content = new Border
                {
                    BorderBrush = (Brush)owner.FindResource("GoldDim"),
                    BorderThickness = new Thickness(1),
                    Child = body,
                },
            };
            dialog.MouseLeftButtonDown += (s, e) => { try { dialog.DragMove(); } catch { } };
            return dialog;
        }
    }
}
