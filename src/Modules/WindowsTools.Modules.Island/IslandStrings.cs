using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace WindowsTools.Modules.Island;

/// <summary>
/// Loads Island strings from this library's <c>Strings/*/Resources.resw</c>, merged into the app's resources.pri.
/// </summary>
internal static class IslandStrings
{
    private static readonly Lazy<ResourceMap?> _map = new(CreateMap);

    public static string Get(string key, string fallback)
    {
        try
        {
            var value = _map.Value?.TryGetValue(key)?.ValueAsString;
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public static string Format(string key, string fallback, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key, fallback), args);

    private static ResourceMap? CreateMap()
    {
        try
        {
            return new ResourceManager().MainResourceMap.TryGetSubtree("WindowsTools.Modules.Island/Resources");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
