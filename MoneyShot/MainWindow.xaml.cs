using System.Windows;
using System.Windows.Forms;
using MoneyShot.Services;
using MoneyShot.Views;
using Application = System.Windows.Application;

namespace MoneyShot;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ScreenshotService _screenshotService;
    private readonly SaveService _saveService;
    private readonly SettingsService _settingsService;
    private readonly HotKeyService _hotKeyService;
    private NotifyIcon? _notifyIcon;

    public MainWindow()
    {
        InitializeComponent();
        _screenshotService = new ScreenshotService();
        _saveService = new SaveService();
        _settingsService = new SettingsService();
        _hotKeyService = new HotKeyService();

        SetupSystemTray();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hotKeyService.Initialize(this);
        RegisterHotKeys();
    }

    private void RegisterHotKeys()
    {
        // Print Screen for full screen capture
        _hotKeyService.RegisterHotKey(0, HotKeyService.VK_SNAPSHOT, () =>
        {
            Dispatcher.Invoke(CaptureFullScreen);
        });

        // Ctrl + Print Screen for region capture
        _hotKeyService.RegisterHotKey(HotKeyService.MOD_CONTROL, HotKeyService.VK_SNAPSHOT, () =>
        {
            Dispatcher.Invoke(CaptureRegion);
        });
    }

    private void SetupSystemTray()
    {
        try
        {
            var processModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
            var iconPath = processModule?.FileName;
            
            System.Drawing.Icon? icon = null;
            if (iconPath != null)
            {
                icon = System.Drawing.Icon.ExtractAssociatedIcon(iconPath);
            }
            
            // Fallback to default icon if extraction fails
            icon ??= System.Drawing.SystemIcons.Application;

            _notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Visible = true,
                Text = "Money Shot - Screenshot Tool"
            };
        }
        catch
        {
            // If setup fails, use default icon
            _notifyIcon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Visible = true,
                Text = "Money Shot - Screenshot Tool"
            };
        }

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Capture Full Screen", null, (s, e) => CaptureFullScreen());
        contextMenu.Items.Add("Capture Region", null, (s, e) => CaptureRegion());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Settings", null, (s, e) => ShowSettings());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Show Window", null, (s, e) => ShowMainWindow());
        contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
    }

    private void CaptureFullScreen()
    {
        Hide();
        System.Threading.Thread.Sleep(200); // Small delay to hide the window

        var screenshot = _screenshotService.CaptureFullScreen();
        OpenEditor(screenshot);
    }

    private void CaptureRegion()
    {
        Hide();
        System.Threading.Thread.Sleep(200);

        var regionSelector = new RegionSelector();
        if (regionSelector.ShowDialog() == true && regionSelector.SelectedRegion != null)
        {
            var screenshot = _screenshotService.CaptureRegion(regionSelector.SelectedRegion.Value);
            OpenEditor(screenshot);
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void OpenEditor(System.Windows.Media.Imaging.BitmapSource screenshot)
    {
        var editor = new EditorWindow(screenshot);
        editor.ShowDialog();
        ShowMainWindow();
    }

    private void ShowSettings()
    {
        var settings = new SettingsWindow();
        settings.ShowDialog();
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _hotKeyService.UnregisterAll();
        _notifyIcon?.Dispose();
        Application.Current.Shutdown();
    }

    private void CaptureFullScreen_Click(object sender, RoutedEventArgs e)
    {
        CaptureFullScreen();
    }

    private void CaptureRegion_Click(object sender, RoutedEventArgs e)
    {
        CaptureRegion();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ShowSettings();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.MessageBox.Show(
            "Money Shot - Modern Screenshot Tool\n\n" +
            "Version 1.0.0\n\n" +
            "A comprehensive screenshot tool for Windows 11+ with annotation capabilities.\n\n" +
            "Features:\n" +
            "• Full screen and region capture\n" +
            "• Rich annotation tools (shapes, text, arrows, numbers)\n" +
            "• Save to file or clipboard\n" +
            "• Global hotkeys\n" +
            "• System tray integration\n\n" +
            "Hotkeys:\n" +
            "• Print Screen - Capture full screen\n" +
            "• Ctrl+Print Screen - Capture region",
            "About Money Shot",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _notifyIcon?.ShowBalloonTip(2000, "Money Shot", "App minimized to system tray", ToolTipIcon.Info);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        if (settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _notifyIcon?.ShowBalloonTip(2000, "Money Shot", "App is still running in the system tray", ToolTipIcon.Info);
        }
        else
        {
            ExitApplication();
        }
    }
}