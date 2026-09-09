using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MoneyShot.Abstractions;
using MoneyShot.Services;

namespace MoneyShot.Platform.Linux;

/// <summary>
/// X11-based screen capture (XGetImage on the root window) — the Linux counterpart to
/// Win32ScreenCapture. See LINUX_PORT.md § Capture pipeline / Phase 2.
///
/// This only works under an X11 session (a real Xorg session, or XWayland for apps that don't
/// need pixels from native-Wayland surfaces — capturing the root window through XWayland reliably
/// returns whatever XWayland itself is compositing, which in practice means Xorg-session content;
/// behavior when the *only* thing running is a pure-Wayland compositor with no X clients at all is
/// untested here). A portal-based (org.freedesktop.portal.Screenshot) implementation would be the
/// correct approach for native Wayland sessions (GNOME/KDE Wayland, now the default on most
/// mainstream distros including Debian/Fedora) but requires an interactive per-capture permission
/// dialog from the compositor — a real UX change from Windows' instant, silent capture — and portal
/// backend services aren't present in this development environment to implement against
/// interactively (see LINUX_PORT.md Phase 2 status). Treat this class as the X11-session path only;
/// a portal-based path is the documented, tracked gap for pure-Wayland sessions.
/// </summary>
public sealed class LinuxScreenCapture : IScreenCapture
{
    public CapturedImage CaptureFullScreen()
    {
        var display = OpenDisplayOrThrow();
        try
        {
            var screen = X11.XDefaultScreen(display);
            var width = X11.XDisplayWidth(display, screen);
            var height = X11.XDisplayHeight(display, screen);
            return CaptureRegion(display, 0, 0, width, height);
        }
        finally
        {
            X11.XCloseDisplay(display);
        }
    }

    public CapturedImage CaptureMonitor(int monitorIndex)
    {
        var monitors = GetAllMonitors();
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex));

        var m = monitors[monitorIndex];
        var display = OpenDisplayOrThrow();
        try
        {
            return CaptureRegion(display, m.Left, m.Top, m.Width, m.Height);
        }
        finally
        {
            X11.XCloseDisplay(display);
        }
    }

    public IReadOnlyList<MonitorInfo> GetAllMonitors()
    {
        // Shelling out to `xrandr --query` rather than P/Invoking libXrandr directly: the RandR
        // protocol's monitor-enumeration ABI (XRRGetMonitors et al.) has changed shape across
        // versions, whereas xrandr's plain-text --query output has been stable for over a decade
        // and is present on effectively every X11 desktop install (it's the same tool GNOME
        // Settings/KDE Display Config shell out to internally in some cases). If xrandr isn't
        // installed or the session has no RandR (rare, but possible in minimal WMs), this falls
        // back to reporting a single monitor matching the X11 display size.
        try
        {
            var monitors = ParseXrandr(RunCommand("xrandr", "--query"));
            if (monitors.Count > 0) return monitors;
        }
        catch (Exception ex)
        {
            Logger.Warn($"xrandr enumeration failed, falling back to single-monitor: {ex.Message}");
        }

        return FallbackSingleMonitor();
    }

    private static List<MonitorInfo> ParseXrandr(string xrandrOutput)
    {
        // Matches lines like: "eDP-1 connected primary 1920x1080+0+0 (normal left inverted...) 344mm x 194mm"
        // or "HDMI-1 connected 1920x1080+1920+0 ..." (non-primary, no "primary" token).
        var regex = new Regex(@"^(?<name>\S+) connected (?<primary>primary )?(?<w>\d+)x(?<h>\d+)\+(?<x>\d+)\+(?<y>\d+)", RegexOptions.Multiline);
        var result = new List<MonitorInfo>();
        foreach (Match match in regex.Matches(xrandrOutput))
        {
            result.Add(new MonitorInfo(
                Left: int.Parse(match.Groups["x"].Value),
                Top: int.Parse(match.Groups["y"].Value),
                Width: int.Parse(match.Groups["w"].Value),
                Height: int.Parse(match.Groups["h"].Value),
                IsPrimary: match.Groups["primary"].Success,
                DeviceName: match.Groups["name"].Value));
        }

        // xrandr doesn't always mark a "primary" — if none was flagged, treat the first (typically
        // leftmost/topmost) as primary so downstream UI (monitor button list, tray menu) has one.
        if (result.Count > 0 && !result.Exists(m => m.IsPrimary))
        {
            result[0] = result[0] with { IsPrimary = true };
        }

        return result;
    }

    private static List<MonitorInfo> FallbackSingleMonitor()
    {
        var display = OpenDisplayOrThrow();
        try
        {
            var screen = X11.XDefaultScreen(display);
            var width = X11.XDisplayWidth(display, screen);
            var height = X11.XDisplayHeight(display, screen);
            return new List<MonitorInfo> { new(0, 0, width, height, true, "X11 default screen") };
        }
        finally
        {
            X11.XCloseDisplay(display);
        }
    }

    private static CapturedImage CaptureRegion(IntPtr display, int x, int y, int width, int height)
    {
        var root = X11.XDefaultRootWindow(display);
        var imagePtr = X11.XGetImage(display, root, x, y, (uint)width, (uint)height, X11.AllPlanes, X11.ZPixmap);
        if (imagePtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"XGetImage failed for region ({x},{y},{width}x{height}) — the X server may not support ZPixmap capture of this size, or the region is off-screen.");
        }

        try
        {
            var image = Marshal.PtrToStructure<X11.XImage>(imagePtr);
            // 32bpp TrueColor visuals (the near-universal case on modern X11 — 16bpp/8bpp visuals
            // are not supported here) pack pixels as 0x00RRGGBB in the server's native byte order;
            // on every actually-relevant architecture (x86-64, aarch64) that's little-endian, i.e.
            // B,G,R,X per pixel in memory — already exactly CapturedImage's expected BGRA32 layout
            // modulo the alpha byte, which XGetImage leaves as whatever padding byte the X server
            // wrote (typically 0, not the opaque 0xFF CapturedImage's other producers guarantee).
            if (image.bits_per_pixel != 32)
            {
                throw new NotSupportedException($"Unsupported X11 visual depth: {image.bits_per_pixel} bits/pixel (only 32bpp TrueColor is supported).");
            }

            var stride = image.bytes_per_line;
            var byteCount = stride * height;
            var bytes = new byte[byteCount];
            Marshal.Copy(image.data, bytes, 0, byteCount);

            // Force alpha opaque — see comment above.
            for (var i = 3; i < bytes.Length; i += 4)
            {
                bytes[i] = 0xFF;
            }

            return new CapturedImage(width, height, stride, bytes);
        }
        finally
        {
            X11.XDestroyImage(imagePtr);
        }
    }

    private static IntPtr OpenDisplayOrThrow()
    {
        var display = X11.XOpenDisplay(null);
        if (display == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "Could not open the X11 display (XOpenDisplay returned NULL). This usually means " +
                "there is no X server / XWayland compatibility available — a pure Wayland session " +
                "without XWayland isn't supported by this build's capture implementation yet; see " +
                "LINUX_PORT.md Phase 2.");
        }
        return display;
    }

    internal static string RunCommand(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"'{fileName} {arguments}' exited {process.ExitCode}: {stderr}");
        }
        return stdout;
    }
}
