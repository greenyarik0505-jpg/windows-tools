using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsTools.Core.Activities;

namespace WindowsTools.Modules.Agents;

/// <summary>
/// Polls every agent provider and publishes one activity per live session to the <see cref="IActivityHub"/>.
/// A failing provider keeps its last known sessions and never stops the others.
/// </summary>
public sealed partial class AgentHub : IAsyncDisposable
{
    /// <summary>Module identifier used as the activity owner.</summary>
    public const string ModuleId = "agents";

    // Segoe Fluent Icons "Robot".
    private const string _agentGlyph = "";

    private readonly IReadOnlyList<IAgentProvider> _providers;
    private readonly IActivityHub _activityHub;
    private readonly ILogger<AgentHub> _logger;
    private readonly TimeSpan _interval;
    private readonly Dictionary<AgentKind, IReadOnlyList<AgentSession>> _lastKnown = [];
    private readonly Dictionary<string, ActivityDescriptor> _published = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentHub"/> class.
    /// </summary>
    public AgentHub(IEnumerable<IAgentProvider> providers, IActivityHub activityHub, ILogger<AgentHub> logger, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(activityHub);
        ArgumentNullException.ThrowIfNull(logger);

        _providers = providers.ToList();
        _activityHub = activityHub;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Gets the latest merged sessions from all providers.</summary>
    public IReadOnlyList<AgentSession> Sessions { get; private set; } = [];

    /// <summary>Starts background polling.</summary>
    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    /// <summary>Stops polling and removes every activity this hub published.</summary>
    public async Task StopAsync()
    {
        if (_cts is null || _loop is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
        _lastKnown.Clear();
        Sessions = [];
        Publish([]);
    }

    /// <summary>Runs one poll of every provider and publishes the result.</summary>
    public async Task PollOnceAsync(CancellationToken ct)
    {
        foreach (var provider in _providers)
        {
            try
            {
                _lastKnown[provider.Agent] = await provider.GetSessionsAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogProviderFailed(_logger, provider.Agent, ex);
            }
        }

        Sessions = _lastKnown.Values.SelectMany(s => s).ToList();
        Publish(Sessions.Select(ToActivity).ToList());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Maps a session to the shared activity contract. Text is localized here because the hub expects ready-to-show strings.</summary>
    internal static ActivityDescriptor ToActivity(AgentSession session)
    {
        var parts = new List<string> { AgentDisplayName(session.Agent), AgentStrings.State(session.State) };
        if (!string.IsNullOrEmpty(session.CurrentAction))
        {
            parts.Add(session.CurrentAction);
        }

        if (session.Tokens.Total > 0)
        {
            parts.Add(AgentStrings.Tokens(FormatCount(session.Tokens.Total)));
        }

        if (!string.IsNullOrEmpty(session.Model))
        {
            parts.Add(ShortModel(session.Model));
        }

        return new ActivityDescriptor(
            Id: $"{ModuleId}:{session.Agent}:{session.SessionId}",
            ModuleId: ModuleId,
            Title: session.Title,
            Subtitle: string.Join(" · ", parts),
            IconGlyph: _agentGlyph,
            Priority: session.State switch
            {
                AgentSessionState.WaitingForUser => ActivityPriority.Critical,
                AgentSessionState.Working => ActivityPriority.High,
                _ => ActivityPriority.Low,
            },
            State: session.State switch
            {
                AgentSessionState.WaitingForUser => ActivityState.WaitingForUser,

                // The hub has no idle state; an idle session has finished its last turn.
                AgentSessionState.Idle => ActivityState.Completed,
                _ => ActivityState.Active,
            });
    }

    /// <summary>Formats a counter compactly with invariant digits: 950, 12.3k, 4.1M.</summary>
    internal static string FormatCount(long value) => value switch
    {
        >= 1_000_000 => (value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    internal static string ShortModel(string model) =>
        model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ? model["claude-".Length..] : model;

    private static string AgentDisplayName(AgentKind agent) => agent switch
    {
        AgentKind.ClaudeCode => "Claude Code",
        AgentKind.Codex => "Codex",
        AgentKind.OpenCode => "OpenCode",
        _ => agent.ToString(),
    };

    // Sends only real changes so the Island does not re-render on every poll.
    private void Publish(IReadOnlyList<ActivityDescriptor> current)
    {
        var currentIds = new HashSet<string>(current.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var gone in _published.Keys.Where(id => !currentIds.Contains(id)).ToList())
        {
            _activityHub.Remove(gone);
            _published.Remove(gone);
        }

        foreach (var activity in current)
        {
            if (_published.TryGetValue(activity.Id, out var previous) && previous == activity)
            {
                continue;
            }

            _activityHub.Update(activity);
            _published[activity.Id] = activity;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            await PollOnceAsync(ct).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Agent provider {Agent} failed; keeping its last known sessions.")]
    private static partial void LogProviderFailed(ILogger logger, AgentKind agent, Exception exception);
}
