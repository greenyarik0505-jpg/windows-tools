using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WindowsTools.Core.Activities;
using WindowsTools.Core.Tray;
using WindowsTools.Interop;
using WinRT.Interop;

namespace WindowsTools.Modules.Island;

/// <summary>
/// Non-activating always-on-top overlay window positioned top-center across monitors.
/// </summary>
public sealed partial class IslandOverlayWindow : Window
{
    private const int _collapsedWidth = 220;
    private const int _collapsedHeight = 38;
    private const int _expandedWidth = 380;
    private const int _expandedHeight = 164;
    private const int _topOffset = 8;

    private readonly IslandViewModel _viewModel;
    private readonly nint _hwnd;
    private readonly Visual _pillVisual;
    private readonly Compositor _compositor;
    private readonly DisplayAreaWatcher _displayWatcher;
    private readonly DispatcherQueueTimer _fullscreenTimer;
    private readonly DispatcherQueueTimer _collapseDelayTimer;

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IslandOverlayWindow"/> class.
    /// </summary>
    /// <param name="viewModel">Island presentation state.</param>
    public IslandOverlayWindow(IslandViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;

        InitializeComponent();
        _hwnd = WindowNative.GetWindowHandle(this);

        _pillVisual = ElementCompositionPreview.GetElementVisual(PillContainer);
        _compositor = _pillVisual.Compositor;

        ConfigureOverlayChrome();
        ApplyLocalizedStaticStrings();

        _viewModel.StateChanged += OnViewModelStateChanged;

        _displayWatcher = DisplayArea.CreateWatcher();
        _displayWatcher.Added += OnDisplayChanged;
        _displayWatcher.Removed += OnDisplayChanged;
        _displayWatcher.Updated += OnDisplayChanged;
        _displayWatcher.Start();

        _collapseDelayTimer = DispatcherQueue.CreateTimer();
        _collapseDelayTimer.Interval = TimeSpan.FromMilliseconds(350);
        _collapseDelayTimer.IsRepeating = false;
        _collapseDelayTimer.Tick += OnCollapseDelayTick;

        _fullscreenTimer = DispatcherQueue.CreateTimer();
        _fullscreenTimer.Interval = TimeSpan.FromSeconds(1);
        _fullscreenTimer.IsRepeating = true;
        _fullscreenTimer.Tick += OnFullscreenCheckTick;
        _fullscreenTimer.Start();

        RenderState();
        RepositionAndSyncVisibility();
    }

    /// <summary>
    /// Releases all watchers, timers, and subscriptions, then closes the overlay window.
    /// </summary>
    public void Shutdown()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _fullscreenTimer.Stop();
        _fullscreenTimer.Tick -= OnFullscreenCheckTick;

        _collapseDelayTimer.Stop();
        _collapseDelayTimer.Tick -= OnCollapseDelayTick;

        _displayWatcher.Stop();
        _displayWatcher.Added -= OnDisplayChanged;
        _displayWatcher.Removed -= OnDisplayChanged;
        _displayWatcher.Updated -= OnDisplayChanged;

        _viewModel.StateChanged -= OnViewModelStateChanged;

