namespace MoneyShot.Models;

public class AppSettings
{
    public SaveDestination DefaultSaveDestination { get; set; } = SaveDestination.Both;
    public string DefaultSavePath { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public string DefaultFileFormat { get; set; } = "PNG";
    public bool RunOnStartup { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartInTray { get; set; } = true;
    public bool DisableWindowsPrintScreen { get; set; } = false;
    public bool HideUiFromScreenshots { get; set; } = true;
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// Packed 0xAARRGGBB. Platform-neutral replacement for the old WPF-typed
    /// <c>System.Windows.Media.Color DefaultAnnotationColor</c> field, so this project has no UI
    /// framework dependency (part of the Linux-port groundwork — see LINUX_PORT.md Phase 0). Named
    /// differently from the old field so a settings.json written by a pre-port build (which
    /// serialized a WPF Color's full field set) is simply ignored as an unknown property rather
    /// than causing a whole-file JSON deserialization failure. UI layers convert to/from their own
    /// native color type at the boundary — see MoneyShot's ArgbColorConversions.
    /// </summary>
    public uint DefaultAnnotationColorArgb { get; set; } = 0xFFFF0000; // opaque red

    public int DefaultLineThickness { get; set; } = 3;
    public string HotKeyCapture { get; set; } = "PrintScreen";
    public string HotKeyRegionCapture { get; set; } = "Ctrl+PrintScreen";
    public bool SaveCapturesToHistory { get; set; } = true;
    public int HistoryRetentionCount { get; set; } = 50;
}
