using Avalonia.Controls;
using Avalonia.Layout;

namespace HardwareTest.Features.RunTest;

/// Owns list↔Details star shares and Phase 16 Focus/Band nested star reset.
public sealed class RunBoardLayoutController
{
    private readonly Func<Grid?> _boardGrid;
    private readonly Func<Grid?> _detailDrawerGrid;
    private readonly int _stepListRowIndex;
    private readonly int _detailDrawerRowIndex;
    private readonly int _detailFocusRowIndex;
    private readonly int _detailBandRowIndex;
    private double _listStarShare = 1;
    private double _detailStarShare = 1;

    public RunBoardLayoutController(
        Func<Grid?> boardGrid,
        Func<Grid?> detailDrawerGrid,
        int stepListRowIndex,
        int detailDrawerRowIndex,
        int detailFocusRowIndex,
        int detailBandRowIndex)
    {
        _boardGrid = boardGrid;
        _detailDrawerGrid = detailDrawerGrid;
        _stepListRowIndex = stepListRowIndex;
        _detailDrawerRowIndex = detailDrawerRowIndex;
        _detailFocusRowIndex = detailFocusRowIndex;
        _detailBandRowIndex = detailBandRowIndex;
    }

    /// Restores list/drawer/Focus star shares after Details or Focus chrome changes.
    public void ApplyDetailDrawerRows(bool showDetails, bool showFocus)
    {
        var board = _boardGrid();
        if (board is null || board.RowDefinitions.Count <= _detailDrawerRowIndex)
        {
            return;
        }

        _listStarShare = 1;
        _detailStarShare = 1;
        ApplyListDetailShares(showDetails);

        var drawer = _detailDrawerGrid();
        if (drawer is null || drawer.RowDefinitions.Count <= _detailBandRowIndex)
        {
            return;
        }

        drawer.RowDefinitions[_detailFocusRowIndex].Height = showDetails && showFocus
            ? new GridLength(3, GridUnitType.Star)
            : new GridLength(0);
        drawer.RowDefinitions[_detailBandRowIndex].Height = showDetails && showFocus
            ? new GridLength(2, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
    }

    public void NudgeDetailsTaller()
    {
        _detailStarShare = Math.Min(3.0, _detailStarShare + 0.35);
        _listStarShare = Math.Max(0.45, _listStarShare - 0.35);
        ApplyListDetailShares(showDetails: true);
    }

    public void NudgeDetailsShorter()
    {
        _listStarShare = Math.Min(3.0, _listStarShare + 0.35);
        _detailStarShare = Math.Max(0.45, _detailStarShare - 0.35);
        ApplyListDetailShares(showDetails: true);
    }

    private void ApplyListDetailShares(bool showDetails)
    {
        var board = _boardGrid();
        if (board is null || board.RowDefinitions.Count <= _detailDrawerRowIndex)
        {
            return;
        }

        board.RowDefinitions[_stepListRowIndex].Height = new GridLength(_listStarShare, GridUnitType.Star);
        board.RowDefinitions[_detailDrawerRowIndex].Height = showDetails
            ? new GridLength(_detailStarShare, GridUnitType.Star)
            : new GridLength(0);
    }
}
