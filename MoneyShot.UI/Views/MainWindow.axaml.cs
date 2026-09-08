using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MoneyShot.Abstractions;
using MoneyShot.UI.Interop;
using MoneyShot.UI.Services;
using MoneyShot.Platform.Windows;
using MoneyShot.Services;
using Path = Avalonia.Controls.Shapes.Path;

namespace MoneyShot.UI.Views;

/// <summary>
/// Avalonia port of MoneyShot/MainWindow.xaml.cs — see LINUX_PORT.md Phase 1. Logic mirrors the
/// WPF version closely; the differences are all Avalonia API shape (async dialogs instead of
/// blocking MessageBox, Dispatcher.UIThread instead of WPF's Dispatcher, PointerPressed +
/// BeginMoveDrag instead of MouseLeftButtonDown + DragMove).
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

        Closing += Window_Closing;
        PropertyChanged += MainWindow_PropertyChanged;

        SetupSystemTray();
    }

    /// <summary>
    /// Mirrors MoneyShot/MainWindow.xaml.cs's InitializeApplication: binds global hotkeys and
    /// shows the window only when the user hasn't opted to start in the tray. Called once from
    /// App.OnFrameworkInitializationCompleted. IGlobalHotkeys owns its own native handle
    /// independent of this window (see Win32GlobalHotkeys), so nothing here needs the window to
    /// exist at all when starting hidden in the tray.
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
        catch (System.Exception ex)
        {
            Logger.Error("Main window startup failed", ex);
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            await CheckForUpdatesAsync(showUpToDateMessage: false, showErrorsToUser: false);
        }
        catch (System.Exception ex)
        {
            Logger.Error("Auto-update check failed", ex);
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
                    await SimpleMessageBox.ShowAsync(this, "You already have the latest version of Money Shot.", "No Updates Available");
                }
                return;
            }

            var result = await SimpleMessageBox.ShowAsync(this,
                $"A new version of MoneyShot is available ({updateInfo.RemoteVersion}). Install now?",
                "Update Available", SimpleMessageBoxButtons.YesNo);

            if (result != SimpleMessageBoxResult.Yes)
            {
                return;
            }

            await _autoUpdateService.StageAndPrepareUpdateAsync(updateInfo);
            _hotKeyService.UnregisterAll();
            _trayIcon?.Dispose();
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (System.Exception ex)
        {
            if (showErrorsToUser)
            {
                var guidance = ex.Message.Contains("GitHub Releases endpoint", System.StringComparison.OrdinalIgnoreCase)
                    ? "\n\nNo Money Shot configuration is required. Please ensure this device can access https://api.github.com (network/firewall/proxy) and try again."
                    : string.Empty;
                await SimpleMessageBox.ShowAsync(this, $"Failed to check for updates: {ex.Message}{guidance}", "Update Error");
            }
            else
            {
                Logger.Error("Auto-update check failed", ex);
            }
        }
    }

    private void PopulateMonitorButtons()
    {
        var screens = _screenshotService.GetAllMonitors();
        if (screens.Count <= 1) return;

        var resources = Avalonia.Application.Current!.Resources;
        var label = new TextBlock
        {
            Text = "Individual monitors",
            FontSize = 12,
            Foreground = (IBrush)resources["Cocoa.TextSecondaryBrush"]!,
            Margin = new Thickness(0, 10, 0, 6)
        };
        MonitorButtonsPanel.Children.Add(label);

        var subtleTheme = (Avalonia.Styling.ControlTheme)resources["SubtleButton"]!;
        var monitorIcon = (Geometry)resources["Icon.Monitor"]!;
        var iconTheme = (Avalonia.Styling.ControlTheme)resources["ToolIcon"]!;

        for (int i = 0; i < screens.Count; i++)
        {
            var screenIndex = i;
            var screen = screens[i];
            var isPrimary = screen.IsPrimary ? "  ·  primary" : string.Empty;
            var hotkeyHint = screenIndex < MaxMonitorHotkeys ? $"Ctrl+Shift+{screenIndex + 1}" : null;

            var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            content.Children.Add(new Path { Theme = iconTheme, Data = monitorIcon });
            content.Children.Add(new TextBlock
            {
                Text = $"Monitor {i + 1}{isPrimary}",
                Margin = new Thickness(9, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });

            var button = new Button
            {
                Content = content,
                Theme = subtleTheme,
                Padding = new Thickness(14, 9, 14, 9),
                Margin = new Thickness(0, 3, 0, 3),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left
            };
            if (hotkeyHint != null)
            {
                ToolTip.SetTip(button, $"Capture this monitor (hotkey: {hotkeyHint})");
            }
            button.Click += (_, _) => _ = CaptureMonitorAsync(screenIndex);
            MonitorButtonsPanel.Children.Add(button);
        }
    }

    private void RegisterHotKeys()
    {
        var settings = _settingsService.LoadSettings();

        _hotKeyService.RegisterHotKeyFromString(settings.HotKeyCapture, () =>
        {
            Dispatcher.UIThread.Post(() => _ = CaptureFullScreenAsync());
        });

        _hotKeyService.RegisterHotKeyFromString(settings.HotKeyRegionCapture, () =>
        {
            Dispatcher.UIThread.Post(() => _ = CaptureRegionAsync());
        });

        var screens = _screenshotService.GetAllMonitors();
        for (int i = 0; i < System.Math.Min(screens.Count, MaxMonitorHotkeys); i++)
        {
            var monitorIndex = i;
            var hotkey = $"Ctrl+Shift+{i + 1}";
            _hotKeyService.RegisterHotKeyFromString(hotkey, () =>
            {
                Dispatcher.UIThread.Post(() => _ = CaptureMonitorAsync(monitorIndex));
            });
        }
    }

    public void ReloadHotKeys()
    {
        _hotKeyService.UnregisterAll();
        RegisterHotKeys();
    }

    private void SetupSystemTray()
    {
        var menuItems = new System.Collections.Generic.List<TrayMenuItem>
        {
            new("Capture Full Screen", () => _ = CaptureFullScreenAsync()),
            new("Capture Region", () => _ = CaptureRegionAsync()),
        };

        var screens = _screenshotService.GetAllMonitors();
        if (screens.Count > 1)
        {
            menuItems.Add(TrayMenuItem.Separator());
            for (int i = 0; i < screens.Count; i++)
            {
                var screenIndex = i;
                var isPrimary = screens[i].IsPrimary ? " (Primary)" : "";
                menuItems.Add(new TrayMenuItem($"Capture Monitor {i + 1}{isPrimary}", () => _ = CaptureMonitorAsync(screenIndex)));
            }
        }

        menuItems.Add(TrayMenuItem.Separator());
        menuItems.Add(new TrayMenuItem("History", ShowHistory));
        menuItems.Add(new TrayMenuItem("Check for Updates", () => _ = CheckForUpdatesAsync(showUpToDateMessage: true, showErrorsToUser: true)));
        menuItems.Add(new TrayMenuItem("Settings", () => _ = ShowSettingsAsync()));
        menuItems.Add(TrayMenuItem.Separator());
        menuItems.Add(new TrayMenuItem("Show Window", ShowMainWindow));
        menuItems.Add(new TrayMenuItem("Exit", ExitApplication));

        _trayIcon = new Win32TrayIcon();
        _trayIcon.Show("Money Shot - Screenshot Tool", menuItems);
        _trayIcon.DoubleClicked += ShowMainWindow;
    }

    private async Task CaptureFullScreenAsync()
    {
        try
        {
            Hide();
            await Task.Delay(200);

            var screenshot = _screenshotService.CaptureFullScreen().ToAvaloniaBitmap();
            await OpenEditorAsync(screenshot, "FullScreen");
        }
        catch (System.Exception ex)
        {
            Logger.Error("Error capturing full screen", ex);
            await SimpleMessageBox.ShowAsync(this, $"Failed to capture screenshot: {ex.Message}", "Capture Error");
            ShowMainWindow();
        }
    }

    private async Task CaptureRegionAsync()
    {
        try
        {
            var settings = _settingsService.LoadSettings();
            if (settings.HideUiFromScreenshots)
            {
                Hide();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                await Task.Delay(300);
            }

            var frozenScreen = _screenshotService.CaptureFullScreen().ToAvaloniaBitmap();

            if (!settings.HideUiFromScreenshots)
            {
                Hide();
                await Task.Delay(200);
            }

            var regionSelector = new RegionSelector(frozenScreen);
            var confirmed = await regionSelector.ShowDialog<bool>(this);
            if (confirmed && regionSelector.CroppedScreenshot != null)
            {
                await OpenEditorAsync(regionSelector.CroppedScreenshot, "Region");
            }
            else
            {
                ShowMainWindow();
            }
        }
        catch (System.Exception ex)
        {
            Logger.Error("Error capturing region", ex);
            await SimpleMessageBox.ShowAsync(this, $"Failed to capture region: {ex.Message}", "Capture Error");
            ShowMainWindow();
        }
    }

    private async Task CaptureMonitorAsync(int monitorIndex)
    {
        try
        {
            Hide();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(300);

            var screenshot = _screenshotService.CaptureMonitor(monitorIndex).ToAvaloniaBitmap();
            await OpenEditorAsync(screenshot, $"Monitor {monitorIndex + 1}");
        }
        catch (System.Exception ex)
        {
            Logger.Error($"Error capturing monitor {monitorIndex}", ex);
            await SimpleMessageBox.ShowAsync(this, $"Failed to capture monitor: {ex.Message}", "Capture Error");
            ShowMainWindow();
        }
    }

    private async Task OpenEditorAsync(Avalonia.Media.Imaging.Bitmap screenshot, string source = "Capture")
    {
        try
        {
            var settings = _settingsService.LoadSettings();
            if (settings.SaveCapturesToHistory)
            {
                _historyService.Save(screenshot, source, settings.HistoryRetentionCount);
            }

            var editor = new EditorWindow(screenshot);
            await editor.ShowDialog(this);
        }
        catch (System.Exception ex)
        {
            Logger.Error("Error opening editor", ex);
            await SimpleMessageBox.ShowAsync(this, $"Failed to open image editor: {ex.Message}", "Editor Error");
            ShowMainWindow();
        }
        finally
        {
            MemoryTrimmer.TrimAfterEditorClose();
        }
    }

    private async Task ShowSettingsAsync()
    {
        var settings = new SettingsWindow();
        await settings.ShowDialog(this);
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
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void CaptureFullScreen_Click(object? sender, RoutedEventArgs e) => _ = CaptureFullScreenAsync();
    private void CaptureRegion_Click(object? sender, RoutedEventArgs e) => _ = CaptureRegionAsync();
    private void Settings_Click(object? sender, RoutedEventArgs e) => _ = ShowSettingsAsync();
    private void History_Click(object? sender, RoutedEventArgs e) => ShowHistory();

    private async void About_Click(object? sender, RoutedEventArgs e)
    {
        var settings = _settingsService.LoadSettings();
        var screens = _screenshotService.GetAllMonitors();
        var monitorHotkeys = screens.Count > 1 ? $"\n• Ctrl+Shift+1-{System.Math.Min(screens.Count, MaxMonitorHotkeys)} — Capture individual monitors" : "";
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var displayVersion = version == null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";

        await SimpleMessageBox.ShowAsync(this,
            "Money Shot — screenshot capture and annotation (Avalonia build)\n\n" +
            $"Version {displayVersion}\n" +
            "Developed by Daolyap & iSaluki\n\n" +
            "Current hotkeys:\n" +
            $"• {settings.HotKeyCapture} — Capture full screen\n" +
            $"• {settings.HotKeyRegionCapture} — Capture region" +
            monitorHotkeys +
            "\n\nFeedback & issues: https://github.com/Daolyap/Money-Shot/issues\n" +
            "Security contact: moneyshot@daolyap.dev",
            "About Money Shot");
    }

    private void CheckForUpdates_Click(object? sender, RoutedEventArgs e) =>
        _ = CheckForUpdatesAsync(showUpToDateMessage: true, showErrorsToUser: true);

    private void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized)
        {
            Hide();
            _trayIcon?.ShowBalloonTip(2000, "Money Shot", "App minimized to system tray");
        }
    }

    private void Window_Closing(object? sender, WindowClosingEventArgs e)
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

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeRestore_Click(sender, new RoutedEventArgs());
        }
        else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object? sender, RoutedEventArgs e)
    {
        var resources = Avalonia.Application.Current!.Resources;
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            if (MaximizeRestoreIcon != null) MaximizeRestoreIcon.Data = (Geometry)resources["Icon.WindowMaximize"]!;
        }
        else
        {
            WindowState = WindowState.Maximized;
            if (MaximizeRestoreIcon != null) MaximizeRestoreIcon.Data = (Geometry)resources["Icon.WindowRestore"]!;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
