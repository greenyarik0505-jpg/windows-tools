using Xunit;

namespace WindowsTools.Core.Tests;

/// <summary>
/// Verifies the initial Core assembly project scaffold.
/// </summary>
public sealed class CoreScaffoldTests
{
    /// <summary>
    /// Verifies that the Core assembly loads and exposes its expected name.
    /// </summary>
    [Fact]
    public void CoreAssemblyShouldBeLoadable()
    {
        var assemblyName = typeof(CoreAssemblyMarker).Assembly.GetName().Name;
        Assert.Equal("WindowsTools.Core", assemblyName);
    }
}
