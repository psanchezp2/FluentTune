using System.IO;
using System.Windows;

namespace FluentTune;

public partial class App
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "fluenttune.log");

    public static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {msg}{Environment.NewLine}"); }
        catch { /* logging must never throw */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { File.WriteAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  === startup ==={Environment.NewLine}"); } catch { }

        DispatcherUnhandledException += (_, args) =>
        {
            Log("DispatcherUnhandled: " + args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log("DomainUnhandled: " + args.ExceptionObject);

        // Tray-resident: the app keeps running when the flyout hides; only the
        // tray "Salir" command shuts it down.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var window = new MainWindow();
            Log("MainWindow constructed");
            window.Show();
            Log("MainWindow.Show() returned");
        }
        catch (Exception ex)
        {
            Log("Startup exception: " + ex);
        }
    }
}
