#if WINDOWS
namespace snipv.Services;

/// <summary>
/// Wires the Windows-specific pieces together once the app window exists:
/// global hotkeys, snippet pasting, the tray icon, autostart and the
/// --tray startup mode. Keeps that orchestration out of the page code-behind.
/// </summary>
public class WindowsIntegration
{
    private readonly SnippetService _snippetService;
    private readonly HotkeyService _hotkeyService;
    private readonly SnippetInserterService _snippetInserter;
    private readonly TrayIconService _trayIcon;
    private readonly WindowPlacementService _windowPlacement;
    private readonly StartupService _startup;
    private readonly IDialogService _dialogService;
    private string? _lastReportedFailures;
    private bool _initialized;

    public WindowsIntegration(
        SnippetService snippetService,
        HotkeyService hotkeyService,
        SnippetInserterService snippetInserter,
        TrayIconService trayIcon,
        WindowPlacementService windowPlacement,
        StartupService startup,
        IDialogService dialogService)
    {
        _snippetService = snippetService;
        _hotkeyService = hotkeyService;
        _snippetInserter = snippetInserter;
        _trayIcon = trayIcon;
        _windowPlacement = windowPlacement;
        _startup = startup;
        _dialogService = dialogService;
    }

    public bool IsAutostartEnabled => _startup.IsEnabled;

    public void SetAutostart(bool enabled) => _startup.SetEnabled(enabled);

    public void Initialize(MauiWinUIWindow window)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        _hotkeyService.Initialize(window.WindowHandle);
        _hotkeyService.HotkeyPressed += content => _ = _snippetInserter.InsertAsync(content);
        _snippetService.SnippetsChanged += RefreshHotkeys;
        RefreshHotkeys();

        // Registered before the tray icon so its WM_CLOSE handler records the
        // geometry before close-to-tray hides the window.
        _windowPlacement.Initialize(window.WindowHandle);
        _trayIcon.Initialize(window.WindowHandle);

        if (StartupService.LaunchedToTray)
        {
            _trayIcon.StartHidden();
            _ = TrimOnceStartupSettlesAsync();
        }
    }

    /// <summary>
    /// The hide above runs in the middle of startup, so the pages it hands back
    /// are allocated again moments later. Trim once more when that is over,
    /// otherwise an app nobody has opened sits on its whole startup footprint.
    /// </summary>
    private async Task TrimOnceStartupSettlesAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        _trayIcon.TrimWorkingSet();
    }

    private void RefreshHotkeys()
    {
        _hotkeyService.UnregisterAllHotkeys();

        List<string> failed = [];
        foreach (var snippet in _snippetService.GetSnippets())
        {
            // Parked snippets keep their text but claim no shortcut.
            if (string.IsNullOrEmpty(snippet.Shortcut))
            {
                continue;
            }

            if (!_hotkeyService.RegisterHotkey(snippet.Shortcut, snippet.Content))
            {
                failed.Add(snippet.Shortcut);
            }
        }

        if (failed.Count == 0)
        {
            _lastReportedFailures = null;
            return;
        }

        // Alert once per distinct failing set so re-registrations don't nag.
        var failedList = string.Join(", ", failed);
        if (failedList != _lastReportedFailures)
        {
            _lastReportedFailures = failedList;
            _ = _dialogService.AlertAsync(
                "Hotkey conflict",
                $"These shortcuts could not be registered and will not work (possibly taken by another application): {failedList}");
        }
    }
}
#endif
