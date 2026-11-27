using WindowsTools.Interop;

namespace WindowsTools.Interop.Tests;

public class ProcessInteropTests
{
    [Fact]
    public void GetCurrentProcessId_ReturnsCurrentProcessId()
    {
        Assert.Equal((uint)Environment.ProcessId, ProcessInterop.GetCurrentProcessId());
    }
}
