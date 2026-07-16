using HardwareTest.OpenTap.Host;
var dir = args.Length > 0 ? args[0] : Path.Combine("plans", "opentap");
SampleProgramFactory.SaveBeside(dir);
Console.WriteLine($"Wrote {Path.Combine(dir, SampleProgramFactory.EmbeddedName)}");
