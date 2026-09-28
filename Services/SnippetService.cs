using snipv.ViewModels;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace snipv.Services;

/// <summary>
/// Stores the snippets as a JSON list. A shortcut identifies at most one
/// snippet, but is not the snippet's identity: assigning a shortcut that
/// another snippet already holds parks that other snippet without a shortcut
/// rather than dropping its text.
/// </summary>
public class SnippetService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // The file is read by people (it is the only copy of the text), so keep
        // '+' and non-ASCII characters as themselves instead of \u escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _filePath;
    private readonly List<SnippetModel> _snippets;

    public event Action? SnippetsChanged;

    /// <summary>
    /// Raised with the reason when a change could not be written to disk, so it
    /// exists only in memory and will be gone at the next start. Raised again
    /// only after a save has succeeded in between, to report a broken file
    /// rather than every edit made while it stays broken.
    /// </summary>
    public event Action<string>? SaveFailed;

    private bool _saveFailed;

    public SnippetService(string filePath)
    {
        _filePath = filePath;
        _snippets = Load();
    }

    public bool Contains(string shortcut) => FindByShortcut(shortcut) is not null;

    /// <summary>The snippet holding this shortcut, or null when it is free.</summary>
    public SnippetModel? FindByShortcut(string shortcut) =>
        string.IsNullOrWhiteSpace(shortcut)
            ? null
            : _snippets.Find(s => string.Equals(s.Shortcut, shortcut, StringComparison.OrdinalIgnoreCase));

    public void AddSnippet(string? shortcut, string content)
    {
        ParkHolderOf(shortcut, keep: null);
        _snippets.Add(new SnippetModel { Shortcut = Normalize(shortcut), Content = content });
        Persist();
    }

    /// <summary>
    /// Rewrites one snippet in place, optionally moving it to another shortcut.
    /// </summary>
    public void UpdateSnippet(SnippetModel snippet, string? shortcut, string content)
    {
        var index = _snippets.IndexOf(snippet);
        if (index < 0)
        {
            return;
        }

        ParkHolderOf(shortcut, keep: snippet);
        _snippets[index] = new SnippetModel { Shortcut = Normalize(shortcut), Content = content };
        Persist();
    }

    public void RemoveSnippet(SnippetModel snippet)
    {
        if (_snippets.Remove(snippet))
        {
            Persist();
        }
    }

    /// <summary>Snippets with a shortcut first, then the parked ones.</summary>
    public IReadOnlyList<SnippetModel> GetSnippets() =>
        [.. _snippets
            .OrderBy(s => s.HasNoShortcut)
            .ThenBy(s => s.Shortcut, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Content, StringComparer.CurrentCulture)];

    private static string? Normalize(string? shortcut) =>
        string.IsNullOrWhiteSpace(shortcut) ? null : shortcut.Trim();

    /// <summary>
    /// Takes the shortcut away from whichever snippet holds it, keeping that
    /// snippet's text. <paramref name="keep"/> is the snippet being written,
    /// which must not park itself.
    /// </summary>
    private void ParkHolderOf(string? shortcut, SnippetModel? keep)
    {
        if (Normalize(shortcut) is not string wanted)
        {
            return;
        }

        var holder = FindByShortcut(wanted);
        if (holder is null || ReferenceEquals(holder, keep))
        {
            return;
        }

        _snippets[_snippets.IndexOf(holder)] = new SnippetModel { Shortcut = null, Content = holder.Content };
    }

    private List<SnippetModel> Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var loaded = JsonSerializer.Deserialize<List<SnippetModel>>(File.ReadAllText(_filePath), JsonOptions);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (JsonException ex)
        {
            // Unreadable content would be overwritten by the next save, and the
            // file holds the only copy of the text - move it aside instead.
            Debug.WriteLine($"Failed to parse snippets from '{_filePath}': {ex.Message}");
            Quarantine();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load snippets from '{_filePath}': {ex.Message}");
        }

        return [];
    }

    private void Quarantine()
    {
        try
        {
            File.Move(_filePath, _filePath + ".corrupt", overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set aside '{_filePath}': {ex.Message}");
        }
    }

    private void Persist()
    {
        Save();
        SnippetsChanged?.Invoke();
    }

    private void Save()
    {
        var temporaryPath = _filePath + ".tmp";

        try
        {
            // Written beside the real file and moved over it, because writing in
            // place truncates first: a crash between the two would leave the only
            // copy of every snippet half written. A move within a folder is
            // atomic, so the file is either the old list or the new one, and a
            // crash before it leaves a stray .tmp rather than a damaged file.
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_snippets, JsonOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
            _saveFailed = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save snippets to '{_filePath}': {ex.Message}");

            // Reaching here means the write failed rather than the process dying
            // mid-write, so the half-finished copy is only litter. A crash cannot
            // run this, which is why the comment above still holds.
            try { File.Delete(temporaryPath); } catch { }

            if (!_saveFailed)
            {
                _saveFailed = true;
                SaveFailed?.Invoke(ex.Message);
            }
        }
    }
}
