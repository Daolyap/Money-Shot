using System.Diagnostics;
using MoneyShot.Abstractions;
using SkiaSharp;

namespace MoneyShot.Platform.Linux;

/// <summary>
/// Image clipboard via wl-copy (Wayland) or xclip (X11) — the Linux counterpart to Win32Clipboard.
/// See LINUX_PORT.md § Clipboard image support / Phase 2.
///
/// Unlike Windows' DIB-based clipboard, X11/Wayland selections are MIME-typed and most
/// image-accepting apps register for image/png specifically, so the CapturedImage buffer is
/// PNG-encoded (via SkiaSharp — see MoneyShot.Platform.Linux.csproj) before handing it to whichever
/// CLI clipboard tool is available. Session type is detected via $WAYLAND_DISPLAY the same way
/// LinuxScreenCapture/LinuxGlobalHotkeys check for X11 — there is no portal-based clipboard API
/// needed here since wl-copy/xclip already handle both cases correctly on their respective
/// sessions without any permission prompt.
/// </summary>
public sealed class LinuxClipboard : IClipboard
{
    public void SetImage(CapturedImage image)
    {
        var pngBytes = EncodePng(image);
        var (fileName, arguments) = ResolveClipboardCommand();

        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}' for clipboard access.");

        process.StandardInput.BaseStream.Write(pngBytes, 0, pngBytes.Length);
        process.StandardInput.BaseStream.Flush();
        process.StandardInput.Close();

        // wl-copy backgrounds itself to keep serving the selection after this process exits (that
        // is the whole point of Wayland's clipboard model — whoever last set the selection must
        // keep running to serve paste requests), so it returns immediately; xclip -selection
        // clipboard historically also forks and detaches for the same reason on X11. Waiting here
        // is still correct: both tools' *parent* process (the one Process.Start launched) exits
        // right away in both cases, it's only the detached clipboard-serving child that lingers.
        process.WaitForExit(5000);
        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"'{fileName} {arguments}' failed: {stderr}");
        }
    }

    private static (string fileName, string arguments) ResolveClipboardCommand()
    {
        var isWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        if (isWayland && CommandExists("wl-copy"))
        {
            return ("wl-copy", "--type image/png");
        }

        if (CommandExists("xclip"))
        {
            return ("xclip", "-selection clipboard -t image/png");
        }

        if (CommandExists("wl-copy"))
        {
            // XWayland session without xclip installed — still try wl-copy as a last resort.
            return ("wl-copy", "--type image/png");
        }

        throw new InvalidOperationException(
            "No clipboard tool found. Install 'wl-clipboard' (Wayland) or 'xclip' (X11) — e.g. " +
            "'sudo apt install wl-clipboard xclip' on Debian/Ubuntu, 'sudo dnf install wl-clipboard xclip' on Fedora.");
    }

    private static bool CommandExists(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("which", command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            if (process == null) return false;
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] EncodePng(CapturedImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        unsafe
        {
            fixed (byte* pixelPtr = image.PixelDataBgra32)
            {
                bitmap.InstallPixels(info, (IntPtr)pixelPtr, image.Stride);
                using var pngData = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                return pngData.ToArray();
            }
        }
    }
}
