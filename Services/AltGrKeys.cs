namespace snipv.Services;

/// <summary>
/// What the keyboard layouts installed on this machine type with AltGr: the
/// affected keys, and the character each one produces.
///
/// AltGr is not a modifier of its own: the layout driver presses a virtual Ctrl
/// alongside the right Alt, so Windows sees AltGr as Ctrl+Alt and AltGr+Shift as
/// Ctrl+Alt+Shift. A global hotkey on one of those combinations therefore
/// swallows the character before the layout can compose it.
/// </summary>
public sealed class AltGrKeys
{
    // Key to the character it types, or null for a dead key, which composes one
    // together with the next keystroke.
    private readonly Dictionary<uint, string?> _plain;
    private readonly Dictionary<uint, string?> _shifted;

    /// <summary>No layout uses AltGr for typing, so no combination is at risk.</summary>
    public static AltGrKeys None { get; } = new([], []);

    public AltGrKeys(
        IEnumerable<KeyValuePair<uint, string?>> plain, IEnumerable<KeyValuePair<uint, string?>> shifted)
    {
        _plain = new Dictionary<uint, string?>(plain);
        _shifted = new Dictionary<uint, string?>(shifted);
    }

    public bool IsEmpty => _plain.Count == 0 && _shifted.Count == 0;

    /// <summary>Whether AltGr, optionally with Shift, types something on this key.</summary>
    public bool Types(uint key, bool withShift) => Map(withShift).ContainsKey(key);

    /// <summary>
    /// The character AltGr types on this key, or null when the key is free or
    /// composes an accent rather than producing a character of its own.
    /// </summary>
    public string? CharacterFor(uint key, bool withShift) => Map(withShift).GetValueOrDefault(key);

    public override string ToString() =>
        IsEmpty ? "none" : $"AltGr: {Format(_plain)} | AltGr+Shift: {Format(_shifted)}";

    private Dictionary<uint, string?> Map(bool withShift) => withShift ? _shifted : _plain;

    private static string Format(Dictionary<uint, string?> map) =>
        map.Count == 0
            ? "-"
            : string.Join(" ", map.OrderBy(entry => entry.Key).Select(entry => $"{(char)entry.Key}={entry.Value ?? "dead"}"));
}
