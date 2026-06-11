using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using MoneyShot.Models;
using MoneyShot.Services;
using Application = System.Windows.Application;

namespace MoneyShot.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private AppSettings _settings;

    public SettingsWindow()
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        _settings = _settingsService.LoadSettings();
        LoadSettings();
        LoadMonitorHotkeysInfo();
    }

    private void LoadMonitorHotkeysInfo()
    {
        var screenshotService = new ScreenshotService();
        var screens = screenshotService.GetAllScreens();
        
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
        if (_settingsService.TryGetWindowsPrintScreenDisabled(out var isPrintScreenDisabled))
        {
            _settings.DisableWindowsPrintScreen = isPrintScreenDisabled;
        }

        DisableWindowsPrintScreenCheckbox.IsChecked = _settings.DisableWindowsPrintScreen;
        SavePathTextBox.Text = _settings.DefaultSavePath;

        SaveToClipboardRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.Clipboard;
        SaveToFileRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.File;
        SaveToBothRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.Both;

        SelectComboBoxItem(FormatComboBox, _settings.DefaultFileFormat);

        // History settings
        SaveToHistoryCheckbox.IsChecked = _settings.SaveCapturesToHistory;
        HistoryRetentionTextBox.Text = _settings.HistoryRetentionCount.ToString();
        HistoryFolderText.Text = new HistoryService().HistoryDirectory;

        // Load hotkey settings
        SelectComboBoxItem(HotKeyCaptureComboBox, _settings.HotKeyCapture);
        SelectComboBoxItem(HotKeyRegionCaptureComboBox, _settings.HotKeyRegionCapture);
    }

    private void SelectComboBoxItem(System.Windows.Controls.ComboBox comboBox, string value)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in comboBox.Items)
        {
            if (item.Content.ToString() == value)
            {
                item.IsSelected = true;
                return;
            }
        }
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.SelectedPath = _settings.DefaultSavePath;
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Validate the selected path
                if (!string.IsNullOrWhiteSpace(dialog.SelectedPath) && Directory.Exists(dialog.SelectedPath))
                {
                    SavePathTextBox.Text = dialog.SelectedPath;
                }
                else
                {
                    MessageBox.Show("The selected folder is invalid.", "Invalid Folder", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error selecting folder: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.StartInTray = StartInTrayCheckbox.IsChecked ?? true;
            _settings.RunOnStartup = RunOnStartupCheckbox.IsChecked ?? false;
            _settings.MinimizeToTray = MinimizeToTrayCheckbox.IsChecked ?? false;
            _settings.CheckForUpdatesOnStartup = CheckForUpdatesCheckbox.IsChecked ?? true;
            _settings.HideUiFromScreenshots = HideUiFromScreenshotsCheckbox.IsChecked ?? true;
            _settings.DisableWindowsPrintScreen = DisableWindowsPrintScreenCheckbox.IsChecked ?? false;
            _settings.DefaultSavePath = SavePathTextBox.Text;

            if (SaveToClipboardRadio.IsChecked == true)
                _settings.DefaultSaveDestination = SaveDestination.Clipboard;
            else if (SaveToFileRadio.IsChecked == true)
                _settings.DefaultSaveDestination = SaveDestination.File;
            else
                _settings.DefaultSaveDestination = SaveDestination.Both;

            // Items are ComboBoxItems, not raw strings — reading SelectedItem as a string used to
            // silently skip saving the format.
            if (FormatComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem formatItem)
                _settings.DefaultFileFormat = formatItem.Content?.ToString() ?? "PNG";

            _settings.SaveCapturesToHistory = SaveToHistoryCheckbox.IsChecked ?? true;
            if (int.TryParse(HistoryRetentionTextBox.Text, out var retention))
            {
                _settings.HistoryRetentionCount = Math.Clamp(retention, 0, 500);
            }

            // Save hotkey settings
            if (HotKeyCaptureComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem captureItem)
                _settings.HotKeyCapture = captureItem.Content.ToString() ?? "PrintScreen";
            
            if (HotKeyRegionCaptureComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem regionItem)
                _settings.HotKeyRegionCapture = regionItem.Content.ToString() ?? "Ctrl+PrintScreen";

            _settingsService.SaveSettings(_settings);
            
            try
            {
                _settingsService.SetStartupWithWindows(_settings.RunOnStartup);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show($"Warning: {ex.Message}\nOther settings were saved successfully.", 
                    "Partial Success", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            
            var printScreenApplied = _settingsService.SetWindowsPrintScreenDisabled(_settings.DisableWindowsPrintScreen);

            // Reload hotkeys in the main window
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ReloadHotKeys();
            }

            var successMessage = printScreenApplied
                ? "Settings saved successfully! Hotkeys have been updated."
                : "Settings saved, but Windows Print Screen integration could not be fully updated. You may need to reopen the app as admin or update the Print Screen snipping setting in Windows keyboard settings.";

            MessageBox.Show(successMessage, 
                printScreenApplied ? "Success" : "Partial Success",
                MessageBoxButton.OK,
                printScreenApplied ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving settings: {ex.Message}", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderInExplorer(Logger.LogDirectoryPath);
    }

    private void OpenHistoryFolder_Click(object sender, RoutedEventArgs e)
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
            MessageBox.Show($"Could not open folder:\n{path}", "Money Shot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var history = new HistoryService();
        var entries = history.List();
        if (entries.Count == 0)
        {
            MessageBox.Show("History is already empty.", "Money Shot",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Delete all {entries.Count} captures from local history? This cannot be undone.",
            "Clear history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        foreach (var entry in entries)
        {
            history.Delete(entry);
        }
        MessageBox.Show("History cleared.", "Money Shot", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MaximizeRestoreIcon.Data = (System.Windows.Media.Geometry)FindResource("Icon.WindowMaximize");
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaximizeRestoreIcon.Data = (System.Windows.Media.Geometry)FindResource("Icon.WindowRestore");
        }
    }
    
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
