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

                string webUiDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebUI");
                if (!Directory.Exists(webUiDir))
                {
                    // Fallback to source directory if running in dev mode
                    var devPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "WebUI"));
                    if (Directory.Exists(devPath)) webUiDir = devPath;
                }

                MainWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "app.pss790",
                    webUiDir,
                    CoreWebView2HostResourceAccessKind.Allow);

                _uiBridge = new UiBridge(MainWebView);
                MainWebView.CoreWebView2.WebMessageReceived += (s, args) =>
                {
                    _uiBridge.HandleMessage(args.WebMessageAsJson);
                };

                MainWebView.CoreWebView2.Navigate("https://app.pss790/index.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "PSS-790 Player Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _uiBridge?.Dispose();
        }
    }
}
