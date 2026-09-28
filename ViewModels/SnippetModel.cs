using System.Text.Json.Serialization;

namespace snipv.ViewModels;

/// <summary>
/// One snippet. The text survives without a shortcut: handing a shortcut to
/// another snippet parks the previous holder here instead of deleting it.
///
/// Deliberately a class rather than a record - <see cref="Services.SnippetService"/>
/// identifies snippets by reference, so two parked entries with identical text
/// stay distinct.
/// </summary>
public class SnippetModel
{
    /// <summary>Null or empty when the snippet is parked without a shortcut.</summary>
    public string? Shortcut { get; init; }

    public required string Content { get; init; }

    [JsonIgnore]
    public bool HasShortcut => !string.IsNullOrEmpty(Shortcut);

    [JsonIgnore]
    public bool HasNoShortcut => !HasShortcut;

    /// <summary>
    /// What this snippet's shortcut costs the keyboard, when it turns out to be
    /// a combination the layout needs for typing. Filled in for display only:
    /// the check runs when a shortcut is saved, and a snippet can predate it or
    /// be overtaken by a layout installed later.
    /// </summary>
    [JsonIgnore]
    public string? Warning { get; set; }

    [JsonIgnore]
    public bool HasWarning => !string.IsNullOrEmpty(Warning);
}
