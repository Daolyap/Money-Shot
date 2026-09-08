namespace MoneyShot.Abstractions;

/// <summary>
/// Image clipboard access, abstracted from the OS mechanism (Win32/WinForms clipboard on Windows,
/// MIME-typed X11/Wayland selection via Avalonia's IClipboard on Linux — see LINUX_PORT.md
/// § Clipboard image support).
/// </summary>
public interface IClipboard
{
    void SetImage(CapturedImage image);
}
