using System.Threading.Tasks;
using Avalonia.Controls;

namespace MoneyShot.UI.Interop;

/// <summary>
/// Avalonia's Window.ShowDialog(owner) throws InvalidOperationException("Cannot show window with
/// non-visible owner") if the owner isn't currently shown — confirmed by actually running this
/// app: MainWindow.CaptureFullScreenAsync() calls Hide() (so the main window doesn't appear in
/// its own screenshot) and then used to call EditorWindow.ShowDialog(this), which threw every
/// single time, breaking the entire capture flow. WPF has no such restriction, so this didn't
/// surface until the Avalonia port was actually launched and clicked through — see LINUX_PORT.md
/// Phase 1 verification notes.
///
/// This extension makes "show as a dialog, wait for it to close" work regardless of whether the
/// owner is visible: it uses the real ShowDialog(owner) (proper modality/centering) when the
/// owner is visible, and falls back to a plain Show() + await-on-Closed otherwise. Every place in
/// this app that used to call window.ShowDialog(owner) directly should go through this instead.
/// </summary>
public static class WindowExtensions
{
    /// <summary>
    /// Waits until <paramref name="window"/> closes. There's deliberately no generic
    /// TResult-returning overload mirroring ShowDialog&lt;TResult&gt;: Avalonia's dialog-result
    /// value (set via Window.Close(result)) has no public API to read back once the fallback path
    /// (plain Show()) is taken, so callers that need a result should expose it as an ordinary
    /// property on the window instead (see RegionSelector.CroppedScreenshot) and read that after
    /// awaiting this.
    /// </summary>
    public static Task ShowAsDialogAsync(this Window window, Window? owner)
    {
        if (owner != null && owner.IsVisible)
        {
            return window.ShowDialog(owner);
        }

        var tcs = new TaskCompletionSource();
        window.Closed += (_, _) => tcs.TrySetResult();
        window.Show();
        return tcs.Task;
    }
}
