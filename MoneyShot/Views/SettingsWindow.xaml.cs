using System.IO;
using System.Windows;
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
    }

    private void LoadSettings()
    {
        StartInTrayCheckbox.IsChecked = _settings.StartInTray;
        RunOnStartupCheckbox.IsChecked = _settings.RunOnStartup;
        MinimizeToTrayCheckbox.IsChecked = _settings.MinimizeToTray;
        DisableWindowsPrintScreenCheckbox.IsChecked = _settings.DisableWindowsPrintScreen;
        SavePathTextBox.Text = _settings.DefaultSavePath;
        
        SaveToClipboardRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.Clipboard;
        SaveToFileRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.File;
        SaveToBothRadio.IsChecked = _settings.DefaultSaveDestination == SaveDestination.Both;

        FormatComboBox.SelectedItem = _settings.DefaultFileFormat;
        
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
            _settings.DisableWindowsPrintScreen = DisableWindowsPrintScreenCheckbox.IsChecked ?? false;
            _settings.DefaultSavePath = SavePathTextBox.Text;

            if (SaveToClipboardRadio.IsChecked == true)
                _settings.DefaultSaveDestination = SaveDestination.Clipboard;
            else if (SaveToFileRadio.IsChecked == true)
                _settings.DefaultSaveDestination = SaveDestination.File;
            else
                _settings.DefaultSaveDestination = SaveDestination.Both;

            if (FormatComboBox.SelectedItem is string format)
                _settings.DefaultFileFormat = format;

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
            
            _settingsService.SetWindowsPrintScreenDisabled(_settings.DisableWindowsPrintScreen);

            // Reload hotkeys in the main window
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ReloadHotKeys();
            }

            MessageBox.Show("Settings saved successfully! Hotkeys have been updated.", 
                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
}
