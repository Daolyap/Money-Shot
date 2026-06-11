using System.Runtime;
using System.Runtime.InteropServices;

namespace MoneyShot.Services;

/// <summary>
/// Releases the large native/managed bitmap backings the editor leaves behind and asks Windows
/// to trim the working set. Without this a tray app that should idle near ~80 MB sits at several
/// hundred MB after the editor closes, until the next major GC happens on its own schedule.
/// Shared by every code path that closes an <c>EditorWindow</c> (capture flow and history).
/// </summary>
internal static class MemoryTrimmer
{
    public static void TrimAfterEditorClose()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Ask Windows to trim the working set. -1, -1 is the documented "trim now" sentinel.
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            SetProcessWorkingSetSize(process.Handle, new IntPtr(-1), new IntPtr(-1));
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not release editor memory", ex);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
}
