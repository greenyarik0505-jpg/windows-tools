using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace WindowsTools.Core.Logging;

/// <summary>
/// Configuration options for <see cref="RollingFileLoggerProvider"/>.
/// </summary>
public sealed class RollingFileLoggerOptions
{
    /// <summary>
    /// Gets or sets the directory where log files are written.
    /// </summary>
    public string LogDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowsTools",
        "logs");

    /// <summary>
    /// Gets or sets the base file name prefix for log files.
    /// </summary>
    public string FilePrefix { get; set; } = "windowstools";

    /// <summary>
    /// Gets or sets the maximum size in bytes for a single log file before rolling.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Gets or sets the maximum number of log files to retain.
    /// </summary>
    public int RetainedFileCountLimit { get; set; } = 14;

    /// <summary>
    /// Gets or sets the minimum log level written to disk.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
}

/// <summary>
/// Writes structured log entries to daily size-rolling files without background timers or threads.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly RollingFileLoggerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _writeLock = new();

    private IExternalScopeProvider? _scopeProvider;
    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private int _currentIndex;
    private long _currentSize;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RollingFileLoggerProvider"/> class.
    /// </summary>
    /// <param name="options">Rolling file logger options.</param>
    /// <param name="timeProvider">Optional time provider for deterministic testing.</param>
    public RollingFileLoggerProvider(RollingFileLoggerOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LogDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FilePrefix);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxFileSizeBytes, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.RetainedFileCountLimit, 1);

        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        return new RollingFileLogger(this, categoryName);
    }

    /// <inheritdoc />
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    internal bool IsEnabled(LogLevel logLevel) =>
        !_disposed && logLevel != LogLevel.None && logLevel >= _options.MinimumLevel;

    internal void WriteEntry(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception? exception)
    {
        var now = _timeProvider.GetLocalNow();
        var lineBuilder = new StringBuilder(256);
        lineBuilder
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [")
            .Append(FormatLevel(logLevel))
            .Append("] [")
            .Append(categoryName)
            .Append(']');

        if (eventId.Id != 0)
        {
            lineBuilder.Append(" (").Append(eventId.Id).Append(')');
        }

        _scopeProvider?.ForEachScope(
            static (scope, sb) => sb.Append(" => ").Append(scope),
            lineBuilder);

        lineBuilder.Append(' ').Append(message);

        if (exception is not null)
        {
            lineBuilder.AppendLine().Append(exception);
        }

        var payload = lineBuilder.ToString();

        lock (_writeLock)
        {
            if (_disposed)
            {
                return;
            }

            var today = DateOnly.FromDateTime(now.DateTime);
            EnsureWriterLocked(today, Encoding.UTF8.GetByteCount(payload) + Environment.NewLine.Length);
            _writer!.WriteLine(payload);
            _writer.Flush();
            _currentSize += Encoding.UTF8.GetByteCount(payload) + Environment.NewLine.Length;
        }
    }

    private void EnsureWriterLocked(DateOnly today, int incomingBytes)
    {
        if (_writer is not null && today == _currentDate && _currentSize + incomingBytes <= _options.MaxFileSizeBytes)
        {
            return;
        }

        _writer?.Dispose();
        _writer = null;

        Directory.CreateDirectory(_options.LogDirectory);

        if (today != _currentDate)
        {
            _currentDate = today;
            _currentIndex = FindStartingIndex(today, incomingBytes);
        }
        else if (_currentSize + incomingBytes > _options.MaxFileSizeBytes)
        {
            _currentIndex++;
        }

        var path = BuildFilePath(_currentDate, _currentIndex);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _currentSize = stream.Length;
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        EnforceRetentionLocked();
    }

    private int FindStartingIndex(DateOnly date, int incomingBytes)
    {
        var index = 0;
        while (true)
        {
            var candidate = BuildFilePath(date, index);
            if (!File.Exists(candidate))
            {
                return index;
            }

            var length = new FileInfo(candidate).Length;
            if (length + incomingBytes <= _options.MaxFileSizeBytes)
            {
                return index;
            }

            index++;
        }
    }

    private string BuildFilePath(DateOnly date, int index)
    {
        var stamp = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return index == 0
            ? Path.Combine(_options.LogDirectory, $"{_options.FilePrefix}-{stamp}.log")
            : Path.Combine(_options.LogDirectory, $"{_options.FilePrefix}-{stamp}.{index}.log");
    }

    private void EnforceRetentionLocked()
    {
        var files = new DirectoryInfo(_options.LogDirectory)
            .GetFiles($"{_options.FilePrefix}-*.log")
            .OrderByDescending(static f => f.LastWriteTimeUtc)
            .ThenByDescending(static f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = _options.RetainedFileCountLimit; i < files.Count; i++)
        {
            try
            {
                files[i].Delete();
            }
            catch (IOException)
            {
                // Ignore transient file locks during cleanup.
            }
        }
    }

    private static string FormatLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "UNK"
    };

    private sealed class RollingFileLogger(RollingFileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            provider._scopeProvider?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);
            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null)
            {
                return;
            }

            provider.WriteEntry(categoryName, logLevel, eventId, message, exception);
        }
    }
}
