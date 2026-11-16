using HardwareTest.OpenTap.Host;
using OpenTap.Package;

namespace HardwareTest.Authoring;

/// Backtracks across the complete dependency graph; a greedy highest-version walk breaks diamonds.
internal sealed class OfflineBenchPackageResolver(OfflineBenchPackageInventory inventory, string owned,
    OfflineBenchExportOptions options, CancellationToken cancellationToken)
{
    private readonly HashSet<string> _queried = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _downloaded = new(StringComparer.OrdinalIgnoreCase);
    private int _attempts;
    private string _failure = "No compatible package closure was found.";

    internal IReadOnlyList<OfflineBenchCandidate> Resolve(AuthoringManifest manifest)
    {
        options.Progress?.Invoke("Resolving required and transitive package version constraints…");
        var requirements = manifest.Dependencies.Select(dependency => new OfflineBenchRequirement
        { Name = dependency.Package, VersionRange = dependency.Version }).ToList();
        var selected = Search(new(StringComparer.OrdinalIgnoreCase), requirements);
        return selected?.Values.OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase).ToArray()
            ?? throw new AuthoringWorkspaceException(_failure + " Copy matching .TapPackage/.zip archives into a package folder and retry. "
                + (options.DownloadMissing ? "Check the repository or use the manual package folder." : "Automatic download is disabled; enable it explicitly or use the manual package folder."));

        Dictionary<string, OfflineBenchCandidate>? Search(Dictionary<string, OfflineBenchCandidate> chosen, List<OfflineBenchRequirement> constraints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++_attempts > 10000 || chosen.Count > 64) throw new AuthoringWorkspaceException("Package dependency resolution exceeded its bounded search limit. Narrow the declared version constraints.");
            var active = constraints.Concat(manifest.OptionalDependencies.Where(optional => constraints.Any(required => required.Name.Equals(optional.Package, StringComparison.OrdinalIgnoreCase)))
                .Select(optional => new OfflineBenchRequirement { Name = optional.Package, VersionRange = optional.Version })).ToList();
            foreach (var package in chosen.Values)
            {
                var packageConstraints = active.Where(requirement => requirement.Name.Equals(package.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (!Satisfies(package, packageConstraints))
                {
                    RecordFailure(package.Name, packageConstraints, chosen);
                    return null;
                }
            }
            var next = active.Select(requirement => requirement.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !chosen.ContainsKey(name)).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (next is null) return chosen;
            if (string.IsNullOrWhiteSpace(next)) throw new AuthoringWorkspaceException("A package dependency has an empty name.");
            inventory.EnsureCandidates(next);
            var required = active.Where(requirement => requirement.Name.Equals(next, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (options.DownloadMissing && !inventory.Candidates.Any(candidate => candidate.Name.Equals(next, StringComparison.OrdinalIgnoreCase) && Satisfies(candidate, required)))
                Download(next, required[0].VersionRange);
            var candidates = inventory.Candidates.Where(candidate => candidate.Name.Equals(next, StringComparison.OrdinalIgnoreCase) && Satisfies(candidate, required))
                .OrderBy(candidate => candidate.Priority).ThenByDescending(candidate => candidate.Version).ThenBy(candidate => candidate.Archive, StringComparer.Ordinal).ToArray();
            foreach (var sameVersion in candidates.GroupBy(candidate => candidate.Version.ToString(), StringComparer.Ordinal))
                if (sameVersion.Select(candidate => candidate.PayloadHash).Distinct(StringComparer.Ordinal).Skip(1).Any())
                    throw new AuthoringWorkspaceException($"Ambiguous package '{next}' {sameVersion.Key}: matching sources contain different payloads. Keep one verified release in the manual folder and choose an unambiguous OpenTAP home.");
            foreach (var candidate in candidates.DistinctBy(candidate => (candidate.Version.ToString(), candidate.PayloadHash)))
            {
                var branch = new Dictionary<string, OfflineBenchCandidate>(chosen, StringComparer.OrdinalIgnoreCase) { [next] = candidate };
                var result = Search(branch, [.. constraints, .. candidate.Dependencies]);
                if (result is not null) return result;
            }
            // A locally available version can fail only after its transitive closure is considered.
            // In opt-in mode, repository alternatives must still be tried before declaring conflict.
            if (options.DownloadMissing && Download(next, required[0].VersionRange)) return Search(chosen, constraints);
            // A parent with available candidates failed because of a real leaf
            // problem; replacing it with the parent's name hides the remedy.
            if (candidates.Length == 0) RecordFailure(next, required, chosen);
            return null;
        }

        void RecordFailure(string name, IEnumerable<OfflineBenchRequirement> constraints, IReadOnlyDictionary<string, OfflineBenchCandidate> chosen)
        {
            _failure = $"Cannot resolve '{name}' for {options.TargetOS}/{options.TargetArchitecture}; required constraints: {string.Join(" AND ", constraints.Select(requirement => requirement.VersionRange).Distinct(StringComparer.Ordinal))}. A required archive is missing or the dependency constraints conflict."
                + (chosen.Count == 0 ? string.Empty : " Selected candidates: " + string.Join(", ", chosen.Values.Select(package => package.Name + " " + package.Version)) + ".")
                + inventory.Diagnostics(name);
        }
    }

    private static bool Satisfies(OfflineBenchCandidate candidate, IEnumerable<OfflineBenchRequirement> requirements)
        => requirements.All(requirement => VersionSpecifier.Parse(requirement.VersionRange).IsCompatible(candidate.Version));

    private bool Download(string name, string constraint)
    {
        var query = name + "\n" + constraint;
        if (!_queried.Add(query)) return false;
        options.Progress?.Invoke($"Looking up {name} ({constraint}) in the selected HTTPS repository…");
        var url = AuthoringPackageDownloads.RepositoryUri(options.RepositoryUrl).AbsoluteUri;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            AuthoringPluginSearch.Search();
            var repository = new HttpPackageRepository(url);
            var architecture = Enum.Parse<CpuArchitecture>(options.TargetArchitecture, ignoreCase: true);
            var specifier = new PackageSpecifier(name, VersionSpecifier.Parse(constraint), architecture, options.TargetOS);
            var available = repository.GetPackages(specifier, timeout.Token, []).Where(package => package.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && specifier.Version.IsCompatible(package.Version) && package.IsPlatformCompatible(architecture, options.TargetOS))
                .OrderByDescending(package => package.Version).Take(128).ToArray();
            var added = false;
            foreach (var package in available)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (!_downloaded.Add(package.Name + "\n" + package.Version + "\n" + package.OS + "\n" + package.Architecture)) continue;
                options.Progress?.Invoke($"Downloading {package.Name} {package.Version} for {options.TargetOS}/{options.TargetArchitecture}…");
                var archive = Path.Combine(owned, Guid.NewGuid().ToString("N") + ".TapPackage");
                repository.DownloadPackage(package, archive, timeout.Token);
                inventory.AddSnapshot(archive, 3);
                added = true;
            }
            return added;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        { throw new AuthoringWorkspaceException("The package repository did not complete within two minutes. Choose matching manual archives and retry.", error); }
        catch (Exception error) when (error is not OperationCanceledException && error is not AuthoringWorkspaceException)
        { throw new AuthoringWorkspaceException("Package repository lookup/download failed. The selected home and prior bundle were preserved; use matching manual archives. " + error.Message, error); }
    }
}
