namespace WindowsTools.Core.Tray;

/// <summary>
/// Describes a module section displayed inside the tray flyout.
/// </summary>
/// <param name="ModuleId">Stable identifier of the owning module.</param>
/// <param name="Title">Localized display title for the section.</param>
/// <param name="Order">Sort order inside the flyout (lower values appear first).</param>
/// <param name="CreateContent">Factory returning the UI content element for the section.</param>
public sealed record TraySectionDescriptor(
    string ModuleId,
    string Title,
    int Order,
    Func<object> CreateContent);

/// <summary>
/// Exposes tray flyout extensibility and tray host commands to modules without referencing the UI shell.
/// </summary>
public interface ITrayHost
{
    /// <summary>
    /// Occurs when registered flyout sections change.
    /// </summary>
    event EventHandler? SectionsChanged;

    /// <summary>
    /// Occurs when the user asks to open the settings window from the tray.
    /// </summary>
    event EventHandler? SettingsRequested;

    /// <summary>
    /// Gets the currently registered flyout sections ordered for display.
    /// </summary>
    IReadOnlyList<TraySectionDescriptor> Sections { get; }

    /// <summary>
    /// Registers a module section in the tray flyout. Dispose the returned handle to unregister.
    /// </summary>
    /// <param name="section">Section descriptor.</param>
    IDisposable RegisterSection(TraySectionDescriptor section);

    /// <summary>
    /// Requests opening or focusing the application settings window.
    /// </summary>
    void RequestSettings();
}
