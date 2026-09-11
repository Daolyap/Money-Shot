using MoneyShot.Abstractions;
using SkiaSharp;
using Tmds.DBus;

namespace MoneyShot.Platform.Linux;

// Tmds.DBus builds the proxy for these at runtime via Reflection.Emit (Connection.CreateProxy<T>),
// in a *different* dynamic assembly ("Tmds.DBus.Emit") — an internal interface is inaccessible to
// that assembly and CreateTypeInfoImpl() throws TypeLoadException ("attempting to implement an
// inaccessible interface"). Confirmed by actually hitting this on a real Fedora KDE run: both
// interfaces must be public, no way around it short of InternalsVisibleTo (which still wouldn't
// help — Tmds.DBus's emit assembly name/key isn't something this project controls to add a
// matching InternalsVisibleTo for).
[DBusInterface("org.freedesktop.portal.Screenshot")]
public interface IScreenshotPortal : IDBusObject
{
    Task<ObjectPath> ScreenshotAsync(string parentWindow, IDictionary<string, object> options);
}

[DBusInterface("org.freedesktop.portal.Request")]
public interface IPortalRequest : IDBusObject
{
    Task<IDisposable> WatchResponseAsync(Action<(uint response, IDictionary<string, object> results)> handler, Action<Exception>? onError = null);
}

/// <summary>
/// Screen capture via the XDG Desktop Portal (org.freedesktop.portal.Screenshot) — the native-
/// Wayland counterpart to LinuxScreenCapture's XGetImage path. See LINUX_PORT.md § Wayland status
/// / Phase 4.
///
/// XGetImage against the X11 root window doesn't see native Wayland compositor content (that's the
/// whole point of Wayland's security model — clients can't read each other's or the desktop's
/// pixels without going through an arbitrating service), so under a Wayland session the only
/// correct mechanism is this portal, brokered by whichever backend the desktop provides
/// (xdg-desktop-portal-kde, -gnome, -wlr, ...). The portal call itself uses Tmds.DBus (a pure
/// managed D-Bus client — no libdbus dependency, so no extra packaging requirement) to talk to the
/// session bus; the actual backend service (xdg-desktop-portal + a desktop-specific implementation)
/// must be running, which it is by default on every mainstream KDE/GNOME desktop install but not
/// necessarily in a minimal/headless environment.
///
/// `interactive: false` is passed so the backend takes an immediate full screenshot rather than
/// showing its own region/window picker UI — MoneyShot already has RegionSelector for cropping a
/// frozen full capture, so the portal's own picker would be redundant. Depending on the backend,
/// this may still show a brief consent/notification UI (KDE and GNOME's screenshot portals do not
/// require a persisted per-app grant the way camera/microphone portals do, but this wasn't
/// observable against a real compositor in this project's build environment — see LINUX_PORT.md).
///
/// Only full-desktop capture is implemented. There is no portal-level "capture just monitor N"
/// equivalent to xrandr's per-output geometry outside of the heavier ScreenCast portal (which
/// requires the user to pick a source from a live picker UI and hands back a PipeWire stream, not a
/// single screenshot) — see LinuxScreenCapture.GetAllMonitorsWayland for how monitor selection
/// degrades gracefully under this path.
/// </summary>
internal static class WaylandPortalScreenCapture
{
    private const string PortalService = "org.freedesktop.portal.Desktop";
    private static readonly ObjectPath PortalObjectPath = new("/org/freedesktop/portal/desktop");
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60);

    public static CapturedImage CaptureFullScreen()
    {
        var pngPath = RequestScreenshotFileAsync().GetAwaiter().GetResult();
        try
        {
            return DecodePng(pngPath);
        }
        finally
        {
            // Best-effort: the portal typically serves this through the document portal (a path
            // under /run/user/<uid>/doc/...) which may be read-only or auto-cleaned by that
            // service; failing to delete it here isn't a capture failure.
            try { File.Delete(pngPath); } catch { }
        }
    }

    private static async Task<string> RequestScreenshotFileAsync()
    {
        var connection = Connection.Session;
        var portal = connection.CreateProxy<IScreenshotPortal>(PortalService, PortalObjectPath);

        var tcs = new TaskCompletionSource<(uint response, IDictionary<string, object> results)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var options = new Dictionary<string, object>
        {
            ["handle_token"] = "moneyshot_" + Guid.NewGuid().ToString("N"),
            ["interactive"] = false
        };

        // The portal spec's race-free pattern is to pre-compute the Request object path from our
        // own unique bus name + handle_token and subscribe before calling Screenshot. Subscribing
        // to the *returned* handle after the call, as done here, has a small documented race if the
        // backend answers between the method return and the subscribe call — in practice this
        // doesn't happen (answering takes at least one more D-Bus round trip / compositor tick),
        // and every simple portal client example in the wild takes this same shortcut. Not verified
        // against a real compositor in this project's build environment — see LINUX_PORT.md.
        var handle = await portal.ScreenshotAsync(string.Empty, options).ConfigureAwait(false);
        var request = connection.CreateProxy<IPortalRequest>(PortalService, handle);

        using var watcher = await request.WatchResponseAsync(result => tcs.TrySetResult(result)).ConfigureAwait(false);

        var winner = await Task.WhenAny(tcs.Task, Task.Delay(ResponseTimeout)).ConfigureAwait(false);
        if (winner != tcs.Task)
        {
            throw new InvalidOperationException(
                "Timed out waiting for the screenshot portal to respond. This usually means no " +
                "portal backend is running (xdg-desktop-portal + a desktop-specific implementation " +
                "like xdg-desktop-portal-kde/-gnome — present by default on mainstream desktops, " +
                "but not in minimal/headless environments), or a consent dialog is waiting " +
                "off-screen for a response. See LINUX_PORT.md Phase 4.");
        }

        var (response, results) = tcs.Task.Result;
        if (response != 0)
        {
            throw new InvalidOperationException(response == 1
                ? "Screenshot request was cancelled."
                : $"Screenshot portal request failed (response code {response}).");
        }

        if (!results.TryGetValue("uri", out var uriObj) || uriObj is not string uriString)
        {
            throw new InvalidOperationException("Screenshot portal response did not include a 'uri' result.");
        }

        return new Uri(uriString).LocalPath;
    }

    private static CapturedImage DecodePng(string path)
    {
        using var decoded = SKBitmap.Decode(path)
            ?? throw new InvalidOperationException($"Failed to decode portal screenshot PNG at '{path}'.");

        var bgra = decoded;
        SKBitmap? converted = null;
        try
        {
            if (decoded.ColorType != SKColorType.Bgra8888)
            {
                converted = decoded.Copy(SKColorType.Bgra8888)
                    ?? throw new InvalidOperationException("Failed to convert portal screenshot to BGRA8888.");
                bgra = converted;
            }

            var pixels = bgra.Bytes;
            // Force alpha opaque — matches LinuxScreenCapture's X11 path (see its CaptureRegion
            // comment); a desktop screenshot should already be fully opaque, this just guards the
            // same edge case defensively.
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 0xFF;

            return new CapturedImage(bgra.Width, bgra.Height, bgra.RowBytes, pixels);
        }
        finally
        {
            converted?.Dispose();
        }
    }
}
