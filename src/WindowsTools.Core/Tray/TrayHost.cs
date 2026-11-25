namespace WindowsTools.Core.Tray;

/// <summary>
/// Thread-safe default implementation of <see cref="ITrayHost"/>.
/// </summary>
public sealed class TrayHost : ITrayHost
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TraySectionDescriptor> _sections = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event EventHandler? SectionsChanged;

    /// <inheritdoc />
    public event EventHandler? SettingsRequested;

    /// <inheritdoc />
    public IReadOnlyList<TraySectionDescriptor> Sections
    {
        get
        {
            lock (_gate)
            {
                return _sections.Values
                    .OrderBy(static s => s.Order)
                    .ThenBy(static s => s.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(static s => s.ModuleId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }
    }

    /// <inheritdoc />
    public IDisposable RegisterSection(TraySectionDescriptor section)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(section.ModuleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(section.Title);
        ArgumentNullException.ThrowIfNull(section.CreateContent);

        lock (_gate)
        {
            if (!_sections.TryAdd(section.ModuleId, section))
            {
                throw new InvalidOperationException($"Tray section for module '{section.ModuleId}' is already registered.");
            }
        }

        SectionsChanged?.Invoke(this, EventArgs.Empty);
        return new SectionRegistration(this, section.ModuleId);
    }

    /// <inheritdoc />
    public void RequestSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void Unregister(string moduleId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _sections.Remove(moduleId);
        }

        if (removed)
        {
            SectionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class SectionRegistration(TrayHost host, string moduleId) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                host.Unregister(moduleId);
            }
        }
    }
}
