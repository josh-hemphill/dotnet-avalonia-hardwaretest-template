using Avalonia.Controls;
using HardwareTest.Features.Shell;
using HardwareTest.Shell;

namespace HardwareTest;

/// Binds bake-time shell apps onto the page catalog and view registrar. No assembly scanning.
public static class ShellApplicationComposer
{
    /// Builds guest pages, refusing reserved ids and apps that require a newer host ABI.
    public static IReadOnlyList<ShellPage> BindPages(
        IEnumerable<IShellApplication> applications,
        IServiceProvider services,
        IViewRegistrar views)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(views);

        var pages = new List<ShellPage>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenViewModelTypes = new HashSet<Type>();
        foreach (var application in applications)
        {
            ArgumentNullException.ThrowIfNull(application);
            if (application.MinHostAbi > ShellHostAbi.Current)
            {
                throw new InvalidOperationException(
                    $"Shell app '{application.Id}' requires ABI {application.MinHostAbi}; host ABI is {ShellHostAbi.Current}.");
            }

            foreach (var registration in application.Pages)
            {
                var id = registration.Descriptor.Id;
                if (ShellBuiltInPageIds.IsReserved(id))
                {
                    throw new InvalidOperationException(
                        $"Shell app '{application.Id}' cannot reuse reserved page id '{id}'.");
                }

                if (!seenIds.Add(id))
                {
                    throw new InvalidOperationException(
                        $"Shell app '{application.Id}' duplicates page id '{id}'.");
                }

                var viewModelType = registration.Descriptor.ViewModelType;
                if (BuiltinShellPages.Descriptors.Any(d => d.ViewModelType == viewModelType)
                    || !seenViewModelTypes.Add(viewModelType))
                {
                    throw new InvalidOperationException(
                        $"Shell app '{application.Id}' page '{id}' reuses a registered ViewModel type '{viewModelType}'.");
                }

                views.Register(
                    viewModelType,
                    () => registration.CreateView() as Control
                          ?? throw new InvalidOperationException(
                              $"Shell page '{id}' CreateView must return an Avalonia Control."));
                pages.Add(new ShellPage
                {
                    Descriptor = registration.Descriptor,
                    ViewModel = registration.CreateViewModel(services),
                });
            }
        }

        return pages;
    }
}
