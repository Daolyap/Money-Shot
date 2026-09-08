using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MoneyShot.Models;
using MoneyShot.Platform.Windows;
using MoneyShot.Services;
using MoneyShot.UI.Services;

namespace MoneyShot.UI.Views;

/// <summary>
/// Avalonia port of MoneyShot/Views/HistoryWindow.xaml.cs — see LINUX_PORT.md Phase 1.
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly HistoryService _history;
    private readonly SaveService _saveService = new(new Win32Clipboard());

    public HistoryWindow(HistoryService history)
    {
        InitializeComponent();
        _history = history;
        Refresh();
    }

    private void Refresh()
    {
        var entries = _history.List();
        CountLabel.Text = entries.Count == 0
            ? "No captures yet — your saved screenshots will appear here."
            : $"{entries.Count} capture{(entries.Count == 1 ? string.Empty : "s")}";

        HistoryList.Items.Clear();
        foreach (var entry in entries)
        {
            HistoryList.Items.Add(BuildThumbnailTile(entry));
        }
    }

    private Control BuildThumbnailTile(HistoryEntry entry)
    {
        var thumb = _history.LoadThumbnail(entry) ?? _history.LoadImage(entry);
        var image = new Image
        {
            Source = thumb,
            Width = 220,
            Height = 140,
            Stretch = Stretch.Uniform,
        };

        var meta = new TextBlock
        {
            Text = $"{entry.CapturedAt:yyyy-MM-dd HH:mm:ss}\n{entry.Width}×{entry.Height} · {entry.Source}",
            Foreground = (IBrush)Avalonia.Application.Current!.Resources["Cocoa.TextSecondaryBrush"]!,
            FontSize = 11,
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };

        var stack = new StackPanel { Width = 220 };
        stack.Children.Add(image);
        stack.Children.Add(meta);

        var border = new Border
        {
            Classes = { "thumb-tile" },
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = entry,
            Child = stack,
        };
        border.PointerReleased += Tile_OpenInEditor;
        border.ContextMenu = BuildContextMenu(entry);
        return border;
    }

    private ContextMenu BuildContextMenu(HistoryEntry entry)
    {
        var menu = new ContextMenu();
        var items = new Avalonia.Controls.Controls();

        var openItem = new MenuItem { Header = "Open in Editor" };
        openItem.Click += (_, _) => OpenInEditor(entry);
        items.Add(openItem);

        var copyItem = new MenuItem { Header = "Copy to Clipboard" };
        copyItem.Click += (_, _) => CopyToClipboard(entry);
        items.Add(copyItem);

        items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += async (_, _) =>
        {
            var result = await SimpleMessageBox.ShowAsync(this, "Delete this capture from history?", "Money Shot", SimpleMessageBoxButtons.YesNo);
            if (result != SimpleMessageBoxResult.Yes) return;
            _history.Delete(entry);
            Refresh();
        };
        items.Add(deleteItem);

        menu.ItemsSource = items;
        return menu;
    }

    private void Tile_OpenInEditor(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (sender is Border b && b.Tag is HistoryEntry entry) OpenInEditor(entry);
    }

    private async void OpenInEditor(HistoryEntry entry)
    {
        var image = _history.LoadImage(entry);
        if (image == null)
        {
            await SimpleMessageBox.ShowAsync(this, "This capture's image file could not be loaded.", "Money Shot");
            return;
        }
        try
        {
            var editor = new EditorWindow(image);
            await editor.ShowDialog(this);
        }
        finally
        {
            // Same working-set trim the capture flow does — without it, opening a capture from
            // history left hundreds of MB of bitmap backings resident after the editor closed.
            MemoryTrimmer.TrimAfterEditorClose();
        }
    }

    private async void CopyToClipboard(HistoryEntry entry)
    {
        var image = _history.LoadImage(entry);
        if (image == null) return;
        try
        {
            _saveService.SaveToClipboard(image);
        }
        catch (Exception ex)
        {
            Logger.Error("Copy from history failed", ex);
            await SimpleMessageBox.ShowAsync(this, "Failed to copy image to clipboard.", "Money Shot");
        }
    }

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _history.HistoryDirectory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to open history folder", ex);
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 1 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
