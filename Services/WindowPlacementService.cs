#if WINDOWS
using System.Runtime.InteropServices;

namespace snipv.Services;

/// <summary>
/// Remembers the window's position and size across restarts.
///
/// It persists the Win32 "restored" rectangle (<c>GetWindowPlacement</c>), which
/// stays meaningful while the window is minimized or hidden in the tray - the
/// live window rect does not, because Windows parks minimized windows at
/// -32000,-32000. The geometry is only reapplied when it still lands on a
/// connected monitor, so a position saved on a since-unplugged external screen
/// cannot leave the window unreachable.
/// </summary>
public class WindowPlacementService
{
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_EXITSIZEMOVE = 0x0232;

    private const uint MONITOR_DEFAULTTONULL = 0;
    private const uint SPI_GETWORKAREA = 0x0030;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    // Smaller than this is not a window the user positioned - a minimized one
    // reports a ~160x28 rect - and restoring it would produce an unusable sliver.
    private const int MinWidth = 200;
    private const int MinHeight = 150;

    // Used when a window has to be pulled back onto the desktop and its own size
    // is unusable.
    private const int FallbackWidth = 1000;
    private const int FallbackHeight = 700;

    private const string XKey = "window.x";
    private const string YKey = "window.y";
    private const string WidthKey = "window.width";
    private const string HeightKey = "window.height";

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int Length;
        public int Flags;
        public uint ShowCmd;
        public POINT MinPosition;
        public POINT MaxPosition;
        public RECT NormalPosition;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out RECT pvParam, uint fWinIni);

    private readonly NativeWindowHook _windowHook;
    private bool _initialized;

    public WindowPlacementService(NativeWindowHook windowHook)
    {
        _windowHook = windowHook;
    }

    public void Initialize(IntPtr windowHandle)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _windowHook.Attach(windowHandle);
        _windowHook.AddMessageHandler(OnMessageReceived);
        Restore(windowHandle);
    }

    private bool OnMessageReceived(uint msg, IntPtr wParam, IntPtr lParam)
    {
        // WM_EXITSIZEMOVE ends a move or resize; WM_CLOSE covers both hiding to
        // tray and a real exit. Never reported as handled - the window and the
        // other services still need these messages.
        if (msg is WM_EXITSIZEMOVE or WM_CLOSE)
        {
            Save();
        }

        return false;
    }

    private void Save()
    {
        var placement = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(_windowHook.WindowHandle, ref placement))
        {
            return;
        }

        var rect = placement.NormalPosition;
        Preferences.Set(XKey, (double)rect.Left);
        Preferences.Set(YKey, (double)rect.Top);
        Preferences.Set(WidthKey, (double)rect.Width);
        Preferences.Set(HeightKey, (double)rect.Height);
    }

    private static void Restore(IntPtr windowHandle)
    {
        var x = Preferences.Get(XKey, double.NaN);
        var y = Preferences.Get(YKey, double.NaN);
        var width = Preferences.Get(WidthKey, double.NaN);
        var height = Preferences.Get(HeightKey, double.NaN);
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(width) || double.IsNaN(height))
        {
            return;
        }

        if (width < MinWidth || height < MinHeight)
        {
            return;
        }

        var rect = new RECT
        {
            Left = (int)x,
            Top = (int)y,
            Right = (int)(x + width),
            Bottom = (int)(y + height),
        };

        // Null means the rectangle overlaps no connected monitor - the screen it
        // was saved on is gone, so the default position is the safer choice.
        if (MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL) == IntPtr.Zero)
        {
            return;
        }

        var placement = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(windowHandle, ref placement))
        {
            return;
        }

        // Only the restored rectangle changes; leaving ShowCmd alone keeps the
        // window hidden when the app was started with --tray.
        placement.NormalPosition = rect;
        SetWindowPlacement(windowHandle, ref placement);
    }

    /// <summary>
    /// Moves the window back onto the primary monitor if it currently sits
    /// entirely outside the connected ones - the state a window ends up in when
    /// the monitor it lived on is unplugged while the app waits in the tray.
    /// </summary>
    public static void EnsureOnScreen(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var rect)
            || MonitorFromRect(ref rect, MONITOR_DEFAULTTONULL) != IntPtr.Zero)
        {
            return;
        }

        if (!SystemParametersInfo(SPI_GETWORKAREA, 0, out var work, 0))
        {
            return;
        }

        var width = Math.Min(rect.Width >= MinWidth ? rect.Width : FallbackWidth, work.Width);
        var height = Math.Min(rect.Height >= MinHeight ? rect.Height : FallbackHeight, work.Height);
        var left = work.Left + ((work.Width - width) / 2);
        var top = work.Top + ((work.Height - height) / 2);

        SetWindowPos(windowHandle, IntPtr.Zero, left, top, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
#endif
