namespace snipv.Services;

public enum ShortcutRisk
{
    /// <summary>Safe combination (two or more modifiers).</summary>
    Ok,
    /// <summary>Single-modifier combination that may collide with in-app shortcuts of other programs.</summary>
    Common,
    /// <summary>The keyboard uses this combination to type a character (AltGr).</summary>
    TypesCharacter,
    /// <summary>Reserved by Windows or near-universal (copy/paste, Alt+F4, plain Shift/Win).</summary>
    Reserved,
}

/// <summary>
/// Everything about shortcut strings: parsing them into Win32 modifier/key
/// codes and judging how risky a combination is to register globally.
/// </summary>
public static class ShortcutPolicy
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private const uint VK_F4 = 0x73;

    // Keys allowed as the main (non-modifier) key of a shortcut, mapped to their display names.
    private static readonly Dictionary<int, string> KeyNames = BuildKeyNames();
    private static readonly Dictionary<string, int> KeyCodes =
        KeyNames.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    // Common single-modifier Ctrl shortcuts (copy/paste, undo, find, save, new tab, ...)
    // that would break everyday use of other applications if registered globally.
    private static readonly HashSet<uint> ReservedCtrlKeys = [.. "ACFNPSTVWXYZ".Select(c => (uint)c)];

    // Near-universal Ctrl+Shift shortcuts: Esc (Task Manager), T (reopen tab),
    // N (incognito/new folder), P (private window/command palette), V (paste plain),
    // C (dev tools/terminal copy), F (search).
    private static readonly HashSet<uint> ReservedCtrlShiftKeys = [(uint)0x1B, .. "TNPVCF".Select(c => (uint)c)];

    /// <summary>Keys that can serve as a shortcut's main key, with display names (used by the recorder).</summary>
    public static IReadOnlyDictionary<int, string> CapturableKeys => KeyNames;

    public static bool TryParseShortcut(string shortcut, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;

        foreach (var part in shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_CONTROL;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_ALT;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_SHIFT;
            else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_WIN;
            else if (key == 0 && KeyCodes.TryGetValue(part, out var vk)) key = (uint)vk;
            else return false;
        }

        // A global hotkey without a modifier would swallow plain keystrokes system-wide.
        return modifiers != 0 && key != 0;
    }

    /// <param name="altGrKeys">
    /// What the machine's keyboard layouts type with AltGr, from
    /// <see cref="KeyboardLayoutProbe"/>. Pass <see cref="AltGrKeys.None"/> to
    /// judge a shortcut on the universal rules alone.
    /// </param>
    public static ShortcutRisk EvaluateShortcut(string shortcut, AltGrKeys altGrKeys)
    {
        if (!TryParseShortcut(shortcut, out var modifiers, out var key))
        {
            return ShortcutRisk.Reserved;
        }

        if (modifiers == MOD_SHIFT) return ShortcutRisk.Reserved;                              // Shift+key just types a character
        if (modifiers == MOD_WIN) return ShortcutRisk.Reserved;                                // Win+key belongs to the Windows shell
        if (modifiers == MOD_CONTROL && ReservedCtrlKeys.Contains(key)) return ShortcutRisk.Reserved;
        if (modifiers == MOD_ALT && key == VK_F4) return ShortcutRisk.Reserved;
        if (modifiers == (MOD_CONTROL | MOD_SHIFT) && ReservedCtrlShiftKeys.Contains(key)) return ShortcutRisk.Reserved;

        // AltGr reaches Windows as Ctrl+Alt, and AltGr+Shift as Ctrl+Alt+Shift.
        // Any other modifier next to them (Win) is not something typing produces.
        if (modifiers == (MOD_CONTROL | MOD_ALT) && altGrKeys.Types(key, withShift: false)) return ShortcutRisk.TypesCharacter;
        if (modifiers == (MOD_CONTROL | MOD_ALT | MOD_SHIFT) && altGrKeys.Types(key, withShift: true)) return ShortcutRisk.TypesCharacter;

        return modifiers is MOD_CONTROL or MOD_ALT ? ShortcutRisk.Common : ShortcutRisk.Ok;
    }

    /// <summary>
    /// The character the keyboard types with this combination, for explaining a
    /// <see cref="ShortcutRisk.TypesCharacter"/> verdict. Null when nothing is
    /// typed, or when the key composes an accent instead of a character.
    /// </summary>
    public static string? TypedCharacter(string shortcut, AltGrKeys altGrKeys)
    {
        if (!TryParseShortcut(shortcut, out var modifiers, out var key))
        {
            return null;
        }

        var withShift = modifiers == (MOD_CONTROL | MOD_ALT | MOD_SHIFT);
        return withShift || modifiers == (MOD_CONTROL | MOD_ALT)
            ? altGrKeys.CharacterFor(key, withShift)
            : null;
    }

    private static Dictionary<int, string> BuildKeyNames()
    {
        var names = new Dictionary<int, string> { [0x20] = "Space" };

        for (var vk = 0x30; vk <= 0x39; vk++) names[vk] = ((char)vk).ToString(); // 0-9
        for (var vk = 0x41; vk <= 0x5A; vk++) names[vk] = ((char)vk).ToString(); // A-Z
        for (var i = 0; i < 12; i++) names[0x70 + i] = $"F{i + 1}";              // F1-F12

        return names;
    }
}
