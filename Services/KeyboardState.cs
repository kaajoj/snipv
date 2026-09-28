using System.Runtime.InteropServices;

namespace snipv.Services;

/// <summary>
/// Reads the physical keyboard state (GetAsyncKeyState) - used by the shortcut
/// recorder and by the paster when waiting for modifiers to be released.
/// </summary>
public static class KeyboardState
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsAnyModifierDown() =>
        IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_SHIFT) || IsDown(VK_LWIN) || IsDown(VK_RWIN);

    /// <summary>Display name of the currently pressed capturable key, or null when none is down.</summary>
    public static string? GetPressedKeyName()
    {
        foreach (var (vk, name) in ShortcutPolicy.CapturableKeys)
        {
            if (IsDown(vk))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>Currently held modifiers as e.g. "Ctrl+Alt", or an empty string.</summary>
    public static string GetPressedModifiers()
    {
        List<string> modifiers = [];
        if (IsDown(VK_CONTROL)) modifiers.Add("Ctrl");
        if (IsDown(VK_MENU)) modifiers.Add("Alt");
        if (IsDown(VK_SHIFT)) modifiers.Add("Shift");
        if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) modifiers.Add("Win");
        return string.Join("+", modifiers);
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
}
