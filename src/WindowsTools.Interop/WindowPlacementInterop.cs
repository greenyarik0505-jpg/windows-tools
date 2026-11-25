using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace WindowsTools.Interop;

/// <summary>
/// Represents the work area bounds and DPI of a target monitor in physical pixels.
/// </summary>
public readonly record struct MonitorWorkArea(int Left, int Top, int Right, int Bottom);

/// <summary>
/// Provides managed Win32 helpers for monitor work-area queries and window foreground activation.
/// </summary>
public static class WindowPlacementInterop
{
    /// <summary>
    /// Gets the current pointer position in physical screen coordinates.
    /// </summary>
    public static (int X, int Y) GetCursorPosition()
    {
        return PInvoke.GetCursorPos(out var point)
            ? (point.X, point.Y)
            : (0, 0);
    }

    /// <summary>
    /// Gets the work area in physical pixels for the monitor nearest to the specified point.
    /// </summary>
    public static MonitorWorkArea GetMonitorWorkArea(int pointX, int pointY)
    {
        var monitor = PInvoke.MonitorFromPoint(new Point(pointX, pointY), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<MONITORINFO>()
        };

        if (!PInvoke.GetMonitorInfo(monitor, ref info))
        {
            return new MonitorWorkArea(0, 0, 1920, 1080);
        }

        return new MonitorWorkArea(info.rcWork.left, info.rcWork.top, info.rcWork.right, info.rcWork.bottom);
    }

    /// <summary>
    /// Gets the DPI for the specified window handle, defaulting to 96 on failure.
    /// </summary>
    public static uint GetWindowDpi(nint hwnd)
    {
        if (hwnd == 0)
        {
            return 96;
        }

        var dpi = PInvoke.GetDpiForWindow((HWND)hwnd);
        return dpi == 0 ? 96u : dpi;
    }

    /// <summary>
    /// Brings the specified window to the foreground so focus-loss dismissal behaves reliably.
    /// </summary>
    public static bool BringToForeground(nint hwnd)
    {
        return hwnd != 0 && PInvoke.SetForegroundWindow((HWND)hwnd);
    }
}
