using System.Text.Json;

namespace HardwareTest.Authoring;

public static partial class AuthoringBuildService
{
    private static void ReadShellLock(string path, HashSet<string> packages)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
            foreach (var package in framework.Value.EnumerateObject())
            {
                if (package.Value.TryGetProperty("type", out var kind) && kind.GetString() == "Project") continue;
                if (!package.Value.TryGetProperty("resolved", out var version)) continue;
                packages.Add(ShellPackageId(package.Name, version.GetString()!));
            }
    }

    private static void ReadShellAssets(string path, HashSet<string> packages, Dictionary<string, string>? payloads = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
            if (library.Value.GetProperty("type").GetString() == "package")
            {
                var parts = library.Name.Split('/');
                if (parts.Length != 2) Unsupported(path, "invalid resolved package identity");
                Add(parts[0], parts[1]);
            }
        // SDK-inferred runtime, reference and app-host packs are restore downloads, not libraries.
        if (document.RootElement.TryGetProperty("project", out var project)
            && project.TryGetProperty("frameworks", out var frameworks))
            foreach (var framework in frameworks.EnumerateObject())
                if (framework.Value.TryGetProperty("downloadDependencies", out var downloads))
                    foreach (var download in downloads.EnumerateArray())
                    {
                        var range = download.GetProperty("version").GetString()!;
                        var version = range.Trim('[', ']', '(', ')').Split(',')[0].Trim();
                        Add(download.GetProperty("name").GetString()!, version);
                    }
        void Add(string name, string version)
        {
            var id = ShellPackageId(name, version);
            packages.Add(id);
            if (payloads is null) return;
            var packagePath = document.RootElement.GetProperty("packageFolders").EnumerateObject()
                .Select(folder => Path.Combine(Path.GetFullPath(folder.Name), id))
                .FirstOrDefault(Directory.Exists);
            if (packagePath is null) return;
            if (payloads.TryGetValue(id, out var previous) && previous != packagePath)
                Unsupported(path, "graph resolves one package identity from different payload directories");
            payloads[id] = packagePath;
        }
    }

    private static string ShellPackageId(string name, string version)
    {
        if (new[] { name, version }.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".."
            || p.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0))
            Unsupported(name + "/" + version, "invalid resolved package identity");
        return name.ToLowerInvariant() + "/" + version.ToLowerInvariant();
    }

    private static void ResolveShellPackages(BuildInputTree source, IReadOnlyList<BuildInputTree> ancestors,
        IEnumerable<string> projects, string packagesRoot, HashSet<string> packages, Dictionary<string, string> payloads, bool offline)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "authoring-shell-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var file in source.Files)
            {
                var destination = Path.Combine(temporary, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, file.Bytes);
            }
            CopyShellAncestors(temporary, ancestors);
            // This boundary affects only restore-time analyzers/config discovery in the disposable tree.
            if (!File.Exists(Path.Combine(temporary, ".editorconfig"))) File.WriteAllText(Path.Combine(temporary, ".editorconfig"), "root = true\n");
            var config = Path.Combine(temporary, "authoring-offline-nuget.config");
            if (offline) File.WriteAllText(config, "<configuration><packageSources><clear /></packageSources></configuration>");
            foreach (var project in projects)
            {
                var staged = Path.Combine(temporary, Path.GetRelativePath(source.Root, project));
                // Only restore targets execute here, against saved source bytes in a disposable tree.
                // Publication subsequently uses the exact package payloads captured below offline.
                var arguments = new List<string> { "restore", staged, "--nologo",
                    "-p:RestorePackagesPath=" + packagesRoot, "-p:RestorePackagesWithLockFile=true", "-p:Configuration=Release",
                    "-p:NuGetAudit=false", "-p:SourceRevisionId=local", "-p:SourceRevisionDate=1970-01-01T00:00:00Z" };
                if (offline) arguments.Add("-p:RestoreConfigFile=" + config);
                RunShellDotNet(Path.GetDirectoryName(staged)!, arguments);
                ReadShellAssets(Path.Combine(Path.GetDirectoryName(staged)!, "obj", "project.assets.json"), packages, payloads);
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
