using MoneyShot.Services;
using Xunit;

namespace MoneyShot.Tests;

// Parsing moved to the platform-neutral HotKeyParser (MoneyShot.Core) as part of the Linux-port
// groundwork — see LINUX_PORT.md Phase 0. The Win32-specific registration mechanics now live in
// MoneyShot.Platform.Windows.Win32GlobalHotkeys, which isn't unit-testable (it P/Invokes
// RegisterHotKey against a real HWND) — same category of "needs manual verification on Windows"
// as the rest of the OS-touching surface, per CLAUDE.md.
public class HotKeyServiceTests
{
    [Theory]
    [InlineData("Ctrl+PrintScreen", HotKeyParser.MOD_CONTROL, HotKeyParser.VK_SNAPSHOT)]
    [InlineData("Alt+F1", HotKeyParser.MOD_ALT, HotKeyParser.VK_F1)]
    [InlineData("Shift+F12", HotKeyParser.MOD_SHIFT, HotKeyParser.VK_F12)]
    [InlineData("Win+1", HotKeyParser.MOD_WIN, HotKeyParser.VK_1)]
    [InlineData("Ctrl+Shift+1", HotKeyParser.MOD_CONTROL | HotKeyParser.MOD_SHIFT, HotKeyParser.VK_1)]
    [InlineData("Ctrl+Alt+Shift+Win+0", HotKeyParser.MOD_CONTROL | HotKeyParser.MOD_ALT | HotKeyParser.MOD_SHIFT | HotKeyParser.MOD_WIN, HotKeyParser.VK_0)]
    [InlineData("Control+F5", HotKeyParser.MOD_CONTROL, HotKeyParser.VK_F5)]
    [InlineData("Windows+9", HotKeyParser.MOD_WIN, HotKeyParser.VK_9)]
    [InlineData("PrtSc", 0u, HotKeyParser.VK_SNAPSHOT)]
    [InlineData("PrintScreen", 0u, HotKeyParser.VK_SNAPSHOT)]
    public void ParseHotKey_RecognizedCombinations(string input, uint expectedModifiers, uint expectedKey)
    {
        var (modifiers, key) = HotKeyParser.ParseHotKey(input);
        Assert.Equal(expectedModifiers, modifiers);
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData("F1", HotKeyParser.VK_F1)]
    [InlineData("F2", HotKeyParser.VK_F2)]
    [InlineData("F3", HotKeyParser.VK_F3)]
    [InlineData("F4", HotKeyParser.VK_F4)]
    [InlineData("F5", HotKeyParser.VK_F5)]
    [InlineData("F6", HotKeyParser.VK_F6)]
    [InlineData("F7", HotKeyParser.VK_F7)]
    [InlineData("F8", HotKeyParser.VK_F8)]
    [InlineData("F9", HotKeyParser.VK_F9)]
    [InlineData("F10", HotKeyParser.VK_F10)]
    [InlineData("F11", HotKeyParser.VK_F11)]
    [InlineData("F12", HotKeyParser.VK_F12)]
    public void ParseHotKey_AllFunctionKeys(string input, uint expectedKey)
    {
        var (modifiers, key) = HotKeyParser.ParseHotKey(input);
        Assert.Equal(0u, modifiers);
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData("0", HotKeyParser.VK_0)]
    [InlineData("1", HotKeyParser.VK_1)]
    [InlineData("9", HotKeyParser.VK_9)]
    public void ParseHotKey_DigitKeys(string input, uint expectedKey)
    {
        var (_, key) = HotKeyParser.ParseHotKey(input);
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseHotKey_EmptyOrWhitespace_ReturnsZero(string? input)
    {
        var (modifiers, key) = HotKeyParser.ParseHotKey(input!);
        Assert.Equal(0u, modifiers);
        Assert.Equal(0u, key);
    }

    [Theory]
    [InlineData("Garbage")]
    [InlineData("Ctrl+Garbage")]
    [InlineData("F13")]
    [InlineData("A")]
    [InlineData("Space")]
    public void ParseHotKey_UnknownKey_ReturnsZeroKey(string input)
    {
        var (_, key) = HotKeyParser.ParseHotKey(input);
        Assert.Equal(0u, key);
    }

    [Theory]
    [InlineData("ctrl+printscreen")]
    [InlineData("CTRL+PRINTSCREEN")]
    [InlineData("CtRl+PrInTsCrEeN")]
    public void ParseHotKey_IsCaseInsensitive(string input)
    {
        var (modifiers, key) = HotKeyParser.ParseHotKey(input);
        Assert.Equal(HotKeyParser.MOD_CONTROL, modifiers);
        Assert.Equal(HotKeyParser.VK_SNAPSHOT, key);
    }

    [Fact]
    public void ParseHotKey_ToleratesExtraSpaces()
    {
        var (modifiers, key) = HotKeyParser.ParseHotKey("  Ctrl  +  PrintScreen  ");
        Assert.Equal(HotKeyParser.MOD_CONTROL, modifiers);
        Assert.Equal(HotKeyParser.VK_SNAPSHOT, key);
    }
}
