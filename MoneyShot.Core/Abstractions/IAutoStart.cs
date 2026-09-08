namespace MoneyShot.Abstractions;

/// <summary>
/// The two registry-backed OS integrations SettingsService used to own directly: launch-at-login
/// and (Windows-only) suppressing the OS's own screenshot tool on PrintScreen. See LINUX_PORT.md
/// § Registry settings — on Linux, autostart becomes a ~/.config/autostart/*.desktop file and
/// PrintScreen suppression has no analogue (implementations should no-op / return false).
/// </summary>
public interface IAutoStart
{
    /// <summary>Throws InvalidOperationException with a user-facing message on failure.</summary>
    void SetStartupWithApp(bool enabled);

    bool IsSetToRunOnStartup();

    /// <summary>Returns whether the change was applied. Platforms without this concept return false.</summary>
    bool SetPrintScreenSuppressed(bool suppressed);

    bool TryGetPrintScreenSuppressed(out bool suppressed);
}
