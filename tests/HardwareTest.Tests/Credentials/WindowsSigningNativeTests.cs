using System.Runtime.InteropServices;
using System.Text;
using HardwareTest.Core.Credentials;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class WindowsSigningNativeTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void CallerFree_controls_key_release_independently_of_provider_reference(bool cng, bool callerFree)
    {
        var calls = new Calls(); var native = new WindowsSigningNative(calls); var key = new WindowsSigningKey(10, cng ? uint.MaxValue : 2, callerFree);
        Assert.True(native.IsHardware(key)); native.Release(key);
        Assert.Equal(cng ? new nint[] { 20 }.Concat(callerFree ? new nint[] { 10 } : []).ToArray() : [], calls.CngFreed);
        Assert.Equal(!cng && callerFree ? new nint[] { 10 } : [], calls.CspReleased);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Hardware_provenance_is_required_from_actual_provider(bool cng)
    {
        var calls = new Calls { Implementation = 2 }; var native = new WindowsSigningNative(calls);
        Assert.False(native.IsHardware(new(10, cng ? uint.MaxValue : 2, true)));
        if (cng) Assert.Equal(new nint[] { 20 }, calls.CngFreed);
    }
    [Fact]
    public void Provider_reference_released_even_when_implementation_query_fails()
    {
        var calls = new Calls { ImplementationFailure = true }; var native = new WindowsSigningNative(calls);
        Assert.Throws<WindowsSigningNativeException>(() => native.IsHardware(new(10, uint.MaxValue, true)));
        Assert.Equal(new nint[] { 20 }, calls.CngFreed);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CNG_signing_passes_known_buffer_and_expected_padding(bool rsa)
    {
        var calls = new Calls(); var native = new WindowsSigningNative(calls);
        var signature = native.SignHash(new(10, uint.MaxValue, true), new byte[32], rsa, rsa ? 256 : 64);
        Assert.Equal(rsa ? 256 : 64, signature.Length); Assert.Equal(1, calls.SignCount);
        Assert.Equal(rsa ? "SHA256" : null, calls.PaddingAlgorithm); Assert.Equal(rsa ? 2u : 0u, calls.SignFlags);
    }
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void CSP_signing_sets_SHA256_hash_uses_returned_spec_reverses_RSA_and_destroys_hash(uint spec)
    {
        var calls = new Calls(); var native = new WindowsSigningNative(calls);
        var signature = native.SignHash(new(10, spec, true), new byte[32], true, 4);
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, signature); Assert.Equal(spec, calls.KeySpec);
        Assert.Equal(0x800cu, calls.HashAlgorithm); Assert.Equal(2u, calls.HashProperty); Assert.Equal(1, calls.HashDestroyed);
    }
    [Fact]
    public void CSP_hash_destroyed_on_sign_failure()
    {
        var calls = new Calls { SignFailure = true }; var native = new WindowsSigningNative(calls);
        Assert.Throws<WindowsSigningNativeException>(() => native.SignHash(new(10, 2, true), new byte[32], true, 256)); Assert.Equal(1, calls.HashDestroyed);
    }
    [Fact]
    public void CSP_cleanup_failure_cannot_replace_wrong_PIN_failure()
    {
        var calls = new Calls { SignFailure = true, CleanupThrows = true }; var native = new WindowsSigningNative(calls);
        var failure = Assert.Throws<WindowsSigningNativeException>(() => native.SignHash(new(10, 2, true), new byte[32], true, 256));
        Assert.Equal(0x8010006bu, failure.Code); Assert.Equal("signing", failure.Stage); Assert.Equal(1, calls.HashDestroyed);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reader_query_decodes_encoding_and_accepts_unsupported_missing_property(bool cng)
    {
        var calls = new Calls(); var native = new WindowsSigningNative(calls); var key = new WindowsSigningKey(10, cng ? uint.MaxValue : 2, true);
        Assert.Equal("Synthetic reader", native.ReaderName(key)); calls.ReaderMissing = true; Assert.Null(native.ReaderName(key));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Native_signature_written_size_must_match_certificate_width(bool cng)
    {
        var calls = new Calls { ShortWrite = true }; var native = new WindowsSigningNative(calls);
        Assert.Throws<WindowsSigningNativeException>(() => native.SignHash(new(10, cng ? uint.MaxValue : 2, true), new byte[32], true, 256));
    }
    private sealed class Calls : IWindowsSigningInterop
    {
        public uint LastError => ReaderMissing ? 0x8009000a : 0x8010006b;
        public List<nint> CngFreed { get; } = [];
        public List<nint> CspReleased { get; } = [];
        public uint Implementation { get; set; } = 1;
        public bool ImplementationFailure { get; set; }
        public bool ReaderMissing { get; set; }
        public bool SignFailure { get; set; }
        public bool ShortWrite { get; set; }
        public bool CleanupThrows { get; set; }
        public string? PaddingAlgorithm { get; private set; }
        public uint SignFlags { get; private set; }
        public int SignCount { get; private set; }
        public uint HashAlgorithm { get; private set; }
        public uint HashProperty { get; private set; }
        public uint KeySpec { get; private set; }
        public int HashDestroyed { get; private set; }
        public bool Acquire(nint context, uint flags, nint ownerAddress, out nint key, out uint keySpec, out bool callerFree) { key = 10; keySpec = uint.MaxValue; callerFree = true; return true; }
        public int GetCngProperty(nint handle, string property, byte[] output, out int written)
        {
            written = 0;
            if (property == "Impl Type" && ImplementationFailure) return unchecked((int)0x80090029);
            if (property == "SmartCardReader" && ReaderMissing) return unchecked((int)0x80090011);
            var data = property switch { "Provider Handle" => nint.Size == 8 ? BitConverter.GetBytes(20L) : BitConverter.GetBytes(20), "Impl Type" => BitConverter.GetBytes(Implementation), _ => Encoding.Unicode.GetBytes("Synthetic reader\0") };
            data.CopyTo(output, 0); written = data.Length; return 0;
        }
        public bool GetCspProperty(nint handle, uint property, byte[] output, ref uint written)
        {
            if (property == 43 && ReaderMissing) return false;
            var data = property == 3 ? BitConverter.GetBytes(Implementation) : Encoding.ASCII.GetBytes("Synthetic reader\0"); data.CopyTo(output, 0); written = (uint)data.Length; return true;
        }
        public int SignCng(nint key, nint padding, byte[] hash, byte[] signature, out int written, uint flags)
        {
            SignCount++; SignFlags = flags; PaddingAlgorithm = padding == 0 ? null : Marshal.PtrToStringUni(Marshal.ReadIntPtr(padding));
            written = signature.Length - (ShortWrite ? 1 : 0); return 0;
        }
        public bool CreateHash(nint provider, uint algorithm, out nint hash) { HashAlgorithm = algorithm; hash = 30; return true; }
        public bool SetHash(nint hash, uint property, byte[] value) { HashProperty = property; return true; }
        public bool SignCsp(nint hash, uint keySpec, byte[] signature, ref uint written)
        { SignCount++; KeySpec = keySpec; for (var i = 0; i < signature.Length; i++) signature[i] = (byte)(i + 1); if (ShortWrite) written--; return !SignFailure; }
        public void DestroyHash(nint hash) { HashDestroyed++; if (CleanupThrows) throw new Exception("Destroy failed"); }
        public void FreeCng(nint handle) => CngFreed.Add(handle);
        public void ReleaseCsp(nint handle) => CspReleased.Add(handle);
    }
}
