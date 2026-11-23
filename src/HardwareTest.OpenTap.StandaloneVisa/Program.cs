using InstrumentComponents.OpenTap.Visa;
using OpenTap;
using OpenTap.Cli;

// The selected installed home contains this wrapper, as with tap.dll.
// Registration is synchronous and precedes action dispatch, including instrument Open.
var console = new ConsoleTraceListener(false, false, false);
Log.AddListener(console);
PluginManager.DirectoriesToSearch.Add(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
PluginManager.Search();
OpenTapVisa.Register(new OpenTapVisaDeviceProvider(connector =>
[
    .. InstrumentComponents.Drivers.Keysight.Drivers.Factories(connector),
    .. InstrumentComponents.Drivers.Keithley.Drivers.Factories(connector),
    .. InstrumentComponents.Drivers.Rigol.Drivers.Factories(connector),
    .. InstrumentComponents.Drivers.Siglent.Drivers.Factories(connector),
    .. InstrumentComponents.Drivers.Tektronix.Drivers.Factories(connector)
]));
HardwareTest.OpenTap.Plugins.Instruments.RegisteredDevices.Connector = timeout =>
    new VisaScpiConnector(new InstrumentComponents.Visa.VisaSessionOpener(), timeout);
return CliActionExecutor.Execute(args);
