using WindowsTools.Core.Activities;

namespace WindowsTools.Modules.Island;

/// <summary>
/// Presentation state for the Island overlay, decoupled from WinUI for unit testing.
/// </summary>
public sealed class IslandViewModel : IDisposable
{
    private readonly IActivityHub _activityHub;
    private string? _selectedActivityId;
    private bool _isExpanded;
    private bool _isHiddenForFullscreen;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IslandViewModel"/> class.
    /// </summary>
    /// <param name="activityHub">Shared activity hub.</param>
    public IslandViewModel(IActivityHub activityHub)
    {
        ArgumentNullException.ThrowIfNull(activityHub);
        _activityHub = activityHub;
        _activityHub.ActivitiesChanged += OnActivitiesChanged;
    }

    /// <summary>
    /// Occurs when any observable Island presentation state changes.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Gets all active activities ordered by priority.
    /// </summary>
    public IReadOnlyList<ActivityDescriptor> Activities => _activityHub.Activities;

    /// <summary>
    /// Gets the activity currently featured in the expanded Island view.
    /// </summary>
    public ActivityDescriptor? FeaturedActivity
    {
        get
        {
            var list = _activityHub.Activities;
            if (list.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(_selectedActivityId))
            {
                foreach (var item in list)
                {
                    if (string.Equals(item.Id, _selectedActivityId, StringComparison.OrdinalIgnoreCase))
                    {
                        return item;
                    }
                }
            }

            return list[0];
        }
    }

    /// <summary>
    /// Gets the remaining activities shown as compact indicator chips/dots.
    /// </summary>
    public IReadOnlyList<ActivityDescriptor> SecondaryActivities
    {
        get
        {
            var featured = FeaturedActivity;
            if (featured is null)
            {
                return [];
            }

            return _activityHub.Activities
                .Where(a => !string.Equals(a.Id, featured.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the Island is currently expanded.
    /// </summary>
    public bool IsExpanded => _isExpanded;

    /// <summary>
    /// Gets a value indicating whether the Island is hidden because a fullscreen app is active.
    /// </summary>
    public bool IsHiddenForFullscreen => _isHiddenForFullscreen;

    /// <summary>
    /// Expands the Island overlay.
    /// </summary>
    public void Expand()
    {
        if (_isExpanded)
        {
            return;
        }

        _isExpanded = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Collapses the Island overlay back to the pill state and resets selection to the top-priority activity.
    /// </summary>
    public void Collapse()
    {
        if (!_isExpanded && _selectedActivityId is null)
        {
            return;
        }

        _isExpanded = false;
        _selectedActivityId = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Selects a specific activity by identifier to inspect in the expanded view.
    /// </summary>
    public void SelectActivity(string activityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);
        _selectedActivityId = activityId;
        _isExpanded = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates fullscreen suppression state.
    /// </summary>
    public void SetFullscreenHidden(bool hidden)
    {
        if (_isHiddenForFullscreen == hidden)
        {
            return;
        }

        _isHiddenForFullscreen = hidden;
        if (hidden)
        {
            _isExpanded = false;
            _selectedActivityId = null;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activityHub.ActivitiesChanged -= OnActivitiesChanged;
    }

    private void OnActivitiesChanged(object? sender, EventArgs e)
    {
        if (_selectedActivityId is not null &&
            !_activityHub.Activities.Any(a => string.Equals(a.Id, _selectedActivityId, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedActivityId = null;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
