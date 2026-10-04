using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class OpenTapHomeBootstrapperTests
{
    [Fact]
    public void Bootstrap_declared_visa_installs_adapter_and_loads_from_selected_home()
    {
        var workspaceRoot = NewTempDir();
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "plans"));
        File.WriteAllText(Path.Combine(workspaceRoot, "authoring.json"),
            """
            {
              "schemaVersion": 1,
              "displayName": "VISA authoring",
              "plansDirectory": "plans",
              "package": { "name": "VISA authoring", "version": "0.1.0" },
              "dependencies": [
                { "package": "OpenTAP", "version": "^9.32.2" },
                { "package": "HardwareTest Basic", "version": "^0.2.0" },
                { "package": "HardwareTest VISA", "version": "^0.1.0" }
              ]
            }
            """);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            AuthoringWorkspaceLoader.Load(workspaceRoot),
            new BootstrapOptions { HomeDirectory = NewTempDir(), Offline = true });

        Assert.Contains(OpenTapHomeBootstrapper.ListInstalledPackages(home),
            p => p.Name == OpenTapHomeBootstrapper.VisaPackageName);
        var visaDirectory = Path.Combine(home.Root, "Packages", OpenTapHomeBootstrapper.VisaPackageName);
        Assert.True(File.Exists(Path.Combine(visaDirectory, "HardwareTest.Core.dll")));
        Assert.True(File.Exists(Path.Combine(visaDirectory, "Ivi.Visa.dll")));
        var adapterPath = Path.Combine(visaDirectory, OpenTapHomeBootstrapper.VisaAssemblyFileName);
        Assert.True(File.Exists(adapterPath));

        // Resolve plugin dependencies exclusively from this home, without the application's assemblies.
        var context = new HomeAssemblyLoadContext(home.Root);
        try
        {
            var assembly = context.LoadFromAssemblyPath(adapterPath);
            var type = assembly.GetType("HardwareTest.OpenTap.Plugins.Basic.VisaDmmInstrument", throwOnError: true)!;
            var instrument = Activator.CreateInstance(type);
            Assert.NotNull(instrument);
            Assert.Equal("", type.GetProperty("VisaAddress")!.GetValue(instrument));
            Assert.Equal(adapterPath, type.Assembly.Location);
        }
        finally
        {
            context.Unload();
        }
    }

    private sealed class HomeAssemblyLoadContext(string homeRoot)
        : AssemblyLoadContext("visa-home-" + Guid.NewGuid().ToString("N"), isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            var path = Directory.EnumerateFiles(homeRoot, name.Name + ".dll", SearchOption.AllDirectories)
                .SingleOrDefault();
            if (path is not null)
            {
                return LoadFromAssemblyPath(path);
            }

            if (name.Name?.StartsWith("HardwareTest.", StringComparison.Ordinal) == true)
            {
                throw new FileNotFoundException($"Selected home lacks '{name.Name}'.");
            }

            return null;
        }
    }

    [Fact]
    public void Bootstrap_template_installs_basic_and_mixins_without_visa()
    {
        var workspace = AuthoringWorkspaceLoader.Load(Path.Combine(FindRepoRoot(), "plans", "opentap"));
        var homeDir = NewTempDir();
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions { HomeDirectory = homeDir, Offline = true });

        Assert.Equal(Path.GetFullPath(homeDir), home.Root);
        var names = OpenTapHomeBootstrapper.ListInstalledPackages(home)
            .Select(p => p.Name)
            .ToArray();
        Assert.Contains("OpenTAP", names);
        Assert.Contains("HardwareTest Basic", names);
        Assert.Contains("HardwareTest Mixins", names);
        Assert.DoesNotContain(
            names,
            n => n.Contains("Visa", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(home.Root, "Packages", "HardwareTest Basic", "HardwareTest.OpenTap.Plugins.Basic.dll")));
        Assert.True(File.Exists(Path.Combine(home.Root, "Packages", "HardwareTest Mixins", "HardwareTest.OpenTap.Plugins.Mixins.dll")));
        Assert.Empty(Directory.EnumerateFiles(home.Root, OpenTapHomeBootstrapper.VisaAssemblyFileName, SearchOption.AllDirectories));
        AssertNoVisaAdapterAssemblies(home.Root);
        Assert.True(File.Exists(Path.Combine(home.Root, "OpenTap.dll")));
    }

    [Fact]
    public void Bootstrap_fails_when_instrument_components_is_declared_without_a_path()
    {
        var previous = Environment.GetEnvironmentVariable("HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE");
        Environment.SetEnvironmentVariable("HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE", null);
        try
        {
            var dir = NewTempDir();
            Directory.CreateDirectory(Path.Combine(dir, "plans"));
            File.WriteAllText(
                Path.Combine(dir, "authoring.json"),
                """
                {
                  "schemaVersion": 1,
                  "displayName": "Needs IC",
                  "plansDirectory": "plans",
                  "package": { "name": "Needs IC", "version": "0.1.0" },
                  "dependencies": [
                    { "package": "OpenTAP", "version": "^9.32.2" },
                    { "package": "HardwareTest Basic", "version": "^0.1.0" },
                    { "package": "HardwareTest Mixins", "version": "^0.1.0" },
                    { "package": "InstrumentComponents.OpenTap", "version": "^0.1.0" }
                  ],
                  "includeTui": false
                }
                """);

            var workspace = AuthoringWorkspaceLoader.Load(dir);
            var ex = Assert.Throws<AuthoringWorkspaceException>(() =>
                new OpenTapHomeBootstrapper().Bootstrap(
                    workspace,
                    new BootstrapOptions { HomeDirectory = NewTempDir(), Offline = true }));
            Assert.Contains(AuthoringBootstrapCodes.InstrumentComponentsPackageMissing, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HARDWARETEST_INSTRUMENT_COMPONENTS_PACKAGE", previous);
        }
    }

    [Fact]
    public void Bootstrap_resolves_instrument_components_path_relative_to_workspace()
    {
        var workspaceRoot = NewTempDir();
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "plans"));
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "packs", "ic"));
        File.WriteAllText(
            Path.Combine(workspaceRoot, "packs", "ic", "package.xml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Package Name="InstrumentComponents.OpenTap" xmlns="http://opentap.io/schemas/package" Version="0.1.0" />
            """);
        File.WriteAllText(
            Path.Combine(workspaceRoot, "authoring.json"),
            """
            {
              "schemaVersion": 1,
              "displayName": "With IC",
              "plansDirectory": "plans",
              "package": { "name": "With IC", "version": "0.1.0" },
              "instrumentComponentsPackage": "packs/ic",
              "dependencies": [
                { "package": "OpenTAP", "version": "^9.32.2" },
                { "package": "InstrumentComponents.OpenTap", "version": "^0.1.0" }
              ]
            }
            """);

        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions { HomeDirectory = NewTempDir(), Offline = true });

        Assert.Contains(
            "InstrumentComponents.OpenTap",
            OpenTapHomeBootstrapper.ListInstalledPackages(home).Select(p => p.Name));
    }

    [Fact]
    public void Bootstrap_installs_instrument_components_from_unpacked_folder()
    {
        var workspaceRoot = NewTempDir();
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "plans"));
        File.WriteAllText(
            Path.Combine(workspaceRoot, "authoring.json"),
            """
            {
              "schemaVersion": 1,
              "displayName": "With IC",
              "plansDirectory": "plans",
              "package": { "name": "With IC", "version": "0.1.0" },
              "dependencies": [
                { "package": "OpenTAP", "version": "^9.32.2" },
                { "package": "InstrumentComponents.OpenTap", "version": "^0.1.0" }
              ]
            }
            """);
        var icDir = Path.Combine(NewTempDir(), "ic");
        Directory.CreateDirectory(icDir);
        File.WriteAllText(
            Path.Combine(icDir, "package.xml"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Package Name="InstrumentComponents.OpenTap" xmlns="http://opentap.io/schemas/package" Version="0.1.0" />
            """);

        var workspace = AuthoringWorkspaceLoader.Load(workspaceRoot);
        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions
            {
                HomeDirectory = NewTempDir(),
                InstrumentComponentsPackagePath = icDir,
                Offline = true,
            });

        Assert.Contains(
            "InstrumentComponents.OpenTap",
            OpenTapHomeBootstrapper.ListInstalledPackages(home).Select(p => p.Name));
    }

    [Fact]
    public void Bootstrap_installs_optional_tui_package_file()
    {
        var workspace = AuthoringWorkspaceLoader.Load(Path.Combine(FindRepoRoot(), "plans", "opentap"));
        var tapPackage = Path.Combine(NewTempDir(), "TUI.TapPackage");
        using (var zip = ZipFile.Open(tapPackage, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("package.xml");
            using var stream = entry.Open();
            var xml = """
                <?xml version="1.0" encoding="UTF-8"?>
                <Package Name="TUI" xmlns="http://opentap.io/schemas/package" Version="1.0.0" />
                """;
            stream.Write(Encoding.UTF8.GetBytes(xml));
        }

        var home = new OpenTapHomeBootstrapper().Bootstrap(
            workspace,
            new BootstrapOptions
            {
                HomeDirectory = NewTempDir(),
                TuiPackagePath = tapPackage,
                Offline = true,
            });

        Assert.Contains("TUI", OpenTapHomeBootstrapper.ListInstalledPackages(home).Select(p => p.Name));
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ht-otaphome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("dirs.proj").Any())
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate dirs.proj above '{AppContext.BaseDirectory}'.");
    }

    private static void AssertNoVisaAdapterAssemblies(string homeRoot)
    {
        foreach (var dll in Directory.EnumerateFiles(homeRoot, "*.dll", SearchOption.AllDirectories))
        {
            AssemblyName name;
            try
            {
                name = AssemblyName.GetAssemblyName(dll);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            Assert.False(
                string.Equals(name.Name, "HardwareTest.OpenTap.Plugins.Visa", StringComparison.OrdinalIgnoreCase),
                $"Authoring home contains VISA adapter assembly at '{dll}'.");
        }
    }
}
