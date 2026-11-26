using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using PSS790Player.Core;
using PSS790Player.Models;
using PSS790Player.Services;

namespace PSS790Player
{
    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

        private readonly ServiceProvider _serviceProvider;
        private readonly AppStateStore _state;
        private readonly HardwareDeviceService _devices;
        private readonly LibraryService _library;
        private readonly PlaybackCoordinator _playback;
        private readonly SoundDesignService _soundDesign;
        private readonly RecorderService _recorder;
        private string _lastAppliedModel = string.Empty;

        public MainWindow()
        {
            var services = new ServiceCollection();
            services.AddWpfBlazorWebView();
#if DEBUG
            services.AddBlazorWebViewDeveloperTools();
#endif
            services.AddSingleton<AppStateStore>();
            services.AddSingleton<HardwareDeviceService>();
            services.AddSingleton<LibraryService>();
            services.AddSingleton<PlaybackCoordinator>();
            services.AddSingleton<SoundDesignService>();
            services.AddSingleton<RecorderService>();

            _serviceProvider = services.BuildServiceProvider();
            Resources.Add("services", _serviceProvider);

            _state = _serviceProvider.GetRequiredService<AppStateStore>();
            _devices = _serviceProvider.GetRequiredService<HardwareDeviceService>();
            _library = _serviceProvider.GetRequiredService<LibraryService>();
            _playback = _serviceProvider.GetRequiredService<PlaybackCoordinator>();
            _soundDesign = _serviceProvider.GetRequiredService<SoundDesignService>();
            _recorder = _serviceProvider.GetRequiredService<RecorderService>();

            InitializeComponent();

            if (!string.IsNullOrEmpty(App.SmokeMarkerPath))
            {
                WindowState = WindowState.Minimized;
                ShowInTaskbar = false;
                MainBlazorWebView.BlazorWebViewInitialized += (_, args) =>
                {
                    args.WebView.NavigationCompleted += async (_, _) =>
                    {
                        await System.Threading.Tasks.Task.Delay(900);
                        try
                        {
                            var res = await args.WebView.ExecuteScriptAsync("document.querySelector('#app')?.innerText?.length || 0");
                            if (int.TryParse(res, out int len) && len > 20)
                            {
                                System.IO.File.WriteAllText(App.SmokeMarkerPath, "BLAZOR_SINGLEFILE_RENDERED_OK");
                            }
                            else
                            {
                                System.IO.File.WriteAllText(App.SmokeMarkerPath, "DOM_EMPTY:" + res);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.IO.File.WriteAllText(App.SmokeMarkerPath, "ERROR:" + ex.Message);
                        }
                        _ = Dispatcher.BeginInvoke(Close);
                    };
                };
            }

            _state.Changed += OnStateChanged;
            _library.RefreshLibrary();
            _soundDesign.Initialize();
            _recorder.Initialize();
            ApplyWindowModelBranding(_state.ActiveModel);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (PresentationSource.FromVisual(this) is HwndSource source)
            {
                source.AddHook(WndProcHook);
            }
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE && wParam.ToInt32() == DBT_DEVNODES_CHANGED)
            {
                _devices.TriggerRescan(300);
            }
            return IntPtr.Zero;
        }

        private void OnStateChanged()
        {
            var model = _state.ActiveModel;
            if (!string.Equals(model, _lastAppliedModel, StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.BeginInvoke(() => ApplyWindowModelBranding(model));
            }
        }

        private void ApplyWindowModelBranding(string model)
        {
            _lastAppliedModel = model;
            bool is680 = string.Equals(model, "pss680", StringComparison.OrdinalIgnoreCase);
            Title = is680 ? "Yamaha PSS-680 Studio (2-Op FM & SysEx)" : "Yamaha PSS-790 Studio (AWM & Vector)";

            try
            {
                var uri = new Uri(is680
                    ? "pack://application:,,,/Assets/pss680.ico"
                    : "pack://application:,,,/Assets/app.ico");
                Icon = BitmapFrame.Create(uri);
            }
            catch { }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _devices.PrepareForShutdown();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _state.Changed -= OnStateChanged;
            try
            {
                if (PresentationSource.FromVisual(this) is HwndSource source)
                {
                    source.RemoveHook(WndProcHook);
                }
            }
            catch { }

            try { _serviceProvider.Dispose(); } catch { }
            base.OnClosed(e);
        }
    }
}
