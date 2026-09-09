using System.Runtime;
using System.Runtime.InteropServices;
using MoneyShot.Services;

namespace MoneyShot.Platform.Linux;

/// <summary>
/// Linux counterpart to MoneyShot.Platform.Windows.MemoryTrimmer — same rationale (release the
/// large bitmap backings EditorWindow leaves behind rather than waiting for the next scheduled
/// GC), minus the Windows-specific SetProcessWorkingSetSize call. glibc's malloc_trim(0) is the
/// closest Linux equivalent for the *native* heap specifically (SkiaSharp's bitmap backings are
/// native allocations, not managed ones) — best-effort: on a non-glibc libc (musl, used by e.g.
/// Alpine) this P/Invoke will fail to resolve and is swallowed by the same catch that guards
/// Windows' equivalent.
/// </summary>
public static class LinuxMemoryTrimmer
{
    public static void TrimAfterEditorClose()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect();

            try
            {
                malloc_trim(0);
            }
            catch (DllNotFoundException)
            {
                // Non-glibc libc (e.g. musl/Alpine) — the managed-heap trim above already ran,
                // this is just a native-heap best-effort extra.
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not release editor memory", ex);
        }
    }

    [DllImport("libc.so.6")]
    private static extern int malloc_trim(nuint pad);
}
