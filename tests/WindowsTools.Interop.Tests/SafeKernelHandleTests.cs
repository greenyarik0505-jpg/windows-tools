using Xunit;

namespace WindowsTools.Interop.Tests;

/// <summary>
/// Tests ownership and invalid-handle behavior of <see cref="SafeKernelHandle"/>.
/// </summary>
public sealed class SafeKernelHandleTests
{
    /// <summary>Checks that disposal releases an owned handle once, even when called repeatedly.</summary>
    [Fact]
    public void DisposeClosesOwnedHandleExactlyOnce()
    {
        var releaseCount = 0;
        using var handle = SafeKernelHandle.CreateForTesting(new IntPtr(42), _ =>
        {
            releaseCount++;
            return true;
        });

        handle.Dispose();
        handle.Dispose();

        Assert.Equal(1, releaseCount);
        Assert.True(handle.IsClosed);
    }

    /// <summary>Checks that invalid Win32 handle values cannot be wrapped as owned handles.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FromOwnedHandleRejectsInvalidValues(int value)
    {
        Assert.Throws<ArgumentException>(() => SafeKernelHandle.FromOwnedHandle(new IntPtr(value)));
    }
}
