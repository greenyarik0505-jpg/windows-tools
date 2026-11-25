using Windows.Win32;
using Windows.Win32.Foundation;

namespace WindowsTools.Interop;

/// <summary>
/// Enforces single-instance execution across the current user session and signals the active instance on duplicate launches.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    internal const string ActivateMessageName = "WindowsTools.ActivateInstance";

    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, bool isPrimaryInstance)
    {
        _mutex = mutex;
        IsPrimaryInstance = isPrimaryInstance;
    }

    /// <summary>
    /// Gets a value indicating whether the current process owns the single-instance lock.
    /// </summary>
    public bool IsPrimaryInstance { get; }

    /// <summary>
    /// Attempts to acquire the single-instance mutex.
    /// </summary>
    /// <param name="mutexName">Unique mutex name.</param>
    public static SingleInstanceGuard Acquire(string mutexName = @"Local\WindowsTools.SingleInstance")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        return new SingleInstanceGuard(mutex, createdNew);
    }

    /// <summary>
    /// Broadcasts an activation message to the already running primary instance.
    /// </summary>
    public static void NotifyPrimaryInstance()
    {
        var messageId = PInvoke.RegisterWindowMessage(ActivateMessageName);
        if (messageId != 0)
        {
            PInvoke.PostMessage(new HWND(0xFFFF), messageId, 0, 0);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (IsPrimaryInstance)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Ignore if already released on shutdown.
            }
        }

        _mutex.Dispose();
    }
}
