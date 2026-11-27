using System.Globalization;
using System.Resources;

namespace WindowsTools.Modules.Agents;

/// <summary>
/// Localized text for agent activities. This module has no WinUI dependency, so it uses .NET <c>.resx</c>
/// resources (<c>Strings.resx</c>, <c>Strings.uk.resx</c>) instead of <c>.resw</c>.
/// </summary>
internal static class AgentStrings
{
    private static readonly ResourceManager _resources = new("WindowsTools.Modules.Agents.Strings", typeof(AgentStrings).Assembly);

    public static string State(AgentSessionState state) => state switch
    {
        AgentSessionState.Working => Get("State_Working", "Working"),
        AgentSessionState.WaitingForUser => Get("State_Waiting", "Needs you"),
        _ => Get("State_Idle", "Idle"),
    };

    // The template depends on the UI culture, so it cannot be cached as a CompositeFormat; a plain
    // placeholder replacement also cannot throw on a malformed translation.
    public static string Tokens(string formattedCount) =>
        Get("Metric_Tokens", "{0} tokens").Replace("{0}", formattedCount, StringComparison.Ordinal);

    private static string Get(string key, string fallback)
    {
        try
        {
            return _resources.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
        }
        catch (MissingManifestResourceException)
        {
            return fallback;
        }
    }
}
