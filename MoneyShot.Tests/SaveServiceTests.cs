using System.IO;
using MoneyShot.Services;
using Xunit;

namespace MoneyShot.Tests;

public class SaveServiceTests
{
    private readonly SaveService _service = new();

    // Path validation runs before the image is touched, so a null image never reaches the
    // encoder for these rejection cases — no WPF/STA setup needed.

    [Fact]
    public void SaveToFile_InsideWindowsDirectory_IsRejected()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var path = Path.Combine(windows, "moneyshot-test.png");
        Assert.Throws<ArgumentException>(() => _service.SaveToFile(null!, path));
    }

    [Fact]
    public void SaveToFile_NestedUnderSystemDirectory_IsRejected()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var path = Path.Combine(system, "drivers", "moneyshot-test.png");
        Assert.Throws<ArgumentException>(() => _service.SaveToFile(null!, path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SaveToFile_EmptyPath_IsRejected(string path)
    {
        Assert.Throws<ArgumentException>(() => _service.SaveToFile(null!, path));
    }

    [Fact]
    public void SaveToFile_BareDirectoryPath_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => _service.SaveToFile(null!, Path.GetTempPath()));
    }

    [Fact]
    public void SaveToFile_UserWritablePath_PassesValidation()
    {
        // With a valid path, validation must NOT throw ArgumentException; the failure (if any)
        // comes from the null image and is wrapped as InvalidOperationException.
        var path = Path.Combine(Path.GetTempPath(), "moneyshot-test.png");
        var ex = Record.Exception(() => _service.SaveToFile(null!, path));
        Assert.IsType<InvalidOperationException>(ex);
    }

    [Theory]
    [InlineData("PNG", ".png")]
    [InlineData("JPG", ".jpg")]
    [InlineData("BMP", ".bmp")]
    public void GenerateFileName_UsesLowercaseExtension(string format, string expectedExtension)
    {
        var name = _service.GenerateFileName(format);
        Assert.StartsWith("Screenshot_", name);
        Assert.EndsWith(expectedExtension, name);
    }
}
