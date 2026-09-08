using Avalonia;
using Avalonia.Controls;

namespace MoneyShot.UI.Editor;

/// <summary>
/// Avalonia port of MoneyShot/Editor/CanvasPosition.cs — see LINUX_PORT.md Phase 1. Centralizes
/// the NaN guards that used to be repeated everywhere we read Canvas.GetLeft/GetTop. Canvas
/// attached properties default to NaN when never set, which propagates through arithmetic and
/// ends up displayed as 0 — the cause of past snap-to-(0,0) regressions in the WPF build.
/// </summary>
internal static class CanvasPosition
{
    public static double GetLeft(Control element)
    {
        var value = Canvas.GetLeft(element);
        return double.IsNaN(value) ? 0 : value;
    }

    public static double GetTop(Control element)
    {
        var value = Canvas.GetTop(element);
        return double.IsNaN(value) ? 0 : value;
    }
}
