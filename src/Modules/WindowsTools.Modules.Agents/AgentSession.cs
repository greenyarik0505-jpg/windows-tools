namespace WindowsTools.Modules.Agents;

/// <summary>
/// Coding agents the module can observe.
/// </summary>
public enum AgentKind
{
    /// <summary>Anthropic Claude Code.</summary>
    ClaudeCode,

    /// <summary>OpenAI Codex.</summary>
    Codex,

    /// <summary>OpenCode.</summary>
    OpenCode,
}

/// <summary>
/// What an agent session is doing right now.
/// </summary>
public enum AgentSessionState
{
    /// <summary>Session is open but not processing a turn.</summary>
    Idle,

    /// <summary>Session is processing a turn.</summary>
    Working,

    /// <summary>Session is blocked on a permission prompt or question.</summary>
    WaitingForUser,
}

/// <summary>
/// Token counters for a session. Cache counters are kept separate because they are priced differently.
/// </summary>
public readonly record struct TokenUsage(long Input, long Output, long CacheCreation, long CacheRead)
{
    /// <summary>Gets the sum of all counters.</summary>
    public long Total => Input + Output + CacheCreation + CacheRead;

    /// <summary>Adds two usages.</summary>
    public static TokenUsage operator +(TokenUsage left, TokenUsage right) => new(
        left.Input + right.Input,
        left.Output + right.Output,
        left.CacheCreation + right.CacheCreation,
        left.CacheRead + right.CacheRead);
}

/// <summary>
/// Metadata snapshot of one live agent session. Never contains prompt, response or command text.
/// </summary>
/// <param name="Agent">Which agent runs the session.</param>
/// <param name="SessionId">Agent-specific session identifier.</param>
/// <param name="Title">Session name, or the project folder name when the session is unnamed.</param>
/// <param name="ProjectPath">Working directory of the session.</param>
/// <param name="State">Current state.</param>
/// <param name="CurrentAction">Name of the tool in progress, if any (for example <c>Bash</c>).</param>
/// <param name="Model">Model identifier of the latest response, if known.</param>
/// <param name="Tokens">Tokens used by the session so far.</param>
/// <param name="StartedAt">When the session started.</param>
/// <param name="UpdatedAt">When the session last changed state.</param>
public sealed record AgentSession(
    AgentKind Agent,
    string SessionId,
    string Title,
    string ProjectPath,
    AgentSessionState State,
    string? CurrentAction,
    string? Model,
    TokenUsage Tokens,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Reads live sessions of one agent from local data. Implementations must be read-only and tolerate format drift.
/// </summary>
public interface IAgentProvider
{
    /// <summary>Gets the agent this provider observes.</summary>
    AgentKind Agent { get; }

    /// <summary>
    /// Returns the agent's currently live sessions. Returns an empty list when the agent is not installed.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<AgentSession>> GetSessionsAsync(CancellationToken ct);
}
