using System;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MoneyShot.Abstractions;
using MoneyShot.Models;
using MoneyShot.Platform.Windows;
using MoneyShot.Services;
using MoneyShot.UI.Services;

namespace MoneyShot.UI.Views;

/// <summary>
/// Avalonia port of MoneyShot/Views/SettingsWindow.xaml.cs — see LINUX_PORT.md Phase 1. Logic is
/// unchanged; MessageBox.Show becomes async SimpleMessageBox.ShowAsync, and WinForms'
/// FolderBrowserDialog becomes Avalonia's async IStorageProvider.OpenFolderPickerAsync.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly IScreenCapture _screenCapture;
    private readonly IAutoStart _autoStart;
    private AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        _screenCapture = new Win32ScreenCapture();
        _autoStart = new Win32AutoStart();
        _settings = _settingsService.LoadSettings();
        LoadSettings();
        LoadMonitorHotkeysInfo();
    }

    private void LoadMonitorHotkeysInfo()
    {
        var screens = _screenCapture.GetAllMonitors();

        if (screens.Count > 1)
        {
            var hotkeys = new StringBuilder();
            for (int i = 0; i < Math.Min(screens.Count, 9); i++)
            {
                if (i > 0) hotkeys.Append(", ");
                hotkeys.Append($"Ctrl+Shift+{i + 1}");
            }
            MonitorHotkeysInfo.Text = $"Detected {screens.Count} monitor(s). Hotkeys: {hotkeys}";
        }
        else
        {
            MonitorHotkeysInfo.Text = "Only one monitor detected. Individual monitor hotkeys are not available.";
        }
    }

    private void LoadSettings()
    {
        StartInTrayCheckbox.IsChecked = _settings.StartInTray;
        RunOnStartupCheckbox.IsChecked = _settings.RunOnStartup;
        MinimizeToTrayCheckbox.IsChecked = _settings.MinimizeToTray;
        CheckForUpdatesCheckbox.IsChecked = _settings.CheckForUpdatesOnStartup;
        HideUiFromScreenshotsCheckbox.IsChecked = _settings.HideUiFromScreenshots;
        if (_autoStart.TryGetPrintScreenSuppressed(out var isPrintScreenDisabled))
        {
            _settings.DisableWindowsPrintScreen = isPrintScreenDisabled;
        }

        DisableWindowsPrintScreenCheckbox.IsChecked = _settings.DisableWindowsPrintScreen;
        SavePathTextBox.Text = _settings.DefaultSavePath;

        SaveToClipboardRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.Clipboard;
        SaveToFileRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.File;
        SaveToBothRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.Both;

        SelectComboBoxItem(FormatComboBox, _settings.DefaultFileFormat);

        SaveToHistoryCheckbox.IsChecked = _settings.SaveCapturesToHistory;
        HistoryRetentionTextBox.Text = _settings.HistoryRetentionCount.ToString();
        HistoryFolderText.Text = new HistoryService().HistoryDirectory;

        SelectComboBoxItem(HotKeyCaptureComboBox, _settings.HotKeyCapture);
        SelectComboBoxItem(HotKeyRegionCaptureComboBox, _settings.HotKeyRegionCapture);
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string value)
    {
        foreach (var obj in comboBox.Items)
        {
            if (obj is ComboBoxItem item && item.Content?.ToString() == value)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private async void BrowsePath_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var startFolder = await StorageProvider.TryGetFolderFromPathAsync(new Uri(_settings.DefaultSavePath));
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose default save folder",
                SuggestedStartLocation = startFolder,
                AllowMultiple = false
            });

            var selected = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrWhiteSpace(selected) && System.IO.Directory.Exists(selected))
            {
                SavePathTextBox.Text = selected;
            }
        }
        catch (Exception ex)
        {
            await SimpleMessageBox.ShowAsync(this, $"Error selecting folder: {ex.Message}", "Error");
        }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            _settings.StartInTray = StartInTrayCheckbox.IsChecked ?? true;
            _settings.RunOnStartup = RunOnStartupCheckbox.IsChecked ?? false;
            _settings.MinimizeToTray = MinimizeToTrayCheckbox.IsChecked ?? false;
            _settings.CheckForUpdatesOnStartup = CheckForUpdatesCheckbox.IsChecked ?? true;
            _settings.HideUiFromScreenshots = HideUiFromScreenshotsCheckbox.IsChecked ?? true;
            _settings.DisableWindowsPrintScreen = DisableWindowsPrintScreenCheckbox.IsChecked ?? false;
            _settings.DefaultSavePath = SavePathTextBox.Text ?? _settings.DefaultSavePath;

            if (SaveToClipboardRadio.IsChecked == true)
                _settings.DefaultSaveDestination = SaveDestination.Clipboard;
            else if (SaveToFileRadio.IsChecked == true)
                _settings.DefaultSaveDestination = SaveDestination.File;
            else
                _settings.DefaultSaveDestination = SaveDestination.Both;

            if (FormatComboBox.SelectedItem is ComboBoxItem formatItem)
                _settings.DefaultFileFormat = formatItem.Content?.ToString() ?? "PNG";

            _settings.SaveCapturesToHistory = SaveToHistoryCheckbox.IsChecked ?? true;
            if (int.TryParse(HistoryRetentionTextBox.Text, out var retention))
            {
                _settings.HistoryRetentionCount = Math.Clamp(retention, 0, 500);
            }

            if (HotKeyCaptureComboBox.SelectedItem is ComboBoxItem captureItem)
                _settings.HotKeyCapture = captureItem.Content?.ToString() ?? "PrintScreen";

            if (HotKeyRegionCaptureComboBox.SelectedItem is ComboBoxItem regionItem)
                _settings.HotKeyRegionCapture = regionItem.Content?.ToString() ?? "Ctrl+PrintScreen";

            _settingsService.SaveSettings(_settings);

            try
            {
                _autoStart.SetStartupWithApp(_settings.RunOnStartup);
            }
            catch (InvalidOperationException ex)
            {
                await SimpleMessageBox.ShowAsync(this, $"Warning: {ex.Message}\nOther settings were saved successfully.", "Partial Success");
            }

            var printScreenApplied = _autoStart.SetPrintScreenSuppressed(_settings.DisableWindowsPrintScreen);

            if (Owner is MainWindow mainWindow)
            {
                mainWindow.ReloadHotKeys();
            }

            var successMessage = printScreenApplied
                ? "Settings saved successfully! Hotkeys have been updated."
                : "Settings saved, but Windows Print Screen integration could not be fully updated. You may need to reopen the app as admin or update the Print Screen snipping setting in Windows keyboard settings.";

            await SimpleMessageBox.ShowAsync(this, successMessage, printScreenApplied ? "Success" : "Partial Success");
            Close();
        }
        catch (Exception ex)
        {
            await SimpleMessageBox.ShowAsync(this, $"Error saving settings: {ex.Message}", "Error");
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ViewLogs_Click(object? sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(Logger.LogDirectoryPath);
    }

    private void OpenHistoryFolder_Click(object? sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(new HistoryService().HistoryDirectory);
    }

    private static void OpenFolderInExplorer(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to open folder '{path}'", ex);
        }
    }

    private async void ClearHistory_Click(object? sender, RoutedEventArgs e)
    {
        var history = new HistoryService();
        var entries = history.List();
        if (entries.Count == 0)
        {
            await SimpleMessageBox.ShowAsync(this, "History is already empty.", "Money Shot");
            return;
        }

        var result = await SimpleMessageBox.ShowAsync(this,
            $"Delete all {entries.Count} captures from local history? This cannot be undone.",
            "Clear history", SimpleMessageBoxButtons.YesNo);
        if (result != SimpleMessageBoxResult.Yes) return;

        foreach (var entry in entries)
        {
            history.Delete(entry);
        }
        await SimpleMessageBox.ShowAsync(this, "History cleared.", "Money Shot");
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
            MaximizeRestoreIcon.Data = (Geometry)resources["Icon.WindowMaximize"]!;
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaximizeRestoreIcon.Data = (Geometry)resources["Icon.WindowRestore"]!;
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
