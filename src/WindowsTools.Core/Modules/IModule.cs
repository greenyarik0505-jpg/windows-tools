using Microsoft.Extensions.DependencyInjection;

namespace WindowsTools.Core.Modules;

/// <summary>
/// Contract implemented by every independent feature module in WindowsTools.
/// </summary>
public interface IModule
{
    /// <summary>
    /// Gets the stable, lowercase module identifier used in settings and diagnostics.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets descriptive metadata for the module.
    /// </summary>
    ModuleMetadata Metadata { get; }

    /// <summary>
    /// Registers module services with the application dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to populate.</param>
    void ConfigureServices(IServiceCollection services);

    /// <summary>
    /// Starts the module and acquires runtime resources such as hooks, timers, or windows.
    /// </summary>
    /// <param name="ct">A token used to cancel module startup.</param>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Stops the module and releases all runtime resources so a disabled module consumes zero overhead.
    /// </summary>
    /// <param name="ct">A token used to cancel module shutdown.</param>
    Task StopAsync(CancellationToken ct);
}
