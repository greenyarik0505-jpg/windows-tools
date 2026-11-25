using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WindowsTools.Interop;

/// <summary>
/// Hosts a Win32 system tray icon, DPI/theme-aware icon rendering, and native context menu.
/// </summary>
public sealed unsafe class TrayIconHost : IDisposable
{
    private const uint _trayIconId = 1;
    private const uint _trayCallbackMessage = PInvoke.WM_APP + 1;
    private const uint _ninKeySelect = PInvoke.NIN_SELECT + 1;
    private const uint _menuIdSettings = 1001;
    private const uint _menuIdExit = 1002;

    private readonly string _windowClassName;
    private readonly string _tooltip;
    private readonly string _settingsLabel;
    private readonly string _exitLabel;
    private readonly WNDPROC _wndProcDelegate;
    private readonly uint _taskbarCreatedMessage;
    private readonly uint _activateInstanceMessage;

    private HWND _hwnd;
    private DestroyIconSafeHandle? _currentIcon;
    private bool _iconAdded;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayIconHost"/> class and registers the tray icon.
    /// </summary>
    /// <param name="tooltip">Tooltip text displayed when hovering the tray icon.</param>
    /// <param name="settingsLabel">Localized text for the Settings menu item.</param>
    /// <param name="exitLabel">Localized text for the Exit menu item.</param>
    public TrayIconHost(string tooltip, string settingsLabel, string exitLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(exitLabel);

        _tooltip = tooltip;
        _settingsLabel = settingsLabel;
        _exitLabel = exitLabel;
        _windowClassName = $"WindowsTools.TrayHost.{Guid.NewGuid():N}";
        _wndProcDelegate = WindowProc;
        _taskbarCreatedMessage = PInvoke.RegisterWindowMessage("TaskbarCreated");
        _activateInstanceMessage = PInvoke.RegisterWindowMessage(SingleInstanceGuard.ActivateMessageName);

        CreateMessageWindow();
        UpdateTrayIcon(add: true);
    }

    /// <summary>
    /// Occurs when the user activates the tray icon with a primary click.
    /// </summary>
    public event EventHandler<(int X, int Y)>? PrimaryClicked;

    /// <summary>
    /// Occurs when the user selects Settings from the tray context menu.
    /// </summary>
    public event EventHandler? SettingsClicked;

    /// <summary>
    /// Occurs when the user selects Exit from the tray context menu.
    /// </summary>
    public event EventHandler? ExitClicked;

    /// <summary>
    /// Occurs when a duplicate launch signals the primary instance to show its UI.
    /// </summary>
    public event EventHandler<(int X, int Y)>? SecondInstanceActivated;

    /// <summary>
    /// Gets the anchor point of the tray icon in physical screen pixels.
    /// </summary>
    public (int X, int Y) GetAnchorPoint()
    {
        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = _trayIconId
        };

        if (PInvoke.Shell_NotifyIconGetRect(in identifier, out var rect).Succeeded)
        {
            return ((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2);
        }

        return WindowPlacementInterop.GetCursorPosition();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_iconAdded && !_hwnd.IsNull)
        {
            var data = CreateNotifyIconData();
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
            _iconAdded = false;
        }

        if (_currentIcon is not null)
        {
            _currentIcon.Dispose();
            _currentIcon = null;
        }

        if (!_hwnd.IsNull)
        {
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = default;
        }

