using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace HardwareTest.Core.Credentials;

/// Supplies a live dialog owner. Zero deliberately disables interactive signing in headless hosts.
public interface INativeSigningDialogOwner
{
    Task<nint> GetWindowHandleAsync(CancellationToken cancellationToken = default);
}

public sealed class HeadlessSigningDialogOwner : INativeSigningDialogOwner
{
    public Task<nint> GetWindowHandleAsync(CancellationToken cancellationToken = default) => Task.FromResult<nint>(0);
}

internal interface IWindowsSigningCertificateSource
{
    // Certificates retain their store context and its provider properties; never reload DER for acquisition.
    IReadOnlyList<X509Certificate2> ReadCertificates();
}

internal sealed class WindowsSigningCertificateSource : IWindowsSigningCertificateSource
{
    public IReadOnlyList<X509Certificate2> ReadCertificates()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        return store.Certificates.Cast<X509Certificate2>().ToArray();
    }
}

internal readonly record struct WindowsSigningKey(nint Handle, uint KeySpec, bool CallerFree)
{
    public bool IsCng => KeySpec == uint.MaxValue;
}

internal interface IWindowsSigningNative
{
    void InitializeApartment();
    void UninitializeApartment();
    WindowsSigningKey Acquire(nint certificateContext, uint flags, nint ownerAddress);
    bool IsHardware(WindowsSigningKey key);
    string? ReaderName(WindowsSigningKey key);
    byte[] SignHash(WindowsSigningKey key, byte[] hash, bool rsa, int signatureSize);
    void Release(WindowsSigningKey key);
}

internal sealed class WindowsSigningNativeException(uint code, string stage) : Exception("Windows smart-card operation failed.")
{
    public uint Code { get; } = code;
    public string Stage { get; } = stage;
}

internal sealed class WindowsSigningNative : IWindowsSigningNative
{
    private readonly IWindowsSigningInterop _interop;
    internal WindowsSigningNative(IWindowsSigningInterop? interop = null) => _interop = interop ?? new WindowsSigningInterop();
    public void InitializeApartment()
    {
        var status = OleInitialize(0);
        if (status < 0) throw new WindowsSigningNativeException(unchecked((uint)status), "apartment");
    }
    public void UninitializeApartment() => OleUninitialize();
    public WindowsSigningKey Acquire(nint certificateContext, uint flags, nint ownerAddress)
    {
        if (!_interop.Acquire(certificateContext, flags, ownerAddress, out var handle, out var spec, out var free))
            throw new WindowsSigningNativeException(_interop.LastError, "authentication");
        if (handle == 0) throw new WindowsSigningNativeException(0, "authentication");
        return new(handle, spec, free);
    }
    public bool IsHardware(WindowsSigningKey key)
    {
        if (key.IsCng)
        {
            var providerBytes = GetCngProperty(key.Handle, "Provider Handle", nint.Size, false)!;
            if (providerBytes.Length != nint.Size) throw new WindowsSigningNativeException(0, "provider-properties");
            var provider = nint.Size == 8 ? (nint)BitConverter.ToInt64(providerBytes) : (nint)BitConverter.ToInt32(providerBytes);
            if (provider == 0) throw new WindowsSigningNativeException(0, "provider-properties");
            // NCRYPT_PROVIDER_HANDLE_PROPERTY returns a provider reference owned by this query.
            try { return (ReadImplementation(GetCngProperty(provider, "Impl Type", sizeof(uint), false)!) & 1) != 0; }
            finally { WindowsOperatorCredentialBroker.DisposeResource(() => _interop.FreeCng(provider)); }
        }
        return (ReadImplementation(GetCspProperty(key.Handle, 3, sizeof(uint), false)!) & 1) != 0;
    }
    private static uint ReadImplementation(byte[] bytes)
    {
        if (bytes.Length != sizeof(uint)) throw new WindowsSigningNativeException(0, "provider-properties");
        return BitConverter.ToUInt32(bytes);
    }
    public string? ReaderName(WindowsSigningKey key)
    {
        var bytes = key.IsCng ? GetCngProperty(key.Handle, "SmartCardReader", 4096, true) : GetCspProperty(key.Handle, 43, 4096, true);
        if (bytes is null) return null;
        if (key.IsCng) return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        var pointer = Marshal.AllocHGlobal(bytes.Length + 1);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length); Marshal.WriteByte(pointer, bytes.Length, 0);
            return Marshal.PtrToStringAnsi(pointer)?.TrimEnd('\0');
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    private byte[]? GetCngProperty(nint handle, string property, int capacity, bool optional)
    {
        var bytes = new byte[capacity];
        var status = _interop.GetCngProperty(handle, property, bytes, out var written);
        if (optional && (uint)status is 0x80090029 or 0x80090027 or 0x80090010 or 0x80090011) return null;
        Check(status, "provider-properties");
        if (written < 0 || written > bytes.Length) throw new WindowsSigningNativeException(0, "provider-properties");
        return bytes[..written];
    }
    private byte[]? GetCspProperty(nint handle, uint property, int capacity, bool optional)
    {
        var bytes = new byte[capacity]; uint written = (uint)bytes.Length;
        if (!_interop.GetCspProperty(handle, property, bytes, ref written))
        {
            var code = _interop.LastError;
            if (optional && code is 0x80090029 or 0x8009000a or 87) return null;
            throw new WindowsSigningNativeException(code, "provider-properties");
        }
        if (written > bytes.Length) throw new WindowsSigningNativeException(0, "provider-properties");
        return bytes[..(int)written];
    }
    public byte[] SignHash(WindowsSigningKey key, byte[] hash, bool rsa, int signatureSize)
    {
        var signature = new byte[signatureSize];
        if (key.IsCng)
        {
            var padding = rsa ? Marshal.StringToHGlobalUni("SHA256") : 0;
            var info = rsa ? Marshal.AllocHGlobal(nint.Size) : 0;
            try
            {
                if (rsa) Marshal.WriteIntPtr(info, padding);
                Check(_interop.SignCng(key.Handle, info, hash, signature, out var written, rsa ? 2u : 0u), "signing");
                if (written != signatureSize) throw new WindowsSigningNativeException(0, "signature-format");
                return signature;
            }
            finally { if (info != 0) Marshal.FreeHGlobal(info); if (padding != 0) Marshal.FreeHGlobal(padding); }
        }
        if (!rsa || key.KeySpec is not (1 or 2)) throw new WindowsSigningNativeException(0, "key-specification");
        if (!_interop.CreateHash(key.Handle, 0x800c, out var nativeHash)) throw new WindowsSigningNativeException(_interop.LastError, "hash-create");
        try
        {
            if (!_interop.SetHash(nativeHash, 2, hash)) throw new WindowsSigningNativeException(_interop.LastError, "hash-value");
            uint written = (uint)signature.Length;
            if (!_interop.SignCsp(nativeHash, key.KeySpec, signature, ref written)) throw new WindowsSigningNativeException(_interop.LastError, "signing");
            if (written != signatureSize) throw new WindowsSigningNativeException(0, "signature-format");
            Array.Reverse(signature); // CryptoAPI RSA is little endian; CMS uses network byte order.
            return signature;
        }
        finally { WindowsOperatorCredentialBroker.DisposeResource(() => _interop.DestroyHash(nativeHash)); }
    }
    public void Release(WindowsSigningKey key)
    {
        if (!key.CallerFree) return;
        if (key.IsCng) _interop.FreeCng(key.Handle); else _interop.ReleaseCsp(key.Handle);
    }
    private static void Check(int status, string stage) { if (status != 0) throw new WindowsSigningNativeException(unchecked((uint)status), stage); }

