using Avalonia.Controls;
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

                if (pages.Any(p => string.Equals(p.Descriptor.Id, id, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        $"Shell app '{application.Id}' duplicates page id '{id}'.");
                }

                if (views.IsRegistered(registration.Descriptor.ViewModelType))
                {
                    throw new InvalidOperationException(
                        $"Shell app '{application.Id}' page '{id}' reuses a registered ViewModel type '{registration.Descriptor.ViewModelType}'.");
                }

                views.Register(
                    registration.Descriptor.ViewModelType,
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
