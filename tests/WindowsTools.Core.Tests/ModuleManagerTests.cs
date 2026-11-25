using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WindowsTools.Core.Logging;
using WindowsTools.Core.Modules;
using WindowsTools.Core.Settings;
using Xunit;

namespace WindowsTools.Core.Tests;

/// <summary>
/// Tests for <see cref="ModuleManager"/> lifecycle, isolation, persistence, and resource cleanup.
/// </summary>
public sealed class ModuleManagerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly RollingFileLoggerProvider _logProvider;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a temporary directory and logger factory for tests.
    /// </summary>
    public ModuleManagerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "WindowsToolsTests", Guid.NewGuid().ToString("N"));
        _logProvider = new RollingFileLoggerProvider(new RollingFileLoggerOptions
        {
            LogDirectory = Path.Combine(_tempRoot, "logs")
        });
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_logProvider));
    }

    /// <summary>
    /// Verifies that a stub module can be enabled and disabled at runtime and its state persists.
    /// </summary>
    [Fact]
    public async Task StubModuleCanEnableDisableAndPersistWithoutRestart()
    {
        var store = CreateSettingsStore();
        var stub = new StubModule("stub", enabledByDefault: false);

        using (var manager = new ModuleManager([stub], store, _loggerFactory.CreateLogger<ModuleManager>()))
        {
            await manager.InitializeAsync();
            Assert.False(stub.IsActive);

            var enabled = await manager.EnableModuleAsync("stub");
            Assert.True(enabled);
            Assert.True(stub.IsActive);
            Assert.Equal(1, stub.StartCount);

            await manager.DisableModuleAsync("stub");
            Assert.False(stub.IsActive);
            Assert.Equal(1, stub.StopCount);

            // Re-enable again without restarting the host.
            Assert.True(await manager.EnableModuleAsync("stub"));
            Assert.True(stub.IsActive);
            Assert.Equal(2, stub.StartCount);
        }

        // Verify persisted enabled state survives across ModuleManager instances.
        var reloadedStub = new StubModule("stub", enabledByDefault: false);
        using var reloadedManager = new ModuleManager([reloadedStub], store, _loggerFactory.CreateLogger<ModuleManager>());
        await reloadedManager.InitializeAsync();

        Assert.True(reloadedStub.IsActive);
        Assert.Equal(1, reloadedStub.StartCount);
    }

    /// <summary>
    /// Verifies that after <see cref="IModule.StopAsync"/> a module holds no active timers or background tasks.
    /// </summary>
    [Fact]
    public async Task DisabledModuleReleasesAllTimersAndBackgroundTasks()
    {
        var store = CreateSettingsStore();
        using var timerModule = new TimerStubModule("timer-stub");
        using var manager = new ModuleManager([timerModule], store, _loggerFactory.CreateLogger<ModuleManager>());

        await manager.InitializeAsync();
        Assert.True(await manager.EnableModuleAsync("timer-stub"));
        Assert.True(timerModule.HasActiveResources);

        await manager.DisableModuleAsync("timer-stub");

        Assert.False(timerModule.HasActiveResources);
        Assert.NotNull(timerModule.WorkerTask);
        Assert.True(timerModule.WorkerTask.IsCompleted);
    }

    /// <summary>
    /// Verifies that a startup failure in one module does not crash initialization and is recorded in the log.
    /// </summary>
    [Fact]
    public async Task FailingStartAsyncDoesNotCrashAppAndLogsError()
    {
        var store = CreateSettingsStore();
        var broken = new FailingModule("broken");
        var healthy = new StubModule("healthy", enabledByDefault: true);

        using var manager = new ModuleManager([broken, healthy], store, _loggerFactory.CreateLogger<ModuleManager>());
        await manager.InitializeAsync();

        var states = manager.Modules.ToDictionary(static m => m.Module.Id);
        Assert.False(states["broken"].IsRunning);
        Assert.False(states["broken"].IsEnabled);
        Assert.Contains("boom", states["broken"].LastError, StringComparison.OrdinalIgnoreCase);

        Assert.True(states["healthy"].IsRunning);
        Assert.True(healthy.IsActive);

        _loggerFactory.Dispose();
        _logProvider.Dispose();
        var logFiles = Directory.GetFiles(Path.Combine(_tempRoot, "logs"), "*.log");
        Assert.NotEmpty(logFiles);
        var logContent = await File.ReadAllTextAsync(logFiles[0]);
        Assert.Contains("Failed to start module broken", logContent, StringComparison.Ordinal);
        Assert.Contains("boom", logContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that <see cref="ModuleManager.ShutdownAsync"/> stops running modules while keeping enabled preferences intact.
    /// </summary>
    [Fact]
    public async Task ShutdownStopsRunningModulesWithoutDisablingPreference()
    {
        var store = CreateSettingsStore();
        var stub = new StubModule("stub", enabledByDefault: true);

        using (var manager = new ModuleManager([stub], store, _loggerFactory.CreateLogger<ModuleManager>()))
        {
            await manager.InitializeAsync();
            Assert.True(stub.IsActive);

            await manager.ShutdownAsync();
            Assert.False(stub.IsActive);
            Assert.True(manager.Modules.Single().IsEnabled);
        }

        var nextInstance = new StubModule("stub", enabledByDefault: true);
        using var nextManager = new ModuleManager([nextInstance], store, _loggerFactory.CreateLogger<ModuleManager>());
        await nextManager.InitializeAsync();
        Assert.True(nextInstance.IsActive);
    }

    /// <summary>
    /// Verifies that duplicate module identifiers are rejected during construction.
    /// </summary>
    [Fact]
    public void DuplicateModuleIdsThrowInvalidOperationException()
    {
        var store = CreateSettingsStore();
        var first = new StubModule("dup");
        var second = new StubModule("DUP");

        Assert.Throws<InvalidOperationException>(() =>
            new ModuleManager([first, second], store, _loggerFactory.CreateLogger<ModuleManager>()));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _loggerFactory.Dispose();
        _logProvider.Dispose();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private FileSettingsStore CreateSettingsStore() =>
        new(Path.Combine(_tempRoot, "settings"), _loggerFactory.CreateLogger<FileSettingsStore>());

    private sealed class StubModule(string id, bool enabledByDefault = false) : IModule
    {
        public string Id { get; } = id;
        public ModuleMetadata Metadata { get; } = new(id, "Stub module", EnabledByDefault: enabledByDefault);
        public bool IsActive { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public void ConfigureServices(IServiceCollection services)
        {
        }

        public Task StartAsync(CancellationToken ct)
        {
            IsActive = true;
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            IsActive = false;
            StopCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TimerStubModule(string id) : IModule, IDisposable
    {
        private CancellationTokenSource? _cts;
        private PeriodicTimer? _timer;

        public string Id { get; } = id;
        public ModuleMetadata Metadata { get; } = new(id, "Timer stub module");
        public Task? WorkerTask { get; private set; }
        public bool HasActiveResources => _timer is not null || (_cts is not null && !_cts.IsCancellationRequested);

        public void ConfigureServices(IServiceCollection services)
        {
        }

        public Task StartAsync(CancellationToken ct)
        {
            _cts = new CancellationTokenSource();
            _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            var token = _cts.Token;
            var timer = _timer;
            WorkerTask = Task.Run(async () =>
            {
                try
                {
                    while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                    {
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, CancellationToken.None);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken ct)
        {
            if (_cts is not null)
            {
                await _cts.CancelAsync().ConfigureAwait(false);
            }

            if (WorkerTask is not null)
            {
                await WorkerTask.ConfigureAwait(false);
            }

            _timer?.Dispose();
            _timer = null;
            _cts?.Dispose();
            _cts = null;
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _timer?.Dispose();
            _timer = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private sealed class FailingModule(string id) : IModule
    {
        public string Id { get; } = id;
        public ModuleMetadata Metadata { get; } = new(id, "Failing module", EnabledByDefault: true);

        public void ConfigureServices(IServiceCollection services)
        {
        }

        public Task StartAsync(CancellationToken ct) =>
            throw new InvalidOperationException("Simulated boom during module startup.");

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
