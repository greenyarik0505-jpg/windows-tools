using System.Text.Json;

namespace WindowsTools.Modules.Agents.ClaudeCode;

/// <summary>
/// One entry of Claude Code's live session registry (<c>~/.claude/sessions/&lt;pid&gt;.json</c>).
/// </summary>
internal sealed record ClaudeRegistryEntry(
    int Pid,
    string SessionId,
    string Cwd,
    string? Name,
    string? Status,
    string? PidDomain,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Parses the registry. The format is internal to Claude Code, so every field is optional and unknown fields are ignored.
/// </summary>
internal static class ClaudeSessionRegistry
{
    /// <summary>
    /// Parses one registry file body. Returns <c>null</c> when required fields are missing or the JSON is invalid.
    /// </summary>
    public static ClaudeRegistryEntry? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var pid = GetInt(root, "pid");
            var sessionId = GetString(root, "sessionId");
            var cwd = GetString(root, "cwd");
            if (pid is null || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(cwd))
            {
                return null;
            }

            var started = FromUnixMs(GetLong(root, "startedAt"));
            var updated = FromUnixMs(GetLong(root, "statusUpdatedAt") ?? GetLong(root, "updatedAt")) ?? started;

            return new ClaudeRegistryEntry(
                pid.Value,
                sessionId,
                cwd,
                GetString(root, "name"),
                GetString(root, "status"),
                GetString(root, "pidDomain"),
                started ?? DateTimeOffset.UnixEpoch,
                updated ?? DateTimeOffset.UnixEpoch);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps the registry <c>status</c> to a session state. Only <c>busy</c> and <c>idle</c> have been observed;
    /// anything else is treated as idle rather than guessed.
    /// </summary>
    public static AgentSessionState MapStatus(string? status) =>
        string.Equals(status, "busy", StringComparison.OrdinalIgnoreCase)
            ? AgentSessionState.Working
            : AgentSessionState.Idle;

    /// <summary>
    /// Returns <c>true</c> when the entry's PID belongs to this Windows host rather than, for example, a WSL distribution.
    /// </summary>
    public static bool IsLocalWindowsPid(ClaudeRegistryEntry entry) =>
        entry.PidDomain is null || entry.PidDomain.StartsWith("win32", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static long? GetLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;

    private static DateTimeOffset? FromUnixMs(long? ms) =>
        ms is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value) : null;
}
