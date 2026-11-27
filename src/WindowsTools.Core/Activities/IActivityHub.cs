namespace WindowsTools.Core.Activities;

/// <summary>
/// Lifecycle state of a live activity, used by hosts such as the Island to pick colors and animation.
/// </summary>
public enum ActivityState
{
    /// <summary>The activity is idle and does not need attention.</summary>
    Idle = 0,

    /// <summary>The activity is actively doing work.</summary>
    Working = 1,

    /// <summary>The activity is blocked until the user responds.</summary>
    WaitingForUser = 2,

    /// <summary>The activity finished successfully.</summary>
    Completed = 3,

    /// <summary>The activity stopped because of an error.</summary>
    Error = 4,
}

/// <summary>
/// A compact value shown next to an activity, for example a token count or elapsed time.
/// </summary>
/// <param name="Kind">Stable, non-localized metric kind (for example <c>tokens</c> or <c>model</c>) that hosts map to icons and labels.</param>
/// <param name="Value">Pre-formatted value to display.</param>
public sealed record ActivityMetric(string Kind, string Value);

/// <summary>
/// A live activity published by a module and rendered by activity hosts (Island, tray flyout).
/// </summary>
/// <param name="Id">Stable identifier, unique across modules (for example <c>agents:claude:&lt;session&gt;</c>).</param>
/// <param name="SourceModuleId">Identifier of the module that owns the activity.</param>
/// <param name="Category">Stable, non-localized grouping key (for example <c>Claude Code</c>).</param>
/// <param name="Title">Primary display text.</param>
/// <param name="Detail">Secondary display text, such as the current action.</param>
/// <param name="State">Current lifecycle state.</param>
/// <param name="Priority">Higher values are shown first and win the collapsed Island slot.</param>
/// <param name="StartedAt">When the activity started.</param>
/// <param name="UpdatedAt">When the activity last changed.</param>
/// <param name="Metrics">Additional compact values.</param>
public sealed record Activity(
    string Id,
    string SourceModuleId,
    string Category,
    string Title,
    string? Detail,
    ActivityState State,
    int Priority,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ActivityMetric> Metrics);

/// <summary>
/// In-process bus through which modules publish live activities without referencing the hosts that display them.
/// </summary>
public interface IActivityHub
{
    /// <summary>
    /// Occurs after the set of activities changes. Raised on the publishing thread.
    /// </summary>
    event EventHandler? ActivitiesChanged;

    /// <summary>
    /// Gets a snapshot of current activities ordered by attention: waiting first, then priority, then most recent.
    /// </summary>
    IReadOnlyList<Activity> Activities { get; }

    /// <summary>
    /// Replaces every activity owned by <paramref name="sourceModuleId"/> with <paramref name="activities"/> in one change.
    /// </summary>
    /// <param name="sourceModuleId">Owning module identifier.</param>
    /// <param name="activities">The module's complete current set of activities.</param>
    void ReplaceAll(string sourceModuleId, IReadOnlyCollection<Activity> activities);

    /// <summary>
    /// Removes every activity owned by <paramref name="sourceModuleId"/>.
    /// </summary>
    /// <param name="sourceModuleId">Owning module identifier.</param>
    void Clear(string sourceModuleId);
}
