using Microsoft.Extensions.Logging;
using WindowsTools.Core.Settings;

namespace WindowsTools.Core.Modules;

/// <summary>
/// Persisted module activation settings stored under the <c>modules</c> settings section.
/// </summary>
public sealed class ModuleActivationSettings
{
    /// <summary>
    /// Gets or sets the schema version of the settings payload.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the map of module identifier to enabled state.
    /// </summary>
    public Dictionary<string, bool> EnabledModules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Represents the runtime state of a registered module.
/// </summary>
public sealed record ModuleRuntimeState(
    IModule Module,
    bool IsEnabled,
    bool IsRunning,
    string? LastError);

/// <summary>
/// Event arguments raised when a module's enabled or running state changes.
/// </summary>
public sealed class ModuleStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleStateChangedEventArgs"/> class.
    /// </summary>
    /// <param name="state">The updated runtime state.</param>
    public ModuleStateChangedEventArgs(ModuleRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
    }

    /// <summary>
    /// Gets the updated module runtime state.
    /// </summary>
    public ModuleRuntimeState State { get; }
}

/// <summary>
/// Manages registration and runtime lifecycle of WindowsTools modules.
/// </summary>
public interface IModuleManager
{
    /// <summary>
    /// Occurs when a module starts, stops, or encounters a lifecycle error.
    /// </summary>
    event EventHandler<ModuleStateChangedEventArgs>? ModuleStateChanged;

    /// <summary>
    /// Gets the current snapshot of all registered modules and their runtime states.
    /// </summary>
    IReadOnlyList<ModuleRuntimeState> Modules { get; }

    /// <summary>
    /// Loads saved module states and starts all enabled modules.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Enables and starts a module without restarting the application.
    /// </summary>
    /// <param name="moduleId">Identifier of the module to enable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the module is running after the call; otherwise <c>false</c>.</returns>
    Task<bool> EnableModuleAsync(string moduleId, CancellationToken ct = default);

    /// <summary>
    /// Stops and disables a module without restarting the application.
    /// </summary>
    /// <param name="moduleId">Identifier of the module to disable.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DisableModuleAsync(string moduleId, CancellationToken ct = default);

    /// <summary>
    /// Stops all currently running modules during application shutdown without clearing persisted preferences.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ShutdownAsync(CancellationToken ct = default);
}

/// <summary>
/// Default thread-safe implementation of <see cref="IModuleManager"/>.
/// </summary>
public sealed partial class ModuleManager : IModuleManager, IDisposable
{
    internal const string SettingsSectionId = "modules";

    private readonly List<IModule> _orderedModules;
    private readonly Dictionary<string, ModuleEntry> _entries;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<ModuleManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ModuleManager"/> class.
    /// </summary>
    /// <param name="modules">All registered modules.</param>
    /// <param name="settingsStore">Settings store used to persist enabled states.</param>
    /// <param name="logger">Logger for lifecycle diagnostics.</param>
    public ModuleManager(
        IEnumerable<IModule> modules,
        ISettingsStore settingsStore,
        ILogger<ModuleManager> logger)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(logger);

        _settingsStore = settingsStore;
        _logger = logger;

        var list = new List<IModule>();
        var map = new Dictionary<string, ModuleEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            ArgumentException.ThrowIfNullOrWhiteSpace(module.Id);

            if (!map.TryAdd(module.Id, new ModuleEntry(module, module.Metadata.EnabledByDefault)))
            {
                throw new InvalidOperationException($"Duplicate module id '{module.Id}' registered.");
            }

