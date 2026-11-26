using WindowsTools.Core.Tray;
using Xunit;

namespace WindowsTools.Core.Tests;

/// <summary>
/// Tests for <see cref="TrayHost"/> and <see cref="TrayPlacementCalculator"/>.
/// </summary>
public sealed class TrayHostTests
{
    /// <summary>
    /// Verifies that sections are ordered by Order, Title, and ModuleId and unregistered on dispose.
    /// </summary>
    [Fact]
    public void RegisterSectionOrdersAndDisposesCleanly()
    {
        var host = new TrayHost();
        var notifications = 0;
        host.SectionsChanged += (_, _) => notifications++;

        using var second = host.RegisterSection(new TraySectionDescriptor("b", "Beta", 20, static () => new object()));
        using var first = host.RegisterSection(new TraySectionDescriptor("a", "Alpha", 10, static () => new object()));

        Assert.Equal(2, notifications);
        Assert.Equal(["a", "b"], host.Sections.Select(static s => s.ModuleId));

        first.Dispose();
        first.Dispose(); // Idempotent dispose.

        Assert.Equal(3, notifications);
        Assert.Equal(["b"], host.Sections.Select(static s => s.ModuleId));
    }

    /// <summary>
    /// Verifies that registering two active sections for the same module identifier is rejected.
    /// </summary>
    [Fact]
    public void DuplicateSectionRegistrationThrowsInvalidOperationException()
    {
        var host = new TrayHost();
        using var first = host.RegisterSection(new TraySectionDescriptor("tools", "Tools", 1, static () => new object()));

        Assert.Throws<InvalidOperationException>(() =>
            host.RegisterSection(new TraySectionDescriptor("TOOLS", "Tools Duplicate", 2, static () => new object())));
    }

    /// <summary>
    /// Verifies that requesting settings raises <see cref="ITrayHost.SettingsRequested"/>.
    /// </summary>
    [Fact]
    public void RequestSettingsRaisesEvent()
    {
        var host = new TrayHost();
        var raised = false;
        host.SettingsRequested += (_, _) => raised = true;

        host.RequestSettings();

        Assert.True(raised);
    }

    /// <summary>
    /// Verifies tray flyout placement across multi-monitor coordinates and DPI scales.
    /// </summary>
    [Theory]
    [InlineData(1850, 1050, 0, 0, 1920, 1032, 340, 260, 12)]
    [InlineData(-200, 1050, -1920, 0, 0, 1032, 680, 520, 24)]
    [InlineData(1850, 20, 0, 48, 1920, 1080, 340, 260, 12)]
    public void PlacementCalculatorClampsInsideMonitorWorkArea(
        int anchorX,
        int anchorY,
        int left,
        int top,
        int right,
        int bottom,
        int width,
        int height,
        int margin)
    {
        var workArea = new PixelRect(left, top, right, bottom);
        var pos = TrayPlacementCalculator.ComputePosition(new PixelPoint(anchorX, anchorY), workArea, width, height, margin);

        Assert.InRange(pos.X, workArea.Left, workArea.Right - width);
        Assert.InRange(pos.Y, workArea.Top, workArea.Bottom - height);
    }
}
