using HardwareTest.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace HardwareTest;

/// Registers bake-time <see cref="IShellApplication"/> instances and runs <see cref="IShellApplication.Configure"/>.
public static class ShellApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddShellApplications(
        this IServiceCollection services,
        params IShellApplication[] applications)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(applications);
        foreach (var application in applications)
        {
            ArgumentNullException.ThrowIfNull(application);
            services.AddSingleton<IShellApplication>(application);
            application.Configure(services);
        }

        return services;
    }
}
