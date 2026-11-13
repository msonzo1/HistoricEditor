using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HistoricEditor;

// La barra del titolo standard è disegnata da Windows e non usa le risorse WPF:
// si chiede a DWM di usare la variante scura quando il tema dell'app è scuro.
internal static class TitleBarTheme
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    private static bool registered;

    public static void Register()
    {
        if (registered)
        {
            return;
        }

        registered = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window)
            {
                Apply(window, ThemeManager.Current == AppTheme.Dark);
            }
        }));
    }

    public static void ApplyToOpenWindows(bool dark)
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows)
        {
            Apply(window, dark);
        }
    }

    private static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            return;
        }

        var value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }

        // La cornice già disegnata non cambia da sola: senza questo la scheda personaggio, che al caricamento compila
        // molti campi, restava con la barra bianca e senza i pulsanti riduci/ingrandisci finché non si ridimensionava.
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, RdwFrame | RdwInvalidate | RdwUpdateNow);

        // Su Windows 10 non basta: la barra cambia solo quando la finestra cambia dimensione (come ingrandendola a mano).
        // Si allarga di un pixel e la si riporta subito com'era.
        if (window.WindowState == WindowState.Normal && GetWindowRect(handle, out var rect))
        {
            var (width, height) = (rect.Right - rect.Left, rect.Bottom - rect.Top);
            const uint flags = SwpNoMove | SwpNoZOrder | SwpNoActivate;
            SetWindowPos(handle, IntPtr.Zero, 0, 0, width + 1, height, flags);
            SetWindowPos(handle, IntPtr.Zero, 0, 0, width, height, flags);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRegion, uint flags);
}