            list.Add(module);
        }

        _orderedModules = list;
        _entries = map;
    }

    /// <inheritdoc />
    public event EventHandler<ModuleStateChangedEventArgs>? ModuleStateChanged;

    /// <inheritdoc />
    public IReadOnlyList<ModuleRuntimeState> Modules
    {
        get
        {
            var snapshot = new List<ModuleRuntimeState>(_orderedModules.Count);
            foreach (var module in _orderedModules)
            {
                snapshot.Add(_entries[module.Id].ToState());
            }

            return snapshot;
        }
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var settings = await _settingsStore
                .LoadAsync(SettingsSectionId, new ModuleActivationSettings(), ct)
                .ConfigureAwait(false);

            foreach (var module in _orderedModules)
            {
                var entry = _entries[module.Id];
                var isEnabled = settings.EnabledModules.TryGetValue(module.Id, out var saved)
                    ? saved
                    : module.Metadata.EnabledByDefault;

                entry.IsEnabled = isEnabled;

                if (isEnabled && !entry.IsRunning)
                {
                    await TryStartEntryAsync(entry, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> EnableModuleAsync(string moduleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        ModuleRuntimeState? notification = null;
        bool running;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entry = GetEntry(moduleId);
            if (!entry.IsRunning)
            {
                await TryStartEntryAsync(entry, ct).ConfigureAwait(false);
            }

            entry.IsEnabled = entry.IsRunning;
            running = entry.IsRunning;
            await PersistStatesAsync(ct).ConfigureAwait(false);
            notification = entry.ToState();
        }
        finally
        {
            _gate.Release();
        }

        if (notification is not null)
        {
            ModuleStateChanged?.Invoke(this, new ModuleStateChangedEventArgs(notification));
        }

        return running;
    }

    /// <inheritdoc />
    public async Task DisableModuleAsync(string moduleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        ModuleRuntimeState? notification = null;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entry = GetEntry(moduleId);
            entry.IsEnabled = false;

            if (entry.IsRunning)
            {
                await TryStopEntryAsync(entry, ct).ConfigureAwait(false);
            }
            else
            {
                entry.LastError = null;
            }

            await PersistStatesAsync(ct).ConfigureAwait(false);
            notification = entry.ToState();
        }
        finally
        {
            _gate.Release();
        }

        if (notification is not null)
        {
            ModuleStateChanged?.Invoke(this, new ModuleStateChangedEventArgs(notification));
        }
    }

    /// <inheritdoc />
    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        var notifications = new List<ModuleRuntimeState>();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var i = _orderedModules.Count - 1; i >= 0; i--)
            {
                var entry = _entries[_orderedModules[i].Id];
                if (entry.IsRunning)
                {
                    await TryStopEntryAsync(entry, ct).ConfigureAwait(false);
                    notifications.Add(entry.ToState());
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        foreach (var state in notifications)
        {
            ModuleStateChanged?.Invoke(this, new ModuleStateChangedEventArgs(state));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
    }

    private ModuleEntry GetEntry(string moduleId)
    {
        if (!_entries.TryGetValue(moduleId, out var entry))
        {
            throw new KeyNotFoundException($"Module '{moduleId}' is not registered.");
        }

        return entry;
    }

    private async Task TryStartEntryAsync(ModuleEntry entry, CancellationToken ct)
    {
        try
        {
            await entry.Module.StartAsync(ct).ConfigureAwait(false);
            entry.IsRunning = true;
            entry.LastError = null;
            LogModuleStarted(_logger, entry.Module.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            entry.IsRunning = false;
            entry.IsEnabled = false;
            entry.LastError = ex.Message;
            LogModuleStartFailed(_logger, entry.Module.Id, ex);
        }
    }

    private async Task TryStopEntryAsync(ModuleEntry entry, CancellationToken ct)
    {
        try
        {
            await entry.Module.StopAsync(ct).ConfigureAwait(false);
            entry.LastError = null;
            LogModuleStopped(_logger, entry.Module.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            entry.LastError = ex.Message;
            LogModuleStopFailed(_logger, entry.Module.Id, ex);
        }
        finally
        {
            entry.IsRunning = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started module {ModuleId}.")]
    private static partial void LogModuleStarted(ILogger logger, string moduleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to start module {ModuleId}.")]
    private static partial void LogModuleStartFailed(ILogger logger, string moduleId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped module {ModuleId}.")]
    private static partial void LogModuleStopped(ILogger logger, string moduleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error while stopping module {ModuleId}.")]
    private static partial void LogModuleStopFailed(ILogger logger, string moduleId, Exception exception);

    private Task PersistStatesAsync(CancellationToken ct)
    {
        var settings = new ModuleActivationSettings();
        foreach (var module in _orderedModules)
        {
            settings.EnabledModules[module.Id] = _entries[module.Id].IsEnabled;
        }

        return _settingsStore.SaveAsync(SettingsSectionId, settings, ct);
    }

    private sealed class ModuleEntry(IModule module, bool isEnabled)
    {
        public IModule Module { get; } = module;
        public bool IsEnabled { get; set; } = isEnabled;
        public bool IsRunning { get; set; }
        public string? LastError { get; set; }

        public ModuleRuntimeState ToState() => new(Module, IsEnabled, IsRunning, LastError);
    }
}