        fixed (char* classNamePtr = _windowClassName)
        {
            PInvoke.UnregisterClass(classNamePtr, HINSTANCE.Null);
        }
    }

    private void CreateMessageWindow()
    {
        fixed (char* classNamePtr = _windowClassName)
        {
            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _wndProcDelegate,
                lpszClassName = classNamePtr
            };

            if (PInvoke.RegisterClassEx(in wndClass) == 0)
            {
                throw new InvalidOperationException("Failed to register tray host window class.");
            }

            _hwnd = PInvoke.CreateWindowEx(
                0,
                classNamePtr,
                classNamePtr,
                WINDOW_STYLE.WS_OVERLAPPED,
                0,
                0,
                0,
                0,
                HWND.Null,
                HMENU.Null,
                HINSTANCE.Null,
                null);

            if (_hwnd.IsNull)
            {
                throw new InvalidOperationException("Failed to create tray host window.");
            }
        }
    }

    private void UpdateTrayIcon(bool add)
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        var dpi = WindowPlacementInterop.GetWindowDpi(_hwnd);
        var size = Math.Max(16, PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, dpi));
        var isLightTaskbar = IsSystemUsingLightTheme();
        var newIcon = CreateAdaptiveTrayIcon(size, isLightTaskbar);

        var previousIcon = _currentIcon;
        _currentIcon = newIcon;

        var data = CreateNotifyIconData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP;
        data.uCallbackMessage = _trayCallbackMessage;
        data.hIcon = (HICON)_currentIcon.DangerousGetHandle();
        data.szTip = _tooltip;

        var message = add || !_iconAdded
            ? NOTIFY_ICON_MESSAGE.NIM_ADD
            : NOTIFY_ICON_MESSAGE.NIM_MODIFY;

        if (PInvoke.Shell_NotifyIcon(message, in data))
        {
            _iconAdded = true;
        }

        previousIcon?.Dispose();
    }

    private NOTIFYICONDATAW CreateNotifyIconData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = _trayIconId
    };

    private LRESULT WindowProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            _iconAdded = false;
            UpdateTrayIcon(add: true);
            return new LRESULT(0);
        }

        if (msg == _activateInstanceMessage && _activateInstanceMessage != 0)
        {
            SecondInstanceActivated?.Invoke(this, GetAnchorPoint());
            return new LRESULT(0);
        }

        if (msg == PInvoke.WM_SETTINGCHANGE || msg == PInvoke.WM_DPICHANGED)
        {
            UpdateTrayIcon(add: false);
            return new LRESULT(0);
        }

        if (msg == _trayCallbackMessage)
        {
            var notification = (uint)(lParam.Value & 0xFFFF);
            switch (notification)
            {
                case PInvoke.WM_LBUTTONUP:
                case PInvoke.NIN_SELECT:
                case _ninKeySelect:
                    PrimaryClicked?.Invoke(this, GetAnchorPoint());
                    break;
                case PInvoke.WM_RBUTTONUP:
                case PInvoke.WM_CONTEXTMENU:
                    ShowContextMenu();
                    break;
            }

            return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        using var menu = PInvoke.CreatePopupMenu_SafeHandle();
        if (menu.IsInvalid)
        {
            return;
        }

        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, _menuIdSettings, _settingsLabel);
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null);
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, _menuIdExit, _exitLabel);

        var (x, y) = WindowPlacementInterop.GetCursorPosition();
        PInvoke.SetForegroundWindow(_hwnd);

        var selected = PInvoke.TrackPopupMenuEx(
            menu,
            (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON),
            x,
            y,
            _hwnd,
            null);

        PInvoke.PostMessage(_hwnd, PInvoke.WM_NULL, 0, 0);

        if ((uint)selected.Value == _menuIdSettings)
        {
            SettingsClicked?.Invoke(this, EventArgs.Empty);
        }
        else if ((uint)selected.Value == _menuIdExit)
        {
            ExitClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    private static bool IsSystemUsingLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var value = key?.GetValue("SystemUsesLightTheme");
        return value is int intValue && intValue != 0;
    }

    private static DestroyIconSafeHandle CreateAdaptiveTrayIcon(int size, bool isLightTaskbar)
    {
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size, // Top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = (uint)BI_COMPRESSION.BI_RGB
            }
        };

        void* bitsPtr = null;
        var screenDc = PInvoke.GetDC(HWND.Null);
        DeleteObjectHBITMAPSafeHandle colorBitmap;
        try
        {
            colorBitmap = PInvoke.CreateDIBSection(screenDc, &bmi, DIB_USAGE.DIB_RGB_COLORS, out bitsPtr, null, 0);
        }
        finally
        {
            _ = PInvoke.ReleaseDC(HWND.Null, screenDc);
        }

        using (colorBitmap)
        {
            if (colorBitmap.IsInvalid || bitsPtr is null)
            {
                throw new InvalidOperationException("Failed to create tray icon DIB section.");
            }

            var maskBitmap = PInvoke.CreateBitmap(size, size, 1, 1, null);
            try
            {
                RenderSwissGridGlyph((uint*)bitsPtr, size, isLightTaskbar);

                var iconInfo = new ICONINFO
                {
                    fIcon = true,
                    hbmColor = (HBITMAP)colorBitmap.DangerousGetHandle(),
                    hbmMask = maskBitmap
                };

                var icon = PInvoke.CreateIconIndirect(in iconInfo);
                if (icon.IsInvalid)
                {
                    icon.Dispose();
                    throw new InvalidOperationException("Failed to create tray HICON.");
                }

                return icon;
            }
            finally
            {
                if (!maskBitmap.IsNull)
                {
                    PInvoke.DeleteObject(maskBitmap);
                }
            }
        }
    }

    private static void RenderSwissGridGlyph(uint* pixels, int size, bool isLightTaskbar)
    {
        // High-contrast foreground (dark slate on light taskbar, crisp white on dark taskbar) + accent tile.
        uint primaryRgb = isLightTaskbar ? 0x001E293Bu : 0x00F8FAFCu;
        uint accentRgb = isLightTaskbar ? 0x000F6CBDu : 0x0060CDFFu;

        var margin = Math.Max(1, size / 8);
        var gap = Math.Max(1, size / 8);
        var tileSize = Math.Max(2, (size - (margin * 2) - gap) / 2);

        var start1 = margin;
        var start2 = margin + tileSize + gap;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var inLeft = x >= start1 && x < start1 + tileSize;
                var inRight = x >= start2 && x < start2 + tileSize;
                var inTop = y >= start1 && y < start1 + tileSize;
                var inBottom = y >= start2 && y < start2 + tileSize;

                uint argb = 0;
                if ((inLeft && inTop) || (inRight && inBottom) || (inLeft && inBottom))
                {
                    argb = 0xFF000000u | primaryRgb;
                }
                else if (inRight && inTop)
                {
                    argb = 0xFF000000u | accentRgb;
                }

                pixels[(y * size) + x] = argb;
            }
        }
    }
}
