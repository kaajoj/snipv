#if WINDOWS
using System.Runtime.InteropServices;

namespace snipv.Services;

/// <summary>
/// Adds a Windows notification-area (tray) icon and turns the window's close
/// button into "hide to tray". Left-click restores the window; right-click
/// opens a Show/Exit menu. Shares the app window's message hook with
/// <see cref="HotkeyService"/> via <see cref="NativeWindowHook"/>.
/// </summary>
public class TrayIconService
{
    private const int WM_APP = 0x8000;
    private const uint TrayCallbackMessage = WM_APP + 1;
    private const int TrayIconId = 1;

    private const uint WM_CLOSE = 0x0010;
    private const uint WM_WINDOWPOSCHANGING = 0x0046;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;

    private const uint SWP_SHOWWINDOW = 0x0040;

    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIM_ADD = 0x00;
    private const uint NIM_DELETE = 0x02;

    private const int SW_HIDE = 0;
    private const int SW_RESTORE = 9;

    private const uint MF_STRING = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint CmdShow = 1;
    private const uint CmdExit = 2;

    private const int IDI_APPLICATION = 32512;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    // System-wide message id shared by all snipv processes; a second instance
    // broadcasts it so the first one can bring its window back.
    private static readonly uint ActivateMessage = RegisterWindowMessage("SNIPV_ACTIVATE");

    // Broadcast by Windows to every top-level window when Explorer (and with it
    // the taskbar) restarts; all tray icons are gone then and must be re-added.
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private static readonly IntPtr HWND_BROADCAST = 0xFFFF;

    /// <summary>Asks an already-running snipv instance to show its window.</summary>
    public static void ActivateRunningInstance() =>
        PostMessage(HWND_BROADCAST, ActivateMessage, IntPtr.Zero, IntPtr.Zero);

    private readonly NativeWindowHook _windowHook;
    private bool _initialized;
    private bool _iconAdded;
    private bool _exiting;
    private bool _startingHidden;

    public TrayIconService(NativeWindowHook windowHook)
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
        AddIcon();
    }

    private bool OnMessageReceived(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_startingHidden && msg == WM_WINDOWPOSCHANGING)
        {
            // Startup shows the window on its own, after and regardless of any
            // hide, and it does so without activating it when it comes up behind
            // another application - so there is no event to hide it from. Take
            // the show out of the request instead, until something asks for the
            // window on purpose.
            var position = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((position.Flags & SWP_SHOWWINDOW) != 0)
            {
                position.Flags &= ~SWP_SHOWWINDOW;
                Marshal.StructureToPtr(position, lParam, fDeleteOld: false);
            }
        }

        if (msg == WM_CLOSE && !_exiting)
        {
            HideToTray(); // swallow the close: hide to tray instead of exiting
            return true;
        }

        if (msg == ActivateMessage)
        {
            RestoreWindow();
            return true;
        }

        if (msg == TaskbarCreatedMessage)
        {
            _iconAdded = false;
            AddIcon();
            return false;
        }

        if (msg == TrayCallbackMessage)
        {
            var mouseEvent = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseEvent == WM_LBUTTONUP)
            {
                RestoreWindow();
                return true;
            }

            if (mouseEvent == WM_RBUTTONUP)
            {
                ShowContextMenu();
                return true;
            }
        }

        return false;
    }

    private void AddIcon()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHook.WindowHandle,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = LoadAppIcon(),
            szTip = "snipv",
        };

        _iconAdded = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    public void RemoveIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _windowHook.WindowHandle,
            uID = TrayIconId,
        };

        Shell_NotifyIcon(NIM_DELETE, ref data);
        _iconAdded = false;
    }

    /// <summary>
    /// Keeps the window off screen from the start, for a launch that is meant to
    /// go straight to the tray. Undone by the first deliberate restore.
    /// </summary>
    public void StartHidden()
    {
        _startingHidden = true;
        HideToTray();
    }

    /// <summary>Hides the window, leaving the app running in the tray.</summary>
    public void HideToTray()
    {
        ShowWindow(_windowHook.WindowHandle, SW_HIDE);
        TrimWorkingSet();
    }

    /// <summary>
    /// Gives the UI's memory pages back to the OS while the window is not shown;
    /// they are paged back in on demand when it reopens. This is what Task
    /// Manager reflects as the app's memory use.
    /// </summary>
    public void TrimWorkingSet()
    {
        GC.Collect();
        SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
    }

    private void RestoreWindow()
    {
        _startingHidden = false; // this is the deliberate ask the suppression waits for
        ShowWindow(_windowHook.WindowHandle, SW_RESTORE);

        // The window may have been parked on a monitor that got unplugged while
        // the app sat in the tray; showing it there would look like nothing
        // happened.
        WindowPlacementService.EnsureOnScreen(_windowHook.WindowHandle);
        SetForegroundWindow(_windowHook.WindowHandle);
    }

    private void ShowContextMenu()
    {
        // Per Win32 docs the owning window must be foreground or the menu never closes.
        SetForegroundWindow(_windowHook.WindowHandle);
        GetCursorPos(out var cursor);

        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, CmdShow, "Show snipv");
        AppendMenu(menu, MF_STRING, CmdExit, "Exit");

        var command = (uint)TrackPopupMenuEx(
            menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, cursor.X, cursor.Y, _windowHook.WindowHandle, IntPtr.Zero);
        DestroyMenu(menu);

        switch (command)
        {
            case CmdShow:
                RestoreWindow();
                break;
            case CmdExit:
                Exit();
                break;
        }
    }

    private void Exit()
    {
        _exiting = true;
        RemoveIcon();
        // WM_CLOSE now passes through the hook and closes the window for real.
        PostMessage(_windowHook.WindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    private static IntPtr LoadAppIcon()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            var icon = ExtractIcon(GetModuleHandle(null), exePath, 0);
            if (icon != IntPtr.Zero)
            {
                return icon;
            }
        }

        return LoadIcon(IntPtr.Zero, IDI_APPLICATION);
    }
}
#endif
