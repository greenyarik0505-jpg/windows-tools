using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WindowsTools.Core.Tray;
using WindowsTools.Interop;
using WinRT.Interop;

namespace WindowsTools.App;

/// <summary>
/// Lightweight tray flyout window hosting registered module sections.
/// </summary>
public sealed partial class TrayFlyoutWindow : Window
{
    private const int _logicalWidth = 340;
    private const int _logicalHeight = 260;
    private const int _logicalMargin = 12;

    private readonly ITrayHost _trayHost;
    private readonly nint _hwnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayFlyoutWindow"/> class.
    /// </summary>
    /// <param name="trayHost">Tray host service providing module sections.</param>
    public TrayFlyoutWindow(ITrayHost trayHost)
    {
        ArgumentNullException.ThrowIfNull(trayHost);
        _trayHost = trayHost;

        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        ConfigureWindowChrome();
        ApplyLocalizedStrings();

        _trayHost.SectionsChanged += OnSectionsChanged;
        Activated += OnWindowActivated;
        Closed += OnWindowClosed;

        RebuildSections();
    }

    /// <summary>
    /// Toggles flyout visibility anchored near the specified screen coordinates.
    /// </summary>
    public void ToggleAt(int anchorX, int anchorY)
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
            return;
        }

        ShowAt(anchorX, anchorY);
    }

    /// <summary>
    /// Positions and activates the flyout near the specified screen coordinates.
    /// </summary>
    public void ShowAt(int anchorX, int anchorY)
    {
        RebuildSections();

        var workArea = WindowPlacementInterop.GetMonitorWorkArea(anchorX, anchorY);
        var dpi = WindowPlacementInterop.GetWindowDpi(_hwnd);
        var scale = dpi / 96.0;

        var widthPx = (int)Math.Round(_logicalWidth * scale);
        var heightPx = (int)Math.Round(_logicalHeight * scale);
        var marginPx = (int)Math.Round(_logicalMargin * scale);

        var position = TrayPlacementCalculator.ComputePosition(
            new PixelPoint(anchorX, anchorY),
            new PixelRect(workArea.Left, workArea.Top, workArea.Right, workArea.Bottom),
            widthPx,
            heightPx,
            marginPx);

        AppWindow.MoveAndResize(new RectInt32(position.X, position.Y, widthPx, heightPx));
        AppWindow.Show();
        Activate();
        WindowPlacementInterop.BringToForeground(_hwnd);
    }

    private void ConfigureWindowChrome()
    {
        SystemBackdrop = new DesktopAcrylicBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        AppWindow.IsShownInSwitchers = false;
        AppWindow.Hide();
    }

    private void ApplyLocalizedStrings()
    {
        Title = AppStrings.Get("TrayFlyout_Title", "WindowsTools");
        TitleText.Text = AppStrings.Get("TrayFlyout_Title", "WindowsTools");
        EmptyStateText.Text = AppStrings.Get("TrayFlyout_EmptyState", "No active module panels yet.");
        ToolTipService.SetToolTip(SettingsButton, AppStrings.Get("TrayFlyout_SettingsTooltip", "Settings"));
    }

    private void OnSectionsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(RebuildSections);
    }

    private void RebuildSections()
    {
        SectionsPanel.Children.Clear();
        var sections = _trayHost.Sections;

        if (sections.Count == 0)
        {
            SectionsPanel.Children.Add(EmptyStateText);
            return;
        }

        foreach (var section in sections)
        {
            var container = new StackPanel { Spacing = 6 };
            container.Children.Add(new TextBlock
            {
                Text = section.Title,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
            });

            if (section.CreateContent() is UIElement element)
            {
                container.Children.Add(element);
            }

            SectionsPanel.Children.Add(container);
        }
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
        _trayHost.RequestSettings();
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            AppWindow.Hide();
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _trayHost.SectionsChanged -= OnSectionsChanged;
        Activated -= OnWindowActivated;
        Closed -= OnWindowClosed;
    }
}
