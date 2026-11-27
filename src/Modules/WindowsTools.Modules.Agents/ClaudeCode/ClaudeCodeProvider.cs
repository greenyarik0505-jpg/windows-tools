using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WindowsTools.Modules.Agents.ClaudeCode;

/// <summary>
/// Reads live Claude Code sessions from the local session registry and enriches them from transcripts.
/// See <c>docs/agents/DATA_SOURCES.md</c> for the formats and their stability.
/// </summary>
public sealed partial class ClaudeCodeProvider : IAgentProvider
{
    private readonly string _claudeRoot;
    private readonly Func<int, bool> _isProcessAlive;
    private readonly ILogger _logger;
    private readonly Dictionary<string, ClaudeTranscriptReader> _transcripts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="ClaudeCodeProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger for parse diagnostics. Never logs message content.</param>
    /// <param name="claudeRoot">Root of the Claude Code data folder; defaults to <c>%USERPROFILE%\.claude</c>.</param>
    /// <param name="isProcessAlive">Liveness check for registry PIDs; defaults to a real process lookup.</param>
    public ClaudeCodeProvider(ILogger<ClaudeCodeProvider> logger, string? claudeRoot = null, Func<int, bool>? isProcessAlive = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _claudeRoot = claudeRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        _isProcessAlive = isProcessAlive ?? IsProcessAlive;
    }

    /// <inheritdoc />
    public AgentKind Agent => AgentKind.ClaudeCode;

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentSession>> GetSessionsAsync(CancellationToken ct)
    {
        var registryDir = Path.Combine(_claudeRoot, "sessions");
        if (!Directory.Exists(registryDir))
        {
            return Task.FromResult<IReadOnlyList<AgentSession>>([]);
        }

        var sessions = new List<AgentSession>();
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Only "<pid>.json"; the neighbouring "<pid>.<hash>.key" files are session secrets and are never opened.
        foreach (var file in Directory.EnumerateFiles(registryDir, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out _))
            {
                continue;
            }

            var entry = ClaudeSessionRegistry.Parse(ReadShared(file) ?? string.Empty);
            if (entry is null || !ClaudeSessionRegistry.IsLocalWindowsPid(entry) || !_isProcessAlive(entry.Pid))
            {
                continue;
            }

            live.Add(entry.SessionId);
            sessions.Add(BuildSession(entry));
        }

        foreach (var stale in _transcripts.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _transcripts.Remove(stale);
        }

        return Task.FromResult<IReadOnlyList<AgentSession>>(sessions);
    }

    private AgentSession BuildSession(ClaudeRegistryEntry entry)
    {
        var transcript = GetTranscript(entry);
        if (transcript is not null)
        {
            try
            {
                transcript.ReadNewLines();
            }
            catch (IOException ex)
            {
                LogTranscriptReadFailed(_logger, entry.SessionId, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogTranscriptReadFailed(_logger, entry.SessionId, ex);
            }
        }

        var state = ClaudeSessionRegistry.MapStatus(entry.Status);
        var title = !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name
            : !string.IsNullOrWhiteSpace(transcript?.CustomTitle) ? transcript.CustomTitle
            : FolderName(entry.Cwd);

        return new AgentSession(
            AgentKind.ClaudeCode,
            entry.SessionId,
            title,
            entry.Cwd,
            state,
            state == AgentSessionState.Working ? transcript?.PendingTool : null,
            transcript?.Model,
            transcript?.Tokens ?? default,
            entry.StartedAt,
            entry.UpdatedAt);
    }

    private ClaudeTranscriptReader? GetTranscript(ClaudeRegistryEntry entry)
    {
        if (_transcripts.TryGetValue(entry.SessionId, out var cached))
        {
            return cached;
        }

        var path = FindTranscript(entry);
        if (path is null)
        {
            return null;
        }

        var reader = new ClaudeTranscriptReader(path);
        _transcripts[entry.SessionId] = reader;
        return reader;
    }

    private string? FindTranscript(ClaudeRegistryEntry entry)
    {
        var projects = Path.Combine(_claudeRoot, "projects");
        var fileName = entry.SessionId + ".jsonl";

        var direct = Path.Combine(projects, ClaudeTranscriptReader.ProjectFolderName(entry.Cwd), fileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        if (!Directory.Exists(projects))
        {
            return null;
        }

        // The folder naming rule is internal; fall back to looking in every project folder.
        foreach (var dir in Directory.EnumerateDirectories(projects))
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string? ReadShared(string path)
    {
        var fileName = Path.GetFileName(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            // The registry file can disappear between enumeration and open when a session exits.
            LogRegistryReadFailed(_logger, fileName, ex);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogRegistryReadFailed(_logger, fileName, ex);
            return null;
        }
    }

    private static string FolderName(string cwd)
    {
        var trimmed = cwd.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not read Claude Code registry file {FileName}.")]
    private static partial void LogRegistryReadFailed(ILogger logger, string fileName, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read transcript for Claude Code session {SessionId}.")]
    private static partial void LogTranscriptReadFailed(ILogger logger, string sessionId, Exception exception);
}
