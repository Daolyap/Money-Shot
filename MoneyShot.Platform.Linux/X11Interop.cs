using System.Runtime.InteropServices;

namespace MoneyShot.Platform.Linux;

/// <summary>
/// Minimal libX11 P/Invoke surface shared by LinuxScreenCapture and LinuxGlobalHotkeys. Only X11
/// sessions (native Xorg, or XWayland-with-caveats — see LinuxGlobalHotkeys) are supported; a pure
/// Wayland session with no XWayland has no Display to open at all, and callers must handle
/// XOpenDisplay returning IntPtr.Zero gracefully rather than throwing.
/// </summary>
internal static class X11
{
    private const string LibX11 = "libX11.so.6";

    [StructLayout(LayoutKind.Sequential)]
    public struct XImage
    {
        public int width;
        public int height;
        public int xoffset;
        public int format;
        public IntPtr data;
        public int byte_order;
        public int bitmap_unit;
        public int bitmap_bit_order;
        public int bitmap_pad;
        public int depth;
        public int bytes_per_line;
        public int bits_per_pixel;
        public nuint red_mask;
        public nuint green_mask;
        public nuint blue_mask;
        public IntPtr obdata;
        // f. (procedure pointers) omitted — never called from managed code, only laid out so the
        // struct size matches Xlib's for the fields above; XDestroyImage is invoked on the raw
        // pointer via the library's own function, not through this struct's function pointers.
    }

    public const int ZPixmap = 2;
    public const long AllPlanes = ~0L;

    [DllImport(LibX11)]
    public static extern IntPtr XOpenDisplay(string? display);

    [DllImport(LibX11)]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    public static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    public static extern int XDefaultScreen(IntPtr display);

    [DllImport(LibX11)]
    public static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, long planeMask, int format);

    [DllImport(LibX11)]
    public static extern void XDestroyImage(IntPtr image);

    [DllImport(LibX11)]
    public static extern int XDisplayWidth(IntPtr display, int screenNumber);

    [DllImport(LibX11)]
    public static extern int XDisplayHeight(IntPtr display, int screenNumber);

    [DllImport(LibX11)]
    public static extern nuint XStringToKeysym(string keyName);

    [DllImport(LibX11)]
    public static extern byte XKeysymToKeycode(IntPtr display, nuint keysym);

    [DllImport(LibX11)]
    public static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(LibX11)]
    public static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

    [DllImport(LibX11)]
    public static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);

    [DllImport(LibX11)]
    public static extern int XPending(IntPtr display);

    [DllImport(LibX11)]
    public static extern int XNextEvent(IntPtr display, [Out] byte[] eventBuffer);

    [DllImport(LibX11)]
    public static extern int XFlush(IntPtr display);

    // X11 modifier bits (same numeric values as XGrabKey expects).
    public const uint ShiftMask = 1 << 0;
    public const uint LockMask = 1 << 1; // Caps Lock — must grab with/without it, see LinuxGlobalHotkeys
    public const uint ControlMask = 1 << 2;
    public const uint Mod1Mask = 1 << 3; // Alt
    public const uint Mod2Mask = 1 << 4; // usually Num Lock — same caveat as LockMask
    public const uint Mod4Mask = 1 << 6; // Super/Windows key

    public const int GrabModeAsync = 1;
    public const long KeyPressMask = 1 << 0;

    // XEvent is a large union; only the KeyPress case (XKeyEvent layout) is read, and only the
    // fields needed to recover which grabbed (keycode, modifiers) pair fired. XEvent's first field
    // is `type` (int) — the raw 8-byte-aligned struct is large enough that reading it as a fixed
    // byte buffer and decoding just the offsets we need is far less error-prone across libc/X11
    // header ABI variations than declaring the full union in C#.
    public const int KeyPress = 2;
    public const int XEventBufferSize = 192; // XEvent is a union sized for the largest member (XClientMessageEvent etc.); 192 bytes is safely larger than every variant on 64-bit.

    public static int ReadEventType(byte[] buffer) => BitConverter.ToInt32(buffer, 0);

    // XKeyEvent layout (System V x86-64 ABI, 8-byte-aligned fields):
    //   0:type(4) [4 pad] 8:serial(8) 16:send_event(4) [4 pad] 24:display(8) 32:window(8)
    //   40:root(8) 48:subwindow(8) 56:time(8) 64:x(4) 68:y(4) 72:x_root(4) 76:y_root(4)
    //   80:state(4) 84:keycode(4) 88:same_screen(4)
    // Verified empirically against a real XGrabKey/XSendEvent round-trip under Xvfb — see
    // LINUX_PORT.md Phase 2 verification notes — not just derived from the Xlib.h struct by hand.
    public static uint ReadKeyEventState(byte[] buffer) => BitConverter.ToUInt32(buffer, 80);
    public static uint ReadKeyEventKeycode(byte[] buffer) => BitConverter.ToUInt32(buffer, 84);
}
