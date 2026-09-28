using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using snipv.Services;
using System.Collections.ObjectModel;

namespace snipv.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SnippetService _snippetService;
    private readonly HotkeyService _hotkeyService;
    private readonly IDialogService _dialogService;
    private readonly AltGrKeys _altGrKeys;
    private CancellationTokenSource? _shortcutCaptureCts;

    public ObservableCollection<SnippetModel> Snippets { get; } = [];

    private string _newSnippet = string.Empty;
    private string _newShortcut = string.Empty;
    private SnippetModel? _selectedSnippet;

    public string NewSnippet
    {
        get => _newSnippet;
        set => SetProperty(ref _newSnippet, value);
    }

    public string NewShortcut
    {
        get => _newShortcut;
        set
        {
            if (SetProperty(ref _newShortcut, value))
            {
                OnPropertyChanged(nameof(HasShortcut));
            }
        }
    }

    /// <summary>Whether the editor currently holds a shortcut that could be cleared.</summary>
    public bool HasShortcut => !string.IsNullOrWhiteSpace(NewShortcut);

    /// <summary>The snippet currently loaded into the editor, or null when adding a new one.</summary>
    public SnippetModel? SelectedSnippet
    {
        get => _selectedSnippet;
        set
        {
            if (SetProperty(ref _selectedSnippet, value))
            {
                RemoveSnippetCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsEditing));
                OnPropertyChanged(nameof(SaveButtonText));

                if (value is not null)
                {
                    NewSnippet = value.Content;
                    NewShortcut = value.Shortcut ?? string.Empty;
                }
            }
        }
    }

    public bool IsEditing => SelectedSnippet is not null;
    public string SaveButtonText => IsEditing ? "Save Changes" : "Add Snippet";

    public MainViewModel(
        SnippetService snippetService, HotkeyService hotkeyService, IDialogService dialogService, AltGrKeys altGrKeys)
    {
        _snippetService = snippetService;
        _hotkeyService = hotkeyService;
        _dialogService = dialogService;
        _altGrKeys = altGrKeys;

        _snippetService.SnippetsChanged += LoadSnippets;
        _snippetService.SaveFailed += ReportSaveFailure;
        LoadSnippets();
    }

    /// <summary>
    /// The list on screen and the file have parted ways, which is worth saying
    /// out loud: everything looks saved until the next start, when it is gone.
    /// </summary>
    private void ReportSaveFailure(string reason) =>
        _ = _dialogService.AlertAsync(
            "Snippets not saved",
            "snipv could not write the snippets file, so this change lives only in the open window and will be " +
            $"gone when the app restarts. The file itself was left as it was.\n\n{reason}");

    private void LoadSnippets()
    {
        Snippets.Clear();
        foreach (var snippet in _snippetService.GetSnippets())
        {
            snippet.Warning = DescribeCost(snippet);
            Snippets.Add(snippet);
        }
    }

    /// <summary>
    /// Names the character a stored shortcut is swallowing, so a snippet saved
    /// before the layout check existed - or overtaken by a keyboard layout
    /// installed since - is visible on the list instead of quietly costing the
    /// user a key.
    /// </summary>
    private string? DescribeCost(SnippetModel snippet)
    {
        if (snippet.Shortcut is not string shortcut
            || ShortcutPolicy.EvaluateShortcut(shortcut, _altGrKeys) != ShortcutRisk.TypesCharacter)
        {
            return null;
        }

        var typed = ShortcutPolicy.TypedCharacter(shortcut, _altGrKeys);
        return typed is null ? "blocks a character" : $"blocks {typed}";
    }

    [RelayCommand]
    private async Task SaveSnippetAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSnippet))
        {
            await _dialogService.AlertAsync("Missing text", "Enter the snippet text first.");
            return;
        }

        // An empty field is allowed: the snippet is kept, just without a shortcut.
        var shortcut = string.IsNullOrWhiteSpace(NewShortcut) ? null : NewShortcut.Trim();
        if (shortcut is not null && !await IsShortcutAcceptableAsync(shortcut))
        {
            return;
        }

        if (SelectedSnippet is SnippetModel editing)
        {
            _snippetService.UpdateSnippet(editing, shortcut, NewSnippet);
        }
        else
        {
            _snippetService.AddSnippet(shortcut, NewSnippet);
        }

        ClearEditor();
    }

    /// <summary>
    /// Runs the shortcut past the policy, the other snippets and the rest of the
    /// system, asking the user wherever the answer is theirs to give.
    /// </summary>
    private async Task<bool> IsShortcutAcceptableAsync(string shortcut)
    {
        if (!ShortcutPolicy.TryParseShortcut(shortcut, out _, out _))
        {
            await _dialogService.AlertAsync(
                "Invalid shortcut",
                "Click the shortcut field and press a combination with at least one modifier (Ctrl, Alt, Shift or Win) plus a key.");
            return false;
        }

        // Reserved combinations are refused outright; single-modifier ones warn.
        var risk = ShortcutPolicy.EvaluateShortcut(shortcut, _altGrKeys);
        if (risk == ShortcutRisk.Reserved)
        {
            await _dialogService.AlertAsync(
                "Shortcut not allowed",
                $"{shortcut} is reserved by Windows or used almost everywhere (copy, paste, Task Manager, ...). " +
                "Try Ctrl+Alt+F1-F12 or Ctrl+Alt+Shift+letter instead.");
            return false;
        }

        if (risk == ShortcutRisk.TypesCharacter)
        {
            // Confirmable rather than refused outright: this verdict comes from
            // probing the keyboard layouts, and the person at the keyboard is the
            // one who can tell when that probe got it wrong.
            var typed = ShortcutPolicy.TypedCharacter(shortcut, _altGrKeys);
            var what = typed is null ? "a character" : $"\"{typed}\"";

            return await _dialogService.ConfirmAsync(
                "Shortcut types a character",
                $"Your keyboard types {what} with {shortcut}. AltGr is not a modifier of its own: the layout " +
                "holds a virtual Ctrl with it, so Windows sees AltGr as Ctrl+Alt, and AltGr+Shift as " +
                $"Ctrl+Alt+Shift. Taking this combination means you can no longer type {what} in any " +
                "application while snipv runs.",
                "Take it anyway", "Pick another");
        }

        // Everything below is about moving the shortcut somewhere it is not already.
        if (string.Equals(shortcut, SelectedSnippet?.Shortcut, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_snippetService.FindByShortcut(shortcut) is SnippetModel holder)
        {
            return await _dialogService.ConfirmAsync(
                "Shortcut already used",
                $"{shortcut} currently pastes:\n\n{Preview(holder.Content)}\n\n" +
                "Give the shortcut to this snippet instead? The other one stays in the list, without a shortcut.",
                "Reassign", "Cancel");
        }

        if (!_hotkeyService.IsShortcutAvailable(shortcut))
        {
            await _dialogService.AlertAsync(
                "Shortcut unavailable",
                $"{shortcut} is already held globally by another application, so snipv can't use it. " +
                "Please choose a different combination.");
            return false;
        }

        if (risk == ShortcutRisk.Common)
        {
            return await _dialogService.ConfirmAsync(
                "Common shortcut",
                $"{shortcut} uses a single modifier and may clash with shortcuts inside other apps " +
                "(for example Ctrl+F for Find) while snipv is running. Use it anyway?",
                "Use it", "Cancel");
        }

        return true;
    }

    /// <summary>
    /// Drops the shortcut from the editor. Saving then parks the snippet: it
    /// keeps its text and stops claiming a global hotkey.
    /// </summary>
    [RelayCommand]
    private void ClearShortcut() => NewShortcut = string.Empty;

    [RelayCommand]
    private void ClearEditor()
    {
        SelectedSnippet = null;
        NewSnippet = string.Empty;
        NewShortcut = string.Empty;
    }

    private bool CanRemoveSnippet() => SelectedSnippet is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveSnippet))]
    private async Task RemoveSnippetAsync()
    {
        if (SelectedSnippet is not SnippetModel selected)
        {
            return;
        }

        var what = selected.HasShortcut ? $"the snippet for {selected.Shortcut}" : "this snippet, which has no shortcut";
        var confirmed = await _dialogService.ConfirmAsync(
            "Remove snippet",
            $"Delete {what}? This cannot be undone.\n\n{Preview(selected.Content)}",
            "Remove", "Cancel");
        if (!confirmed)
        {
            return;
        }

        _snippetService.RemoveSnippet(selected);
        ClearEditor();
    }

    /// <summary>First non-empty line of a snippet, shortened to fit in a dialog.</summary>
    private static string Preview(string content)
    {
        const int maxLength = 80;

        var line = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(string.Empty);

        if (line.Length == 0)
        {
            return "(blank)";
        }

        return line.Length <= maxLength ? line : string.Concat(line.AsSpan(0, maxLength), "...");
    }

    /// <summary>
    /// Polls the keyboard while the shortcut entry is focused and writes the
    /// last pressed modifier+key combination into <see cref="NewShortcut"/>.
    /// </summary>
    public void StartCapturingShortcut()
    {
        if (_shortcutCaptureCts is not null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _shortcutCaptureCts = cts;

        Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var key = KeyboardState.GetPressedKeyName();
                    var modifiers = KeyboardState.GetPressedModifiers();

                    if (key is not null && modifiers.Length > 0)
                    {
                        var shortcut = $"{modifiers}+{key}";
                        MainThread.BeginInvokeOnMainThread(() => NewShortcut = shortcut);
                    }

                    await Task.Delay(50, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    public void StopCapturingShortcut()
    {
        _shortcutCaptureCts?.Cancel();
        _shortcutCaptureCts?.Dispose();
        _shortcutCaptureCts = null;
    }
}
