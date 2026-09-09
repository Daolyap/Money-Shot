using MoneyShot.Abstractions;

namespace MoneyShot.UI.Platform;

/// <summary>
/// Picks the right IScreenCapture/IGlobalHotkeys/ITrayIcon/IAutoStart/IClipboard implementation
/// for the TFM this assembly was built for — Win32* (MoneyShot.Platform.Windows) under the
/// net10.0-windows TFM, Linux*/LinuxTrayIcon (MoneyShot.Platform.Linux) under plain net10.0. See
/// MoneyShot.UI.csproj's WINDOWS compile constant and TFM-conditional ProjectReferences.
///
/// This is a compile-time branch (#if WINDOWS), not a runtime OperatingSystem.IsWindows() check,
/// because the two TFMs don't even reference the same platform project — the net10.0 build has no
/// Win32* types to construct in the first place. Every MainWindow/SettingsWindow/EditorWindow call
/// site that used to do `new Win32ScreenCapture()` etc. now calls PlatformServices.Create*()
/// instead, so this file is the single place that needs to change if a third platform is ever
/// added.
/// </summary>
internal static class PlatformServices
{
#if WINDOWS
    public static IScreenCapture CreateScreenCapture() => new MoneyShot.Platform.Windows.Win32ScreenCapture();
    public static IGlobalHotkeys CreateGlobalHotkeys() => new MoneyShot.Platform.Windows.Win32GlobalHotkeys();
    public static ITrayIcon CreateTrayIcon() => new MoneyShot.Platform.Windows.Win32TrayIcon();
    public static IAutoStart CreateAutoStart() => new MoneyShot.Platform.Windows.Win32AutoStart();
    public static IClipboard CreateClipboard() => new MoneyShot.Platform.Windows.Win32Clipboard();
#else
    public static IScreenCapture CreateScreenCapture() => new MoneyShot.Platform.Linux.LinuxScreenCapture();
    public static IGlobalHotkeys CreateGlobalHotkeys() => new MoneyShot.Platform.Linux.LinuxGlobalHotkeys();
    public static ITrayIcon CreateTrayIcon() => new LinuxTrayIcon();
    public static IAutoStart CreateAutoStart() => new MoneyShot.Platform.Linux.LinuxAutoStart();
    public static IClipboard CreateClipboard() => new MoneyShot.Platform.Linux.LinuxClipboard();
#endif
}
