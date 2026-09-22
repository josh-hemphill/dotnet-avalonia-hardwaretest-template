using HardwareTest.Shell;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace HardwareTest.Features.Home;

/// One guest Home tile. Visibility follows operator vs engineer placement.
public partial class HomeShellTileViewModel : ReactiveObject
{
    private readonly ShellPagePlacement _placement;

    public HomeShellTileViewModel(ShellHomeTile tile, bool engineerMode, Action<string> navigate)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(navigate);
        _placement = tile.Placement;
        Title = tile.Title;
        Body = tile.Body;
        ActionLabel = tile.ActionLabel;
        NavigateCommand = ReactiveCommand.Create(() => navigate(tile.NavigatePageId));
        RefreshVisibility(engineerMode);
    }

    public string Title { get; }
    public string Body { get; }
    public string ActionLabel { get; }
    public ReactiveCommand<ReactiveUI.Primitives.RxVoid, ReactiveUI.Primitives.RxVoid> NavigateCommand { get; }

    [Reactive] private bool _isVisible;

    /// Updates visibility when engineer presentation changes.
    public void RefreshVisibility(bool engineerMode)
    {
        IsVisible = _placement == ShellPagePlacement.Operator
                    || (engineerMode && _placement == ShellPagePlacement.Engineer);
    }
}
