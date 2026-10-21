using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace HardwareTest.Authoring;

public partial class MainWindow
{
    private Window? _stepPalette;

    internal async void OnOpenStepPalette(object? sender, RoutedEventArgs e)
    {
        if (_stepPalette is not null) { _stepPalette.Activate(); return; }
        if (_viewModel.SelectedProgram is null) return;
        CommitFocusedEditor();
        var current = OwnerContext();
        var plan = _viewModel.SelectedProgram.PlanId;
        var node = _viewModel.SelectedSequence?.Key;
        bool TargetCurrent() => current() && plan == _viewModel.SelectedProgram?.PlanId && node == _viewModel.SelectedSequence?.Key;
        var content = new StepPaletteView { DataContext = _viewModel };
        var dialog = new Window
        {
            Title = "Add a step", Width = 620, Height = Math.Min(650, ClientSize.Height - 60),
            MinWidth = 520, MinHeight = 440, FontSize = FontSize, Content = content,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Classes = { "authoringPalette" }
        };
        content.InsertRequested += (_, _) =>
        {
            if (!TargetCurrent()) { dialog.Close(); return; }
            OnAddRecipe(sender, e);
            if (_viewModel.Error is null) dialog.Close();
        };
        void OnTargetChanged(object? _, System.ComponentModel.PropertyChangedEventArgs change)
        {
            if (!TargetCurrent()) dialog.Close();
        }
        _viewModel.PropertyChanged += OnTargetChanged;
        dialog.Opened += (_, _) => content.FocusSearch();
        dialog.KeyDown += (_, args) => { if (args.Key == Key.Escape) { dialog.Close(); args.Handled = true; } };
        _stepPalette = dialog;
        try { await dialog.ShowDialog(this); }
        finally { _viewModel.PropertyChanged -= OnTargetChanged; _stepPalette = null; }
        if (current() && plan == _viewModel.SelectedProgram?.PlanId)
            this.GetVisualDescendants().OfType<SequenceEditorView>().SingleOrDefault()?.FocusAdd();
    }
}