    private sealed class WindowsSigningInterop : IWindowsSigningInterop
    {
        public uint LastError => unchecked((uint)Marshal.GetLastPInvokeError());
        public bool Acquire(nint context, uint flags, nint ownerAddress, out nint key, out uint keySpec, out bool callerFree)
            => CryptAcquireCertificatePrivateKey(context, flags, ownerAddress, out key, out keySpec, out callerFree);
        public int GetCngProperty(nint handle, string property, byte[] output, out int written) => NCryptGetProperty(handle, property, output, output.Length, out written, 0);
        public bool GetCspProperty(nint handle, uint property, byte[] output, ref uint written) => CryptGetProvParam(handle, property, output, ref written, 0);
        public int SignCng(nint key, nint padding, byte[] hash, byte[] signature, out int written, uint flags) => NCryptSignHash(key, padding, hash, hash.Length, signature, signature.Length, out written, flags);
        public bool CreateHash(nint provider, uint algorithm, out nint hash) => CryptCreateHash(provider, algorithm, 0, 0, out hash);
        public bool SetHash(nint hash, uint property, byte[] value) => CryptSetHashParam(hash, property, value, 0);
        public bool SignCsp(nint hash, uint keySpec, byte[] signature, ref uint written) => CryptSignHashW(hash, keySpec, null, 0, signature, ref written);
        public void DestroyHash(nint hash) => CryptDestroyHash(hash);
        public void FreeCng(nint handle) => NCryptFreeObject(handle);
        public void ReleaseCsp(nint handle) => CryptReleaseContext(handle, 0);
    }

    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptAcquireCertificatePrivateKey(nint certificate, uint flags, nint parameters, out nint key, out uint keySpec, [MarshalAs(UnmanagedType.Bool)] out bool callerFree);
    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)] private static extern int NCryptGetProperty(nint handle, string property, byte[] output, int size, out int written, uint flags);
    [DllImport("ncrypt.dll")] private static extern int NCryptSignHash(nint key, nint padding, byte[] hash, int hashSize, byte[] signature, int signatureSize, out int written, uint flags);
    [DllImport("ncrypt.dll")] private static extern int NCryptFreeObject(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptGetProvParam(nint provider, uint parameter, byte[] output, ref uint size, uint flags);
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCreateHash(nint provider, uint algorithm, nint key, uint flags, out nint hash);
    [DllImport("advapi32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptSetHashParam(nint hash, uint parameter, byte[] data, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptSignHashW(nint hash, uint keySpec, string? description, uint flags, byte[] signature, ref uint size);
    [DllImport("advapi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptDestroyHash(nint hash);
    [DllImport("advapi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptReleaseContext(nint provider, uint flags);
}

/// Managed ABI seam; production forwards these calls directly to the corresponding Win32 APIs.
internal interface IWindowsSigningInterop
{
    uint LastError { get; }
    bool Acquire(nint context, uint flags, nint ownerAddress, out nint key, out uint keySpec, out bool callerFree);
    int GetCngProperty(nint handle, string property, byte[] output, out int written);
    bool GetCspProperty(nint handle, uint property, byte[] output, ref uint written);
    int SignCng(nint key, nint padding, byte[] hash, byte[] signature, out int written, uint flags);
    bool CreateHash(nint provider, uint algorithm, out nint hash);
    bool SetHash(nint hash, uint property, byte[] value);
    bool SignCsp(nint hash, uint keySpec, byte[] signature, ref uint written);
    void DestroyHash(nint hash);
    void FreeCng(nint handle);
    void ReleaseCsp(nint handle);
}
