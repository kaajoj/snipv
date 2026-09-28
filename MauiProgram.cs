using Microsoft.Extensions.Logging;
using snipv.Services;
using snipv.ViewModels;

namespace snipv
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            builder.Services.AddSingleton<NativeWindowHook>();
            // Probed once: what the installed keyboard layouts type with AltGr,
            // which the shortcut policy then refuses to claim.
            builder.Services.AddSingleton(_ => KeyboardLayoutProbe.Detect());
            builder.Services.AddSingleton(_ => new SnippetService(GetSnippetsFilePath()));
            builder.Services.AddSingleton<HotkeyService>();
            builder.Services.AddSingleton<SnippetInserterService>();
            builder.Services.AddSingleton<IDialogService, DialogService>();
            builder.Services.AddSingleton<MainViewModel>();
            builder.Services.AddSingleton<MainPage>();

#if WINDOWS
            builder.Services.AddSingleton<TrayIconService>();
            builder.Services.AddSingleton<WindowPlacementService>();
            builder.Services.AddSingleton<StartupService>();
            builder.Services.AddSingleton<WindowsIntegration>();
#endif

            return builder.Build();
        }

        private static string GetSnippetsFilePath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "snipv");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "snippets.json");
        }
    }
}
