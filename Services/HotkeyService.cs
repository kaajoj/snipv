using System.Diagnostics;
using System.Runtime.InteropServices;

namespace snipv.Services;

/// <summary>
/// Registers snippets' shortcuts as global Win32 hotkeys and raises
/// <see cref="HotkeyPressed"/> when one arrives via WM_HOTKEY.
/// </summary>
public class HotkeyService
{
    private const uint WM_HOTKEY = 0x0312;

    // Without this, holding a shortcut down makes the keyboard's auto-repeat
    // deliver one WM_HOTKEY after another, and each of them pastes the snippet.
    private const uint MOD_NOREPEAT = 0x4000;

    private const int ProbeHotkeyId = 0xBFFF; // top of the valid RegisterHotKey id range, never used for real hotkeys

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly NativeWindowHook _windowHook;
    private readonly Dictionary<int, string> _hotkeyContent = new();
    private int _nextHotkeyId = 1;
    private bool _initialized;

    public event Action<string>? HotkeyPressed;

    public HotkeyService(NativeWindowHook windowHook)
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
    }

    public bool RegisterHotkey(string shortcut, string content)
    {
        if (!_windowHook.IsAttached || !ShortcutPolicy.TryParseShortcut(shortcut, out var modifiers, out var key))
        {
            return false;
        }

        if (!RegisterHotKey(_windowHook.WindowHandle, _nextHotkeyId, modifiers | MOD_NOREPEAT, key))
        {
            Debug.WriteLine($"Failed to register hotkey '{shortcut}' (already in use by another application?).");
            return false;
        }

        _hotkeyContent[_nextHotkeyId] = content;
        _nextHotkeyId++;
        return true;
    }

    public void UnregisterAllHotkeys()
    {
        foreach (var id in _hotkeyContent.Keys)
        {
            UnregisterHotKey(_windowHook.WindowHandle, id);
        }

        _hotkeyContent.Clear();
        _nextHotkeyId = 1; // every id is free again
    }

    /// <summary>
    /// Checks whether the shortcut can still be registered globally, i.e. no
    /// other application holds it. Registers it briefly and lets it go again.
    /// </summary>
    public bool IsShortcutAvailable(string shortcut)
    {
        if (!_windowHook.IsAttached || !ShortcutPolicy.TryParseShortcut(shortcut, out var modifiers, out var key))
        {
            return true;
        }

        // Same flags as a real registration, so the probe tests what will be taken.
        if (!RegisterHotKey(_windowHook.WindowHandle, ProbeHotkeyId, modifiers | MOD_NOREPEAT, key))
        {
            return false;
        }

        UnregisterHotKey(_windowHook.WindowHandle, ProbeHotkeyId);
        return true;
    }

    private bool OnMessageReceived(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && _hotkeyContent.TryGetValue((int)wParam, out var content))
        {
            HotkeyPressed?.Invoke(content);
            return true;
        }

        return false;
    }
}
