using HardwareTest.Core.Settings;
using HardwareTest.Examples.AdvancedProcedure;
using HardwareTest.OpenTap.Host;
using OpenTap;

if (args is ["generate", var programDirectory])
{
    _ = Directory.CreateDirectory(programDirectory);
    ProcedurePlan.Create().Save(Path.Combine(programDirectory, ProcedurePlan.PlanName));
    return 0;
}

if (args is ["inspect", var planPath])
{
    new OpenTapHostCatalog(new AppSettings { UseMockVisa = true }, Serilog.Log.Logger).EnsurePlugins();
    var plan = TestPlan.Load(Path.GetFullPath(planPath));
    plan.ChildTestSteps.OfType<FixtureProcedureStep>().Single().ValidateConfiguration().Describe(Console.Out);
    return 0;
}

if (args is ["authoring-workspace", var sourceDirectory, var workspace])
{
    ProcedureWorkspace.Create(sourceDirectory, workspace);
    return 0;
}

if (args is ["stage", var source, var app, var data])
{
    var programs = Path.Combine(app, "Programs");
    var tables = Path.Combine(programs, "tables", "advanced-procedure");
    var plugins = Path.Combine(data, "plugins", "AdvancedProcedure");
    _ = Directory.CreateDirectory(tables);
    _ = Directory.CreateDirectory(plugins);
    foreach (var file in new[] { ProcedurePlan.PlanName, "advanced-procedure.program.json" })
        File.Copy(Path.Combine(source, file), Path.Combine(programs, file), overwrite: true);
    foreach (var file in new[] { "station.json", "tests.json" })
        File.Copy(Path.Combine(source, "tables", "advanced-procedure", file), Path.Combine(tables, file), overwrite: true);
    foreach (var file in new[] { "HardwareTest.Examples.AdvancedProcedure.dll", "System.IO.Ports.dll" })
        File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(plugins, file), overwrite: true);
    foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*System.IO.Ports.Native*"))
        File.Copy(file, Path.Combine(plugins, Path.GetFileName(file)), overwrite: true);
    Console.WriteLine($"Staged {Path.GetFullPath(programs)} and {Path.GetFullPath(plugins)}");
    return 0;
}

if (args is ["simulate", var simulationApp, var simulationData, .. var responses])
    return await ProcedureVerification.SimulateAsync(simulationApp, simulationData, responses);

if (args is ["verify", var applicationDirectory, var dataDirectory])
{
    await ProcedureVerification.RunAsync(applicationDirectory, dataDirectory);
    return 0;
}

Console.Error.WriteLine("Usage: generate <program-directory> | inspect <TapPlan> | authoring-workspace <program-directory> <empty-workspace> | stage <program-directory> <app-directory> <data-directory> | simulate <app-directory> <data-directory> [response ...] | verify <app-directory> <data-directory>");
return 2;