        AppWindow.Hide();
        Close();
    }

    private void ConfigureOverlayChrome()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        OverlayWindowInterop.ConfigureNoActivateToolOverlay(_hwnd);
    }

    private void ApplyLocalizedStaticStrings()
    {
        WaitingBadgeText.Text = IslandStrings.Get("Island_WaitingBadge", "Waiting");
        ToolTipService.SetToolTip(CollapseButton, IslandStrings.Get("Island_CollapseTooltip", "Collapse"));
    }

    private void OnViewModelStateChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed)
            {
                return;
            }

            RenderState();
            RepositionAndSyncVisibility();
            PlayTransitionAnimation();
        });
    }

    private void OnDisplayChanged(DisplayAreaWatcher sender, DisplayArea args)
    {
        if (_disposed)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_disposed)
            {
                RepositionAndSyncVisibility();
            }
        });
    }

    private void OnFullscreenCheckTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed)
        {
            return;
        }

        var isFullscreen = OverlayWindowInterop.IsFullscreenApplicationActive(_hwnd);
        _viewModel.SetFullscreenHidden(isFullscreen);
    }

    private void OnCollapseDelayTick(DispatcherQueueTimer sender, object args)
    {
        _collapseDelayTimer.Stop();
        if (!_disposed)
        {
            _viewModel.Collapse();
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _collapseDelayTimer.Stop();
        _viewModel.Expand();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _collapseDelayTimer.Start();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _collapseDelayTimer.Stop();
        if (!_viewModel.IsExpanded)
        {
            _viewModel.Expand();
            return;
        }

        _viewModel.FeaturedActivity?.OnPrimaryAction?.Invoke();
    }

    private void OnCollapseClicked(object sender, RoutedEventArgs e)
    {
        _collapseDelayTimer.Stop();
        _viewModel.Collapse();
    }

    private void RenderState()
    {
        var featured = _viewModel.FeaturedActivity;
        var secondary = _viewModel.SecondaryActivities;
        var expanded = _viewModel.IsExpanded;

        CollapsedPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        PillContainer.Padding = expanded ? new Thickness(16, 12, 16, 12) : new Thickness(14, 6, 14, 6);

        if (featured is null)
        {
            CollapsedTitleText.Text = IslandStrings.Get("Island_IdleTitle", "WindowsTools");
            ExpandedTitleText.Text = IslandStrings.Get("Island_IdleTitle", "WindowsTools");
            ExpandedSubtitleText.Text = IslandStrings.Get("Island_IdleSubtitle", "No active events");
            CollapsedExtraBadge.Visibility = Visibility.Collapsed;
            WaitingBadge.Visibility = Visibility.Collapsed;
            CustomContentHost.Visibility = Visibility.Collapsed;
            CustomContentHost.Content = null;
            ActivityProgressBar.Visibility = Visibility.Collapsed;
            IndicatorsPanel.Visibility = Visibility.Collapsed;
            IndicatorsPanel.Children.Clear();
            StatusDot.Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            return;
        }

        var isWaiting = featured.State == ActivityState.WaitingForUser;
        StatusDot.Fill = isWaiting
            ? (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            : (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];

        CollapsedTitleText.Text = featured.Title;
        ExpandedTitleText.Text = featured.Title;
        ExpandedSubtitleText.Text = featured.Subtitle ?? string.Empty;
        ExpandedSubtitleText.Visibility = string.IsNullOrWhiteSpace(featured.Subtitle)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ExpandedIcon.Glyph = string.IsNullOrWhiteSpace(featured.IconGlyph) ? "\uE8A7" : featured.IconGlyph;
        WaitingBadge.Visibility = isWaiting ? Visibility.Visible : Visibility.Collapsed;

        if (secondary.Count > 0)
        {
            var fmt = IslandStrings.Get("Island_MoreActivitiesFormat", "+{0}");
            CollapsedExtraText.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, fmt, secondary.Count);
            CollapsedExtraBadge.Visibility = Visibility.Visible;
        }
        else
        {
            CollapsedExtraBadge.Visibility = Visibility.Collapsed;
        }

        var custom = featured.CreateExpandedContent?.Invoke();
        if (custom is not null)
        {
            CustomContentHost.Content = custom;
            CustomContentHost.Visibility = Visibility.Visible;
            ActivityProgressBar.Visibility = Visibility.Collapsed;
        }
        else if (featured.Progress is double progress)
        {
            CustomContentHost.Content = null;
            CustomContentHost.Visibility = Visibility.Collapsed;
            ActivityProgressBar.Value = Math.Clamp(progress, 0.0, 1.0);
            ActivityProgressBar.Visibility = Visibility.Visible;
        }
        else
        {
            CustomContentHost.Content = null;
            CustomContentHost.Visibility = Visibility.Collapsed;
            ActivityProgressBar.Visibility = Visibility.Collapsed;
        }

        IndicatorsPanel.Children.Clear();
        if (secondary.Count > 0)
        {
            IndicatorsPanel.Visibility = Visibility.Visible;
            foreach (var item in secondary)
            {
                var chip = new Button
                {
                    Content = item.Title,
                    Padding = new Thickness(8, 2, 8, 2),
                    Tag = item.Id,
                    Style = (Style)Application.Current.Resources["SubtleButtonStyle"]
                };
                chip.Click += OnIndicatorClicked;
                IndicatorsPanel.Children.Add(chip);
            }
        }
        else
        {
            IndicatorsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnIndicatorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            _viewModel.SelectActivity(id);
        }
    }

    private void RepositionAndSyncVisibility()
    {
        if (_viewModel.IsHiddenForFullscreen)
        {
            AppWindow.Hide();
            return;
        }

        var dpi = WindowPlacementInterop.GetWindowDpi(_hwnd);
        var scale = dpi / 96.0;

        var widthPx = (int)Math.Round((_viewModel.IsExpanded ? _expandedWidth : _collapsedWidth) * scale);
        var heightPx = (int)Math.Round((_viewModel.IsExpanded ? _expandedHeight : _collapsedHeight) * scale);
        var topOffsetPx = (int)Math.Round(_topOffset * scale);

        var monitor = OverlayWindowInterop.GetMonitorBounds(_hwnd);
        var pos = IslandPlacementCalculator.ComputeTopCenterPosition(
            new PixelRect(monitor.Left, monitor.Top, monitor.Right, monitor.Bottom),
            widthPx,
            heightPx,
            topOffsetPx);

        AppWindow.Show(activateWindow: false);
        OverlayWindowInterop.SetTopMostBoundsNoActivate(_hwnd, pos.X, pos.Y, widthPx, heightPx);
    }

    private void PlayTransitionAnimation()
    {
        var scaleAnim = _compositor.CreateVector3KeyFrameAnimation();
        scaleAnim.InsertKeyFrame(0.0f, new Vector3(0.96f, 0.96f, 1.0f));
        scaleAnim.InsertKeyFrame(1.0f, Vector3.One);
        scaleAnim.Duration = TimeSpan.FromMilliseconds(160);

        var opacityAnim = _compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(0.0f, 0.88f);
        opacityAnim.InsertKeyFrame(1.0f, 1.0f);
        opacityAnim.Duration = TimeSpan.FromMilliseconds(140);

        _pillVisual.CenterPoint = new Vector3((float)(PillContainer.ActualWidth / 2.0), 0f, 0f);
        _pillVisual.StartAnimation(nameof(_pillVisual.Scale), scaleAnim);
        _pillVisual.StartAnimation(nameof(_pillVisual.Opacity), opacityAnim);
    }
}
