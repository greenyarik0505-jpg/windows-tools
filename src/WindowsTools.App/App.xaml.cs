using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WindowsTools.Core;
using WindowsTools.Core.Activities;
using WindowsTools.Core.Modules;
using WindowsTools.Core.Tray;
using WindowsTools.Interop;
using WindowsTools.Modules.Agents;
using WindowsTools.Modules.Island;
using WinRT.Interop;

namespace WindowsTools.App;

/// <summary>
/// Application entry point and composition root for WindowsTools.
/// </summary>
public partial class App : Application, IDisposable
{
    private readonly ServiceProvider _services;

    private SingleInstanceGuard? _singleInstanceGuard;
    private TrayIconHost? _trayIconHost;
    private TrayFlyoutWindow? _trayFlyoutWindow;
    private MainWindow? _settingsWindow;
    private bool _isExiting;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class and builds the DI container.
    /// </summary>
    public App()
    {
        InitializeComponent();

        var collection = new ServiceCollection();
        collection.AddWindowsToolsCore([]);
        collection.AddSingleton<IModule>(sp =>
            new IslandModule(
                sp.GetRequiredService<IActivityHub>(),
                DispatcherQueue.GetForCurrentThread()));
        collection.AddSingleton<IModule>(sp =>
            new AgentsModule(
                sp.GetRequiredService<IActivityHub>(),
                sp.GetRequiredService<ILoggerFactory>()));
        _services = collection.BuildServiceProvider();
    }

    /// <inheritdoc />
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceGuard = SingleInstanceGuard.Acquire();
        if (!_singleInstanceGuard.IsPrimaryInstance)
        {
            SingleInstanceGuard.NotifyPrimaryInstance();
            _singleInstanceGuard.Dispose();
            _singleInstanceGuard = null;
            await _services.DisposeAsync();
            Exit();
            return;
        }

        var logger = _services.GetRequiredService<ILogger<App>>();
        LogAppLaunching(logger);

        var moduleManager = _services.GetRequiredService<IModuleManager>();
        await moduleManager.InitializeAsync();

        var trayHost = _services.GetRequiredService<ITrayHost>();
        trayHost.SettingsRequested += OnSettingsRequested;

        _trayFlyoutWindow = new TrayFlyoutWindow(trayHost);
        _trayIconHost = new TrayIconHost(
            AppStrings.Get("TrayTooltip", "WindowsTools"),
            AppStrings.Get("TrayMenu_Settings", "Settings"),
            AppStrings.Get("TrayMenu_Exit", "Exit"));

        _trayIconHost.PrimaryClicked += OnTrayPrimaryClicked;
        _trayIconHost.SecondInstanceActivated += OnSecondInstanceActivated;
        _trayIconHost.SettingsClicked += OnTraySettingsClicked;
        _trayIconHost.ExitClicked += OnTrayExitClicked;
    }

    private void OnTrayPrimaryClicked(object? sender, (int X, int Y) anchor)
    {
        _trayFlyoutWindow?.DispatcherQueue.TryEnqueue(() => _trayFlyoutWindow.ToggleAt(anchor.X, anchor.Y));
    }

    private void OnSecondInstanceActivated(object? sender, (int X, int Y) anchor)
    {
        _trayFlyoutWindow?.DispatcherQueue.TryEnqueue(() => _trayFlyoutWindow.ShowAt(anchor.X, anchor.Y));
    }

    private void OnTraySettingsClicked(object? sender, EventArgs e)
    {
        _trayFlyoutWindow?.DispatcherQueue.TryEnqueue(OpenOrActivateSettingsWindow);
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        _trayFlyoutWindow?.DispatcherQueue.TryEnqueue(OpenOrActivateSettingsWindow);
    }

    private async void OnTrayExitClicked(object? sender, EventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        await ShutdownApplicationAsync();
    }

    private void OpenOrActivateSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new MainWindow();
            _settingsWindow.Closed += OnSettingsWindowClosed;
        }

        _settingsWindow.Activate();
        var hwnd = WindowNative.GetWindowHandle(_settingsWindow);
        WindowPlacementInterop.BringToForeground(hwnd);
    }

    private void OnSettingsWindowClosed(object sender, WindowEventArgs args)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            _settingsWindow = null;
        }
    }

    private async Task ShutdownApplicationAsync()
    {
        var logger = _services.GetRequiredService<ILogger<App>>();
        LogAppShuttingDown(logger);

        if (_trayIconHost is not null)
        {
            _trayIconHost.PrimaryClicked -= OnTrayPrimaryClicked;
            _trayIconHost.SecondInstanceActivated -= OnSecondInstanceActivated;
            _trayIconHost.SettingsClicked -= OnTraySettingsClicked;
            _trayIconHost.ExitClicked -= OnTrayExitClicked;
            _trayIconHost.Dispose();
            _trayIconHost = null;
        }

        var trayHost = _services.GetService<ITrayHost>();
        if (trayHost is not null)
        {
            trayHost.SettingsRequested -= OnSettingsRequested;
        }

        try
        {
            var moduleManager = _services.GetRequiredService<IModuleManager>();
            await moduleManager.ShutdownAsync();
        }
        finally
        {
            _settingsWindow?.Close();
            _trayFlyoutWindow?.Close();
            _singleInstanceGuard?.Dispose();
            _singleInstanceGuard = null;
            await _services.DisposeAsync();
            Exit();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "WindowsTools application launching into tray.")]
    private static partial void LogAppLaunching(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "WindowsTools application shutting down.")]
    private static partial void LogAppShuttingDown(ILogger logger);

    /// <inheritdoc />
    public void Dispose()
    {
        _trayIconHost?.Dispose();
        _trayIconHost = null;
        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
        _services.Dispose();
        GC.SuppressFinalize(this);
    }
}
