using Microsoft.Windows.ApplicationModel.Resources;

namespace WindowsTools.Modules.Island;

internal static class IslandStrings
{
    private static readonly Lazy<ResourceLoader?> _loader = new(CreateLoader);

    public static string Get(string key, string fallback)
    {
        try
        {
            var value = _loader.Value?.GetString(key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static ResourceLoader? CreateLoader()
    {
        try
        {
            return new ResourceLoader(
                ResourceLoader.GetDefaultResourceFilePath(),
                "WindowsTools.Modules.Island/Resources");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
