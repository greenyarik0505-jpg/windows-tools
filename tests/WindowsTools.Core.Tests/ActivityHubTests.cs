using WindowsTools.Core.Activities;
using WindowsTools.Core.Tray;
using Xunit;

namespace WindowsTools.Core.Tests;

/// <summary>
/// Tests for <see cref="ActivityHub"/> and <see cref="IslandPlacementCalculator"/>.
/// </summary>
public sealed class ActivityHubTests
{
    /// <summary>
    /// Verifies that activities are ordered by priority, WaitingForUser state, and recency.
    /// </summary>
    [Fact]
    public void HubOrdersByPriorityThenWaitingForUserThenRecency()
    {
        var hub = new ActivityHub();
        var notifications = 0;
        hub.ActivitiesChanged += (_, _) => notifications++;

        using var low = hub.Publish(new ActivityDescriptor("low", "mod-a", "Low"));
        using var normalActive = hub.Publish(new ActivityDescriptor("n1", "mod-b", "Normal Active", Priority: ActivityPriority.Normal));
        using var normalWaiting = hub.Publish(new ActivityDescriptor("n2", "mod-b", "Normal Waiting", Priority: ActivityPriority.Normal, State: ActivityState.WaitingForUser));
        using var high = hub.Publish(new ActivityDescriptor("high", "mod-c", "High", Priority: ActivityPriority.High));

        Assert.Equal(4, notifications);
        Assert.Equal("high", hub.PrimaryActivity?.Id);
        Assert.Equal(["high", "n2", "n1", "low"], hub.Activities.Select(static a => a.Id));
    }

    /// <summary>
    /// Verifies that updating an activity refreshes it in place and disposing removes it idempotently.
    /// </summary>
    [Fact]
    public void UpdateAndDisposeBehaveCleanly()
    {
        var hub = new ActivityHub();
        var reg = hub.Publish(new ActivityDescriptor("a1", "agents", "Running", Subtitle: "Step 1"));

        hub.Update(new ActivityDescriptor("a1", "agents", "Waiting", Subtitle: "Approve", State: ActivityState.WaitingForUser));
        Assert.Single(hub.Activities);
        Assert.Equal("Waiting", hub.PrimaryActivity?.Title);
        Assert.Equal(ActivityState.WaitingForUser, hub.PrimaryActivity?.State);

        reg.Dispose();
        reg.Dispose();
        Assert.Empty(hub.Activities);
        Assert.Null(hub.PrimaryActivity);
    }

    /// <summary>
    /// Verifies top-center overlay placement across primary, negative multi-monitor, and 200% DPI sizes.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 1920, 1080, 220, 38, 10, 850, 10)]
    [InlineData(-1920, 0, 0, 1080, 440, 76, 20, -1180, 20)]
    [InlineData(0, 0, 300, 200, 400, 38, 10, 0, 10)]
    public void PlacementCalculatorCentersAndClamps(
        int left,
        int top,
        int right,
        int bottom,
        int width,
        int height,
        int topOffset,
        int expectedX,
        int expectedY)
    {
        var bounds = new PixelRect(left, top, right, bottom);
        var pos = IslandPlacementCalculator.ComputeTopCenterPosition(bounds, width, height, topOffset);

        Assert.Equal(expectedX, pos.X);
        Assert.Equal(expectedY, pos.Y);
    }
}
