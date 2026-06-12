using System.Windows;
using System.Threading;

namespace MoneyShot;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;
    private static bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Create a unique mutex name for the application
        const string mutexName = "MoneyShot_SingleInstance_Mutex_3E6F8A2D";

        _mutex = new Mutex(true, mutexName, out bool createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            // Another instance is already running
            MessageBox.Show(
                "Money Shot is already running. Check the system tray.",
                "Money Shot",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // StartupUri is intentionally not used. It calls Show() during startup, which paints an
        // unrendered (black) frame before the Loaded handler can hide it when StartInTray is set.
        // Instead we create the window here and let InitializeApplication decide whether to show
        // it or just realize its handle for hotkeys — see MainWindow.InitializeApplication.
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.InitializeApplication();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Only the instance that actually acquired the mutex may release it — calling
        // ReleaseMutex without ownership throws and would crash the "already running"
        // second instance on its way out.
        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
