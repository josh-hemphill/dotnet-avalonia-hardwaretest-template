using InstrumentComponents.OpenTap.Visa;
using OpenTap;
using OpenTap.Cli;

// The selected installed home contains this wrapper, as with tap.dll.
// Registration is synchronous and precedes action dispatch, including instrument Open.
var console = new ConsoleTraceListener(false, false, false);
Log.AddListener(console);
PluginManager.DirectoriesToSearch.Add(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!);
PluginManager.Search();
OpenTapVisa.Register();
return CliActionExecutor.Execute(args);
