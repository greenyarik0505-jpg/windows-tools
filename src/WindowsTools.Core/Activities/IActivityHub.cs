namespace WindowsTools.Core.Activities;

/// <summary>
/// Priority tier used by Island and activity hosts to choose the primary displayed activity.
/// </summary>
public enum ActivityPriority
{
    /// <summary>
    /// Background informational activity.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Standard user-visible activity.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Elevated activity such as active AI agent sessions or live recording.
    /// </summary>
    High = 2,

    /// <summary>
    /// Urgent activity requiring user attention.
    /// </summary>
    Critical = 3
}

/// <summary>
/// Lifecycle state of an activity published to <see cref="IActivityHub"/>.
/// </summary>
public enum ActivityState
{
    /// <summary>
    /// Activity is currently running or playing.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Activity is paused waiting for user input or approval.
    /// </summary>
    WaitingForUser = 1,

    /// <summary>
    /// Activity completed recently.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Activity encountered an error.
    /// </summary>
    Error = 3
}

/// <summary>
/// Describes an activity published by a module for display in Island or other surfaces.
/// </summary>
/// <param name="Id">Unique activity identifier.</param>
/// <param name="ModuleId">Identifier of the owning module.</param>
/// <param name="Title">Localized primary label.</param>
/// <param name="Subtitle">Optional localized secondary status text.</param>
/// <param name="IconGlyph">Optional Segoe Fluent Icons glyph.</param>
/// <param name="Priority">Display priority.</param>
/// <param name="State">Current activity state.</param>
/// <param name="Progress">Optional normalized progress in [0, 1].</param>
/// <param name="CreateExpandedContent">Optional factory creating a UI element for the expanded Island card.</param>
/// <param name="OnPrimaryAction">Optional callback invoked when the user activates the activity.</param>
public sealed record ActivityDescriptor(
    string Id,
    string ModuleId,
    string Title,
    string? Subtitle = null,
    string? IconGlyph = null,
    ActivityPriority Priority = ActivityPriority.Normal,
    ActivityState State = ActivityState.Active,
    double? Progress = null,
    Func<object>? CreateExpandedContent = null,
    Action? OnPrimaryAction = null);

/// <summary>
/// Aggregates live module activities ordered by priority so Island can display them without module coupling.
/// </summary>
public interface IActivityHub
{
    /// <summary>
    /// Occurs when an activity is published, updated, or removed.
    /// </summary>
    event EventHandler? ActivitiesChanged;

    /// <summary>
    /// Gets all active activities ordered from highest to lowest display priority.
    /// </summary>
    IReadOnlyList<ActivityDescriptor> Activities { get; }

    /// <summary>
    /// Gets the highest-priority activity, or <c>null</c> when none are active.
    /// </summary>
    ActivityDescriptor? PrimaryActivity { get; }

    /// <summary>
    /// Publishes or replaces an activity. Disposing the returned handle removes the activity.
    /// </summary>
    /// <param name="activity">Activity descriptor.</param>
    IDisposable Publish(ActivityDescriptor activity);

    /// <summary>
    /// Publishes or updates an activity in place.
    /// </summary>
    /// <param name="activity">Updated activity descriptor.</param>
    void Update(ActivityDescriptor activity);

    /// <summary>
    /// Removes an activity by identifier if present.
    /// </summary>
    /// <param name="activityId">Activity identifier.</param>
    void Remove(string activityId);
}
