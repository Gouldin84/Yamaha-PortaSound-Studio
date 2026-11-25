using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using PSS790Player.Bridge;

namespace PSS790Player
{
    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private UiBridge? _uiBridge;
        private HwndSource? _hwndSource;

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("PSS790Player.Assets.app.ico")
                                ?? assembly.GetManifestResourceStream("Assets.app.ico");
                if (stream != null)
                {
                    Icon = System.Windows.Media.Imaging.BitmapFrame.Create(stream);
                }
            }
            catch { }
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSS790Player", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

                await MainWebView.EnsureCoreWebView2Async(env);

                string webUiDir;
                var devPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "WebUI"));
                var baseWebUi = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebUI");

                if (Directory.Exists(devPath) && File.Exists(Path.Combine(devPath, "index.html")))
                {
                    webUiDir = devPath;
                }
                else if (Directory.Exists(baseWebUi) && File.Exists(Path.Combine(baseWebUi, "index.html")))
                {
                    webUiDir = baseWebUi;
                }
                else
                {
                    var versionStr = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "2.9.4";
                    var localWebUi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSS790Player", $"WebUI_{versionStr}");
                    EnsureWebUiExtracted(localWebUi);
                    webUiDir = localWebUi;
                }

                MainWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.pss790",
                    webUiDir,
                    CoreWebView2HostResourceAccessKind.Allow);

                try
                {
                    MainWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                    MainWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                    MainWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                }
                catch { }

                MainWebView.CoreWebView2.ProcessFailed += OnWebViewProcessFailed;

                try
                {
                    await MainWebView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
                }
                catch { }

                _uiBridge = new UiBridge(MainWebView);
                MainWebView.CoreWebView2.WebMessageReceived += (s, args) =>
                {
                    _uiBridge?.HandleMessage(args.WebMessageAsJson);
                };

                MainWebView.CoreWebView2.NavigationCompleted += (s, args) =>
                {
                    if (args.IsSuccess && _uiBridge != null)
                    {
                        _uiBridge.HandleGetInitialData();
                    }
                };

                MainWebView.CoreWebView2.Navigate($"https://app.pss790/index.html?t={DateTime.UtcNow.Ticks}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Yamaha PortaSound Studio Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void EnsureWebUiExtracted(string targetDir)
        {
            try
            {
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();
                foreach (var resName in resourceNames)
                {
                    if (resName.Contains(".WebUI."))
                    {
                        var relativePath = resName.Substring(resName.IndexOf(".WebUI.") + 7);
                        var lastDot = relativePath.LastIndexOf('.');
                        var ext = lastDot >= 0 ? relativePath.Substring(lastDot) : "";
                        var pathWithoutExt = lastDot >= 0 ? relativePath.Substring(0, lastDot) : relativePath;
                        var subPath = pathWithoutExt.Replace('.', Path.DirectorySeparatorChar) + ext;

                        var destFile = Path.Combine(targetDir, subPath);
                        var destFolder = Path.GetDirectoryName(destFile);
                        if (!string.IsNullOrEmpty(destFolder) && !Directory.Exists(destFolder))
                        {
                            Directory.CreateDirectory(destFolder);
                        }

                        try
                        {
                            using var stream = assembly.GetManifestResourceStream(resName);
                            if (stream != null)
                            {
                                using var fs = new FileStream(destFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                                stream.CopyTo(fs);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var helper = new WindowInteropHelper(this);
                _hwndSource = HwndSource.FromHwnd(helper.Handle);
                _hwndSource?.AddHook(HwndMessageHook);
            }
            catch { }
        }

        private IntPtr HwndMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                _uiBridge?.HandleDeviceChangeNotification();
            }
            return IntPtr.Zero;
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                if (MainWebView?.CoreWebView2 != null)
                {
                    MainWebView.CoreWebView2.ProcessFailed -= OnWebViewProcessFailed;
                }
            }
            catch { }

            try
            {
                _hwndSource?.RemoveHook(HwndMessageHook);
                _hwndSource = null;
            }
            catch { }

            _uiBridge?.Dispose();
            _uiBridge = null;
        }

        private void OnWebViewProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            try
            {
                App.WriteCrashLog(
                    $"CoreWebView2.ProcessFailed: {e.ProcessFailedKind} (ExitCode: {e.ExitCode})",
                    new Exception($"WebView2 child process failed: {e.ProcessFailedKind}, Reason: {e.Reason}, FailureSource: {e.FailureSourceModulePath}"),
                    isFatal: false);

                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited ||
                    e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            MainWebView?.CoreWebView2?.Reload();
                        }
                        catch { }
                    });
                }
            }
            catch { }
        }
    }
}
