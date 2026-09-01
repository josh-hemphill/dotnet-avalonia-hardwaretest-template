using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace HardwareTest.Core.Credentials;

/// WinSCard / pcsclite P/Invoke used by the PC/SC chip and tap broker.
internal static class PcscNative
{
    private static nint _library;
    private static int _resolverInstalled;
    public const int ScopeUser = 0;
    public const int ShareShared = 2;
    public const int ProtocolT0 = 1;
    public const int ProtocolT1 = 2;
    public const int LeaveCard = 0;
    public const int Success = 0;
    public const int NoReaders = unchecked((int)0x8010002E);
    public const int Timeout = unchecked((int)0x8010000A);
    public const int RemovedCard = unchecked((int)0x80100069);
    public const int ResetCard = unchecked((int)0x80100068);

    public static bool TryLoad(out string? error)
    {
        error = null;
        try
        {
            if (_library != 0)
            {
                return true;
            }

            nint handle;
            if (OperatingSystem.IsWindows())
            {
                if (!NativeLibrary.TryLoad("winscard", out handle))
                {
                    error = "winscard not found.";
                    return false;
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (!NativeLibrary.TryLoad("/System/Library/Frameworks/PCSC.framework/PCSC", out handle)
                    && !NativeLibrary.TryLoad("PCSC", out handle))
                {
                    error = "PCSC framework not found.";
                    return false;
                }
            }
            else if (!NativeLibrary.TryLoad("libpcsclite.so.1", out handle)
                     && !NativeLibrary.TryLoad("pcsclite", out handle))
            {
                error = "libpcsclite.so.1 not found.";
                return false;
            }

            _library = handle;
            if (Interlocked.Exchange(ref _resolverInstalled, 1) == 0)
            {
                NativeLibrary.SetDllImportResolver(typeof(PcscNative).Assembly, Resolve);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        _ = assembly;
        _ = searchPath;
        if (libraryName is "winscard" or "libpcsclite.so.1" or "PCSC")
        {
            return _library;
        }

        return 0;
    }

    [DllImport("winscard", EntryPoint = "SCardEstablishContext")]
    private static extern int EstablishContextWin(int scope, nint r1, nint r2, out nint context);

    [DllImport("libpcsclite.so.1", EntryPoint = "SCardEstablishContext")]
    private static extern int EstablishContextUnix(int scope, nint r1, nint r2, out nint context);

    [DllImport("winscard", EntryPoint = "SCardReleaseContext")]
    private static extern int ReleaseContextWin(nint context);

    [DllImport("libpcsclite.so.1", EntryPoint = "SCardReleaseContext")]
    private static extern int ReleaseContextUnix(nint context);

    [DllImport("winscard", EntryPoint = "SCardListReadersW", CharSet = CharSet.Unicode)]
    private static extern int ListReadersWin(nint context, nint groups, char[]? readers, ref int length);

    [DllImport("libpcsclite.so.1", EntryPoint = "SCardListReaders")]
    private static extern int ListReadersUnix(nint context, nint groups, byte[]? readers, ref int length);

    [DllImport("winscard", EntryPoint = "SCardConnectW", CharSet = CharSet.Unicode)]
    private static extern int ConnectWin(
        nint context,
        string reader,
        int shareMode,
        int preferredProtocols,
        out nint card,
        out int protocol);

    [DllImport("libpcsclite.so.1", EntryPoint = "SCardConnect")]
    private static extern int ConnectUnix(
        nint context,
        string reader,
        int shareMode,
        int preferredProtocols,
        out nint card,
        out int protocol);

    [DllImport("winscard", EntryPoint = "SCardDisconnect")]
    private static extern int DisconnectWin(nint card, int disposition);

    [DllImport("libpcsclite.so.1", EntryPoint = "SCardDisconnect")]
    private static extern int DisconnectUnix(nint card, int disposition);

    [DllImport("winscard", EntryPoint = "SCardTransmit")]
    private static extern int TransmitWin(
        nint card,
        ref ScardIoRequestDword sendPci,
        byte[] send,
        int sendLen,
        nint recvPci,
        byte[] recv,
        ref int recvLen);

    [DllImport("libpcsclite.so.1", EntryPoint = "SCardTransmit")]
    private static extern int TransmitUnixDword(
        nint card,
        ref ScardIoRequestDword sendPci,
        byte[] send,
        int sendLen,
        nint recvPci,
        byte[] recv,
        ref int recvLen);

    [DllImport("libpcsclite.so.1", EntryPoint = "SCardTransmit")]
    private static extern int TransmitUnixULong(
        nint card,
        ref ScardIoRequestULong sendPci,
        byte[] send,
        int sendLen,
        nint recvPci,
        byte[] recv,
        ref int recvLen);

    [DllImport("winscard", EntryPoint = "SCardStatusW", CharSet = CharSet.Unicode)]
    private static extern int StatusWin(
        nint card,
        char[]? reader,
        ref int readerLen,
        out int state,
        out int protocol,
        byte[]? atr,
        ref int atrLen);

    [DllImport("libpcsclite.so.1", EntryPoint = "SCardStatus")]
    private static extern int StatusUnix(
        nint card,
        byte[]? reader,
        ref int readerLen,
        out int state,
        out int protocol,
        byte[]? atr,
        ref int atrLen);

    public static int EstablishContext(out nint context)
        => OperatingSystem.IsWindows()
            ? EstablishContextWin(ScopeUser, 0, 0, out context)
            : EstablishContextUnix(ScopeUser, 0, 0, out context);

    public static int ReleaseContext(nint context)
        => OperatingSystem.IsWindows() ? ReleaseContextWin(context) : ReleaseContextUnix(context);

    public static int Connect(nint context, string reader, out nint card, out int protocol)
        => OperatingSystem.IsWindows()
            ? ConnectWin(context, reader, ShareShared, ProtocolT0 | ProtocolT1, out card, out protocol)
            : ConnectUnix(context, reader, ShareShared, ProtocolT0 | ProtocolT1, out card, out protocol);

    public static int Disconnect(nint card)
        => OperatingSystem.IsWindows() ? DisconnectWin(card, LeaveCard) : DisconnectUnix(card, LeaveCard);

    public static IReadOnlyList<string> ListReaders(nint context)
    {
        if (OperatingSystem.IsWindows())
        {
            var length = 0;
            var rc = ListReadersWin(context, 0, null, ref length);
            if (rc != Success || length <= 2)
            {
                return [];
            }

            var buffer = new char[length];
            rc = ListReadersWin(context, 0, buffer, ref length);
            return rc == Success ? SplitMultiString(new string(buffer, 0, Math.Max(0, length - 1))) : [];
        }

        var unixLen = 0;
        var unixRc = ListReadersUnix(context, 0, null, ref unixLen);
        if (unixRc != Success || unixLen <= 2)
        {
            return [];
        }

        var bytes = new byte[unixLen];
        unixRc = ListReadersUnix(context, 0, bytes, ref unixLen);
        return unixRc == Success ? SplitMultiString(Encoding.UTF8.GetString(bytes, 0, Math.Max(0, unixLen - 1))) : [];
    }

    public static byte[]? ReadAtr(nint card)
    {
        var atrLen = 32;
        var atr = new byte[atrLen];
        int rc;
        if (OperatingSystem.IsWindows())
        {
            var readerLen = 0;
            rc = StatusWin(card, null, ref readerLen, out _, out _, atr, ref atrLen);
        }
        else
        {
            var readerLen = 0;
            rc = StatusUnix(card, null, ref readerLen, out _, out _, atr, ref atrLen);
        }

        if (rc != Success || atrLen <= 0)
        {
            return null;
        }

        return atr[..atrLen];
    }

    private const int TransmitBufferSize = 4096;
    private const int MaxGetResponseRounds = 16;

    public static byte[]? Transmit(nint card, int protocol, byte[] send)
    {
        var first = TransmitOnce(card, protocol, send);
        if (first is not { Length: >= 2 })
        {
            return null;
        }

        var payload = new List<byte>(first.Length);
        payload.AddRange(first.AsSpan(0, first.Length - 2).ToArray());
        var sw1 = first[^2];
        var sw2 = first[^1];
        for (var round = 0; round < MaxGetResponseRounds && sw1 == 0x61; round++)
        {
            var le = sw2 == 0 ? (byte)0x00 : sw2;
            var more = TransmitOnce(card, protocol, [0x00, 0xC0, 0x00, 0x00, le]);
            if (more is not { Length: >= 2 })
            {
                return null;
            }

            payload.AddRange(more.AsSpan(0, more.Length - 2).ToArray());
            sw1 = more[^2];
            sw2 = more[^1];
        }

        payload.Add(sw1);
        payload.Add(sw2);
        return payload.ToArray();
    }

    private static byte[]? TransmitOnce(nint card, int protocol, byte[] send)
    {
        var proto = protocol == 0 ? ProtocolT1 : protocol;
        var recv = new byte[TransmitBufferSize];
        var recvLen = recv.Length;
        int rc;
        if (OperatingSystem.IsWindows())
        {
            var pci = new ScardIoRequestDword
            {
                Protocol = proto,
                Length = (uint)Marshal.SizeOf<ScardIoRequestDword>(),
            };
            rc = TransmitWin(card, ref pci, send, send.Length, 0, recv, ref recvLen);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // PCSC.framework uses uint32_t members (same as Winscard DWORD).
            var pci = new ScardIoRequestDword
            {
                Protocol = proto,
                Length = (uint)Marshal.SizeOf<ScardIoRequestDword>(),
            };
            rc = TransmitUnixDword(card, ref pci, send, send.Length, 0, recv, ref recvLen);
        }
        else
        {
            // pcsclite SCARD_IO_REQUEST uses unsigned long (pointer-sized on LP64).
            var pci = new ScardIoRequestULong
            {
                Protocol = (nuint)proto,
                Length = (nuint)Marshal.SizeOf<ScardIoRequestULong>(),
            };
            rc = TransmitUnixULong(card, ref pci, send, send.Length, 0, recv, ref recvLen);
        }

        if (rc != Success || recvLen < 2)
        {
            return null;
        }

        return recv[..recvLen];
    }

    private static IReadOnlyList<string> SplitMultiString(string value)
    {
        var parts = value.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
    }

    /// Winscard / macOS PCSC: DWORD / uint32_t protocol and pci length.
    [StructLayout(LayoutKind.Sequential)]
    internal struct ScardIoRequestDword
    {
        public int Protocol;
        public uint Length;
    }

    /// Linux pcsclite: unsigned long protocol and pci length.
    [StructLayout(LayoutKind.Sequential)]
    internal struct ScardIoRequestULong
    {
        public nuint Protocol;
        public nuint Length;
    }
}
