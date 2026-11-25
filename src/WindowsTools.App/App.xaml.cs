using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using WindowsTools.Core;
using WindowsTools.Core.Modules;

namespace WindowsTools.App;

/// <summary>
/// Application entry point and composition root for WindowsTools.
/// </summary>
public partial class App : Application
{
    private readonly ServiceProvider _services;
    private Window? _mainWindow;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class and builds the DI container.
    /// </summary>
    public App()
    {
        InitializeComponent();

        var collection = new ServiceCollection();
        collection.AddWindowsToolsCore(CreateModules());
        _services = collection.BuildServiceProvider();
    }

    /// <inheritdoc />
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var logger = _services.GetRequiredService<ILogger<App>>();
        LogAppLaunching(logger);

        var moduleManager = _services.GetRequiredService<IModuleManager>();
        await moduleManager.InitializeAsync();

        _mainWindow = new MainWindow();
        _mainWindow.Closed += OnMainWindowClosed;
        _mainWindow.Activate();
    }

    private static IReadOnlyList<IModule> CreateModules() => [];

    private async void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        var logger = _services.GetRequiredService<ILogger<App>>();
        LogAppShuttingDown(logger);

        try
        {
            var moduleManager = _services.GetRequiredService<IModuleManager>();
            await moduleManager.ShutdownAsync();
        }
        finally
        {
            await _services.DisposeAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "WindowsTools application launching.")]
    private static partial void LogAppLaunching(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "WindowsTools application shutting down.")]
    private static partial void LogAppShuttingDown(ILogger logger);
}
