using WindowsTools.Core.Activities;

namespace WindowsTools.Modules.Island;

/// <summary>
/// UI-independent rules for what the Island shows. Kept separate from the window so they can be unit-tested.
/// </summary>
internal static class IslandSummary
{
    /// <summary>Maximum rows in the expanded Island before the rest is summarized as "+N".</summary>
    public const int MaxRows = 6;

    /// <summary>
    /// The most urgent state among the activities: waiting beats working beats everything else.
    /// </summary>
    public static ActivityState OverallState(IReadOnlyList<Activity> activities)
    {
        if (activities.Any(a => a.State == ActivityState.WaitingForUser))
        {
            return ActivityState.WaitingForUser;
        }

        if (activities.Any(a => a.State == ActivityState.Working))
        {
            return ActivityState.Working;
        }

        if (activities.Any(a => a.State == ActivityState.Error))
        {
            return ActivityState.Error;
        }

        return ActivityState.Idle;
    }

    /// <summary>
    /// Formats elapsed time compactly: under a minute in seconds, under an hour in minutes, otherwise hours and minutes.
    /// </summary>
    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return IslandStrings.Format("Duration_Seconds", "{0}s", (int)elapsed.TotalSeconds);
        }

        if (elapsed.TotalHours < 1)
        {
            return IslandStrings.Format("Duration_Minutes", "{0}m", (int)elapsed.TotalMinutes);
        }

        return IslandStrings.Format("Duration_HoursMinutes", "{0}h {1}m", (int)elapsed.TotalHours, elapsed.Minutes);
    }

    public static string StateText(ActivityState state) => state switch
    {
        ActivityState.Working => IslandStrings.Get("State_Working", "Working"),
        ActivityState.WaitingForUser => IslandStrings.Get("State_Waiting", "Needs you"),
        ActivityState.Completed => IslandStrings.Get("State_Completed", "Done"),
        ActivityState.Error => IslandStrings.Get("State_Error", "Error"),
        _ => IslandStrings.Get("State_Idle", "Idle"),
    };

    /// <summary>Text for the collapsed pill.</summary>
    public static string CollapsedText(IReadOnlyList<Activity> activities)
    {
        if (activities.Count == 0)
        {
            return IslandStrings.Get("Collapsed_None", "No active agents");
        }

        var waiting = activities.Count(a => a.State == ActivityState.WaitingForUser);
        if (waiting > 0)
        {
            return waiting == 1
                ? IslandStrings.Format("Collapsed_WaitingOne", "{0} needs you", activities.First(a => a.State == ActivityState.WaitingForUser).Title)
                : IslandStrings.Format("Collapsed_WaitingMany", "{0} need you", waiting);
        }

        var working = activities.Count(a => a.State == ActivityState.Working);
        if (working == 1)
        {
            var top = activities.First(a => a.State == ActivityState.Working);
            return $"{top.Title} · {top.Detail ?? StateText(ActivityState.Working)}";
        }

        if (working > 1)
        {
            return IslandStrings.Format("Collapsed_WorkingMany", "{0} agents working", working);
        }

        return IslandStrings.Format("Collapsed_IdleMany", "{0} agents idle", activities.Count);
    }
}
