#if WINDOWS
using Microsoft.Win32;

namespace snipv.Services;

/// <summary>
/// Manages the HKCU Run registry entry that starts snipv with Windows.
/// The autostart entry launches the app with <see cref="TrayArgument"/> so it
/// comes up hidden in the tray instead of opening its window.
/// </summary>
public class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "snipv";

    public const string TrayArgument = "--tray";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" {TrayArgument}");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static bool LaunchedToTray =>
        Environment.GetCommandLineArgs().Contains(TrayArgument, StringComparer.OrdinalIgnoreCase);
}
#endif
