using Microsoft.UI.Xaml;
using snipv.Services;

namespace snipv.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        private static Mutex? _singleInstanceMutex;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, "snipv_single_instance", out var isFirstInstance);
            if (!isFirstInstance)
            {
                // Another snipv is already running (possibly hidden in the tray):
                // bring its window back instead of starting a second instance.
                TrayIconService.ActivateRunningInstance();
                Environment.Exit(0);
                return;
            }

            base.OnLaunched(args);
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

}
