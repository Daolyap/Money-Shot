using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MoneyShot.UI.Views;

/// <summary>
/// Minimal RGB color picker for EditorWindow's "custom color" button on the non-Windows build —
/// see EditorWindow.axaml.cs's CustomColorButton_Click (#if WINDOWS keeps using the WinForms
/// ColorDialog there; WinForms isn't referenced at all under net10.0, see MoneyShot.UI.csproj).
/// Three 0-255 sliders plus a live hex readout/entry and preview swatch — not a full HSV/wheel
/// picker, but covers "pick any RGB color" which is all CustomColorButton needs. Same hand-rolled-
/// window pattern as SimpleMessageBox, for the same reason (Avalonia has no built-in color dialog
/// in the base packages this project already references).
/// </summary>
public static class SimpleColorDialog
{
    public static Task<Color?> ShowAsync(Window owner, Color initial)
    {
        var tcs = new TaskCompletionSource<Color?>();

        var dialog = new Window
        {
            Title = "Custom Color",
            Width = 320,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (IBrush?)Avalonia.Application.Current!.Resources["Cocoa.WindowBrush"],
            Foreground = (IBrush?)Avalonia.Application.Current!.Resources["Cocoa.TextBrush"]
        };

        var preview = new Border
        {
            Height = 48,
            CornerRadius = new Avalonia.CornerRadius(6),
            Background = new SolidColorBrush(initial),
            Margin = new Avalonia.Thickness(0, 0, 0, 12)
        };

        var hexBox = new TextBox { Text = ToHex(initial), Margin = new Avalonia.Thickness(0, 0, 0, 12) };

        var (rPanel, rSlider, rLabel) = BuildChannelRow("R", initial.R);
        var (gPanel, gSlider, gLabel) = BuildChannelRow("G", initial.G);
        var (bPanel, bSlider, bLabel) = BuildChannelRow("B", initial.B);

        var updatingFromHex = false;
        var updatingFromSliders = false;

        void UpdateFromSliders()
        {
            if (updatingFromHex) return;
            updatingFromSliders = true;
            var color = Color.FromRgb((byte)rSlider.Value, (byte)gSlider.Value, (byte)bSlider.Value);
            preview.Background = new SolidColorBrush(color);
            hexBox.Text = ToHex(color);
            rLabel.Text = ((byte)rSlider.Value).ToString();
            gLabel.Text = ((byte)gSlider.Value).ToString();
            bLabel.Text = ((byte)bSlider.Value).ToString();
            updatingFromSliders = false;
        }

        rSlider.ValueChanged += (_, _) => UpdateFromSliders();
        gSlider.ValueChanged += (_, _) => UpdateFromSliders();
        bSlider.ValueChanged += (_, _) => UpdateFromSliders();

        hexBox.TextChanged += (_, _) =>
        {
            if (updatingFromSliders) return;
            if (!TryParseHex(hexBox.Text, out var parsed)) return;
            updatingFromHex = true;
            rSlider.Value = parsed.R;
            gSlider.Value = parsed.G;
            bSlider.Value = parsed.B;
            preview.Background = new SolidColorBrush(parsed);
            updatingFromHex = false;
        };

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Avalonia.Thickness(0, 12, 0, 0) };

        void CloseWith(Color? result)
        {
            tcs.TrySetResult(result);
            dialog.Close();
        }

        var cancelButton = new Button { Content = "Cancel", Theme = (Avalonia.Styling.ControlTheme?)Avalonia.Application.Current!.Resources["SubtleButton"], MinWidth = 76 };
        cancelButton.Click += (_, _) => CloseWith(null);

        var okButton = new Button { Content = "OK", Theme = (Avalonia.Styling.ControlTheme?)Avalonia.Application.Current!.Resources["AccentButton"], MinWidth = 76, IsDefault = true };
        okButton.Click += (_, _) => CloseWith(Color.FromRgb((byte)rSlider.Value, (byte)gSlider.Value, (byte)bSlider.Value));

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children = { preview, hexBox, rPanel, gPanel, bPanel, buttonPanel }
        };

        dialog.Closed += (_, _) => tcs.TrySetResult(null);

        dialog.ShowDialog(owner);
        return tcs.Task;
    }

    private static (StackPanel panel, Slider slider, TextBlock valueLabel) BuildChannelRow(string label, byte initialValue)
    {
        var slider = new Slider { Minimum = 0, Maximum = 255, Value = initialValue, Width = 200 };
        var valueLabel = new TextBlock { Text = initialValue.ToString(), Width = 32, VerticalAlignment = VerticalAlignment.Center };
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 4),
            Children =
            {
                new TextBlock { Text = label, Width = 14, VerticalAlignment = VerticalAlignment.Center },
                slider,
                valueLabel
            }
        };
        return (panel, slider, valueLabel);
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim().TrimStart('#');
        if (trimmed.Length != 6) return false;
        if (!byte.TryParse(trimmed[..2], System.Globalization.NumberStyles.HexNumber, null, out var r)) return false;
        if (!byte.TryParse(trimmed[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g)) return false;
        if (!byte.TryParse(trimmed[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b)) return false;
        color = Color.FromRgb(r, g, b);
        return true;
    }
}
