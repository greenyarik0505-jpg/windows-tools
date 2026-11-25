using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WindowsTools.Core.Settings;

/// <summary>
/// Minimal JSON-backed <see cref="ISettingsStore"/> that persists one file per section using atomic replacement.
/// </summary>
public sealed partial class FileSettingsStore : ISettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _directoryPath;
    private readonly ILogger<FileSettingsStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sectionLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSettingsStore"/> class.
    /// </summary>
    /// <param name="directoryPath">Directory where section JSON files are stored.</param>
    /// <param name="logger">Logger used to report read or parse failures.</param>
    public FileSettingsStore(string directoryPath, ILogger<FileSettingsStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(logger);

        _directoryPath = directoryPath;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    /// <inheritdoc />
    public async Task<T> LoadAsync<T>(string sectionId, T defaultValue, CancellationToken ct = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        ArgumentNullException.ThrowIfNull(defaultValue);

        var filePath = GetSectionPath(sectionId);
        var gate = _sectionLocks.GetOrAdd(sectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath))
            {
                return defaultValue;
            }

            await using var stream = File.OpenRead(filePath);
            var parsed = await JsonSerializer.DeserializeAsync<T>(stream, _serializerOptions, ct).ConfigureAwait(false);
            return parsed ?? defaultValue;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            LogLoadFailed(_logger, sectionId, ex);
            return defaultValue;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync<T>(string sectionId, T value, CancellationToken ct = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        ArgumentNullException.ThrowIfNull(value);

        var filePath = GetSectionPath(sectionId);
        var gate = _sectionLocks.GetOrAdd(sectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directoryPath);
            var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";

            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, _serializerOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            gate.Release();
        }

        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(sectionId));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var entry in _sectionLocks.Values)
        {
            entry.Dispose();
        }

        _sectionLocks.Clear();
    }

    private string GetSectionPath(string sectionId)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            if (sectionId.Contains(ch))
            {
                throw new ArgumentException($"Settings section id '{sectionId}' contains invalid file name character '{ch}'.", nameof(sectionId));
            }
        }

        return Path.Combine(_directoryPath, $"{sectionId}.json");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load settings section {SectionId}; falling back to defaults.")]
    private static partial void LogLoadFailed(ILogger logger, string sectionId, Exception exception);
}
