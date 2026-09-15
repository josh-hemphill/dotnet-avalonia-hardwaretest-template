using HardwareTest.Core.Storage;
using HardwareTest.Shell;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class ShellApplicationLoaderTests
{
    [Fact]
    public void Enumerate_finds_exe_and_data_packages_not_plugins_or_app_data()
    {
        using var root = new LoaderTemp();
        var exePkg = LoaderTemp.CreatePackageDir(root.ExeShellApps, "vendor.exeapp");
        var dataPkg = LoaderTemp.CreatePackageDir(root.DataPackages, "vendor.dataapp");
        Directory.CreateDirectory(Path.Combine(root.DataDirectory, HardwareTest.Core.IO.PluginDirectoryTrust.FolderName, "opentap-extra"));
        Directory.CreateDirectory(Path.Combine(root.DataDirectory, ShellAppStorage.DirectoryName, "vendor.dataapp"));

        var dirs = ShellApplicationLoader.EnumeratePackageDirectories(root.AppDirectory, root.DataDirectory);
        Assert.Equal(2, dirs.Count);
        Assert.Contains(exePkg, dirs);
        Assert.Contains(dataPkg, dirs);
    }

    [Fact]
    public void Load_uses_manifest_type_factory_and_skips_newer_abi()
    {
        using var root = new LoaderTemp();
        WritePackage(root.ExeShellApps, "vendor.ok");
        WritePackage(root.DataPackages, "vendor.future");
        var errors = new List<string>();
        var types = new List<string>();

        var apps = ShellApplicationLoader.Load(
            root.AppDirectory,
            root.DataDirectory,
            onError: (_, ex) => errors.Add(ex.Message),
            create: (assembly, type) =>
            {
                types.Add(type);
                var id = Path.GetFileName(Path.GetDirectoryName(assembly)!);
                var abi = string.Equals(id, "vendor.future", StringComparison.Ordinal)
                    ? ShellHostAbi.Current + 1
                    : ShellHostAbi.Current;
                return new StubApp(id, abi);
            });

        Assert.Single(apps);
        Assert.Equal("vendor.ok", apps[0].Id);
        Assert.Contains("Vendor.App.vendor.ok", types);
        Assert.Contains("Vendor.App.vendor.future", types);
        Assert.Contains(errors, e => e.Contains("ABI", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_rejects_escaped_assembly_path()
    {
        using var root = new LoaderTemp();
        var dir = LoaderTemp.CreatePackageDir(root.ExeShellApps, "vendor.evil");
        File.WriteAllText(
            Path.Combine(dir, ShellApplicationLoader.ManifestFileName),
            """{"id":"vendor.evil","assembly":"../escape.dll","type":"Evil.App"}""");
        File.WriteAllBytes(Path.Combine(dir, "dummy.dll"), [0]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellApplicationLoader.Load(root.AppDirectory, root.DataDirectory, create: StubCreate));
        Assert.Contains("safe file name", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_throws_on_duplicate_package_ids()
    {
        using var root = new LoaderTemp();
        WritePackage(root.ExeShellApps, "vendor.dup");
        WritePackage(root.DataPackages, "vendor.dup");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellApplicationLoader.Load(
                root.AppDirectory,
                root.DataDirectory,
                onError: (_, _) => { },
                create: StubCreate));
        Assert.Contains("Duplicate", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_duplicate_ids_throw_before_second_create_even_when_onError_set()
    {
        using var root = new LoaderTemp();
        WritePackage(root.ExeShellApps, "vendor.dup");
        WritePackage(root.DataPackages, "vendor.dup");
        var creates = 0;

        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellApplicationLoader.Load(
                root.AppDirectory,
                root.DataDirectory,
                onError: (_, inner) => throw new InvalidOperationException(
                    $"onError swallowed create: {inner.Message}"),
                create: (assembly, type) =>
                {
                    creates++;
                    if (creates > 1)
                    {
                        throw new FileLoadException("same assembly identity");
                    }

                    return StubCreate(assembly, type);
                }));
        Assert.Contains("Duplicate", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, creates);
    }

    [Fact]
    public void Load_throws_when_created_app_id_does_not_match_manifest()
    {
        using var root = new LoaderTemp();
        WritePackage(root.ExeShellApps, "vendor.ok");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellApplicationLoader.Load(
                root.AppDirectory,
                root.DataDirectory,
                onError: (_, inner) => throw new InvalidOperationException(
                    $"onError swallowed id mismatch: {inner.Message}"),
                create: (_, _) => new StubApp("vendor.other", ShellHostAbi.Current)));
        Assert.Contains("does not match manifest", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddShellApplications_rejects_duplicate_ids()
    {
        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddShellApplications(
                new StubApp("vendor.shared", ShellHostAbi.Current),
                new StubApp("vendor.shared", ShellHostAbi.Current)));
        Assert.Contains("vendor.shared", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsSafeAssemblyFileName_rejects_traversal()
    {
        Assert.True(ShellApplicationLoader.IsSafeAssemblyFileName("Vendor.Planning.dll"));
        Assert.False(ShellApplicationLoader.IsSafeAssemblyFileName("../Vendor.dll"));
        Assert.False(ShellApplicationLoader.IsSafeAssemblyFileName("sub/Vendor.dll"));
        Assert.False(ShellApplicationLoader.IsSafeAssemblyFileName("Vendor.exe"));
    }

    private static IShellApplication StubCreate(string assemblyPath, string typeName)
    {
        _ = typeName;
        var id = Path.GetFileName(Path.GetDirectoryName(assemblyPath)!);
        return new StubApp(id, ShellHostAbi.Current);
    }

    private static void WritePackage(string root, string id)
    {
        var dir = Path.Combine(root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, ShellApplicationLoader.ManifestFileName),
            $$"""{"id":"{{id}}","assembly":"{{id}}.dll","type":"Vendor.App.{{id}}"}""");
        File.WriteAllBytes(Path.Combine(dir, id + ".dll"), [0]);
    }

    private sealed class LoaderTemp : IDisposable
    {
        public LoaderTemp()
        {
            Root = Path.Combine(Path.GetTempPath(), "hwtest-shell-load-" + Guid.NewGuid().ToString("N"));
            AppDirectory = Path.Combine(Root, "app");
            DataDirectory = Path.Combine(Root, "data");
            ExeShellApps = Path.Combine(AppDirectory, ShellApplicationLoader.ApplicationFolderName);
            DataPackages = Path.Combine(DataDirectory, ShellAppPackageTrust.FolderName);
            Directory.CreateDirectory(ExeShellApps);
            Directory.CreateDirectory(DataPackages);
        }

        public string Root { get; }
        public string AppDirectory { get; }
        public string DataDirectory { get; }
        public string ExeShellApps { get; }
        public string DataPackages { get; }

        public static string CreatePackageDir(string root, string id)
        {
            var dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            return Path.GetFullPath(dir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    private sealed class StubApp : IShellApplication
    {
        public StubApp(string id, int abi)
        {
            Id = id;
            MinHostAbi = abi;
        }

        public string Id { get; }
        public string Title => Id;
        public string Version => "0.0.0";
        public int MinHostAbi { get; }
        public void Configure(IServiceCollection services)
            => _ = services;

        public IReadOnlyList<ShellPageRegistration> Pages => [];
    }
}
