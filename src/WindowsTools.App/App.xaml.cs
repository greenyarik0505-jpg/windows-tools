using Microsoft.UI.Xaml;

namespace WindowsTools.App;

/// <summary>
/// Application entry point for WindowsTools.
/// </summary>
public partial class App : Application
{
    private Window? _mainWindow;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }
}
