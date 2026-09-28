using System.Diagnostics;
using System.Runtime.InteropServices;

namespace snipv.Services;

/// <summary>
/// Asks Windows which keys type a character with AltGr, for every keyboard
/// layout installed on this machine. That answer is what the shortcut policy
/// protects, so it never has to know about particular languages: a Polish
/// layout reports ą ć ę ł ń ó ś ź ż, a German one @ € µ, a US one nothing.
/// </summary>
public static class KeyboardLayoutProbe
{
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const uint VK_SPACE = 0x20;
    private const uint MAPVK_VK_TO_VSC = 0;
    private const byte KeyDown = 0x80;

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(
        uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out] char[] pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    public static AltGrKeys Detect()
    {
        Dictionary<uint, string?> plain = [];
        Dictionary<uint, string?> shifted = [];

        try
        {
            foreach (var layout in GetLayouts())
            {
                Collect(layout, withShift: false, plain);
                Collect(layout, withShift: true, shifted);
            }
        }
        catch (Exception ex)
        {
            // Losing the probe only means the policy accepts more combinations,
            // so a failure here must not stop the app from starting.
            Debug.WriteLine($"Keyboard layout probe failed: {ex}");
        }

        var keys = new AltGrKeys(plain, shifted);
        Debug.WriteLine($"AltGr keys in use: {keys}");
        return keys;
    }

    private static IntPtr[] GetLayouts()
    {
        var count = GetKeyboardLayoutList(0, null);
        if (count <= 0)
        {
            return [];
        }

        var layouts = new IntPtr[count];
        var written = GetKeyboardLayoutList(count, layouts);
        return [.. layouts.Take(written).Where(layout => layout != IntPtr.Zero)];
    }

    private static void Collect(IntPtr layout, bool withShift, Dictionary<uint, string?> into)
    {
        // The key state ToUnicodeEx translates against: AltGr is Ctrl+Alt held down.
        var keyState = new byte[256];
        keyState[VK_CONTROL] = KeyDown;
        keyState[VK_MENU] = KeyDown;
        if (withShift)
        {
            keyState[VK_SHIFT] = KeyDown;
        }

        foreach (var key in CandidateKeys())
        {
            if (TryGetTypedCharacter(layout, key, keyState, out var character))
            {
                // First layout to claim a key wins; the rest agree often enough
                // and the character is only ever shown to explain the refusal.
                into.TryAdd(key, character);
            }
        }
    }

    /// <summary>
    /// Digits and letters: the keys a shortcut can use that layouts also use for
    /// typing. Function keys never produce a character.
    /// </summary>
    private static IEnumerable<uint> CandidateKeys()
    {
        for (uint vk = 0x30; vk <= 0x39; vk++) yield return vk; // 0-9
        for (uint vk = 0x41; vk <= 0x5A; vk++) yield return vk; // A-Z
    }

    /// <param name="character">What the key types, or null when it is a dead key.</param>
    private static bool TryGetTypedCharacter(IntPtr layout, uint key, byte[] keyState, out string? character)
    {
        character = null;

        var buffer = new char[8];
        var written = ToUnicodeEx(key, MapVirtualKeyEx(key, MAPVK_VK_TO_VSC, layout), keyState, buffer, buffer.Length, 0, layout);

        if (written < 0)
        {
            // A dead key. It produces no character by itself but starts one, and
            // it stays pending inside the layout - flush it, or the next key
            // probed here comes back as the combined character instead of its own.
            Flush(layout);
            return true;
        }

        // With Ctrl held, keys without an AltGr meaning translate to control
        // characters (Ctrl+A is 0x01); those are not typing.
        if (written == 0 || buffer.Take(written).Any(char.IsControl))
        {
            return false;
        }

        character = new string(buffer, 0, written);
        return true;
    }

    private static void Flush(IntPtr layout)
    {
        var buffer = new char[8];
        ToUnicodeEx(VK_SPACE, MapVirtualKeyEx(VK_SPACE, MAPVK_VK_TO_VSC, layout), new byte[256], buffer, buffer.Length, 0, layout);
    }
}
