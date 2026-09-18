using HardwareTest.Features.Home;
using HardwareTest.Features.Inspect;
using HardwareTest.Features.Instruments;
using HardwareTest.Features.ReportPreview;
using HardwareTest.Features.Results;
using HardwareTest.Features.RunTest;
using HardwareTest.Features.Settings;
using HardwareTest.Shell;

namespace HardwareTest.Features.Shell;

/// Built-in test-pack pages. Guest apps append later; they must not reuse these ids.
public static class BuiltinShellPages
{
    public static IReadOnlyList<ShellPageDescriptor> Descriptors { get; } =
    [
        new()
        {
            Id = ShellBuiltInPageIds.Home,
            Title = "Home",
            SymbolName = "Home",
            ViewModelType = typeof(HomeViewModel),
            Placement = ShellPagePlacement.Operator,
            Order = 0,
        },
        new()
        {
            Id = ShellBuiltInPageIds.RunTest,
            Title = "Run",
            SymbolName = "Play",
            ViewModelType = typeof(RunTestViewModel),
            Placement = ShellPagePlacement.Operator,
            Order = 10,
        },
        new()
        {
            Id = ShellBuiltInPageIds.Inspect,
            Title = "Inspect",
            SymbolName = "Document",
            ViewModelType = typeof(InspectViewModel),
            Placement = ShellPagePlacement.Engineer,
            Order = 20,
        },
        new()
        {
            Id = ShellBuiltInPageIds.Results,
            Title = "Results",
            SymbolName = "DocumentFilled",
            ViewModelType = typeof(ResultsViewModel),
            Placement = ShellPagePlacement.Operator,
            Order = 30,
        },
        new()
        {
            Id = ShellBuiltInPageIds.ReportPreview,
            Title = "Report Preview",
            SymbolName = "Document",
            ViewModelType = typeof(ReportPreviewViewModel),
            Placement = ShellPagePlacement.Contextual,
            ContextualParentId = ShellBuiltInPageIds.Results,
            Order = 40,
        },
        new()
        {
            Id = ShellBuiltInPageIds.Instruments,
            Title = "Instruments",
            SymbolName = "Repair",
            ViewModelType = typeof(InstrumentsViewModel),
            Placement = ShellPagePlacement.Engineer,
            CanRemainWithoutNav = true,
            Order = 50,
        },
        new()
        {
            Id = ShellBuiltInPageIds.Settings,
            Title = "Settings",
            SymbolName = "Settings",
            ViewModelType = typeof(SettingsViewModel),
            Placement = ShellPagePlacement.Operator,
            Order = 90,
        },
    ];

    /// Binds live ViewModels onto the built-in descriptors (order is descriptor order).
    public static ShellPageCatalog Bind(
        HomeViewModel home,
        RunTestViewModel runTest,
        InspectViewModel inspect,
        ResultsViewModel results,
        ReportPreviewViewModel reportPreview,
        InstrumentsViewModel instruments,
        SettingsViewModel settings)
    {
        var byType = new Dictionary<Type, object>
        {
            [typeof(HomeViewModel)] = home,
            [typeof(RunTestViewModel)] = runTest,
            [typeof(InspectViewModel)] = inspect,
            [typeof(ResultsViewModel)] = results,
            [typeof(ReportPreviewViewModel)] = reportPreview,
            [typeof(InstrumentsViewModel)] = instruments,
            [typeof(SettingsViewModel)] = settings,
        };

        var pages = new List<ShellPage>(Descriptors.Count);
        foreach (var descriptor in Descriptors)
        {
            if (!byType.TryGetValue(descriptor.ViewModelType, out var viewModel))
            {
                throw new InvalidOperationException(
                    $"No ViewModel registered for built-in page '{descriptor.Id}'.");
            }

            pages.Add(new ShellPage { Descriptor = descriptor, ViewModel = viewModel });
        }

        return new ShellPageCatalog(pages);
    }

    /// Built-in descriptor for <paramref name="pageId"/>, or null when the id is not reserved.
    public static ShellPageDescriptor? Find(string pageId)
        => Descriptors.FirstOrDefault(d => string.Equals(d.Id, pageId, StringComparison.Ordinal));
}
