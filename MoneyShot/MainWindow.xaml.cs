using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
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
    
    // Maximum number of monitors that can have individual hotkeys (limited by number keys 1-9)
    private const int MaxMonitorHotkeys = 9;

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
        PopulateMonitorButtons();
        
        // Check if app should start in tray
        var settings = _settingsService.LoadSettings();
        if (settings.StartInTray)
        {
            Hide();
        }
    }

    private void PopulateMonitorButtons()
    {
        var screens = _screenshotService.GetAllScreens();
        if (screens.Count > 1)
        {
            var separator = new Separator
            {
                Margin = new Thickness(0, 10, 0, 5),
                Background = new SolidColorBrush(Color.FromRgb(85, 85, 85))
            };
            MonitorButtonsPanel.Children.Add(separator);

            var label = new TextBlock
            {
                Text = "Individual Monitors:",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                Margin = new Thickness(0, 5, 0, 5),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            MonitorButtonsPanel.Children.Add(label);

            for (int i = 0; i < screens.Count; i++)
            {
                var screenIndex = i;
                var screen = screens[i];
                var isPrimary = screen.Primary ? " (Primary)" : "";
                var button = new System.Windows.Controls.Button
                {
                    Content = $"🖥️ Monitor {i + 1}{isPrimary}",
                    Padding = new Thickness(20, 10, 20, 10),
                    Margin = new Thickness(0, 3, 0, 3),
                    FontSize = 14,
                    Background = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
                    Foreground = new SolidColorBrush(Colors.White),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                button.Click += (s, ev) => CaptureMonitor(screenIndex);
                MonitorButtonsPanel.Children.Add(button);
            }
        }
    }

    private void RegisterHotKeys()
    {
        var settings = _settingsService.LoadSettings();
        
        // Register hotkeys from settings
        _hotKeyService.RegisterHotKeyFromString(settings.HotKeyCapture, () =>
        {
            Dispatcher.Invoke(CaptureFullScreen);
        });

        _hotKeyService.RegisterHotKeyFromString(settings.HotKeyRegionCapture, () =>
        {
            Dispatcher.Invoke(CaptureRegion);
        });
        
        // Register PrintScreen + Number hotkeys for individual monitors
        var screens = _screenshotService.GetAllScreens();
        for (int i = 0; i < Math.Min(screens.Count, MaxMonitorHotkeys); i++)
        {
            var monitorIndex = i;
            var hotkey = $"PrintScreen+{i + 1}";
            _hotKeyService.RegisterHotKeyFromString(hotkey, () =>
            {
                Dispatcher.Invoke(() => CaptureMonitor(monitorIndex));
            });
        }
    }

    public void ReloadHotKeys()
    {
        // Unregister all existing hotkeys
        _hotKeyService.UnregisterAll();
        
        // Re-register with new settings
        RegisterHotKeys();
    }

    private void SetupSystemTray()
    {
        try
        {
            var processModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
            var iconPath = processModule?.FileName;
            
            System.Drawing.Icon? icon = null;
            if (iconPath != null && File.Exists(iconPath))
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
        catch (Exception ex)
        {
            // Log the error and use default icon
            System.Diagnostics.Debug.WriteLine($"Error setting up system tray icon: {ex.Message}");
            
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
        
        // Add individual monitor options
        var screens = _screenshotService.GetAllScreens();
        if (screens.Count > 1)
        {
            contextMenu.Items.Add("-");
            for (int i = 0; i < screens.Count; i++)
            {
                var screenIndex = i;
                var screen = screens[i];
                var isPrimary = screen.Primary ? " (Primary)" : "";
                contextMenu.Items.Add($"Capture Monitor {i + 1}{isPrimary}", null, (s, e) => CaptureMonitor(screenIndex));
            }
        }
        
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
        try
        {
            Hide();
            System.Threading.Thread.Sleep(200); // Small delay to hide the window

            var screenshot = _screenshotService.CaptureFullScreen();
            OpenEditor(screenshot);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error capturing full screen: {ex.Message}");
            System.Windows.MessageBox.Show($"Failed to capture screenshot: {ex.Message}", "Capture Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            ShowMainWindow();
        }
    }

    private void CaptureRegion()
    {
        try
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error capturing region: {ex.Message}");
            System.Windows.MessageBox.Show($"Failed to capture region: {ex.Message}", "Capture Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            ShowMainWindow();
        }
    }

    private void CaptureMonitor(int monitorIndex)
    {
        try
        {
            Hide();
            // Ensure window is completely hidden before capturing
            System.Windows.Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            System.Threading.Thread.Sleep(300);

            var screenshot = _screenshotService.CaptureScreen(monitorIndex);
            OpenEditor(screenshot);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error capturing monitor {monitorIndex}: {ex.Message}");
            System.Windows.MessageBox.Show($"Failed to capture monitor: {ex.Message}", "Capture Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            ShowMainWindow();
        }
    }

    private void OpenEditor(System.Windows.Media.Imaging.BitmapSource screenshot)
    {
        try
        {
            var editor = new EditorWindow(screenshot);
            editor.ShowDialog();
            // Don't show main window here - let it stay hidden as per user preference
            // Main window will only be shown if an error occurs (see catch block)
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening editor: {ex.Message}");
            System.Windows.MessageBox.Show($"Failed to open image editor: {ex.Message}", "Editor Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            // Show main window when error occurs so user knows something went wrong
            ShowMainWindow();
        }
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
        var settings = _settingsService.LoadSettings();
        System.Windows.MessageBox.Show(
            "Money Shot - Modern Screenshot Tool\n\n" +
            "Version 2.0.0\n\n" +
            "A comprehensive screenshot tool for Windows 11+ with annotation capabilities.\n\n" +
            "Features:\n" +
            "• Full screen, region, and individual monitor capture\n" +
            "• Multi-monitor support\n" +
            "• Rich annotation tools (shapes, text, arrows, numbers, blur)\n" +
            "• Customizable hotkeys\n" +
            "• Save to file or clipboard\n" +
            "• System tray integration\n" +
            "• Start in tray option\n\n" +
            "Current Hotkeys:\n" +
            $"• {settings.HotKeyCapture} - Capture full screen\n" +
            $"• {settings.HotKeyRegionCapture} - Capture region",
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