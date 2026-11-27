using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace WindowsTools.Interop;

/// <summary>
/// Owns a Win32 kernel-object handle and releases it with <c>CloseHandle</c>.
/// </summary>
public sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly Func<IntPtr, bool> _release;

    private SafeKernelHandle(IntPtr handle, Func<IntPtr, bool> release)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
        _release = release;
    }

    /// <summary>
    /// Takes ownership of a valid kernel-object handle.
    /// </summary>
    /// <param name="handle">A handle returned by an API that specifies <c>CloseHandle</c> as its release function.</param>
    /// <returns>An owning safe handle.</returns>
    /// <exception cref="ArgumentException">The handle is null or invalid.</exception>
    public static SafeKernelHandle FromOwnedHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            throw new ArgumentException("The native handle must be valid.", nameof(handle));
        }

        return new SafeKernelHandle(handle, CloseNativeHandle);
    }

    internal static SafeKernelHandle CreateForTesting(IntPtr handle, Func<IntPtr, bool> release) => new(handle, release);

    /// <inheritdoc />
    protected override bool ReleaseHandle() => _release(handle);

    private static bool CloseNativeHandle(IntPtr handle) => PInvoke.CloseHandle(new HANDLE(handle)) != 0;
}
