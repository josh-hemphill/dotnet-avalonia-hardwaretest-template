using Avalonia.Input;
using HardwareTest.Features.RunTest;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class RunBoardKeyboardTests
{
    [Theory]
    [InlineData(Key.F, KeyModifiers.None, false, RunBoardShortcut.NextFail)]
    [InlineData(Key.F, KeyModifiers.Shift, false, RunBoardShortcut.PrevFail)]
    [InlineData(Key.Oem2, KeyModifiers.None, false, RunBoardShortcut.FocusSearch)]
    [InlineData(Key.Divide, KeyModifiers.None, false, RunBoardShortcut.FocusSearch)]
    [InlineData(Key.F, KeyModifiers.Control, false, RunBoardShortcut.FocusSearch)]
    [InlineData(Key.F, KeyModifiers.Control, true, RunBoardShortcut.FocusSearch)]
    [InlineData(Key.Oem2, KeyModifiers.Control, true, RunBoardShortcut.FocusSearch)]
    public void Maps_board_and_modifier_shortcuts(
        Key key,
        KeyModifiers modifiers,
        bool textInputFocused,
        RunBoardShortcut expected)
    {
        Assert.True(RunBoardKeyboard.TryMap(key, modifiers, textInputFocused, out var shortcut));
        Assert.Equal(expected, shortcut);
    }

    [Theory]
    [InlineData(Key.F, KeyModifiers.None)]
    [InlineData(Key.Oem2, KeyModifiers.None)]
    [InlineData(Key.Divide, KeyModifiers.None)]
    [InlineData(Key.A, KeyModifiers.None)]
    public void Ignores_bare_letter_and_slash_while_typing(Key key, KeyModifiers modifiers)
    {
        Assert.False(RunBoardKeyboard.TryMap(key, modifiers, textInputFocused: true, out var shortcut));
        Assert.Equal(RunBoardShortcut.None, shortcut);
    }
}
