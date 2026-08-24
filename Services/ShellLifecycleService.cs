using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SmoothFolder.Services;

/// <summary>
/// Receives shell lifecycle broadcasts without creating a visible WPF window.
///
/// Explorer broadcasts the registered "TaskbarCreated" message after rebuilding
/// the shell/taskbar, including the common explorer.exe restart path. A hidden
/// top-level HWND is used because message-only windows do not receive broadcast
/// messages.
/// </summary>
public sealed class ShellLifecycleService : IDisposable
{
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    private readonly HwndSource _source;
    private readonly uint _taskbarCreatedMessage;
    private bool _disposed;

    public event EventHandler? ExplorerRestarted;

    public ShellLifecycleService()
    {
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        var parameters = new HwndSourceParameters("SmoothFolder.ShellLifecycle")
        {
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = 0,
            ExtendedWindowStyle = unchecked((int)(WsExToolWindow | WsExNoActivate))
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        CrashLogService.LogMessage(
            "Shell lifecycle listener started",
            $"TaskbarCreated message id=0x{_taskbarCreatedMessage:X}; " +
            $"listener HWND=0x{_source.Handle.ToInt64():X}.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if ((uint)msg == _taskbarCreatedMessage)
        {
            CrashLogService.LogMessage(
                "Explorer shell event",
                "Received TaskbarCreated. Explorer's desktop hierarchy will be rediscovered.");

            ExplorerRestarted?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);
}
