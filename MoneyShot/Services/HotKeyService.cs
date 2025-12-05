using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MoneyShot.Services;

public class HotKeyService
{
    private const int WM_HOTKEY = 0x0312;
    private readonly Dictionary<int, Action> _hotKeyActions = new();
    private int _currentId = 0;
    private IntPtr _windowHandle;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;
        var source = HwndSource.FromHwnd(_windowHandle);
        source?.AddHook(HwndHook);
    }

    public int RegisterHotKey(uint modifiers, uint key, Action action)
    {
        _currentId++;
        if (RegisterHotKey(_windowHandle, _currentId, modifiers, key))
        {
            _hotKeyActions[_currentId] = action;
            return _currentId;
        }
        return -1;
    }

    public void UnregisterHotKey(int id)
    {
        UnregisterHotKey(_windowHandle, id);
        _hotKeyActions.Remove(id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _hotKeyActions.Keys.ToList())
        {
            UnregisterHotKey(_windowHandle, id);
        }
        _hotKeyActions.Clear();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_hotKeyActions.TryGetValue(id, out var action))
            {
                action?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    // Virtual key codes
    public const uint VK_SNAPSHOT = 0x2C; // Print Screen
    public const uint VK_F1 = 0x70;
    public const uint VK_F2 = 0x71;
    public const uint VK_F3 = 0x72;

    // Modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
}
