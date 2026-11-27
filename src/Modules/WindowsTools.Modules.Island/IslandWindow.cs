using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.UI;
using WindowsTools.Core.Activities;
using WindowsTools.Interop;
using WinRT.Interop;

namespace WindowsTools.Modules.Island;

/// <summary>
/// Always-on-top pill at the top centre of the primary display. Collapsed it summarizes live activities;
/// on hover it expands into a list. It is shown without taking focus from the app the user is working in.
/// </summary>
internal sealed class IslandWindow : Window
{
    private const double _collapsedWidth = 320;
    private const double _collapsedHeight = 38;
    private const double _expandedWidth = 480;
    private const double _headerHeight = 44;
    private const double _rowHeight = 58;
    private const double _verticalPadding = 14;
    private const double _topMargin = 8;

    private static readonly Color _background = Color.FromArgb(255, 12, 12, 16);
    private static readonly Color _primaryText = Color.FromArgb(255, 245, 245, 247);
    private static readonly Color _secondaryText = Color.FromArgb(255, 152, 152, 160);

    private readonly IActivityHub _hub;
    private readonly nint _hwnd;
    private readonly DispatcherQueueTimer _clock;
    private readonly Grid _root;
    private readonly Grid _collapsedView;
    private readonly Ellipse _collapsedDot;
    private readonly TextBlock _collapsedText;
    private readonly TextBlock _collapsedCount;
    private readonly StackPanel _expandedView;
    private readonly TextBlock _headerSummary;
    private readonly StackPanel _rows;

    private IReadOnlyList<Activity> _activities = [];
    private bool _isExpanded;

    public IslandWindow(IActivityHub hub)
    {
        _hub = hub;

        _collapsedDot = CreateDot(10);
        _collapsedText = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_primaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(10, 0, 10, 0),
        };
        _collapsedCount = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(_secondaryText),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _collapsedView = new Grid { Padding = new Thickness(16, 0, 16, 0) };
        _collapsedView.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _collapsedView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _collapsedView.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddToGrid(_collapsedView, _collapsedDot, 0);
        AddToGrid(_collapsedView, _collapsedText, 1);
        AddToGrid(_collapsedView, _collapsedCount, 2);

        _headerSummary = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(_secondaryText),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var header = new Grid { Height = _headerHeight };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddToGrid(header, new TextBlock
        {
            Text = IslandStrings.Get("Island_Title", "AI agents"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_primaryText),
            VerticalAlignment = VerticalAlignment.Center,
        }, 0);
        AddToGrid(header, _headerSummary, 1);

        _rows = new StackPanel();
        _expandedView = new StackPanel
        {
            Padding = new Thickness(18, _verticalPadding / 2, 18, _verticalPadding / 2),
            Visibility = Visibility.Collapsed,
        };
        _expandedView.Children.Add(header);
        _expandedView.Children.Add(_rows);

        _root = new Grid
        {
            Background = new SolidColorBrush(_background),
            RequestedTheme = ElementTheme.Dark,
        };
        _root.Children.Add(_collapsedView);
        _root.Children.Add(_expandedView);
        _root.PointerEntered += (_, _) => SetExpanded(true);
        _root.PointerExited += (_, _) => SetExpanded(false);
        Content = _root;

        _hwnd = WindowNative.GetWindowHandle(this);
        ConfigureChrome();

        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => RebuildRows();

