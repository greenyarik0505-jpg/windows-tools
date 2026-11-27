namespace WindowsTools.Core.Activities;

/// <summary>
/// Thread-safe default <see cref="IActivityHub"/>. Raises <see cref="ActivitiesChanged"/> only when content actually changes.
/// </summary>
public sealed class ActivityHub : IActivityHub
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IReadOnlyList<Activity>> _byModule = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<Activity> _snapshot = [];

    /// <inheritdoc />
    public event EventHandler? ActivitiesChanged;

    /// <inheritdoc />
    public IReadOnlyList<Activity> Activities
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    /// <inheritdoc />
    public void ReplaceAll(string sourceModuleId, IReadOnlyCollection<Activity> activities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceModuleId);
        ArgumentNullException.ThrowIfNull(activities);

        foreach (var activity in activities)
        {
            if (!string.Equals(activity.SourceModuleId, sourceModuleId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Activity '{activity.Id}' belongs to '{activity.SourceModuleId}', not '{sourceModuleId}'.",
                    nameof(activities));
            }
        }

        lock (_gate)
        {
            var next = activities.ToList();
            if (_byModule.TryGetValue(sourceModuleId, out var current) && SameContent(current, next))
            {
                return;
            }

            if (next.Count == 0)
            {
                _byModule.Remove(sourceModuleId);
            }
            else
            {
                _byModule[sourceModuleId] = next;
            }

            Rebuild();
        }

        ActivitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Clear(string sourceModuleId) => ReplaceAll(sourceModuleId, []);

    private void Rebuild()
    {
        _snapshot = _byModule.Values
            .SelectMany(list => list)
            .OrderByDescending(a => a.State == ActivityState.WaitingForUser)
            .ThenByDescending(a => a.Priority)
            .ThenByDescending(a => a.UpdatedAt)
            .ToList();
    }

    // Records compare Metrics by reference, so compare element-wise to avoid redundant change events on every poll.
    private static bool SameContent(IReadOnlyList<Activity> left, List<Activity> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (a with { Metrics = [] } != b with { Metrics = [] } || !a.Metrics.SequenceEqual(b.Metrics))
            {
                return false;
            }
        }

        return true;
    }
}
