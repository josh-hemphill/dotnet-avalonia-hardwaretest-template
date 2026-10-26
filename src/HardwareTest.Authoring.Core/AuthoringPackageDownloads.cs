using OpenTap.Package;

namespace HardwareTest.Authoring;

/// Downloads into operation-owned staging; publication still uses the checked bootstrap transaction.
internal static class AuthoringPackageDownloads
{
    public const string DefaultRepository = "https://packages.opentap.io";

    public static Uri RepositoryUri(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new AuthoringWorkspaceException("Use an HTTPS package repository URL without credentials, a query or a fragment. Manual download and offline import remain available.");
        return uri;
    }

    internal static void Prepare(AuthoringManifest manifest, string home, string repositoryUrl,
        Action<string>? progress = null, Action<AuthoringPackageDependency, string, string>? download = null)
    {
        var missing = AuthoringEnvironmentAssessment.Packages(manifest, new(home)).Where(p => !p.Optional && !p.Satisfied).ToArray();
        if (missing.Length == 0) return;
        var repository = RepositoryUri(repositoryUrl).AbsoluteUri;
        var owned = Path.Combine(home, ".package-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(owned);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requirements = manifest.Dependencies.Concat(manifest.OptionalDependencies).ToList();
        try
        {
            foreach (var requirement in missing) Install(new() { Package = requirement.Package, Version = requirement.RequiredVersion });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new AuthoringWorkspaceException("Package download/preparation failed. The selected home was preserved. "
                + "Open the package source, copy requirements, then use Import offline package to continue. " + error.Message, error);
        }
        finally { Directory.Delete(owned, recursive: true); }

        void Install(AuthoringPackageDependency requirement)
        {
            var check = new AuthoringManifest { Dependencies = [requirement] };
            if (AuthoringEnvironmentAssessment.Packages(check, new(home)).All(p => p.Satisfied)) return;
            if (IsBundled(requirement.Package))
                throw new AuthoringWorkspaceException($"'{requirement.Package}' requires a compatible bundled release or a matching manual import; automatic downloads do not replace the authoring runtime or its bundled adapters.");
            if (visiting.Count >= 64 || !visiting.Add(requirement.Package))
                throw new AuthoringWorkspaceException("Package dependencies contain a cycle or exceed the preparation limit.");
            var archive = Path.Combine(owned, Guid.NewGuid().ToString("N") + ".TapPackage");
            progress?.Invoke($"Downloading {requirement.Package} ({requirement.Version})");
            (download ?? Download)(requirement, repository, archive);
            var package = PackageDef.FromPackage(archive);
            if (!package.Name.Equals(requirement.Package, StringComparison.OrdinalIgnoreCase)
                || !VersionSpecifier.Parse(requirement.Version).IsCompatible(package.Version)
                || !package.IsPlatformCompatible(ArchitectureHelper.HostArchitecture, HostOS))
                throw new AuthoringWorkspaceException($"Downloaded archive does not match '{requirement.Package}' {requirement.Version} for this platform.");
            var dependencies = package.Dependencies.Select(d => new AuthoringPackageDependency { Package = d.Name, Version = d.Version.ToString() }).ToArray();
            requirements.AddRange(dependencies);
            // The importer validates every known constraint against the actual archive bytes.
            var constraints = new AuthoringManifest { Dependencies = requirements };
            progress?.Invoke($"Checking {requirement.Package}");
            OpenTapHomeBootstrapper.InstallFileOrDirectoryPackage(archive, home, constraints);
            foreach (var dependency in dependencies) Install(dependency);
            visiting.Remove(requirement.Package);
        }
    }

    private static string HostOS => System.OperatingSystem.IsWindows() ? "Windows" : System.OperatingSystem.IsMacOS() ? "MacOS" : "Linux";

    private static bool IsBundled(string name) => new[] { "OpenTAP", "HardwareTest Basic", "HardwareTest Mixins",
        "HardwareTest Standalone VISA", OpenTapHomeBootstrapper.InstrumentComponentsPackageName }.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static void Download(AuthoringPackageDependency requirement, string repositoryUrl, string destination)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            IPackageRepository repository = new HttpPackageRepository(repositoryUrl);
            var specifier = new PackageSpecifier(requirement.Package, VersionSpecifier.Parse(requirement.Version), ArchitectureHelper.HostArchitecture, HostOS);
            var package = repository.GetPackages(specifier, timeout.Token, [])
                .Where(p => p.Name.Equals(requirement.Package, StringComparison.OrdinalIgnoreCase)
                    && specifier.Version.IsCompatible(p.Version) && p.IsPlatformCompatible(ArchitectureHelper.HostArchitecture, HostOS))
                .OrderByDescending(p => p.Version).FirstOrDefault()
                ?? throw new AuthoringWorkspaceException($"No compatible '{requirement.Package}' {requirement.Version} was found in the configured repository.");
            repository.DownloadPackage(package, destination, timeout.Token);
        }
        catch (OperationCanceledException error) when (timeout.IsCancellationRequested)
        { throw new TimeoutException("The package repository did not complete the download within two minutes. Try again or use manual download.", error); }
    }
}
