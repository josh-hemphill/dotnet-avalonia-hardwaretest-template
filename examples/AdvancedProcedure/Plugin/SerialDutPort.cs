using System.Diagnostics;
using System.IO.Ports;
using OpenTap;

namespace HardwareTest.Examples.AdvancedProcedure;

[Display("Serial DUT Port", Groups: ["HardwareTest Examples"], Description: "Bounded, line-oriented ASCII request and reply.")]
public sealed class SerialDutPort : Instrument
{
    public const string MockAddress = "MOCK::SERIAL";

    private readonly object _sync = new();
    private SerialPort? _port;
    private bool _exchangeFailed;

    public string Address { get; set; } = MockAddress;
    public int BaudRate { get; set; } = 115200;
    public int TimeoutMs { get; set; } = 1000;
    public bool EnablePhysicalPort { get; set; }

    public override void Close()
    {
        lock (_sync)
        {
            try
            {
                DisposePort();
            }
            finally
            {
                _exchangeFailed = false;
                base.Close();
            }
        }
    }

    public string Query(string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (command.Length > 128 || command.Any(c => c is < ' ' or > '~'))
            throw new ArgumentException("Supply one printable ASCII command of at most 128 characters.", nameof(command));

        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsConnected)
                throw new InvalidOperationException("Open the serial DUT resource before querying it.");
            if (string.Equals(Address, MockAddress, StringComparison.OrdinalIgnoreCase))
                return MockReply(command);
            if (!EnablePhysicalPort)
                throw new InvalidOperationException("EnablePhysicalPort must be set before using a physical serial port.");
            if (_exchangeFailed)
                throw new InvalidOperationException("The serial exchange is out of sync. End the resource session before querying again.");
            if (TimeoutMs is < 1 or > 60000 || BaudRate <= 0)
                throw new InvalidOperationException("Set a positive baud rate and a timeout between 1 and 60000 ms.");

            try
            {
                // The DUT may be plugged in at an operator prompt after OpenTAP opens resources.
                _port ??= OpenPort();
                _port.WriteLine(command);
                return ReadReply(_port, cancellationToken);
            }
            catch
            {
                // Physical commands are never retried. A late reply must not feed another query in this run.
                _exchangeFailed = true;
                try
                {
                    DisposePort();
                }
                catch (Exception closeError)
                {
                    Log.Warning("Could not close {0} after a serial exchange failed: {1}", Address, closeError.Message);
                }
                throw;
            }
        }
    }

    private SerialPort OpenPort()
    {
        var port = new SerialPort(Address, BaudRate, Parity.None, 8, StopBits.One)
        {
            NewLine = "\n",
            WriteTimeout = TimeoutMs,
            Handshake = Handshake.None,
        };
        try
        {
            port.Open();
            port.DiscardInBuffer();
            return port;
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    private string ReadReply(SerialPort port, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var buffer = new char[512];
        var count = 0;
        var carriageReturn = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = TimeoutMs - (int)started.ElapsedMilliseconds;
            if (remaining <= 0)
                throw new TimeoutException($"Serial reply timed out on {Address}.");
            port.ReadTimeout = Math.Min(remaining, 50);

            int next;
            try
            {
                next = port.ReadByte();
            }
            catch (TimeoutException)
            {
                continue;
            }

            if (next == '\n')
                return new string(buffer, 0, count);
            if (carriageReturn)
                throw new IOException("Expected LF immediately after CR.");
            if (next == '\r')
            {
                carriageReturn = true;
                continue;
            }
            if (next is < 32 or > 126 || count == buffer.Length)
                throw new IOException("Serial reply exceeded 512 printable ASCII characters or used invalid framing.");
            buffer[count++] = (char)next;
        }
    }

    private void DisposePort()
    {
        var port = _port;
        _port = null;
        port?.Dispose();
    }

    private static string MockReply(string command) => command switch
    {
        "*IDN?" => "Example Instruments,Signal Board,SIM-001,1.0",
        "PING" => "PONG",
        "STATUS?" => "READY",
        "SENSOR:CURRENT?" => "0.496",
        "COUNTER?" => "1036",
        _ => throw new InvalidOperationException($"No simulated reply for '{command}'."),
    };
}
