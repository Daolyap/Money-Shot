using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MoneyShot.UI.Views;

public enum SimpleMessageBoxButtons
{
    Ok,
    YesNo
}

public enum SimpleMessageBoxResult
{
    Ok,
    Yes,
    No
}

/// <summary>
/// Avalonia has no built-in MessageBox (unlike WPF's System.Windows.MessageBox). This is a
/// minimal stand-in — a modal Window with a message and OK, or Yes/No, buttons — used everywhere
/// the WPF build called MessageBox.Show. Every call site becomes async (ShowDialog is Task-based
/// in Avalonia), which is the one mechanical ripple this causes throughout the port.
/// </summary>
public static class SimpleMessageBox
{
    public static Task<SimpleMessageBoxResult> ShowAsync(Window owner, string text, string title,
        SimpleMessageBoxButtons buttons = SimpleMessageBoxButtons.Ok)
    {
        var tcs = new TaskCompletionSource<SimpleMessageBoxResult>();

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (IBrush?)Avalonia.Application.Current!.Resources["Cocoa.WindowBrush"],
            Foreground = (IBrush?)Avalonia.Application.Current!.Resources["Cocoa.TextBrush"]
        };

        var messageBlock = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 18)
        };

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };

        void CloseWith(SimpleMessageBoxResult result)
        {
            tcs.TrySetResult(result);
            dialog.Close();
        }

        if (buttons == SimpleMessageBoxButtons.YesNo)
        {
            var noButton = new Button { Content = "No", Theme = (Avalonia.Styling.ControlTheme?)Avalonia.Application.Current!.Resources["SubtleButton"], MinWidth = 76 };
            noButton.Click += (_, _) => CloseWith(SimpleMessageBoxResult.No);
            var yesButton = new Button { Content = "Yes", Theme = (Avalonia.Styling.ControlTheme?)Avalonia.Application.Current!.Resources["AccentButton"], MinWidth = 76, IsDefault = true };
            yesButton.Click += (_, _) => CloseWith(SimpleMessageBoxResult.Yes);
            buttonPanel.Children.Add(noButton);
            buttonPanel.Children.Add(yesButton);
        }
        else
        {
            var okButton = new Button { Content = "OK", Theme = (Avalonia.Styling.ControlTheme?)Avalonia.Application.Current!.Resources["AccentButton"], MinWidth = 76, IsDefault = true };
            okButton.Click += (_, _) => CloseWith(SimpleMessageBoxResult.Ok);
            buttonPanel.Children.Add(okButton);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children = { messageBlock, buttonPanel }
        };

        // If the dialog is closed via the OS close button rather than a button click, resolve as
        // Ok/No (whichever reads as "didn't confirm") so awaiting callers never hang.
        dialog.Closed += (_, _) => tcs.TrySetResult(buttons == SimpleMessageBoxButtons.YesNo ? SimpleMessageBoxResult.No : SimpleMessageBoxResult.Ok);

        dialog.ShowDialog(owner);
        return tcs.Task;
    }
}
