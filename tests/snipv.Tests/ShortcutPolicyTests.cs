using snipv.Services;

namespace snipv.Tests;

public class ShortcutPolicyTests
{
    // Modifier flag values from the Win32 RegisterHotKey API.
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;

    // ---------- TryParseShortcut: valid input ----------

    [Theory]
    [InlineData("Ctrl+Alt+T", ModControl | ModAlt, 'T')]
    [InlineData("ctrl+alt+t", ModControl | ModAlt, 'T')]           // case-insensitive
    [InlineData(" Ctrl + Shift + A ", ModControl | ModShift, 'A')] // tolerates spaces
    [InlineData("Ctrl+Alt+0", ModControl | ModAlt, '0')]
    [InlineData("Win+Space", ModWin, 0x20)]
    [InlineData("Ctrl+F12", ModControl, 0x7B)]
    [InlineData("Ctrl+Alt+Shift+S", ModControl | ModAlt | ModShift, 'S')]
    public void TryParseShortcut_ParsesValidShortcuts(string shortcut, uint expectedModifiers, int expectedKey)
    {
        var ok = ShortcutPolicy.TryParseShortcut(shortcut, out var modifiers, out var key);

        Assert.True(ok);
        Assert.Equal(expectedModifiers, modifiers);
        Assert.Equal((uint)expectedKey, key);
    }

    // ---------- TryParseShortcut: invalid input ----------

    [Theory]
    [InlineData("")]
    [InlineData("T")]           // no modifier
    [InlineData("Ctrl")]        // no key
    [InlineData("Ctrl+")]
    [InlineData("+")]
    [InlineData("Ctrl+Ź")]      // unsupported key
    [InlineData("Ctrl+T+G")]    // two main keys
    [InlineData("Foo+T")]       // unknown modifier
    [InlineData("Shift+Esc")]   // Esc is not an allowed main key
    public void TryParseShortcut_RejectsInvalidShortcuts(string shortcut)
    {
        Assert.False(ShortcutPolicy.TryParseShortcut(shortcut, out _, out _));
    }

    // ---------- EvaluateShortcut: reserved ----------

    [Theory]
    // near-universal single-modifier Ctrl shortcuts
    [InlineData("Ctrl+A")]
    [InlineData("Ctrl+C")]
    [InlineData("Ctrl+F")]
    [InlineData("Ctrl+N")]
    [InlineData("Ctrl+P")]
    [InlineData("Ctrl+S")]
    [InlineData("Ctrl+T")]
    [InlineData("Ctrl+V")]
    [InlineData("Ctrl+W")]
    [InlineData("Ctrl+X")]
    [InlineData("Ctrl+Y")]
    [InlineData("Ctrl+Z")]
    // bare Shift types a character; bare Win belongs to the shell
    [InlineData("Shift+A")]
    [InlineData("Win+E")]
    [InlineData("Alt+F4")]
    // popular Ctrl+Shift shortcuts
    [InlineData("Ctrl+Shift+T")]
    [InlineData("Ctrl+Shift+N")]
    [InlineData("Ctrl+Shift+P")]
    [InlineData("Ctrl+Shift+V")]
    [InlineData("Ctrl+Shift+C")]
    [InlineData("Ctrl+Shift+F")]
    // unparseable input is treated as reserved
    [InlineData("not-a-shortcut")]
    public void EvaluateShortcut_FlagsReservedCombinations(string shortcut)
    {
        // Reserved for everyone, whatever the keyboard types.
        Assert.Equal(ShortcutRisk.Reserved, ShortcutPolicy.EvaluateShortcut(shortcut, AltGrKeys.None));
    }

    // ---------- EvaluateShortcut: taken by the keyboard layout ----------

    // A Polish (Programmers) layout: AltGr types ą ć ę ł ń ó ś ź ż on these keys
    // and AltGr+Shift their capitals.
    private static readonly AltGrKeys PolishLayout = new(
        Typed("A=ą C=ć E=ę L=ł N=ń O=ó S=ś X=ź Z=ż"),
        Typed("A=Ą C=Ć E=Ę L=Ł N=Ń O=Ó S=Ś X=Ź Z=Ż"));

    /// <summary>Reads "A=ą C=ć" into the key/character pairs a layout reports.</summary>
    private static IEnumerable<KeyValuePair<uint, string?>> Typed(string pairs) =>
        pairs.Split(' ', StringSplitOptions.RemoveEmptyEntries)
             .Select(pair => pair.Split('='))
             .Select(parts => new KeyValuePair<uint, string?>((uint)parts[0][0], parts[1]));

