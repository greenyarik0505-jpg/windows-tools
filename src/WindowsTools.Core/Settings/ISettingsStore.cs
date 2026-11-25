namespace WindowsTools.Core.Settings;

/// <summary>
/// Event arguments raised when a settings section is saved.
/// </summary>
public sealed class SettingsChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsChangedEventArgs"/> class.
    /// </summary>
    /// <param name="sectionId">Identifier of the settings section that changed.</param>
    public SettingsChangedEventArgs(string sectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        SectionId = sectionId;
    }

    /// <summary>
    /// Gets the identifier of the settings section that changed.
    /// </summary>
    public string SectionId { get; }
}

/// <summary>
/// Persists typed configuration sections per module or core component.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Occurs after a settings section has been saved.
    /// </summary>
    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    /// <summary>
    /// Loads a typed settings section or returns <paramref name="defaultValue"/> when absent or invalid.
    /// </summary>
    /// <typeparam name="T">The settings model type.</typeparam>
    /// <param name="sectionId">Stable section identifier.</param>
    /// <param name="defaultValue">Fallback value used when the section does not exist or cannot be read.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<T> LoadAsync<T>(string sectionId, T defaultValue, CancellationToken ct = default)
        where T : class;

    /// <summary>
    /// Saves a typed settings section atomically.
    /// </summary>
    /// <typeparam name="T">The settings model type.</typeparam>
    /// <param name="sectionId">Stable section identifier.</param>
    /// <param name="value">Settings object to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync<T>(string sectionId, T value, CancellationToken ct = default)
        where T : class;
}
