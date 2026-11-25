using Microsoft.UI.Xaml;

namespace WindowsTools.App;

/// <summary>
/// Settings placeholder window opened on demand from the tray.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        var title = AppStrings.Get("SettingsPlaceholder_Title", "WindowsTools Settings");
        Title = title;
        HeaderText.Text = title;
        BodyText.Text = AppStrings.Get("SettingsPlaceholder_Body", "Module settings will appear here once configured.");
    }
}
