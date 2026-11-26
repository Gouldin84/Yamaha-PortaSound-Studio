using System;
using System.ComponentModel;
using System.Linq;
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
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private readonly ServiceProvider _serviceProvider;
        private readonly AppStateStore _state;
        private readonly HardwareDeviceService _devices;
        private readonly LibraryService _library;
        private readonly PlaybackCoordinator _playback;
        private readonly SoundDesignService _soundDesign;
        private readonly RecorderService _recorder;
        private string _lastAppliedModel = string.Empty;
        private bool? _lastAppliedDarkTheme;

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

            // Ensure initial window fits cleanly on scaled displays (e.g. ROG Ally 1920x1080 @ 150% = 1280x720 DIP)
            var workArea = SystemParameters.WorkArea;
            if (Width > workArea.Width) Width = Math.Max(MinWidth, workArea.Width - 16);
            if (Height > workArea.Height) Height = Math.Max(MinHeight, workArea.Height - 16);

            if (!string.IsNullOrEmpty(App.SmokeMarkerPath) || !string.IsNullOrEmpty(App.DiagMarkerPath) || !string.IsNullOrEmpty(App.ScreenshotsDirPath))
            {
                if (!string.IsNullOrEmpty(App.SmokeMarkerPath))
                {
                    WindowState = WindowState.Minimized;
                    ShowInTaskbar = false;
                }
                else
                {
                    if (!string.IsNullOrEmpty(App.ScreenshotsDirPath))
                    {
                        Width = 1280;
                        Height = 720;
                    }
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
                            if (!string.IsNullOrEmpty(App.ScreenshotsDirPath))
                            {
                                string outDir = App.ScreenshotsDirPath!;
                                System.IO.Directory.CreateDirectory(outDir);
                                async System.Threading.Tasks.Task Snap(string name)
                                {
                                    await System.Threading.Tasks.Task.Delay(500);
                                    var path = System.IO.Path.Combine(outDir, name);
                                    using var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                                    await args.WebView.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, fs);
                                }

                                async System.Threading.Tasks.Task SetTheme(bool dark, bool handheld = false)
                                {
                                    _state.Mutate(s => { s.IsDarkTheme = dark; s.HandheldMode = handheld; });
                                    await args.WebView.ExecuteScriptAsync($"studioInterop.applyBodyPreferences({(dark ? "true" : "false")}, {(handheld ? "true" : "false")}, '{_state.ActiveModel}')");
                                }

                                var firstSong = _state.Library.AllFiles.FirstOrDefault(f => !string.Equals(f.Format, "pss680", StringComparison.OrdinalIgnoreCase));
                                if (firstSong != null)
                                {
                                    await _playback.LoadFileAsync(firstSong.FullPath, autoPlay: false, settleDelayMs: 0);
                                }

                                _playback.SetActiveModel("pss790");
                                _state.Mutate(s => s.ActiveTab = StudioTab.Player);
                                await SetTheme(true);
                                await Snap("01_player_dark.png");

                                await SetTheme(false);
                                await Snap("02_player_light.png");

                                await SetTheme(true, handheld: true);
                                await Snap("07_touch_dark.png");

                                await SetTheme(false, handheld: true);
                                await Snap("08_touch_light.png");

                                _state.Mutate(s => { s.ActiveTab = StudioTab.SysEx; s.SysExViewMode = "vector"; });
                                await SetTheme(false, handheld: false);
                                await Snap("03_vector_light.png");

                                await SetTheme(true, handheld: false);
                                await Snap("04_vector_dark.png");

                                _playback.SetActiveModel("pss680");
                                _state.Mutate(s => { s.ActiveTab = StudioTab.SysEx; s.SysExViewMode = "fm"; });
                                await SetTheme(true);
                                await Snap("05_fm_dark.png");

                                _state.Mutate(s => s.ActiveTab = StudioTab.Recorder);
                                await SetTheme(true, handheld: false);
                                await Snap("06_recorder_dark.png");

                                await SetTheme(false, handheld: false);
                                await Snap("09_recorder_light.png");
                            }
                            else if (!string.IsNullOrEmpty(App.DiagMarkerPath))
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
                                var r4 = await args.WebView.ExecuteScriptAsync("JSON.stringify({ fmOpen: !!document.getElementById('deckFmSoundDesigner'), fmScopeOk: (document.getElementById('fmScopeCanvas')?.width || 0) > 0, modSvg: !!document.getElementById('svgFmModEnv'), carSvg: !!document.getElementById('svgFmCarEnv'), bodyModel: document.body.dataset.model })");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnSysexViewDrums')?.click()");
                                await System.Threading.Tasks.Task.Delay(250);
                                var r4b = await args.WebView.ExecuteScriptAsync("JSON.stringify({ mpcPads: document.querySelectorAll('#drumPadsContainer .mpc-pad').length, has03H: !!document.getElementById('drummerGridContainer') })");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnSysexViewDumps')?.click()");
                                await System.Threading.Tasks.Task.Delay(250);
                                var r4c = await args.WebView.ExecuteScriptAsync("JSON.stringify({ vaultOpen: !!document.getElementById('deckBulkDumps') })");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnSysexViewDesigner')?.click()");
                                await System.Threading.Tasks.Task.Delay(200);
                                await args.WebView.ExecuteScriptAsync("document.getElementById('modelSelectorBtn')?.click()");
                                await System.Threading.Tasks.Task.Delay(200);
                                await args.WebView.ExecuteScriptAsync("document.querySelectorAll('#modelDropdownMenu button')[0]?.click()");
                                await System.Threading.Tasks.Task.Delay(350);
                                var r5 = await args.WebView.ExecuteScriptAsync("JSON.stringify({ title790: document.getElementById('activeModelTitle')?.innerText, vecOpenAfterSwitch: !!document.getElementById('deckVectorSynthesizer'), vecCanvasW: document.getElementById('vecCanvas')?.width || 0, bodyModel: document.body.dataset.model })");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('tabBtnPlayer')?.click()");
                                await System.Threading.Tasks.Task.Delay(300);
                                var firstSong = _state.Library.AllFiles.FirstOrDefault(f => !string.Equals(f.Format, "pss680", StringComparison.OrdinalIgnoreCase));
                                if (firstSong != null)
                                {
                                    await _playback.LoadFileAsync(firstSong.FullPath, autoPlay: false);
                                    await System.Threading.Tasks.Task.Delay(400);
                                }
                                var loadedTitle = await args.WebView.ExecuteScriptAsync("document.getElementById('currentSongTitle')?.innerText");
                                var themeBefore = await args.WebView.ExecuteScriptAsync("document.body.classList.contains('theme-light')");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnThemeToggle')?.click()");
                                await System.Threading.Tasks.Task.Delay(250);
                                var themeAfter = await args.WebView.ExecuteScriptAsync("document.body.classList.contains('theme-light')");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnThemeToggle')?.click()");
                                await System.Threading.Tasks.Task.Delay(200);
                                var touchBefore = await args.WebView.ExecuteScriptAsync("document.body.classList.contains('handheld-touch-mode')");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnToggleHandheldMode')?.click()");
                                await System.Threading.Tasks.Task.Delay(200);
                                var touchAfter = await args.WebView.ExecuteScriptAsync("document.body.classList.contains('handheld-touch-mode')");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnToggleHandheldMode')?.click()");
                                await System.Threading.Tasks.Task.Delay(150);
                                await args.WebView.ExecuteScriptAsync("document.querySelector('#mixerGrid .btn-console-m')?.click()");
                                await System.Threading.Tasks.Task.Delay(200);
                                var mutedCh1 = await args.WebView.ExecuteScriptAsync("document.querySelector('#mixerGrid .btn-console-m')?.classList.contains('muted')");
                                await args.WebView.ExecuteScriptAsync("document.querySelector('#mixerGrid .btn-console-m')?.click()");
                                await System.Threading.Tasks.Task.Delay(150);
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnShortcuts')?.click(); document.getElementById('btnMoreTools')?.click();");
                                await System.Threading.Tasks.Task.Delay(150);
                                var moreOpen = await args.WebView.ExecuteScriptAsync("!!document.getElementById('moreToolsDropdownMenu')");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnVizWaterfall')?.click()");
                                await System.Threading.Tasks.Task.Delay(150);
                                var vizWf = await args.WebView.ExecuteScriptAsync("localStorage.getItem('yamaha_studio_viz_mode')");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnVizScope')?.click()");
                                await System.Threading.Tasks.Task.Delay(150);
                                var vizSc = await args.WebView.ExecuteScriptAsync("localStorage.getItem('yamaha_studio_viz_mode')");
                                await args.WebView.ExecuteScriptAsync("document.getElementById('btnVizSpectrum')?.click()");
                                await System.Threading.Tasks.Task.Delay(200);
                                var r6 = await args.WebView.ExecuteScriptAsync("JSON.stringify({ playerOpen: !!document.getElementById('quickPickSidebar'), has790Tree: document.getElementById('treeContainer')?.innerText?.includes('Yamaha PSS-790'), has680Tree: document.getElementById('treeContainer')?.innerText?.includes('Yamaha PSS-680'), stageW: document.getElementById('liveStageCanvas')?.width || 0, strips: document.querySelectorAll('#mixerGrid .channel-strip-card').length, faders: document.querySelectorAll('#mixerGrid input.v-fader-input').length, vizDefault: localStorage.getItem('yamaha_studio_viz_mode') })");
                                System.IO.File.WriteAllText(App.DiagMarkerPath, $"R1={r1}\nR2={r2}\nR2b={r2b}\nR3={r3}\nR4={r4}\nR4b={r4b}\nR4c={r4c}\nR5={r5}\nLOADED={loadedTitle}\nTHEME={themeBefore}->{themeAfter}\nTOUCH={touchBefore}->{touchAfter}\nMUTE_CH1={mutedCh1}\nMORE_OPEN={moreOpen}\nVIZ_WF={vizWf}\nVIZ_SC={vizSc}\nR6={r6}");
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
                ApplyWindowThemeChrome(source.Handle, _state.IsDarkTheme);
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
            bool isDark = _state.IsDarkTheme;
            if (!string.Equals(model, _lastAppliedModel, StringComparison.OrdinalIgnoreCase))
            {
                Dispatcher.BeginInvoke(() => ApplyWindowModelBranding(model));
            }
            if (_lastAppliedDarkTheme != isDark)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (PresentationSource.FromVisual(this) is HwndSource source)
                    {
                        ApplyWindowThemeChrome(source.Handle, isDark);
                    }
                });
            }
        }

        private void ApplyWindowThemeChrome(IntPtr hwnd, bool isDark)
        {
            _lastAppliedDarkTheme = isDark;
            try
            {
                int darkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                // COLORREF format is 0x00BBGGRR: Smoked Gunmetal (#1e2738) vs Anodized Silver (#e2e8f2)
                int captionColor = isDark ? 0x0038271E : 0x00F2E8E2;
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            }
            catch { }

            Background = new System.Windows.Media.SolidColorBrush(
                isDark
                    ? System.Windows.Media.Color.FromRgb(30, 39, 56)
                    : System.Windows.Media.Color.FromRgb(217, 224, 234));
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
