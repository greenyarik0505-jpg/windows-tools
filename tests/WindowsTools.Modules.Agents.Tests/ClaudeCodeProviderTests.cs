using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsTools.Core.Activities;
using WindowsTools.Modules.Agents.ClaudeCode;
using Xunit;

namespace WindowsTools.Modules.Agents.Tests;

/// <summary>
/// Tests for reading Claude Code's session registry and transcripts. Fixtures are synthetic and contain no real conversation text.
/// </summary>
public sealed class ClaudeCodeProviderTests : IDisposable
{
    private const string _sessionId = "11111111-2222-3333-4444-555555555555";
    private const string _cwd = @"C:\work\demo-project";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "wt-agents-" + Guid.NewGuid().ToString("N"));

    /// <summary>Registry status maps to working or idle; unknown values are not guessed.</summary>
    [Theory]
    [InlineData("busy", AgentSessionState.Working)]
    [InlineData("idle", AgentSessionState.Idle)]
    [InlineData("something-new", AgentSessionState.Idle)]
    [InlineData(null, AgentSessionState.Idle)]
    public void MapStatusMapsKnownValues(string? status, AgentSessionState expected)
    {
        Assert.Equal(expected, ClaudeSessionRegistry.MapStatus(status));
    }

    /// <summary>Invalid or incomplete registry files are ignored instead of throwing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"pid\": 1}")]
    public void ParseReturnsNullForUnusableInput(string json)
    {
        Assert.Null(ClaudeSessionRegistry.Parse(json));
    }

    /// <summary>A response split over several lines is counted once.</summary>
    [Fact]
    public void TranscriptCountsEachResponseOnce()
    {
        var reader = new ClaudeTranscriptReader("unused");
        var block = AssistantLine("msg_1", "req_1", "tool_use", "Bash");

        reader.ProcessLine(Encoding.UTF8.GetBytes(block));
        reader.ProcessLine(Encoding.UTF8.GetBytes(block));
        reader.ProcessLine(Encoding.UTF8.GetBytes(AssistantLine("msg_2", "req_2", "text", null)));

        Assert.Equal(new TokenUsage(20, 10, 6, 200), reader.Tokens);
        Assert.Equal("claude-opus-5-5", reader.Model);
    }

    /// <summary>A tool is pending until the next user record (its result) arrives.</summary>
    [Fact]
    public void TranscriptTracksPendingTool()
    {
        var reader = new ClaudeTranscriptReader("unused");

        reader.ProcessLine(Encoding.UTF8.GetBytes(AssistantLine("msg_1", "req_1", "tool_use", "Edit")));
        Assert.Equal("Edit", reader.PendingTool);

        reader.ProcessLine(Encoding.UTF8.GetBytes("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[]}}"));
        Assert.Null(reader.PendingTool);
    }

    /// <summary>Synthetic records and garbage lines do not affect counters.</summary>
    [Fact]
    public void TranscriptIgnoresSyntheticAndBrokenLines()
    {
        var reader = new ClaudeTranscriptReader("unused");

        reader.ProcessLine(Encoding.UTF8.GetBytes("{\"type\":\"assistant\",\"message\":{\"model\":\"<synthetic>\",\"usage\":{\"output_tokens\":99}}}"));
        reader.ProcessLine(Encoding.UTF8.GetBytes("{broken"));
        reader.ProcessLine(Encoding.UTF8.GetBytes("{\"type\":\"brand-new-record\",\"x\":1}"));

        Assert.Equal(default, reader.Tokens);
        Assert.Null(reader.Model);
    }

    /// <summary>A half-written last line is not consumed until it is complete.</summary>
    [Fact]
    public void TranscriptWaitsForCompleteLines()
    {
        var path = Path.Combine(_root, "t.jsonl");
        Directory.CreateDirectory(_root);
        var line = AssistantLine("msg_1", "req_1", "text", null);
        File.WriteAllText(path, line[..20]);

        var reader = new ClaudeTranscriptReader(path);
        reader.ReadNewLines();
        Assert.Equal(default, reader.Tokens);

        File.WriteAllText(path, line + "\n");
        reader.ReadNewLines();
        Assert.Equal(10, reader.Tokens.Input);
    }

    /// <summary>Only live local sessions are returned and enriched from their transcript.</summary>
    [Fact]
    public async Task ProviderReturnsLiveSessionsWithTranscriptData()
    {
        WriteRegistry(4242, _sessionId, "busy");
        WriteRegistry(5555, "dead-session", "busy");
        WriteRegistry(6666, "wsl-session", "busy", pidDomain: "linux:ubuntu");
        File.WriteAllText(Path.Combine(_root, "sessions", "4242.secret.key"), "must never be read");

        var projectDir = Path.Combine(_root, "projects", ClaudeTranscriptReader.ProjectFolderName(_cwd));
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(
            Path.Combine(projectDir, _sessionId + ".jsonl"),
            AssistantLine("msg_1", "req_1", "tool_use", "Bash") + "\n");

        var provider = new ClaudeCodeProvider(
            NullLogger<ClaudeCodeProvider>.Instance,
            _root,
            pid => pid is 4242 or 6666);

        var sessions = await provider.GetSessionsAsync(CancellationToken.None);

        var session = Assert.Single(sessions);
        Assert.Equal(_sessionId, session.SessionId);
        Assert.Equal("demo-project", session.Title);
        Assert.Equal(AgentSessionState.Working, session.State);
        Assert.Equal("Bash", session.CurrentAction);
        Assert.Equal("claude-opus-5-5", session.Model);
        Assert.Equal(10, session.Tokens.Input);
    }

    /// <summary>Without a Claude folder the provider returns nothing instead of failing.</summary>
    [Fact]
    public async Task ProviderReturnsEmptyWhenNotInstalled()
    {
        var provider = new ClaudeCodeProvider(NullLogger<ClaudeCodeProvider>.Instance, Path.Combine(_root, "missing"));
        Assert.Empty(await provider.GetSessionsAsync(CancellationToken.None));
    }

    /// <summary>A failing provider does not stop others and keeps its last known sessions.</summary>
    [Fact]
    public async Task HubIsolatesProviderFailures()
    {
        var hub = new ActivityHub();
        var good = new FakeProvider(AgentKind.Codex, [Session("a", AgentSessionState.Working)]);
        var flaky = new FakeProvider(AgentKind.ClaudeCode, [Session("b", AgentSessionState.Idle)]);
        await using var agentHub = new AgentHub([flaky, good], hub, NullLogger<AgentHub>.Instance);

        await agentHub.PollOnceAsync(CancellationToken.None);
        flaky.Fail = true;
        await agentHub.PollOnceAsync(CancellationToken.None);

        Assert.Equal(2, hub.Activities.Count);
        Assert.Equal(ActivityState.Active, hub.Activities[0].State);
        Assert.Equal(ActivityPriority.High, hub.Activities[0].Priority);
    }

    /// <summary>Sessions that disappear are removed from the hub; unchanged sessions are not re-published.</summary>
    [Fact]
    public async Task HubRemovesEndedSessionsAndSkipsUnchanged()
    {
        var hub = new ActivityHub();
        var changes = 0;
        hub.ActivitiesChanged += (_, _) => changes++;
        var provider = new FakeProvider(AgentKind.ClaudeCode, [Session("a", AgentSessionState.Working), Session("b", AgentSessionState.Idle)]);
        await using var agentHub = new AgentHub([provider], hub, NullLogger<AgentHub>.Instance);

        await agentHub.PollOnceAsync(CancellationToken.None);
        var afterFirstPoll = changes;
        await agentHub.PollOnceAsync(CancellationToken.None);
        Assert.Equal(afterFirstPoll, changes);

        provider.Sessions = [Session("a", AgentSessionState.Working)];
        await agentHub.PollOnceAsync(CancellationToken.None);
        Assert.Equal("agents:ClaudeCode:a", Assert.Single(hub.Activities).Id);

        await agentHub.StopAsync();
        agentHub.Start();
        await agentHub.StopAsync();
        Assert.Empty(hub.Activities);
    }

    /// <summary>States map to the shared contract: waiting is critical, idle is a completed turn.</summary>
    [Theory]
    [InlineData(AgentSessionState.WaitingForUser, ActivityState.WaitingForUser, ActivityPriority.Critical)]
    [InlineData(AgentSessionState.Working, ActivityState.Active, ActivityPriority.High)]
    [InlineData(AgentSessionState.Idle, ActivityState.Completed, ActivityPriority.Low)]
    public void ToActivityMapsStateAndPriority(AgentSessionState state, ActivityState expectedState, ActivityPriority expectedPriority)
    {
        var activity = AgentHub.ToActivity(Session("s", state) with { Tokens = new TokenUsage(1000, 234, 0, 0), Model = "claude-opus-5-5", CurrentAction = "Bash" });

        Assert.Equal(expectedState, activity.State);
        Assert.Equal(expectedPriority, activity.Priority);
        Assert.Equal(AgentHub.ModuleId, activity.ModuleId);
        Assert.Contains("Claude Code", activity.Subtitle, StringComparison.Ordinal);
        Assert.Contains("1.2k", activity.Subtitle, StringComparison.Ordinal);
        Assert.Contains("opus-5-5", activity.Subtitle, StringComparison.Ordinal);
    }

    /// <summary>Counters are formatted compactly with invariant digits.</summary>
    [Theory]
    [InlineData(950, "950")]
    [InlineData(12_300, "12.3k")]
    [InlineData(4_100_000, "4.1M")]
    public void FormatCountIsCompact(long value, string expected)
    {
        Assert.Equal(expected, AgentHub.FormatCount(value));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WriteRegistry(int pid, string sessionId, string status, string pidDomain = "win32:test")
    {
        var dir = Path.Combine(_root, "sessions");
        Directory.CreateDirectory(dir);
        var cwd = _cwd.Replace(@"\", @"\\", StringComparison.Ordinal);
        File.WriteAllText(
            Path.Combine(dir, $"{pid}.json"),
            $"{{\"pid\":{pid},\"sessionId\":\"{sessionId}\",\"cwd\":\"{cwd}\",\"status\":\"{status}\",\"pidDomain\":\"{pidDomain}\",\"startedAt\":1795000000000,\"statusUpdatedAt\":1795000005000,\"futureField\":true}}");
    }

    private static string AssistantLine(string messageId, string requestId, string blockType, string? toolName)
    {
        var block = blockType == "tool_use"
            ? $"{{\"type\":\"tool_use\",\"name\":\"{toolName}\",\"input\":{{}}}}"
            : "{\"type\":\"text\",\"text\":\"\"}";
        return $"{{\"type\":\"assistant\",\"requestId\":\"{requestId}\",\"message\":{{\"id\":\"{messageId}\",\"model\":\"claude-opus-5-5\",\"content\":[{block}],\"usage\":{{\"input_tokens\":10,\"output_tokens\":5,\"cache_creation_input_tokens\":3,\"cache_read_input_tokens\":100}}}}}}";
    }

    private static AgentSession Session(string id, AgentSessionState state) => new(
        AgentKind.ClaudeCode, id, id, @"C:\x", state, null, null, default, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private sealed class FakeProvider(AgentKind agent, IReadOnlyList<AgentSession> sessions) : IAgentProvider
    {
        public bool Fail { get; set; }

        public IReadOnlyList<AgentSession> Sessions { get; set; } = sessions;

        public AgentKind Agent { get; } = agent;

        public Task<IReadOnlyList<AgentSession>> GetSessionsAsync(CancellationToken ct) =>
            Fail ? throw new IOException("simulated") : Task.FromResult(Sessions);
    }
}
