namespace MoneyShot.Services;

/// <summary>
/// Parses hotkey strings like "Ctrl+PrintScreen" or "Ctrl+Shift+1" into Win32 modifier/virtual-key
/// codes. Pure and platform-neutral — kept in Core so every IGlobalHotkeys implementation (Windows
/// today, X11 later, per LINUX_PORT.md) shares one parsing contract for the strings SettingsService
/// persists. The modifier/key constants below are Win32 RegisterHotKey values; a non-Windows
/// implementation maps them to its own native equivalents (e.g. X11 keysyms) internally.
/// </summary>
public static class HotKeyParser
{
    // Virtual key codes
    public const uint VK_SNAPSHOT = 0x2C; // Print Screen
    public const uint VK_0 = 0x30;
    public const uint VK_1 = 0x31;
    public const uint VK_2 = 0x32;
    public const uint VK_3 = 0x33;
    public const uint VK_4 = 0x34;
    public const uint VK_5 = 0x35;
    public const uint VK_6 = 0x36;
    public const uint VK_7 = 0x37;
    public const uint VK_8 = 0x38;
    public const uint VK_9 = 0x39;
    public const uint VK_F1 = 0x70;
    public const uint VK_F2 = 0x71;
    public const uint VK_F3 = 0x72;
    public const uint VK_F4 = 0x73;
    public const uint VK_F5 = 0x74;
    public const uint VK_F6 = 0x75;
    public const uint VK_F7 = 0x76;
    public const uint VK_F8 = 0x77;
    public const uint VK_F9 = 0x78;
    public const uint VK_F10 = 0x79;
    public const uint VK_F11 = 0x7A;
    public const uint VK_F12 = 0x7B;

    // Modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;

    /// <summary>
    /// Parse a hotkey string like "Ctrl+PrintScreen", "Alt+F1", or "Ctrl+Shift+1" into modifiers and key code
    /// </summary>
    public static (uint modifiers, uint key) ParseHotKey(string hotkeyString)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
        {
            return (0, 0);
        }

        uint modifiers = 0;
        uint key = 0;

        var parts = hotkeyString.Split('+');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            switch (trimmed.ToLower())
            {
                case "ctrl":
                case "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= MOD_WIN;
                    break;
                case "printscreen":
                case "prtsc":
                    key = VK_SNAPSHOT;
                    break;
                case "0":
                    key = VK_0;
                    break;
                case "1":
                    key = VK_1;
                    break;
                case "2":
                    key = VK_2;
                    break;
                case "3":
                    key = VK_3;
                    break;
                case "4":
                    key = VK_4;
                    break;
                case "5":
                    key = VK_5;
                    break;
                case "6":
                    key = VK_6;
                    break;
                case "7":
                    key = VK_7;
                    break;
                case "8":
                    key = VK_8;
                    break;
                case "9":
                    key = VK_9;
                    break;
                case "f1":
                    key = VK_F1;
                    break;
                case "f2":
                    key = VK_F2;
                    break;
                case "f3":
                    key = VK_F3;
                    break;
                case "f4":
                    key = VK_F4;
                    break;
                case "f5":
                    key = VK_F5;
                    break;
                case "f6":
                    key = VK_F6;
                    break;
                case "f7":
                    key = VK_F7;
                    break;
                case "f8":
                    key = VK_F8;
                    break;
                case "f9":
                    key = VK_F9;
                    break;
                case "f10":
                    key = VK_F10;
                    break;
                case "f11":
                    key = VK_F11;
                    break;
                case "f12":
                    key = VK_F12;
                    break;
            }
        }

        return (modifiers, key);
    }
}
