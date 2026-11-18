using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Klaxon.App.Interop;

/// <summary>Positions windows in physical pixels via SetWindowPos.</summary>
internal static class Win32Windows
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>Gets the window handle, creating it if needed.</summary>
    public static IntPtr HandleOf(Window window) => new WindowInteropHelper(window).EnsureHandle();

    /// <summary>Scale factor of the window's monitor (1.0 = 96 DPI).</summary>
    public static double ScaleOf(Window window)
    {
        var dpi = GetDpiForWindow(HandleOf(window));
        return dpi == 0 ? 1d : dpi / 96d;
    }

    /// <summary>Places the window topmost at physical bounds.</summary>
    public static void PlacePhysical(Window window, Rectangle bounds, bool activate)
    {
        var flags = SwpShowWindow | (activate ? 0 : SwpNoActivate);

        SetWindowPos(
            HandleOf(window),
            HwndTopmost,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            flags);
    }

    /// <summary>Docks the window at the bottom-right of the primary work area, using its measured size.
    /// <paramref name="stackOffsetPx"/> lifts it above earlier cards.</summary>
    public static void PlaceInCorner(Window window, int stackOffsetPx = 0, int marginPx = 16)
    {
        var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
                   ?? new Rectangle(0, 0, 1280, 720);

        var destino = new Rectangle(
            area.Right - PhysicalWidth(window) - marginPx,
            area.Bottom - PhysicalHeight(window) - marginPx - stackOffsetPx,
            PhysicalWidth(window),
            PhysicalHeight(window));

        PlacePhysical(window, destino, activate: false);
    }

    /// <summary>Window width in physical pixels, from its measured size.</summary>
    public static int PhysicalWidth(Window window) => (int)Math.Round(window.ActualWidth * ScaleOf(window));

    /// <summary>Window height in physical pixels, from its measured size.</summary>
    public static int PhysicalHeight(Window window) => (int)Math.Round(window.ActualHeight * ScaleOf(window));
}
