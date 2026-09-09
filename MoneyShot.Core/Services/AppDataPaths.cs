namespace MoneyShot.Services;

/// <summary>
/// Resolves the per-user config-root directory ("%AppData%\Roaming" on Windows) that
/// SettingsService/Logger/HistoryService all build their paths under. Exists because
/// <c>Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)</c> is NOT reliable on
/// Linux the way it is on Windows: confirmed by actually publishing and running a build (see
/// LINUX_PORT.md Phase 2 verification notes) that it returns an EMPTY STRING whenever
/// $XDG_CONFIG_HOME isn't set in the environment — which is not some rare edge case, it's the
/// default in plenty of real launch contexts (minimal window managers, some .desktop Exec
/// launches, this project's own Docker/WSL test environments). Left unguarded, every one of those
/// three services would silently write into a relative "MoneyShot" folder under whatever the
/// process's current working directory happened to be at startup — scattering config files
/// unpredictably, and failing outright with an unhandled exception if that directory isn't
/// writable (e.g. an AppImage's read-only mount point).
///
/// On Windows this just returns the OS value unchanged (already correct and tested there). On
/// Linux, this follows the XDG Base Directory Specification's own documented fallback: use
/// $XDG_CONFIG_HOME if set, else default to ~/.config — the same rule GetFolderPath is *supposed*
/// to implement, applied by hand for the case where it doesn't.
/// </summary>
public static class AppDataPaths
{
    public static string GetConfigRoot()
    {
        var fromOs = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(fromOs))
        {
            return fromOs;
        }

        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfigHome))
        {
            return xdgConfigHome;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            home = Environment.GetEnvironmentVariable("HOME");
        }

        if (string.IsNullOrEmpty(home))
        {
            // Every fallback exhausted — this should not happen on any real desktop session, but
            // failing loudly here (rather than silently degrading to a relative path again) makes
            // the underlying environment problem visible instead of scattering files unpredictably.
            throw new InvalidOperationException(
                "Could not determine a per-user config directory: SpecialFolder.ApplicationData, " +
                "$XDG_CONFIG_HOME, and $HOME are all unavailable.");
        }

        return Path.Combine(home, ".config");
    }
}
