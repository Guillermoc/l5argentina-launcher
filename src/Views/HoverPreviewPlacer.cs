using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Ubica la previsualización con Alt al lado del cursor **sin quedar nunca debajo de él**.
    ///
    /// Con <c>PlacementMode.Mouse</c>, WPF resuelve el desborde **volteando** el popup al otro lado
    /// del cursor. Contra el borde derecho de la pantalla eso lo deja justo abajo del mouse, y ahí
    /// arranca un ciclo: el popup tapa la celda → salta su `MouseLeave` → se cierra → el mouse vuelve
    /// a estar sobre la celda → `MouseMove` la reabre → parpadeo infinito. Lo que hay que garantizar
    /// no es "que entre", es que **el cursor nunca quede adentro del popup**; por eso la posición se
    /// calcula a mano en coordenadas absolutas y el volteo corre el popup su ancho completo más el
    /// espacio, en vez de espejar el offset.
    /// </summary>
    internal static class HoverPreviewPlacer
    {
        /// <summary>Separación al cursor. Tiene que ser &gt; 0 o el popup se come el `MouseLeave`.</summary>
        private const double Gap = 26;

        /// <summary>
        /// Reserva del contenido: la imagen va con `MaxWidth=430`/`MaxHeight=620` y `Stretch=Uniform`,
        /// más el borde de 2 y el padding de 5. Con la proporción de una carta manda el ancho, así que
        /// esto es el techo real, no una estimación floja.
        /// </summary>
        private const double ContentWidth = 430 + 14;
        private const double ContentHeight = 620 + 14;

        public static void Place(Popup popup, UIElement reference)
        {
            if (popup == null || reference == null) return;

            Point cursorDevice;
            try
            {
                cursorDevice = PointToScreenDevice(reference);
            }
            catch
            {
                return; // la ventana todavía no tiene HWND: se deja donde esté
            }

            var work = WindowSizingFix.WorkAreaAtDevicePoint(reference, cursorDevice);
            var cursor = ToDip(reference, cursorDevice);
            var at = ComputePosition(cursor, work, new Size(ContentWidth, ContentHeight));

            popup.Placement = PlacementMode.Absolute;
            popup.HorizontalOffset = at.X;
            popup.VerticalOffset = at.Y;
        }

        /// <summary>
        /// Geometría pura, aislada para poder verificarla: dado el cursor, el área de trabajo y el
        /// tamaño del popup, devuelve la esquina superior izquierda.
        ///
        /// El invariante que importa —y el que verifica el selftest barriendo la pantalla entera— es
        /// que **el cursor nunca cae adentro del rectángulo devuelto**. Si cayera, el popup taparía la
        /// celda, saltaría su `MouseLeave` y empezaría el parpadeo.
        /// </summary>
        internal static Point ComputePosition(Point cursor, Rect work, Size content)
        {
            // Preferido: abajo-derecha del cursor. Si no entra, al otro lado corrido su ancho entero
            // (no espejado: espejar el offset es lo que lo mete abajo del mouse).
            var x = cursor.X + Gap;
            if (x + content.Width > work.Right) x = cursor.X - Gap - content.Width;
            if (x < work.Left) x = work.Left;

            var y = cursor.Y + Gap;
            if (y + content.Height > work.Bottom) y = work.Bottom - content.Height;
            if (y < work.Top) y = work.Top;

            // Última red: si la pantalla es tan chica que el popup igual abarca el cursor, se lo
            // empuja al borde más lejano en Y. Sin esto quedaría el bucle en resoluciones mínimas.
            if (new Rect(x, y, content.Width, content.Height).Contains(cursor))
            {
                var below = cursor.Y + Gap;
                var above = cursor.Y - Gap - content.Height;
                y = (work.Bottom - below) >= (above - work.Top) ? below : above;
            }
            return new Point(x, y);
        }

        private static Point PointToScreenDevice(UIElement reference)
        {
            var local = Mouse.GetPosition(reference);
            return reference.PointToScreen(local);
        }

        private static Point ToDip(UIElement reference, Point device)
        {
            var source = PresentationSource.FromVisual(reference) as HwndSource;
            if (source == null) return device;
            return source.CompositionTarget.TransformFromDevice.Transform(device);
        }
    }
}
