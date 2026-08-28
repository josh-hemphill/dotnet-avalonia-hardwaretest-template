using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using HardwareTest.Features.Shell;

namespace HardwareTest.Features.RunTest;

/// <summary>Run page: compact header, reserved progress, and one workspace at a time.</summary>
public partial class RunTestView : UserControl
{
    private RunTestViewModel? _subscribed;

    public RunTestView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnBoardSizeChanged;
        DetachedFromVisualTree += (_, _) => Unsubscribe();
        Loaded += (_, _) => ApplyCompactFlags();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        if (DataContext is not RunTestViewModel vm)
        {
            return;
        }

        _subscribed = vm;
        vm.ProgramSelection.RequestPlanFilePath = PickPlanFileAsync;
        ApplyCompactFlags();
    }

    private void Unsubscribe()
    {
        if (_subscribed is null)
        {
            return;
        }

        _subscribed.ProgramSelection.RequestPlanFilePath = null;
        _subscribed = null;
    }

    private void OnBoardSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged && !e.HeightChanged)
        {
            return;
        }

        ApplyCompactFlags();
    }

    private void ApplyCompactFlags()
    {
        if (_subscribed is null)
        {
            return;
        }

        _subscribed.IsCompactLayout = Bounds.Width > 0 && Bounds.Width < ShellLayoutBreakpoints.CompactBoardWidth;
        _subscribed.IsCompactHeight = Bounds.Height > 0 && Bounds.Height < ShellLayoutBreakpoints.CompactBoardHeight;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_subscribed is null || e.Handled)
        {
            return;
        }

        var textInput = RunBoardKeyboard.IsTextInputTarget(e.Source);
        if (e.Key == Key.Escape && !textInput && _subscribed.Workspace.CanReturnToSteps)
        {
            _subscribed.Workspace.OpenSteps();
            e.Handled = true;
            return;
        }

        if (!RunBoardKeyboard.TryMap(e.Key, e.KeyModifiers, textInput, out var shortcut))
        {
            return;
        }

        switch (shortcut)
        {
            case RunBoardShortcut.FocusSearch:
                ((ICommand)_subscribed.StepTree.FocusStepSearchCommand).Execute(null);
                break;
            case RunBoardShortcut.NextFail:
                ((ICommand)_subscribed.StepTree.NextFailCommand).Execute(null);
                break;
            case RunBoardShortcut.PrevFail:
                ((ICommand)_subscribed.StepTree.PrevFailCommand).Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private async Task<string?> PickPlanFileAsync(CancellationToken cancellationToken)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return null;
        }

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open OpenTAP plan",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("OpenTAP Plan") { Patterns = ["*.TapPlan"] },
                FilePickerFileTypes.All,
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
