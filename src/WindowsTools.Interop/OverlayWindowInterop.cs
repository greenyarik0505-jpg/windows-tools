using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WindowsTools.Interop;

/// <summary>
/// Win32 helpers for always-on-top overlay windows such as the Island.
/// </summary>
public static class OverlayWindowInterop
{
    /// <summary>
    /// Hides the window from Alt+Tab and the taskbar.
    /// </summary>
    /// <remarks>
    /// <c>WS_EX_NOACTIVATE</c> is deliberately not set: WinUI then stops raising pointer events, so hover could not
    /// expand the Island. Show the window with <c>AppWindow.Show(false)</c> to avoid taking focus instead.
    /// </remarks>
    public static void MakeToolWindow(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        var handle = (HWND)hwnd;
        var exStyle = PInvoke.GetWindowLongPtr(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        exStyle |= (nint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
        _ = PInvoke.SetWindowLongPtr(handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle);
    }

    /// <summary>
    /// Asks DWM to draw the window with Windows 11 rounded corners. No effect on older systems.
    /// </summary>
    public static unsafe void UseRoundedCorners(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        _ = PInvoke.DwmSetWindowAttribute(
            (HWND)hwnd,
            DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
            &preference,
            (uint)sizeof(DWM_WINDOW_CORNER_PREFERENCE));
    }
}
