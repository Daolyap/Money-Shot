using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MoneyShot.Abstractions;
using MoneyShot.Interop;
using MoneyShot.Platform.Windows;
using MoneyShot.Services;
using MoneyShot.Views;
using Application = System.Windows.Application;

namespace MoneyShot;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly IScreenCapture _screenshotService;
    private readonly SaveService _saveService;
    private readonly SettingsService _settingsService;
    private readonly IGlobalHotkeys _hotKeyService;
    private readonly AutoUpdateService _autoUpdateService;
    private readonly HistoryService _historyService;
    private ITrayIcon? _trayIcon;

    // Maximum number of monitors that can have individual hotkeys (limited by number keys 1-9)
    private const int MaxMonitorHotkeys = 9;

    public MainWindow()
    {
        InitializeComponent();
        _screenshotService = new Win32ScreenCapture();
        _saveService = new SaveService(new Win32Clipboard());
        _settingsService = new SettingsService();
        _hotKeyService = new Win32GlobalHotkeys();
        _autoUpdateService = new AutoUpdateService();
        _historyService = new HistoryService();

        SetupSystemTray();
    }

    /// <summary>
    /// Performs post-construction startup: binds global hotkeys, and shows the window only when
    /// the user hasn't opted to start in the tray. Called once from <see cref="App.OnStartup"/>.
    /// When <see cref="Models.AppSettings.StartInTray"/> is set the window is never shown or
    /// realized at all — <see cref="IGlobalHotkeys"/> owns its own native handle independent of
    /// the main window (see Win32GlobalHotkeys), so there's nothing here that needs the window's
    /// HWND to exist up front, unlike the Show()-then-Hide() flash this used to work around.
    /// </summary>
    public void InitializeApplication()
    {
        try
        {
            var settings = _settingsService.LoadSettings();

            if (!settings.StartInTray)
            {
                ShowMainWindow();
            }

            _hotKeyService.Initialize();
            RegisterHotKeys();
            PopulateMonitorButtons();

            if (settings.CheckForUpdatesOnStartup)
            {
                _ = CheckForUpdatesOnStartupAsync();
            }
        }
        catch (Exception ex)
        {
            MoneyShot.Services.Logger.Error("Main window startup failed", ex);
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await CheckForUpdatesAsync(showUpToDateMessage: false, showErrorsToUser: false);
        }
        catch (Exception ex)
        {
            MoneyShot.Services.Logger.Error("Auto-update check failed", ex);
        }
    }

    private async Task CheckForUpdatesAsync(bool showUpToDateMessage, bool showErrorsToUser)
    {
        try
        {
            var updateInfo = await _autoUpdateService.GetAvailableUpdateAsync();
            if (updateInfo == null)
            {
                if (showUpToDateMessage)
                {
                    System.Windows.MessageBox.Show(
                        "You already have the latest version of Money Shot.",
                        "No Updates Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }

            var result = System.Windows.MessageBox.Show(
                $"A new version of MoneyShot is available ({updateInfo.RemoteVersion}). Install now?",
                "Update Available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            await _autoUpdateService.StageAndPrepareUpdateAsync(updateInfo);
            _hotKeyService.UnregisterAll();
            _trayIcon?.Dispose();
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            if (showErrorsToUser)
            {
                var guidance = ex.Message.Contains("GitHub Releases endpoint", StringComparison.OrdinalIgnoreCase)
                    ? "\n\nNo Money Shot configuration is required. Please ensure this device can access https://api.github.com (network/firewall/proxy) and try again."
                    : string.Empty;
                System.Windows.MessageBox.Show(
                    $"Failed to check for updates: {ex.Message}{guidance}",
                    "Update Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                MoneyShot.Services.Logger.Error("Auto-update check failed", ex);
            }
        }
    }

    private void PopulateMonitorButtons()
    {
        var screens = _screenshotService.GetAllMonitors();
        if (screens.Count <= 1) return;

        var label = new TextBlock
        {
            Text = "Individual monitors",
            FontSize = 12,
            Foreground = (Brush)FindResource("Cocoa.TextSecondaryBrush"),
            Margin = new Thickness(0, 10, 0, 6)
        };
        MonitorButtonsPanel.Children.Add(label);

        var subtleStyle = (Style)FindResource("SubtleButton");
        var monitorIcon = (Geometry)FindResource("Icon.Monitor");
        var iconStyle = (Style)FindResource("ToolIcon");
        for (int i = 0; i < screens.Count; i++)
        {
            var screenIndex = i;
            var screen = screens[i];
            var isPrimary = screen.IsPrimary ? "  ·  primary" : string.Empty;
            var hotkeyHint = screenIndex < MaxMonitorHotkeys ? $"Ctrl+Shift+{screenIndex + 1}" : null;

            var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            content.Children.Add(new System.Windows.Shapes.Path { Style = iconStyle, Data = monitorIcon });
            content.Children.Add(new TextBlock
            {
                Text = $"Monitor {i + 1}{isPrimary}",
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });

            var button = new System.Windows.Controls.Button
            {
                Content = content,
                Style = subtleStyle,
                Padding = new Thickness(14, 9, 14, 9),
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                ToolTip = hotkeyHint == null ? null : $"Capture this monitor (hotkey: {hotkeyHint})"
            };
            button.Click += (s, ev) => CaptureMonitor(screenIndex);
            MonitorButtonsPanel.Children.Add(button);
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

        // Register Ctrl+Shift+Number hotkeys for individual monitors (PrintScreen+Number not supported by Windows API)
        var screens = _screenshotService.GetAllMonitors();
        for (int i = 0; i < Math.Min(screens.Count, MaxMonitorHotkeys); i++)
        {
            var monitorIndex = i;
            var hotkey = $"Ctrl+Shift+{i + 1}";
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
        var menuItems = new List<TrayMenuItem>
        {
            new("Capture Full Screen", CaptureFullScreen),
            new("Capture Region", CaptureRegion),
        };

        // Add individual monitor options
        var screens = _screenshotService.GetAllMonitors();
        if (screens.Count > 1)
        {
            menuItems.Add(TrayMenuItem.Separator());
            for (int i = 0; i < screens.Count; i++)
            {
                var screenIndex = i;
                var isPrimary = screens[i].IsPrimary ? " (Primary)" : "";
                menuItems.Add(new TrayMenuItem($"Capture Monitor {i + 1}{isPrimary}", () => CaptureMonitor(screenIndex)));
            }
        }

        menuItems.Add(TrayMenuItem.Separator());
        menuItems.Add(new TrayMenuItem("History", ShowHistory));
        menuItems.Add(new TrayMenuItem("Check for Updates", async () => await CheckForUpdatesAsync(showUpToDateMessage: true, showErrorsToUser: true)));
        menuItems.Add(new TrayMenuItem("Settings", ShowSettings));
        menuItems.Add(TrayMenuItem.Separator());
        menuItems.Add(new TrayMenuItem("Show Window", ShowMainWindow));
        menuItems.Add(new TrayMenuItem("Exit", ExitApplication));

        _trayIcon = new Win32TrayIcon();
        _trayIcon.Show("Money Shot - Screenshot Tool", menuItems);
        _trayIcon.DoubleClicked += ShowMainWindow;
    }

    private void CaptureFullScreen()
    {
        try
        {
            Hide();
            System.Threading.Thread.Sleep(200); // Small delay to hide the window

            var screenshot = _screenshotService.CaptureFullScreen().ToBitmapSource();
            OpenEditor(screenshot, "FullScreen");
        }
        catch (Exception ex)
        {
            MoneyShot.Services.Logger.Error("Error capturing full screen", ex);
            System.Windows.MessageBox.Show($"Failed to capture screenshot: {ex.Message}", "Capture Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ShowMainWindow();
        }
    }

    private void CaptureRegion()
    {
        try
        {
            var settings = _settingsService.LoadSettings();
            if (settings.HideUiFromScreenshots)
            {
                Hide();
                System.Windows.Application.Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                System.Threading.Thread.Sleep(300);
            }

            // Capture frozen snapshot after hide when invisibility cloak is enabled.
            var frozenScreen = _screenshotService.CaptureFullScreen().ToBitmapSource();

            if (!settings.HideUiFromScreenshots)
            {
                Hide();
                System.Threading.Thread.Sleep(200);
            }

            var regionSelector = new RegionSelector(frozenScreen);
            if (regionSelector.ShowDialog() == true && regionSelector.CroppedScreenshot != null)
            {
                // Use the cropped screenshot from the frozen screen, not a new capture
                OpenEditor(regionSelector.CroppedScreenshot, "Region");
            }
            else
            {
                ShowMainWindow();
            }
        }
        catch (Exception ex)
        {
            MoneyShot.Services.Logger.Error("Error capturing region", ex);
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

            var screenshot = _screenshotService.CaptureMonitor(monitorIndex).ToBitmapSource();
            OpenEditor(screenshot, $"Monitor {monitorIndex + 1}");
        }
        catch (Exception ex)
        {
            MoneyShot.Services.Logger.Error($"Error capturing monitor {monitorIndex}", ex);
            System.Windows.MessageBox.Show($"Failed to capture monitor: {ex.Message}", "Capture Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ShowMainWindow();
        }
    }

    private void OpenEditor(System.Windows.Media.Imaging.BitmapSource screenshot, string source = "Capture")
    {
        try
        {
            // Persist to local history before the user starts editing — this captures the raw
            // screenshot, not the annotated final, so the history is a record of what the screen
            // actually showed. Local-only by design; never uploaded.
            var settings = _settingsService.LoadSettings();
            if (settings.SaveCapturesToHistory)
            {
                _historyService.Save(screenshot, source, settings.HistoryRetentionCount);
            }

            var editor = new EditorWindow(screenshot);
            editor.ShowDialog();
            // Don't show main window here - let it stay hidden as per user preference
            // Main window will only be shown if an error occurs (see catch block)
        }
        catch (Exception ex)
        {
            MoneyShot.Services.Logger.Error("Error opening editor", ex);
            System.Windows.MessageBox.Show($"Failed to open image editor: {ex.Message}", "Editor Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            // Show main window when error occurs so user knows something went wrong
            ShowMainWindow();
        }
        finally
        {
            // The editor's BitmapSource backings, RenderTargetBitmaps and pixelate brushes live in
            // both managed and native heaps — see MemoryTrimmer for why this is forced here.
            MemoryTrimmer.TrimAfterEditorClose();
        }
    }

    private void ShowSettings()
    {
        var settings = new SettingsWindow();
        settings.ShowDialog();
    }

    private void ShowHistory()
    {
        var history = new HistoryWindow(_historyService);
        history.Show();
        history.Activate();
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
        _trayIcon?.Dispose();
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

    private void History_Click(object sender, RoutedEventArgs e)
    {
        ShowHistory();
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        var screens = _screenshotService.GetAllMonitors();
        var monitorHotkeys = screens.Count > 1 ? $"\n• Ctrl+Shift+1-{Math.Min(screens.Count, MaxMonitorHotkeys)} — Capture individual monitors" : "";
        // SemVer portion only — the 4th part is the CI build number and isn't meaningful to users.
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var displayVersion = version == null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";

        System.Windows.MessageBox.Show(
            "Money Shot — screenshot capture and annotation for Windows\n\n" +
            $"Version {displayVersion}\n" +
            "Developed by Daolyap & iSaluki\n\n" +
            "Current hotkeys:\n" +
            $"• {settings.HotKeyCapture} — Capture full screen\n" +
            $"• {settings.HotKeyRegionCapture} — Capture region" +
            monitorHotkeys +
            "\n\nFeedback & issues: https://github.com/Daolyap/Money-Shot/issues\n" +
            "Security contact: moneyshot@daolyap.dev",
            "About Money Shot",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(showUpToDateMessage: true, showErrorsToUser: true);
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _trayIcon?.ShowBalloonTip(2000, "Money Shot", "App minimized to system tray");
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        if (settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _trayIcon?.ShowBalloonTip(2000, "Money Shot", "App is still running in the system tray");
        }
        else
        {
            ExitApplication();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to maximize/restore
            MaximizeRestore_Click(sender, e);
        }
        else if (e.ClickCount == 1)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove can throw if window state is changing or mouse is not pressed
                // Silently ignore these cases
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaximizeRestoreIcon.Data = (Geometry)FindResource("Icon.WindowMaximize");
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaximizeRestoreIcon.Data = (Geometry)FindResource("Icon.WindowRestore");
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
