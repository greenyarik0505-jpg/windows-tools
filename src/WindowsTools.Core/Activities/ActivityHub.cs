namespace WindowsTools.Core.Activities;

/// <summary>
/// Thread-safe default implementation of <see cref="IActivityHub"/>.
/// </summary>
public sealed class ActivityHub : IActivityHub
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ActivityEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private long _sequence;

    /// <inheritdoc />
    public event EventHandler? ActivitiesChanged;

    /// <inheritdoc />
    public IReadOnlyList<ActivityDescriptor> Activities
    {
        get
        {
            lock (_gate)
            {
                return _entries.Values
                    .OrderByDescending(static e => e.Descriptor.Priority)
                    .ThenByDescending(static e => e.Descriptor.State == ActivityState.WaitingForUser)
                    .ThenByDescending(static e => e.Sequence)
                    .Select(static e => e.Descriptor)
                    .ToList();
            }
        }
    }

    /// <inheritdoc />
    public ActivityDescriptor? PrimaryActivity
    {
        get
        {
            lock (_gate)
            {
                ActivityEntry? best = null;
                foreach (var entry in _entries.Values)
                {
                    if (best is null || CompareEntries(entry, best) > 0)
                    {
                        best = entry;
                    }
                }

                return best?.Descriptor;
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Publish(ActivityDescriptor activity)
    {
        Update(activity);
        return new ActivityRegistration(this, activity.Id);
    }

    /// <inheritdoc />
    public void Update(ActivityDescriptor activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentException.ThrowIfNullOrWhiteSpace(activity.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(activity.ModuleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activity.Title);

        lock (_gate)
        {
            var seq = ++_sequence;
            _entries[activity.Id] = new ActivityEntry(activity, seq);
        }

        ActivitiesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Remove(string activityId)
    {
        if (string.IsNullOrWhiteSpace(activityId))
        {
            return;
        }

        bool removed;
        lock (_gate)
        {
            removed = _entries.Remove(activityId);
        }

        if (removed)
        {
            ActivitiesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static int CompareEntries(ActivityEntry left, ActivityEntry right)
    {
        var priorityCmp = left.Descriptor.Priority.CompareTo(right.Descriptor.Priority);
        if (priorityCmp != 0)
        {
            return priorityCmp;
        }

        var leftWaiting = left.Descriptor.State == ActivityState.WaitingForUser;
        var rightWaiting = right.Descriptor.State == ActivityState.WaitingForUser;
        var waitingCmp = leftWaiting.CompareTo(rightWaiting);
        if (waitingCmp != 0)
        {
            return waitingCmp;
        }

        return left.Sequence.CompareTo(right.Sequence);
    }

    private sealed record ActivityEntry(ActivityDescriptor Descriptor, long Sequence);

    private sealed class ActivityRegistration(ActivityHub hub, string activityId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                hub.Remove(activityId);
            }
        }
    }
}
