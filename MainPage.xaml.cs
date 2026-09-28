using snipv.Services;
using snipv.ViewModels;

namespace snipv
{
    public partial class MainPage : ContentPage
    {
#if WINDOWS
        private readonly WindowsIntegration _windowsIntegration;
        private bool _windowsInitialized;
#endif

        public MainPage(
            MainViewModel viewModel
#if WINDOWS
            , WindowsIntegration windowsIntegration
#endif
            )
        {
            InitializeComponent();
#if WINDOWS
            _windowsIntegration = windowsIntegration;
#endif
            BindingContext = viewModel;

            ShortcutEntry.Focused += (_, _) => viewModel.StartCapturingShortcut();
            ShortcutEntry.Unfocused += (_, _) => viewModel.StopCapturingShortcut();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

#if WINDOWS
            if (!_windowsInitialized && Window?.Handler?.PlatformView is MauiWinUIWindow mauiWindow)
            {
                _windowsIntegration.Initialize(mauiWindow);

                AutostartCheckBox.IsChecked = _windowsIntegration.IsAutostartEnabled;
                AutostartCheckBox.CheckedChanged += (_, e) => _windowsIntegration.SetAutostart(e.Value);

                _windowsInitialized = true;
            }
#endif
        }
    }
}
