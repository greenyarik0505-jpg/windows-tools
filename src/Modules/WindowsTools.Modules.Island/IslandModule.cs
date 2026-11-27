using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using WindowsTools.Core.Activities;
using WindowsTools.Core.Modules;

namespace WindowsTools.Modules.Island;

/// <summary>
/// Shows live activities of all modules in an overlay at the top of the screen.
/// </summary>
public sealed class IslandModule : IModule
{
    private readonly IActivityHub _activityHub;
    private readonly DispatcherQueue _uiQueue;
    private IslandWindow? _window;

    /// <summary>
    /// Initializes a new instance of the <see cref="IslandModule"/> class.
    /// </summary>
    /// <param name="activityHub">Source of activities to display.</param>
    /// <param name="uiQueue">UI thread queue; module start and stop may be called from a background thread.</param>
    public IslandModule(IActivityHub activityHub, DispatcherQueue uiQueue)
    {
        ArgumentNullException.ThrowIfNull(activityHub);
        ArgumentNullException.ThrowIfNull(uiQueue);
        _activityHub = activityHub;
        _uiQueue = uiQueue;
    }

    /// <inheritdoc />
    public string Id => "island";

    /// <inheritdoc />
    public ModuleMetadata Metadata { get; } = new(
        "Island",
        "Live activities in a pill at the top of the screen.",
        IconGlyph: "",
        EnabledByDefault: true);

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct) => RunOnUiAsync(() =>
    {
        _window ??= new IslandWindow(_activityHub);
        _window.ShowWithoutActivation();
    });

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct) => RunOnUiAsync(() =>
    {
        _window?.Close();
        _window = null;
    });

    private Task RunOnUiAsync(Action action)
    {
        if (_uiQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = _uiQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        if (!queued)
        {
            completion.SetException(new InvalidOperationException("The UI dispatcher queue is shutting down."));
        }

        return completion.Task;
    }
}
