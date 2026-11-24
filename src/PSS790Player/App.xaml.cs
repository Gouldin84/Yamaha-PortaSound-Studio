using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PSS790Player.Core;

namespace PSS790Player;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            WriteCrashLog("DispatcherUnhandledException", e.Exception, isFatal: false);

            MessageBox.Show(
                $"An unexpected application error occurred:\n\n{e.Exception?.Message}\n\nDiagnostic details were written to:\n{GetCrashLogPath()}",
                "Yamaha PortaSound Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            e.Handled = true;
        }
        catch
        {
            // Do not crash the crash handler
        }
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            var ex = e.ExceptionObject as Exception;
            WriteCrashLog("AppDomain.UnhandledException", ex, isFatal: e.IsTerminating);
        }
        catch { }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception, isFatal: false);
            e.SetObserved();
        }
        catch { }
    }

    public static string GetCrashLogPath()
    {
        try
        {
            AppPaths.EnsureDirectoriesExist();
            return Path.Combine(AppPaths.MetadataDir, "crash.log");
        }
        catch
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
        }
    }

    public static void WriteCrashLog(string source, Exception? ex, bool isFatal)
    {
        try
        {
            var logPath = GetCrashLogPath();
            var dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var sb = new StringBuilder();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine($"Timestamp : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Source    : {source}");
            sb.AppendLine($"Fatal     : {isFatal}");
            sb.AppendLine($"OS        : {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
            sb.AppendLine($"Runtime   : .NET {Environment.Version}");
            sb.AppendLine($"Process   : {Environment.ProcessPath}");
            sb.AppendLine($"Exception : {ex?.GetType().FullName}");
            sb.AppendLine($"Message   : {ex?.Message}");
            sb.AppendLine($"StackTrace:\n{ex?.StackTrace}");

            if (ex?.InnerException != null)
            {
                sb.AppendLine($"InnerEx   : {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                sb.AppendLine($"InnerStack:\n{ex.InnerException.StackTrace}");
            }
            sb.AppendLine(new string('=', 80));
            sb.AppendLine();

            File.AppendAllText(logPath, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Suppress secondary crash logging failures
        }
    }
}

