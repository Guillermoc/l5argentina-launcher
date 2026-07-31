using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace L5ArgentinaLauncher.Views
{
    /// <summary>
    /// Corrige el dimensionado de las ventanas con <c>WindowStyle=None</c> + <c>WindowChrome</c>
    /// (Buscador y Mazos), que sin esto quedan tapadas por la barra de tareas en dos momentos:
    ///
    /// <list type="bullet">
    /// <item>Al <b>maximizar</b>: Windows maximiza contra el monitor completo, no contra el área
    /// de trabajo, porque el chrome propio le saca el marco que normalmente lo acota. Se responde
    /// <c>WM_GETMINMAXINFO</c> con el rectángulo de trabajo del monitor donde está la ventana.</item>
    /// <item>Al <b>abrir</b>: el alto de diseño (840 DIP) supera el área de trabajo de un 1080p a
    /// 125 % (~826 DIP), y <c>CenterScreen</c> centra contra la pantalla entera, así que el borde
    /// inferior cae debajo de la barra. Se recorta y se reubica la ventana dentro del área útil.</item>
    /// </list>
    ///
    /// Todo el trabajo se hace en píxeles de dispositivo (lo que espera la API de Win32) y recién
    /// se convierte a DIP para tocar <see cref="Window"/>.
    /// </summary>
    internal static class WindowSizingFix
    {
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        public static void Attach(Window window)
        {
            window.SourceInitialized += (s, e) =>
            {
                var source = PresentationSource.FromVisual(window) as HwndSource;
                if (source == null) return;
                source.AddHook(Hook);
                ClampToWorkArea(window, source);
            };
        }

        private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;

            RECT work, monitor;
            if (!TryGetMonitorRects(hwnd, out work, out monitor)) return IntPtr.Zero;

            var info = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            // ptMaxPosition es relativo al origen del monitor, no al escritorio.
            info.ptMaxPosition.X = work.Left - monitor.Left;
            info.ptMaxPosition.Y = work.Top - monitor.Top;
            info.ptMaxSize.X = work.Right - work.Left;
            info.ptMaxSize.Y = work.Bottom - work.Top;
            info.ptMaxTrackSize.X = info.ptMaxSize.X;
            info.ptMaxTrackSize.Y = info.ptMaxSize.Y;
            Marshal.StructureToPtr(info, lParam, true);

            handled = true;
            return IntPtr.Zero;
        }

        private static void ClampToWorkArea(Window window, HwndSource source)
        {
            RECT work, monitor;
            if (!TryGetMonitorRects(source.Handle, out work, out monitor)) return;

            var toDip = source.CompositionTarget.TransformFromDevice;
            var topLeft = toDip.Transform(new Point(work.Left, work.Top));
            var bottomRight = toDip.Transform(new Point(work.Right, work.Bottom));
            var maxWidth = bottomRight.X - topLeft.X;
            var maxHeight = bottomRight.Y - topLeft.Y;

            // MinWidth/MinHeight ganan si el área útil es más chica que el mínimo de la ventana:
            // preferimos una ventana usable aunque desborde a una inutilizablemente comprimida.
            if (window.Width > maxWidth) window.Width = Math.Max(window.MinWidth, maxWidth);
            if (window.Height > maxHeight) window.Height = Math.Max(window.MinHeight, maxHeight);

            if (window.Left < topLeft.X) window.Left = topLeft.X;
            if (window.Top < topLeft.Y) window.Top = topLeft.Y;
            if (window.Left + window.Width > bottomRight.X) window.Left = bottomRight.X - window.Width;
            if (window.Top + window.Height > bottomRight.Y) window.Top = bottomRight.Y - window.Height;
        }

        /// <summary>
        /// Área de trabajo **en DIP** del monitor donde cae un punto de pantalla (en píxeles de
        /// dispositivo). La usa la previsualización con Alt para no dibujarse fuera de pantalla:
        /// <see cref="SystemParameters.WorkArea"/> no sirve porque solo conoce el monitor primario.
        /// Si Win32 falla, cae al monitor primario, que es mejor que no acotar nada.
        /// </summary>
        public static Rect WorkAreaAtDevicePoint(Visual reference, Point devicePoint)
        {
            var source = PresentationSource.FromVisual(reference) as HwndSource;
            var handle = MonitorFromPoint(new POINT { X = (int)devicePoint.X, Y = (int)devicePoint.Y },
                                          MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (handle == IntPtr.Zero || source == null || !GetMonitorInfo(handle, ref info))
                return SystemParameters.WorkArea;

            var toDip = source.CompositionTarget.TransformFromDevice;
            var topLeft = toDip.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
            var bottomRight = toDip.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        /// <summary>
        /// Corre <paramref name="window"/> en diagonal respecto de <paramref name="reference"/> para
        /// que no se abran una **exactamente encima** de la otra y no se vea que son dos: tienen el
        /// mismo tamaño de diseño y las dos se abren centradas, así que sin esto coinciden al píxel.
        ///
        /// Se llama antes de <c>Show</c> y hay que poner <see cref="WindowStartupLocation.Manual"/>
        /// primero: con <c>CenterScreen</c>, WPF recalcula la posición al mostrar y pisa lo que se le
        /// haya puesto.
        ///
        /// **No se valida acá que entre en la pantalla**, a propósito: <c>Window.Left</c> vive en las
        /// unidades del escritorio virtual y el rectángulo del monitor viene en píxeles de
        /// dispositivo, así que compararlos con dos pantallas a distinta escala da cualquier cosa —de
        /// hecho, la primera versión de esto no corría nada porque el desplazamiento "no entraba".
        /// De que entre ya se encarga <see cref="ClampToWorkArea"/> en <c>SourceInitialized</c>, que
        /// hace la cuenta en el espacio correcto. En el peor caso vuelve a quedar donde estaba.
        /// </summary>
        public static void CascadeFrom(Window reference, Window window, double offset = 34)
        {
            if (reference == null || window == null) return;
            // Maximizada, Left/Top son la posición *restaurada*: correrse respecto de eso da un
            // resultado arbitrario, y además contra una maximizada la nueva ya se distingue sola.
            if (reference.WindowState != WindowState.Normal) return;
            if (double.IsNaN(reference.Left) || double.IsNaN(reference.Top)) return;

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = reference.Left + offset;
            window.Top = reference.Top + offset;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT point, int flags);

        private static bool TryGetMonitorRects(IntPtr hwnd, out RECT work, out RECT monitor)
        {
            work = default(RECT);
            monitor = default(RECT);

            var handle = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (handle == IntPtr.Zero) return false;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(handle, ref info)) return false;

            work = info.rcWork;
            monitor = info.rcMonitor;
            return true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }
    }
}
