using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using WindowsTools.Core.Activities;
using WindowsTools.Core.Modules;

namespace WindowsTools.Modules.Island;

/// <summary>
/// Optional top-screen Island overlay module displaying prioritized activities from other modules.
/// </summary>
public sealed class IslandModule : IModule
{
    private readonly IActivityHub _activityHub;
    private readonly DispatcherQueue? _dispatcherQueue;

    private IslandViewModel? _viewModel;
    private IslandOverlayWindow? _overlayWindow;

    /// <summary>
    /// Initializes a new instance of the <see cref="IslandModule"/> class.
    /// </summary>
    /// <param name="activityHub">Shared activity hub.</param>
    /// <param name="dispatcherQueue">Optional UI dispatcher queue; pass <c>null</c> in headless unit tests.</param>
    public IslandModule(IActivityHub activityHub, DispatcherQueue? dispatcherQueue = null)
    {
        ArgumentNullException.ThrowIfNull(activityHub);
        _activityHub = activityHub;
        _dispatcherQueue = dispatcherQueue;
    }

    /// <inheritdoc />
    public string Id => "island";

    /// <inheritdoc />
    public ModuleMetadata Metadata => new(
        IslandStrings.Get("Module_Name", "Island"),
        IslandStrings.Get("Module_Description", "Top-screen activity overlay pill for active modules and AI agents."),
        IconGlyph: "\uE8A7",
        IsExperimental: false,
        RequiresAdmin: false,
        EnabledByDefault: true);

    /// <summary>
    /// Gets the active view model while the module is running, or <c>null</c> when stopped.
    /// </summary>
    public IslandViewModel? CurrentViewModel => _viewModel;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(this);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _viewModel ??= new IslandViewModel(_activityHub);

        if (_dispatcherQueue is not null)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _overlayWindow ??= new IslandOverlayWindow(_viewModel);
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
            {
                tcs.TrySetCanceled(ct);
            }

            return tcs.Task;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_overlayWindow is not null && _dispatcherQueue is not null)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _overlayWindow?.Shutdown();
                    _overlayWindow = null;
                    _viewModel?.Dispose();
                    _viewModel = null;
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
            {
                _viewModel?.Dispose();
                _viewModel = null;
                tcs.TrySetResult();
            }

            return tcs.Task;
        }

        _viewModel?.Dispose();
        _viewModel = null;
        return Task.CompletedTask;
    }
}
