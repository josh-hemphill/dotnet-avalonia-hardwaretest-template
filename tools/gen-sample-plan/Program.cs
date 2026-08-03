using HardwareTest.OpenTap.Host;

var dir = args.Length > 0 ? args[0] : Path.Combine("plans", "opentap");
SampleProgramFactory.SaveBeside(dir);
BoardDemoProgramFactory.SaveBeside(dir);
PlanShapeFixtures.SaveAllBeside(Path.Combine(dir, "fixtures"));
Console.WriteLine($"Wrote {Path.Combine(dir, SampleProgramFactory.EmbeddedName)}");
Console.WriteLine($"Wrote {Path.Combine(dir, BoardDemoProgramFactory.EmbeddedName)}");
Console.WriteLine($"Wrote plan-shape fixtures under {Path.Combine(dir, "fixtures")}");
