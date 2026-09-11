namespace MoneyShot.Abstractions;

/// <summary>
/// Raw captured pixels in top-down BGRA32 (premultiplied alpha) row order, stride in bytes.
/// Platform-neutral so MoneyShot.Core has no UI-framework dependency — UI layers convert to their
/// native bitmap type (WPF BitmapSource today, Avalonia Bitmap once ported) at the boundary.
/// </summary>
public sealed record CapturedImage(int Width, int Height, int Stride, byte[] PixelDataBgra32);

/// <summary>Bounds and identity of one physical display, in physical pixels.</summary>
public sealed record MonitorInfo(int Left, int Top, int Width, int Height, bool IsPrimary, string DeviceName);

/// <summary>
/// Screen capture, abstracted from the OS mechanism (GDI+ on Windows, X11/portal on Linux — see
/// LINUX_PORT.md). Implementations live under MoneyShot.Platform.*.
/// </summary>
public interface IScreenCapture
{
    CapturedImage CaptureFullScreen();
    CapturedImage CaptureMonitor(int monitorIndex);
    IReadOnlyList<MonitorInfo> GetAllMonitors();
}
