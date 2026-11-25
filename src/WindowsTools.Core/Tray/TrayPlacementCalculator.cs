namespace WindowsTools.Core.Tray;

/// <summary>
/// Represents an integer rectangle in physical screen pixels.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>
    /// Gets the rectangle width in pixels.
    /// </summary>
    public int Width => Right - Left;

    /// <summary>
    /// Gets the rectangle height in pixels.
    /// </summary>
    public int Height => Bottom - Top;
}

/// <summary>
/// Represents a 2D coordinate in physical screen pixels.
/// </summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>
/// Computes DPI-aware flyout placement near a tray anchor inside the target monitor's work area.
/// </summary>
public static class TrayPlacementCalculator
{
    /// <summary>
    /// Calculates the top-left coordinates for a flyout window so it stays adjacent to the tray anchor
    /// and clamped within <paramref name="workArea"/>.
    /// </summary>
    /// <param name="anchorPoint">Tray icon center or cursor point in physical pixels.</param>
    /// <param name="workArea">Target monitor work area in physical pixels.</param>
    /// <param name="flyoutWidth">Flyout width in physical pixels.</param>
    /// <param name="flyoutHeight">Flyout height in physical pixels.</param>
    /// <param name="margin">Gap in physical pixels from the screen edge or anchor.</param>
    public static PixelPoint ComputePosition(
        PixelPoint anchorPoint,
        PixelRect workArea,
        int flyoutWidth,
        int flyoutHeight,
        int margin)
    {
        var maxX = Math.Max(workArea.Left, workArea.Right - flyoutWidth - margin);
        var minX = workArea.Left + margin;
        if (minX > maxX)
        {
            minX = workArea.Left;
            maxX = workArea.Left;
        }

        var maxY = Math.Max(workArea.Top, workArea.Bottom - flyoutHeight - margin);
        var minY = workArea.Top + margin;
        if (minY > maxY)
        {
            minY = workArea.Top;
            maxY = workArea.Top;
        }

        // Prefer centering horizontally over the tray anchor when possible, otherwise clamp inside work area.
        var desiredX = anchorPoint.X - (flyoutWidth / 2);
        var clampedX = Math.Clamp(desiredX, minX, maxX);

        // If the anchor sits in the upper half of the work area (top taskbar), place below; otherwise above.
        var workMidY = workArea.Top + (workArea.Height / 2);
        var desiredY = anchorPoint.Y <= workMidY
            ? workArea.Top + margin
            : workArea.Bottom - flyoutHeight - margin;
        var clampedY = Math.Clamp(desiredY, minY, maxY);

        return new PixelPoint(clampedX, clampedY);
    }
}
