using Microsoft.Win32.SafeHandles;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WindowsTools.Interop;

/// <summary>
/// Owns a low-level Windows hook and removes it when disposed.
/// </summary>
public sealed class WindowsHookSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Initializes an empty hook handle for interop marshalling.
    /// </summary>
    public WindowsHookSafeHandle()
        : base(ownsHandle: true)
    {
    }

    /// <summary>
    /// Takes ownership of an existing hook.
    /// </summary>
    /// <param name="handle">Native hook handle returned by a Win32 hook API.</param>
    public WindowsHookSafeHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle() => PInvoke.UnhookWindowsHookEx((HHOOK)handle);
}
