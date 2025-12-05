using System.Configuration;
using System.Data;
using System.Windows;
using System.Threading;

namespace MoneyShot;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        // Create a unique mutex name for the application
        const string mutexName = "MoneyShot_SingleInstance_Mutex_3E6F8A2D";
        
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        
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
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

