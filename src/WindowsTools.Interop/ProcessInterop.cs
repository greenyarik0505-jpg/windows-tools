using Windows.Win32;

namespace WindowsTools.Interop;

/// <summary>
/// Provides managed wrappers around generated Win32 APIs.
/// </summary>
public static class ProcessInterop
{
    /// <summary>
    /// Gets the process identifier of the calling process.
    /// </summary>
    public static uint GetCurrentProcessId() => PInvoke.GetCurrentProcessId();
}
