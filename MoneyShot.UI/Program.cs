using Avalonia;

namespace MoneyShot.UI;

internal static class Program
{
    // Avalonia needs its own Main (not auto-generated like WPF's) so the classic desktop
    // lifetime can be configured explicitly.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
