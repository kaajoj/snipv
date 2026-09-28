using System.Runtime.InteropServices;

namespace snipv.Services;

/// <summary>
/// Subclasses the app's top-level window once and lets several services observe
/// its Win32 messages (global hotkeys, the tray icon callback, close-to-tray).
/// A handler returns <c>true</c> to mark a message handled and suppress the
/// window's default processing for it.
/// </summary>
public class NativeWindowHook
{
    private const int GWLP_WNDPROC = -4;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private WndProcDelegate? _wndProcHook; // held in a field so the GC does not collect the delegate passed to native code
    private IntPtr _originalWndProc;

    // A plain list instead of an event: WndProc runs for every window message,
    // and Delegate.GetInvocationList() would allocate an array on each of them.
    private readonly List<Func<uint, IntPtr, IntPtr, bool>> _messageHandlers = [];

    public IntPtr WindowHandle { get; private set; }
    public bool IsAttached => WindowHandle != IntPtr.Zero;

    /// <summary>Registers a handler called for every window message. It returns true to suppress default processing.</summary>
    public void AddMessageHandler(Func<uint, IntPtr, IntPtr, bool> handler) => _messageHandlers.Add(handler);

    public void Attach(IntPtr windowHandle)
    {
        if (IsAttached)
        {
            return;
        }

        WindowHandle = windowHandle;
        _wndProcHook = WndProc;
        _originalWndProc = SetWindowLongPtrW(windowHandle, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcHook));
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var handled = false;

        for (var i = 0; i < _messageHandlers.Count; i++)
        {
            handled |= _messageHandlers[i](msg, wParam, lParam);
        }

        return handled ? IntPtr.Zero : CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }
}
