using System.Xml.Linq;
using WindowsTools.Core.Activities;
using Xunit;

namespace WindowsTools.Modules.Island.Tests;

/// <summary>
/// Tests for <see cref="IslandViewModel"/>, <see cref="IslandModule"/> lifecycle, and localization parity.
/// </summary>
public sealed class IslandModuleTests
{
    /// <summary>
    /// Verifies that multiple simultaneous activities show the highest-priority item as featured
    /// and expose the remaining activities as secondary indicators.
    /// </summary>
    [Fact]
    public void SimultaneousActivitiesShowHighestPriorityAndSecondaryIndicators()
    {
        var hub = new ActivityHub();
        using var vm = new IslandViewModel(hub);

        using var a1 = hub.Publish(new ActivityDescriptor("timer", "tools", "Timer", Priority: ActivityPriority.Normal));
        using var a2 = hub.Publish(new ActivityDescriptor("agents", "agents", "Claude Code", Priority: ActivityPriority.High, State: ActivityState.WaitingForUser));
        using var a3 = hub.Publish(new ActivityDescriptor("media", "island", "Music", Priority: ActivityPriority.Low));

        Assert.Equal("agents", vm.FeaturedActivity?.Id);
        Assert.Equal(["timer", "media"], vm.SecondaryActivities.Select(static a => a.Id));

        vm.SelectActivity("timer");
        Assert.True(vm.IsExpanded);
        Assert.Equal("timer", vm.FeaturedActivity?.Id);
        Assert.Equal(["agents", "media"], vm.SecondaryActivities.Select(static a => a.Id));

        vm.Collapse();
        Assert.False(vm.IsExpanded);
        Assert.Equal("agents", vm.FeaturedActivity?.Id);
    }

    /// <summary>
    /// Verifies that disabling Island cleanly releases view-model subscriptions without affecting other modules using <see cref="IActivityHub"/>.
    /// </summary>
    [Fact]
    public async Task DisablingIslandLeavesHubIntactAndCleansUpResources()
    {
        var hub = new ActivityHub();
        using var externalActivity = hub.Publish(new ActivityDescriptor("rec", "capture", "Recording"));
        var island = new IslandModule(hub);

        await island.StartAsync(CancellationToken.None);
        Assert.NotNull(island.CurrentViewModel);
        Assert.Equal("rec", island.CurrentViewModel.FeaturedActivity?.Id);

        await island.StopAsync(CancellationToken.None);
        Assert.Null(island.CurrentViewModel);

        // Hub and other modules continue operating cleanly when Island is disabled.
        Assert.Single(hub.Activities);
        hub.Update(new ActivityDescriptor("rec", "capture", "Recording updated"));
        Assert.Equal("Recording updated", hub.PrimaryActivity?.Title);
    }

    /// <summary>
    /// Verifies that en-US and uk-UA resource files define the exact same localization keys.
    /// </summary>
    [Fact]
    public void LocalizationResourceKeysMatchBetweenEnglishAndUkrainian()
    {
        var repoRoot = FindRepoRoot();
        var enKeys = ReadReswKeys(Path.Combine(repoRoot, "src", "Modules", "WindowsTools.Modules.Island", "Strings", "en-US", "Resources.resw"));
        var ukKeys = ReadReswKeys(Path.Combine(repoRoot, "src", "Modules", "WindowsTools.Modules.Island", "Strings", "uk-UA", "Resources.resw"));

        Assert.NotEmpty(enKeys);
        Assert.Equal(enKeys, ukKeys);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WindowsTools.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static List<string> ReadReswKeys(string path)
    {
        var doc = XDocument.Load(path);
        return doc.Descendants("data")
            .Select(static el => (string?)el.Attribute("name") ?? string.Empty)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();
    }
}