    [Theory]
    // AltGr reaches Windows as Ctrl+Alt
    [InlineData("Ctrl+Alt+A")]
    [InlineData("Ctrl+Alt+C")]
    [InlineData("Ctrl+Alt+E")]
    [InlineData("Ctrl+Alt+L")]
    [InlineData("Ctrl+Alt+N")]
    [InlineData("Ctrl+Alt+O")]
    [InlineData("Ctrl+Alt+S")]
    [InlineData("Ctrl+Alt+X")]
    [InlineData("Ctrl+Alt+Z")]
    // and AltGr+Shift, which types the same characters in upper case
    [InlineData("Ctrl+Alt+Shift+A")]
    [InlineData("Ctrl+Alt+Shift+C")]
    [InlineData("Ctrl+Alt+Shift+E")]
    [InlineData("Ctrl+Alt+Shift+L")]
    [InlineData("Ctrl+Alt+Shift+N")]
    [InlineData("Ctrl+Alt+Shift+O")]
    [InlineData("Ctrl+Alt+Shift+S")]
    [InlineData("Ctrl+Alt+Shift+X")]
    [InlineData("Ctrl+Alt+Shift+Z")]
    public void EvaluateShortcut_RefusesWhatTheKeyboardTypes(string shortcut)
    {
        Assert.Equal(ShortcutRisk.TypesCharacter, ShortcutPolicy.EvaluateShortcut(shortcut, PolishLayout));
    }

    [Theory]
    [InlineData("Ctrl+Alt+L")]
    [InlineData("Ctrl+Alt+Shift+L")]
    public void EvaluateShortcut_AcceptsThoseSameKeysWhenNoLayoutTypesThem(string shortcut)
    {
        // The same combinations a Polish keyboard needs are free on, say, a US one.
        Assert.Equal(ShortcutRisk.Ok, ShortcutPolicy.EvaluateShortcut(shortcut, AltGrKeys.None));
    }

    [Fact]
    public void EvaluateShortcut_ChecksTheShiftedSetSeparately()
    {
        // A layout where AltGr+Q types something but AltGr+Shift+Q does not.
        var layout = new AltGrKeys(Typed("Q=@"), []);

        Assert.Equal(ShortcutRisk.TypesCharacter, ShortcutPolicy.EvaluateShortcut("Ctrl+Alt+Q", layout));
        Assert.Equal(ShortcutRisk.Ok, ShortcutPolicy.EvaluateShortcut("Ctrl+Alt+Shift+Q", layout));
    }

    // ---------- TypedCharacter: what the refusal dialog shows ----------

    [Theory]
    [InlineData("Ctrl+Alt+L", "ł")]
    [InlineData("Ctrl+Alt+Shift+L", "Ł")]
    [InlineData("ctrl+alt+o", "ó")]
    public void TypedCharacter_NamesTheCharacterAtStake(string shortcut, string expected)
    {
        Assert.Equal(expected, ShortcutPolicy.TypedCharacter(shortcut, PolishLayout));
    }

    [Theory]
    [InlineData("Ctrl+Alt+T")]        // key the layout leaves alone
    [InlineData("Ctrl+Shift+L")]      // not an AltGr combination
    [InlineData("Ctrl+Alt+Win+L")]    // Win is not part of typing
    [InlineData("not-a-shortcut")]
    public void TypedCharacter_IsNullWhenNothingIsTyped(string shortcut)
    {
        Assert.Null(ShortcutPolicy.TypedCharacter(shortcut, PolishLayout));
    }

    [Fact]
    public void TypedCharacter_IsNullForADeadKey()
    {
        // The probe reports the key as taken but cannot say what it will compose.
        var layout = new AltGrKeys([new KeyValuePair<uint, string?>((uint)'U', null)], []);

        Assert.True(ShortcutPolicy.EvaluateShortcut("Ctrl+Alt+U", layout) == ShortcutRisk.TypesCharacter);
        Assert.Null(ShortcutPolicy.TypedCharacter("Ctrl+Alt+U", layout));
    }

    // ---------- EvaluateShortcut: single-modifier warning ----------

    [Theory]
    [InlineData("Ctrl+G")]
    [InlineData("Ctrl+5")]
    [InlineData("Alt+H")]
    [InlineData("Alt+1")]
    public void EvaluateShortcut_FlagsSingleModifierAsCommon(string shortcut)
    {
        Assert.Equal(ShortcutRisk.Common, ShortcutPolicy.EvaluateShortcut(shortcut, PolishLayout));
    }

    // ---------- EvaluateShortcut: safe ----------

    [Theory]
    [InlineData("Ctrl+Alt+T")]        // letter this layout does not type with AltGr
    [InlineData("Ctrl+Alt+1")]        // recommended pool
    [InlineData("Ctrl+Alt+F9")]       // recommended pool
    [InlineData("Ctrl+Shift+G")]      // two modifiers, not on the blocklist
    [InlineData("Ctrl+Alt+Shift+G")]  // three modifiers, letter outside the AltGr set
    [InlineData("Ctrl+Alt+Win+L")]    // Win is not part of typing, so no AltGr collision
    [InlineData("Win+Shift+K")]
    public void EvaluateShortcut_AcceptsSafeCombinations(string shortcut)
    {
        Assert.Equal(ShortcutRisk.Ok, ShortcutPolicy.EvaluateShortcut(shortcut, PolishLayout));
    }
}
