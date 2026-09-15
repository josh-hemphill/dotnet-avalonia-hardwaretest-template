using HardwareTest.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareTest.ShellApps.Notes;

/// Bake-time sample: one engineer-only Notes page. Operators keep Home / Run / Results / Settings.
public sealed class NotesApplication : IShellApplication
{
    public const string PageId = "hardwaretest.notes";

    public string Id => "hardwaretest.notes";
    public string Title => "Station notes";
    public string Version => "1.0.0";
    public int MinHostAbi => ShellHostAbi.Current;

    public IReadOnlyList<ShellPageRegistration> Pages { get; } =
    [
        new()
        {
            Descriptor = new ShellPageDescriptor
            {
                Id = PageId,
                Title = "Notes",
                SymbolName = "Document",
                ViewModelType = typeof(NotesViewModel),
                Placement = ShellPagePlacement.Engineer,
                Order = 60,
            },
            CreateViewModel = static services => services.GetRequiredService<NotesViewModel>(),
            CreateView = static () => new NotesView(),
        },
    ];

    public IReadOnlyList<ShellHomeTile> HomeTiles { get; } =
    [
        new()
        {
            Title = "Station notes",
            Body = "Engineer-only sample shell app. Open Notes for a placeholder planning/analysis page.",
            ActionLabel = "Open Notes →",
            NavigatePageId = PageId,
            Placement = ShellPagePlacement.Engineer,
        },
    ];

    public void Configure(IServiceCollection services)
        => services.AddSingleton<NotesViewModel>();
}
