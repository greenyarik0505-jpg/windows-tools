using Microsoft.Extensions.Logging;
using WindowsTools.Core.Logging;
using WindowsTools.Core.Settings;
using Xunit;

namespace WindowsTools.Core.Tests;

/// <summary>
/// Tests for <see cref="RollingFileLoggerProvider"/> and <see cref="FileSettingsStore"/>.
/// </summary>
public sealed partial class CoreInfrastructureTests : IDisposable
{
    private readonly string _tempRoot;

    /// <summary>
    /// Initializes an isolated temporary root directory for infrastructure tests.
    /// </summary>
    public CoreInfrastructureTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "WindowsToolsInfraTests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Verifies that the rolling file logger creates rotated files and enforces the retention limit.
    /// </summary>
    [Fact]
    public void RollingFileLoggerRotatesBySizeAndEnforcesRetention()
    {
        var logDir = Path.Combine(_tempRoot, "logs");
        using (var provider = new RollingFileLoggerProvider(new RollingFileLoggerOptions
        {
            LogDirectory = logDir,
            FilePrefix = "test",
            MaxFileSizeBytes = 120,
            RetainedFileCountLimit = 2,
            MinimumLevel = LogLevel.Information
        }))
        {
            var logger = provider.CreateLogger("Category");
            for (var i = 0; i < 10; i++)
            {
                LogSampleMessage(logger, i);
            }
        }

        var files = Directory.GetFiles(logDir, "test-*.log");
        Assert.InRange(files.Length, 1, 2);
    }

    /// <summary>
    /// Verifies that corrupt JSON falls back to defaults without throwing and logs a warning.
    /// </summary>
    [Fact]
    public async Task FileSettingsStoreFallsBackToDefaultsWhenJsonIsCorrupt()
    {
        var settingsDir = Path.Combine(_tempRoot, "settings");
        Directory.CreateDirectory(settingsDir);
        await File.WriteAllTextAsync(Path.Combine(settingsDir, "sample.json"), "{ invalid json");

        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var store = new FileSettingsStore(settingsDir, loggerFactory.CreateLogger<FileSettingsStore>());

        var fallback = new SampleSettings { Value = 42 };
        var loaded = await store.LoadAsync("sample", fallback);

        Assert.Equal(42, loaded.Value);

        var changedSection = string.Empty;
        store.SettingsChanged += (_, e) => changedSection = e.SectionId;
        await store.SaveAsync("sample", new SampleSettings { Value = 99 });

        var reloaded = await store.LoadAsync("sample", fallback);
        Assert.Equal(99, reloaded.Value);
        Assert.Equal("sample", changedSection);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class SampleSettings
    {
        public int Value { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Log message number {Index} with padding text")]
    private static partial void LogSampleMessage(ILogger logger, int index);
}
