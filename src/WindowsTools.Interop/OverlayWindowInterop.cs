using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WindowsTools.Interop;

/// <summary>
/// Represents the full physical pixel bounds of a display monitor.
/// </summary>
public readonly record struct MonitorBounds(int Left, int Top, int Right, int Bottom);

/// <summary>
/// Provides managed Win32 helpers for non-activating topmost overlays, monitor queries, and fullscreen detection.
/// </summary>
public static class OverlayWindowInterop
{
    /// <summary>
    /// Configures a window handle as a non-activating tool overlay that never steals focus or appears in Alt+Tab.
    /// </summary>
    public static void ConfigureNoActivateToolOverlay(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        var handle = (HWND)hwnd;
        var currentStyle = PInvoke.GetWindowLongPtr(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var updatedStyle = currentStyle
            | (nint)WINDOW_EX_STYLE.WS_EX_NOACTIVATE
            | (nint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW
            | (nint)WINDOW_EX_STYLE.WS_EX_TOPMOST;

        _ = PInvoke.SetWindowLongPtr(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, updatedStyle);
        _ = PInvoke.SetWindowPos(
            handle,
            new HWND(-1), // HWND_TOPMOST
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE
            | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Marks a window handle as a tool window so it stays hidden from the taskbar and Alt+Tab switcher.
    /// </summary>
    public static void ConfigureToolWindow(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        var handle = (HWND)hwnd;
        var currentStyle = PInvoke.GetWindowLongPtr(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var updatedStyle = currentStyle | (nint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
        _ = PInvoke.SetWindowLongPtr(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, updatedStyle);
    }

    /// <summary>
    /// Moves and resizes a topmost overlay without activating or focusing it.
    /// </summary>
    public static void SetTopMostBoundsNoActivate(nint hwnd, int x, int y, int width, int height)
    {
        if (hwnd == 0)
        {
            return;
        }

        _ = PInvoke.SetWindowPos(
            (HWND)hwnd,
            new HWND(-1), // HWND_TOPMOST
            x,
            y,
            width,
            height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Gets the full monitor bounds in physical pixels for the primary display or the monitor containing <paramref name="hwnd"/>.
    /// </summary>
    public static MonitorBounds GetMonitorBounds(nint hwnd)
    {
        var monitor = hwnd != 0
            ? PInvoke.MonitorFromWindow((HWND)hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY)
            : PInvoke.MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);

        var info = new MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<MONITORINFO>()
        };

        if (!PInvoke.GetMonitorInfo(monitor, ref info))
        {
            return new MonitorBounds(0, 0, 1920, 1080);
        }

        return new MonitorBounds(info.rcMonitor.left, info.rcMonitor.top, info.rcMonitor.right, info.rcMonitor.bottom);
    }

    /// <summary>
    /// Returns <c>true</c> when a fullscreen game, presentation, or borderless fullscreen application occupies the foreground monitor.
    /// </summary>
    public static unsafe bool IsFullscreenApplicationActive(nint overlayHwnd)
    {
        if (PInvoke.SHQueryUserNotificationState(out var state).Succeeded &&
            state is QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
                or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
        {
            return true;
        }

        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull || foreground == (HWND)overlayHwnd)
        {
            return false;
        }

        var classBuffer = stackalloc char[64];
        var classLength = PInvoke.GetClassName(foreground, classBuffer, 64);
        if (classLength > 0)
        {
            var className = new ReadOnlySpan<char>(classBuffer, classLength);
            if (className.Equals("Progman", StringComparison.Ordinal) ||
                className.Equals("WorkerW", StringComparison.Ordinal) ||
                className.Equals("Shell_TrayWnd", StringComparison.Ordinal) ||
                className.Equals("Shell_SecondaryTrayWnd", StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (!PInvoke.GetWindowRect(foreground, out var windowRect))
        {
            return false;
        }

        var monitor = PInvoke.MonitorFromWindow(foreground, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO
        {
            cbSize = (uint)Marshal.SizeOf<MONITORINFO>()
        };

        if (!PInvoke.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        return windowRect.left <= info.rcMonitor.left
            && windowRect.top <= info.rcMonitor.top
            && windowRect.right >= info.rcMonitor.right
            && windowRect.bottom >= info.rcMonitor.bottom;
    }
}
