using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace HardwareTest.Features.RunTest;

public enum RunBoardShortcut
{
    None,
    FocusSearch,
    NextFail,
    PrevFail,
}

/// Maps Run-board keybindings; ignores letter/slash shortcuts while typing in a field.
public static class RunBoardKeyboard
{
    public static bool IsTextInputTarget(object? source)
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            switch (current)
            {
                case TextBox:
                case AutoCompleteBox:
                case NumericUpDown:
                case ComboBox:
                    return true;
            }
        }

        return false;
    }

    public static bool TryMap(Key key, KeyModifiers modifiers, bool textInputFocused, out RunBoardShortcut shortcut)
    {
        shortcut = RunBoardShortcut.None;
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            return false;
        }

        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && key is Key.F or Key.Oem2 or Key.Divide)
        {
            shortcut = RunBoardShortcut.FocusSearch;
            return true;
        }

        if (textInputFocused)
        {
            return false;
        }

        if (key is Key.Oem2 or Key.Divide)
        {
            shortcut = RunBoardShortcut.FocusSearch;
            return true;
        }

        if (key != Key.F)
        {
            return false;
        }

        shortcut = modifiers.HasFlag(KeyModifiers.Shift)
            ? RunBoardShortcut.PrevFail
            : RunBoardShortcut.NextFail;
        return true;
    }
}
