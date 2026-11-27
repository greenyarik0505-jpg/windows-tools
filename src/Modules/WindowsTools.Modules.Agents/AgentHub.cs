using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsTools.Core.Activities;

namespace WindowsTools.Modules.Agents;

/// <summary>
/// Polls every agent provider and publishes live sessions to the <see cref="IActivityHub"/>.
/// A failing provider keeps its last known sessions and never stops the others.
/// </summary>
public sealed partial class AgentHub : IAsyncDisposable
{
    /// <summary>Module identifier used as the activity owner.</summary>
    public const string ModuleId = "agents";

    private readonly IReadOnlyList<IAgentProvider> _providers;
    private readonly IActivityHub _activityHub;
    private readonly ILogger<AgentHub> _logger;
    private readonly TimeSpan _interval;
    private readonly Dictionary<AgentKind, IReadOnlyList<AgentSession>> _lastKnown = [];

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

    /// <summary>Stops polling and removes all published activities.</summary>
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
        _activityHub.Clear(ModuleId);
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
        _activityHub.ReplaceAll(ModuleId, Sessions.Select(ToActivity).ToList());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    internal static Activity ToActivity(AgentSession session)
    {
        var metrics = new List<ActivityMetric>(2);
        if (session.Tokens.Total > 0)
        {
            metrics.Add(new ActivityMetric("tokens", FormatCount(session.Tokens.Total)));
        }

        if (!string.IsNullOrEmpty(session.Model))
        {
            metrics.Add(new ActivityMetric("model", ShortModel(session.Model)));
        }

        return new Activity(
            Id: $"{ModuleId}:{session.Agent}:{session.SessionId}",
            SourceModuleId: ModuleId,
            Category: AgentDisplayName(session.Agent),
            Title: session.Title,
            Detail: session.CurrentAction,
            State: session.State switch
            {
                AgentSessionState.Working => ActivityState.Working,
                AgentSessionState.WaitingForUser => ActivityState.WaitingForUser,
                _ => ActivityState.Idle,
            },
            Priority: session.State == AgentSessionState.Idle ? 10 : 20,
            StartedAt: session.StartedAt,
            UpdatedAt: session.UpdatedAt,
            Metrics: metrics);
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
