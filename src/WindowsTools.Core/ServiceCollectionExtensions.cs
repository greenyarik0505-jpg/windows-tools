using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WindowsTools.Core.Activities;
using WindowsTools.Core.Logging;
using WindowsTools.Core.Modules;
using WindowsTools.Core.Settings;
using WindowsTools.Core.Tray;

namespace WindowsTools.Core;

/// <summary>
/// Dependency injection registration helpers for WindowsTools Core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers WindowsTools Core services, rolling file logging, settings store, and modules.
    /// </summary>
    /// <param name="services">Target service collection.</param>
    /// <param name="modules">Registered modules.</param>
    /// <param name="appDataDirectory">Optional override for the application data root directory.</param>
    public static IServiceCollection AddWindowsToolsCore(
        this IServiceCollection services,
        IEnumerable<IModule> modules,
        string? appDataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modules);

        var rootDir = string.IsNullOrWhiteSpace(appDataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowsTools")
            : appDataDirectory;

        var logDir = Path.Combine(rootDir, "logs");
        var settingsDir = Path.Combine(rootDir, "settings");

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.Services.AddSingleton<ILoggerProvider>(_ =>
                new RollingFileLoggerProvider(new RollingFileLoggerOptions
                {
                    LogDirectory = logDir
                }));
        });

        services.TryAddSingleton<ISettingsStore>(sp =>
            new FileSettingsStore(settingsDir, sp.GetRequiredService<ILogger<FileSettingsStore>>()));
        services.TryAddSingleton<ITrayHost, TrayHost>();
        services.TryAddSingleton<IActivityHub, ActivityHub>();

        var moduleList = modules.ToList();
        foreach (var module in moduleList)
        {
            services.AddSingleton(module);
            module.ConfigureServices(services);
        }

        services.TryAddSingleton<IModuleManager>(sp =>
            new ModuleManager(
                sp.GetServices<IModule>(),
                sp.GetRequiredService<ISettingsStore>(),
                sp.GetRequiredService<ILogger<ModuleManager>>()));

        return services;
    }
}
