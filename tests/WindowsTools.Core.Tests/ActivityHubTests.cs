using WindowsTools.Core.Activities;
using Xunit;

namespace WindowsTools.Core.Tests;

/// <summary>
/// Tests for <see cref="ActivityHub"/>.
/// </summary>
public sealed class ActivityHubTests
{
    /// <summary>Waiting activities come first, then higher priority, then most recent.</summary>
    [Fact]
    public void ActivitiesAreOrderedByAttention()
    {
        var hub = new ActivityHub();
        hub.ReplaceAll("m", [
            Make("idle", ActivityState.Idle, priority: 10, minute: 3),
            Make("work-old", ActivityState.Working, priority: 20, minute: 1),
            Make("work-new", ActivityState.Working, priority: 20, minute: 2),
            Make("wait", ActivityState.WaitingForUser, priority: 0, minute: 0),
        ]);

        Assert.Equal(["wait", "work-new", "work-old", "idle"], hub.Activities.Select(a => a.Id));
    }

    /// <summary>Publishing identical content does not raise a change event.</summary>
    [Fact]
    public void ReplaceAllRaisesOnlyOnRealChanges()
    {
        var hub = new ActivityHub();
        var raised = 0;
        hub.ActivitiesChanged += (_, _) => raised++;

        hub.ReplaceAll("m", [Make("a", ActivityState.Working)]);
        hub.ReplaceAll("m", [Make("a", ActivityState.Working)]);
        hub.ReplaceAll("m", [Make("a", ActivityState.Idle)]);

        Assert.Equal(2, raised);
    }

    /// <summary>Modules only replace their own activities.</summary>
    [Fact]
    public void ReplaceAllKeepsOtherModulesAndRejectsForeignIds()
    {
        var hub = new ActivityHub();
        hub.ReplaceAll("m", [Make("a", ActivityState.Working)]);
        hub.ReplaceAll("other", [Make("b", ActivityState.Idle, module: "other")]);
        hub.Clear("m");

        Assert.Equal("b", Assert.Single(hub.Activities).Id);
        Assert.Throws<ArgumentException>(() => hub.ReplaceAll("m", [Make("c", ActivityState.Idle, module: "other")]));
    }

    private static Activity Make(string id, ActivityState state, int priority = 0, int minute = 0, string module = "m") => new(
        id,
        module,
        "Category",
        id,
        null,
        state,
        priority,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch.AddMinutes(minute),
        [new ActivityMetric("tokens", "1k")]);
}
