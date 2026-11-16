using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using PSS790Player.Bridge;

namespace PSS790Player
{
    public partial class MainWindow : Window
    {
        private UiBridge? _uiBridge;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Set userDataFolder to local appdata to ensure single-file exe compatibility
                var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSS790Player", "WebView2");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

                await MainWebView.EnsureCoreWebView2Async(env);

                // Locate WebUI directory: prefer dev path if in source tree, then binary base dir, then local app data fallback
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
                    var localWebUi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PSS790Player", "WebUI");
                    EnsureWebUiExtracted(localWebUi);
                    webUiDir = localWebUi;
                }

                MainWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.pss790",
                    webUiDir,
                    CoreWebView2HostResourceAccessKind.Allow);

                // Clear HTTP disk cache to ensure fresh assets between updates
                try
                {
                    await MainWebView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
                }
                catch { }

                _uiBridge = new UiBridge(MainWebView);
                MainWebView.CoreWebView2.WebMessageReceived += (s, args) =>
                {
                    _uiBridge.HandleMessage(args.WebMessageAsJson);
                };

                MainWebView.CoreWebView2.Navigate($"https://app.pss790/index.html?t={DateTime.UtcNow.Ticks}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "PSS-790 Player Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

                        using var stream = assembly.GetManifestResourceStream(resName);
                        if (stream != null)
                        {
                            using var fs = new FileStream(destFile, FileMode.Create, FileAccess.Write);
                            stream.CopyTo(fs);
                        }
                    }
                }
            }
            catch { }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _uiBridge?.Dispose();
        }
    }
}
