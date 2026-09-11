using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using MoneyShot.UI.Views;

namespace MoneyShot.UI.Editor;

/// <summary>
/// Avalonia port of MoneyShot/Editor/UndoController.cs — see LINUX_PORT.md Phase 1. Owns the
/// editor undo stack and the action records; EditorWindow pushes onto this from the places where
/// mutations happen, and calls Undo when the user triggers undo.
/// </summary>
internal sealed class UndoController
{
    private readonly Stack<IUndoAction> _stack = new();

    public int Count => _stack.Count;

    public void Push(IUndoAction action) => _stack.Push(action);

    public void Clear() => _stack.Clear();

    public void Undo(EditorWindow window)
    {
        if (_stack.Count == 0) return;
        _stack.Pop().Undo(window);
    }

    public interface IUndoAction
    {
        void Undo(EditorWindow window);
    }

    public sealed class AddElementUndoAction(Control element) : IUndoAction
    {
        public void Undo(EditorWindow window) => window.UndoAddElement(element);
    }

    public sealed class RemoveElementUndoAction(Control element, int index) : IUndoAction
    {
        public void Undo(EditorWindow window) => window.UndoRemoveElement(element, index);
    }

    public sealed class CropUndoAction(Bitmap previousImage, IReadOnlyList<Control> previousElements, int previousNumberCounter) : IUndoAction
    {
        public void Undo(EditorWindow window) =>
            window.UndoCrop(previousImage, previousElements, previousNumberCounter);
    }

    public sealed class ResizeUndoAction(Control element, ElementState previousState) : IUndoAction
    {
        public void Undo(EditorWindow window) => window.UndoResize(element, previousState);
    }
}
