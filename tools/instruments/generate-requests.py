"""Generate explicit request constructors from the pinned public SDK source, never runtime reflection.
Usage: python3 tools/instruments/generate-requests.py /path/to/instrument-components-v0.3.0-alpha.1
"""
import hashlib
import re
import sys
from pathlib import Path
source = Path(sys.argv[1]) / 'dotnet/src/InstrumentComponents.Abstractions/CatalogGenerated.cs'
text = source.read_text()
requests = re.findall(r'public sealed record (\w+)Request\(([^)]*)\)', text)
parsers = {'double?': 'Optional', 'double': 'Number', 'uint': 'Unsigned', 'bool': 'Boolean', 'string': 'Text', 'Waveform': 'EnumValue<Waveform>', 'PowerUnit': 'EnumValue<PowerUnit>', 'SenseMode': 'EnumValue<SenseMode>'}
lines = ['// Generated from Instrument Components 0.3.0-alpha.1 CatalogGenerated.cs.', '// Source SHA256: ' + hashlib.sha256(source.read_bytes()).hexdigest(), 'using System.Globalization;', 'using InstrumentComponents.Drivers.Catalog;', '', 'namespace HardwareTest.OpenTap.Plugins.Instruments;', '', 'public static partial class DeviceOperationRequests', '{', '    private static CatalogRequest Construct(string operation, Dictionary<string, string> values) => operation switch', '    {']
for name, arguments in requests:
    params = [p.strip().split(' ') for p in arguments.split(',') if p.strip()]
    block = text.split(f'public sealed record {name}Request(', 1)[1].split('public sealed record ', 1)[0]
    keys = re.findall(r'\["([^"]+)"\] = Argument\.', block)
    args = ', '.join(f'{parsers[t]}(values, "{key}")' for (t, n), key in zip(params, keys, strict=True))
    lines.append(f'        "{name}" => new {name}Request({args}),')
lines += ['        _ => throw new ArgumentException($"Unknown operation: {operation}.")', '    };', '}']
Path('src/HardwareTest.OpenTap.Plugins.Instruments/DeviceOperationRequests.g.cs').write_text('\n'.join(lines) + '\n')
