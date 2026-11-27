using Xunit;

namespace WindowsTools.Interop.Tests;

/// <summary>
/// Verifies managed wrappers for process APIs.
/// </summary>
public sealed class ProcessInteropTests
{
    /// <summary>
    /// Ensures the generated Win32 call returns the identifier of this process.
    /// </summary>
    [Fact]
    public void GetCurrentProcessIdReturnsCurrentProcessId()
    {
        Assert.Equal((uint)Environment.ProcessId, ProcessInterop.GetCurrentProcessId());
    }
}
