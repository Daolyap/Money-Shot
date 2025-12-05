using System.Windows;
using MoneyShot.Models;
using MoneyShot.Services;

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
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.SelectedPath = _settings.DefaultSavePath;
        
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SavePathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
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

        _settingsService.SaveSettings(_settings);
        _settingsService.SetStartupWithWindows(_settings.RunOnStartup);
        _settingsService.SetWindowsPrintScreenDisabled(_settings.DisableWindowsPrintScreen);

        MessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
