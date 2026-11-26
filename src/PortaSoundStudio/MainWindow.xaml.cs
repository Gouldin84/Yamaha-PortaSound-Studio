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

            if (!string.IsNullOrEmpty(App.SmokeMarkerPath) || !string.IsNullOrEmpty(App.DiagMarkerPath))
            {
                if (!string.IsNullOrEmpty(App.SmokeMarkerPath))
                {
                    WindowState = WindowState.Minimized;
                    ShowInTaskbar = false;
                }
                else
                {
                    Left = -3000;
                    Top = -3000;
                    ShowInTaskbar = false;
                }

                MainBlazorWebView.BlazorWebViewInitialized += (_, args) =>
                {
                    args.WebView.NavigationCompleted += async (_, _) =>
                    {
                        await System.Threading.Tasks.Task.Delay(1200);
                        try
                        {
                            if (!string.IsNullOrEmpty(App.DiagMarkerPath))
                            {
                                var script = @"
                                    (() => {
                                        const report = {};
                                        const msb = document.getElementById('modelSelectorBtn');
                                        const rec = document.getElementById('tabBtnRecorder');
                                        const msbRect = msb ? msb.getBoundingClientRect() : null;
                                        const recRect = rec ? rec.getBoundingClientRect() : null;
                                        report.hasBlazor = typeof window.Blazor !== 'undefined';
                                        report.msbExists = !!msb;
                                        report.recExists = !!rec;
                                        report.msbHit = msbRect ? (document.elementFromPoint(msbRect.left + msbRect.width/2, msbRect.top + msbRect.height/2)?.id || document.elementFromPoint(msbRect.left + msbRect.width/2, msbRect.top + msbRect.height/2)?.tagName) : 'none';
                                        report.recHit = recRect ? (document.elementFromPoint(recRect.left + recRect.width/2, recRect.top + recRect.height/2)?.id || document.elementFromPoint(recRect.left + recRect.width/2, recRect.top + recRect.height/2)?.tagName) : 'none';
                                        report.errorUi = document.getElementById('blazor-error-ui')?.outerHTML || 'none';
                                        if (msb) msb.click();
                                        return JSON.stringify(report);
                                    })()";
                                var r1 = await args.WebView.ExecuteScriptAsync(script);
                                await System.Threading.Tasks.Task.Delay(250);
                                var r2 = await args.WebView.ExecuteScriptAsync("JSON.stringify({ menuOpen: !!document.getElementById('modelDropdownMenu'), title: document.getElementById('activeModelTitle')?.innerText, treeText790: document.getElementById('treeContainer')?.innerText?.includes('Yamaha PSS-790'), treeText680: document.getElementById('treeContainer')?.innerText?.includes('Yamaha PSS-680') })");
                                await args.WebView.ExecuteScriptAsync("document.querySelectorAll('#modelDropdownMenu button')[1]?.click()");
                                await System.Threading.Tasks.Task.Delay(350);
                                var r2b = await args.WebView.ExecuteScriptAsync("JSON.stringify({ menuOpen: !!document.getElementById('modelDropdownMenu'), title: document.getElementById('activeModelTitle')?.innerText, has790Tree: document.getElementById('treeContainer')?.innerText?.includes('Yamaha PSS-790'), has680Tree: document.getElementById('treeContainer')?.innerText?.includes('Yamaha PSS-680') })");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('tabBtnRecorder')?.click()");
                                await System.Threading.Tasks.Task.Delay(300);
                                var r3 = await args.WebView.ExecuteScriptAsync("JSON.stringify({ recOpen: !!document.getElementById('recordingsSidebar') })");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('tabBtnSysex')?.click()");
                                await System.Threading.Tasks.Task.Delay(300);
                                var r4 = await args.WebView.ExecuteScriptAsync("JSON.stringify({ fmOpen: !!document.getElementById('deckFmSoundDesigner'), vecOpen: !!document.getElementById('deckVectorSynthesizer') })");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('modelSelectorBtn')?.click()");
                                await System.Threading.Tasks.Task.Delay(200);
                                await args.WebView.ExecuteScriptAsync("document.querySelectorAll('#modelDropdownMenu button')[0]?.click()");
                                await System.Threading.Tasks.Task.Delay(350);
                                var r5 = await args.WebView.ExecuteScriptAsync("JSON.stringify({ title790: document.getElementById('activeModelTitle')?.innerText, vecOpenAfterSwitch: !!document.getElementById('deckVectorSynthesizer') })");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('tabBtnPlayer')?.click()");
                                await System.Threading.Tasks.Task.Delay(300);
                                var r6 = await args.WebView.ExecuteScriptAsync("JSON.stringify({ playerOpen: !!document.getElementById('quickPickSidebar'), has790Tree: document.getElementById('treeContainer')?.innerText?.includes('Yamaha PSS-790'), has680Tree: document.getElementById('treeContainer')?.innerText?.includes('Yamaha PSS-680') })");
                                System.IO.File.WriteAllText(App.DiagMarkerPath, $"R1={r1}\nR2={r2}\nR2b={r2b}\nR3={r3}\nR4={r4}\nR5={r5}\nR6={r6}");
                            }
                            else if (!string.IsNullOrEmpty(App.SmokeMarkerPath))
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
                        }
                        catch (Exception ex)
                        {
                            var outPath = App.DiagMarkerPath ?? App.SmokeMarkerPath!;
                            System.IO.File.WriteAllText(outPath, "ERROR:" + ex.Message);
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
