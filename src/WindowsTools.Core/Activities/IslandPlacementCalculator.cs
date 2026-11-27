using WindowsTools.Core.Tray;

namespace WindowsTools.Core.Activities;

/// <summary>
/// Computes top-center screen coordinates for the Island overlay across monitors and DPI scales.
/// </summary>
public static class IslandPlacementCalculator
{
    /// <summary>
    /// Computes the physical top-left position for a centered top-edge overlay clamped within <paramref name="monitorBounds"/>.
    /// </summary>
    /// <param name="monitorBounds">Target monitor bounds in physical pixels.</param>
    /// <param name="overlayWidth">Overlay width in physical pixels.</param>
    /// <param name="overlayHeight">Overlay height in physical pixels.</param>
    /// <param name="topOffset">Distance from the top edge of the monitor in physical pixels.</param>
    public static PixelPoint ComputeTopCenterPosition(
        PixelRect monitorBounds,
        int overlayWidth,
        int overlayHeight,
        int topOffset)
    {
        var maxX = Math.Max(monitorBounds.Left, monitorBounds.Right - overlayWidth);
        var centeredX = monitorBounds.Left + ((monitorBounds.Width - overlayWidth) / 2);
        var clampedX = Math.Clamp(centeredX, monitorBounds.Left, maxX);

        var maxY = Math.Max(monitorBounds.Top, monitorBounds.Bottom - overlayHeight);
        var desiredY = monitorBounds.Top + Math.Max(0, topOffset);
        var clampedY = Math.Clamp(desiredY, monitorBounds.Top, maxY);

        return new PixelPoint(clampedX, clampedY);
    }
}
