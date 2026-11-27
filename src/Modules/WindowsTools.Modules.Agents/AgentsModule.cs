using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WindowsTools.Core.Activities;
using WindowsTools.Core.Modules;
using WindowsTools.Modules.Agents.ClaudeCode;

namespace WindowsTools.Modules.Agents;

/// <summary>
/// Observes local coding agents and publishes their live sessions as activities.
/// </summary>
public sealed class AgentsModule : IModule, IDisposable
{
    private readonly IActivityHub _activityHub;
    private readonly ILoggerFactory _loggerFactory;
    private AgentHub? _hub;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentsModule"/> class.
    /// </summary>
    /// <param name="activityHub">Hub the sessions are published to.</param>
    /// <param name="loggerFactory">Logger factory; logs never contain message content.</param>
    public AgentsModule(IActivityHub activityHub, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(activityHub);
        _activityHub = activityHub;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <inheritdoc />
    public string Id => AgentHub.ModuleId;

    /// <inheritdoc />
    public ModuleMetadata Metadata { get; } = new(
        "AI Agents",
        "Live sessions, tokens and state of local coding agents.",
        IconGlyph: "",
        EnabledByDefault: true);

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        IAgentProvider[] providers = [new ClaudeCodeProvider(_loggerFactory.CreateLogger<ClaudeCodeProvider>())];

        _hub = new AgentHub(providers, _activityHub, _loggerFactory.CreateLogger<AgentHub>());
        _hub.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct)
    {
        if (_hub is null)
        {
            return;
        }

        await _hub.StopAsync().ConfigureAwait(false);
        _hub = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Normally StopAsync has already run; this only guards against a skipped shutdown.
        _hub?.StopAsync().GetAwaiter().GetResult();
        _hub = null;
    }
}