        _hub.ActivitiesChanged += OnActivitiesChanged;
        Closed += OnClosed;
        Refresh();
    }

    public void ShowWithoutActivation()
    {
        PlaceWindow();
        AppWindow.Show(activateWindow: false);
    }

    private void ConfigureChrome()
    {
        Title = "WindowsTools Island";
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        AppWindow.IsShownInSwitchers = false;
        OverlayWindowInterop.MakeToolWindow(_hwnd);
        OverlayWindowInterop.UseRoundedCorners(_hwnd);
    }

    private void OnActivitiesChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Refresh);

    private void Refresh()
    {
        _activities = _hub.Activities;
        var overall = IslandSummary.OverallState(_activities);

        _collapsedText.Text = IslandSummary.CollapsedText(_activities);
        _collapsedCount.Text = _activities.Count > 1 ? _activities.Count.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty;
        StyleDot(_collapsedDot, overall);

        var working = _activities.Count(a => a.State == ActivityState.Working);
        var waiting = _activities.Count(a => a.State == ActivityState.WaitingForUser);
        _headerSummary.Text = IslandStrings.Format(
            "Header_Summary",
            "{0} working · {1} waiting · {2} total",
            working,
            waiting,
            _activities.Count);

        RebuildRows();
        PlaceWindow();
    }

    private void SetExpanded(bool expanded)
    {
        if (_isExpanded == expanded)
        {
            return;
        }

        _isExpanded = expanded;
        _collapsedView.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        _expandedView.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        // The clock only runs while elapsed times are on screen, so an idle Island costs no CPU.
        if (expanded)
        {
            RebuildRows();
            _clock.Start();
        }
        else
        {
            _clock.Stop();
        }

        PlaceWindow();
    }

    private void RebuildRows()
    {
        if (!_isExpanded)
        {
            return;
        }

        _rows.Children.Clear();
        if (_activities.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = IslandStrings.Get("Expanded_Empty", "Start Claude Code and its sessions will appear here."),
                FontSize = 13,
                Foreground = new SolidColorBrush(_secondaryText),
                TextWrapping = TextWrapping.Wrap,
                Height = _rowHeight,
            });
            return;
        }

        var now = DateTimeOffset.Now;
        foreach (var activity in _activities.Take(IslandSummary.MaxRows))
        {
            _rows.Children.Add(CreateRow(activity, now));
        }

        var hidden = _activities.Count - IslandSummary.MaxRows;
        if (hidden > 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = IslandStrings.Format("Expanded_More", "+{0} more", hidden),
                FontSize = 12,
                Foreground = new SolidColorBrush(_secondaryText),
                Height = 24,
            });
        }
    }

    private static Grid CreateRow(Activity activity, DateTimeOffset now)
    {
        var row = new Grid { Height = _rowHeight };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = CreateDot(9);
        StyleDot(dot, activity.State);
        AddToGrid(row, dot, 0);

        var subtitle = $"{activity.Category} · {IslandSummary.StateText(activity.State)}";
        if (!string.IsNullOrEmpty(activity.Detail))
        {
            subtitle += $" · {activity.Detail}";
        }

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        info.Children.Add(new TextBlock
        {
            Text = activity.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_primaryText),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        info.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = new SolidColorBrush(_secondaryText),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        AddToGrid(row, info, 1);

        var metrics = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 2,
            Margin = new Thickness(12, 0, 0, 0),
        };
        var tokens = activity.Metrics.FirstOrDefault(m => m.Kind == "tokens")?.Value;
        if (tokens is not null)
        {
            metrics.Children.Add(RightText(IslandStrings.Format("Metric_Tokens", "{0} tokens", tokens), _primaryText, 13));
        }

        var model = activity.Metrics.FirstOrDefault(m => m.Kind == "model")?.Value;
        var elapsed = IslandSummary.FormatElapsed(now - activity.StartedAt);
        metrics.Children.Add(RightText(model is null ? elapsed : $"{model} · {elapsed}", _secondaryText, 11));
        AddToGrid(row, metrics, 2);

        return row;
    }

    private void PlaceWindow()
    {
        var scale = OverlayScale();
        double width;
        double height;

        if (_isExpanded)
        {
            var rowCount = Math.Max(1, Math.Min(_activities.Count, IslandSummary.MaxRows));
            var extra = _activities.Count > IslandSummary.MaxRows ? 24 : 0;
            width = _expandedWidth;
            height = _verticalPadding + _headerHeight + (rowCount * _rowHeight) + extra;
        }
        else
        {
            width = _collapsedWidth;
            height = _collapsedHeight;
        }

        var bounds = DisplayArea.Primary.OuterBounds;
        var widthPx = (int)Math.Round(width * scale);
        var heightPx = (int)Math.Round(height * scale);
        var x = bounds.X + ((bounds.Width - widthPx) / 2);
        var y = bounds.Y + (int)Math.Round(_topMargin * scale);

        AppWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));
    }

    private double OverlayScale() => WindowPlacementInterop.GetWindowDpi(_hwnd) / 96.0;

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _clock.Stop();
        _hub.ActivitiesChanged -= OnActivitiesChanged;
        Closed -= OnClosed;
    }

    private static Ellipse CreateDot(double size) => new()
    {
        Width = size,
        Height = size,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static void StyleDot(Ellipse dot, ActivityState state)
    {
        var color = state switch
        {
            ActivityState.Working => Color.FromArgb(255, 52, 199, 89),
            ActivityState.WaitingForUser => Color.FromArgb(255, 255, 159, 10),
            ActivityState.Error => Color.FromArgb(255, 255, 69, 58),
            _ => Color.FromArgb(255, 99, 99, 106),
        };
        dot.Fill = new SolidColorBrush(color);

        var pulses = state is ActivityState.Working or ActivityState.WaitingForUser;
        if (dot.Tag is Storyboard running)
        {
            if (pulses)
            {
                return;
            }

            running.Stop();
            dot.Tag = null;
            dot.Opacity = 1;
            return;
        }

        if (!pulses)
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            From = 1,
            To = 0.35,
            Duration = new Duration(TimeSpan.FromMilliseconds(900)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        Storyboard.SetTarget(animation, dot);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        dot.Tag = storyboard;
        storyboard.Begin();
    }

    private static TextBlock RightText(string text, Color color, double size) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = new SolidColorBrush(color),
        HorizontalAlignment = HorizontalAlignment.Right,
    };

    private static void AddToGrid(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
