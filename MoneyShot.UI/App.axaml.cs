using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using MoneyShot.UI.Views;

namespace MoneyShot.UI;

public partial class App : Application
{
    // Deliberately a different mutex name than the WPF build's ("MoneyShot_SingleInstance_Mutex_
    // 3E6F8A2D") — these are two separate builds of the same app (see LINUX_PORT.md Phase 1) that
    // may run side-by-side during evaluation, and sharing a mutex would make one refuse to start
    // just because the other happens to be running.
    private const string MutexName = "MoneyShot_SingleInstance_Mutex_UI_7B1D9E4A";
    private static Mutex? _mutex;
    private static bool _ownsMutex;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mutex = new Mutex(true, MutexName, out var createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                ShowAlreadyRunningDialogAndExit(desktop);
                return;
            }

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => OnExit();

            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            mainWindow.InitializeApplication();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Avalonia has no built-in MessageBox (unlike WPF), so the "already running" notice is a
    /// tiny inline window — same pattern EditorWindow's "Add text" prompt uses on the WPF side.
    /// </summary>
    private static void ShowAlreadyRunningDialogAndExit(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var notice = new Window
        {
            Title = "Money Shot",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = "Money Shot is already running. Check the system tray.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 16)
                    },
                    new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = HorizontalAlignment.Right,
                        IsDefault = true
                    }
                }
            }
        };

        if (notice.Content is StackPanel panel && panel.Children[1] is Button okButton)
        {
            okButton.Click += (_, _) => notice.Close();
        }

        desktop.MainWindow = notice;
        notice.Closed += (_, _) => desktop.Shutdown();
        notice.Show();
    }

    private static void OnExit()
    {
        // Only the instance that actually acquired the mutex may release it — mirrors the WPF
        // build's App.OnExit guard (calling ReleaseMutex without ownership throws).
        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }
        _mutex?.Dispose();
    }
}
