namespace HardwareTest.Core.Credentials;

/// Binds a signature to the badge identity captured in the same attest call.
internal static class CredentialSignBinding
{
    public const string SameBadgeRequired = "Present the same badge to sign.";

    public static bool SerialsMatch(string expected, string? presented)
        => !string.IsNullOrWhiteSpace(expected)
           && string.Equals(expected, presented, StringComparison.OrdinalIgnoreCase);

    /// Signs only when the presented serial matches the identity from this attest call.
    public static CredentialSignResult SignMatching(
        IApduChannel channel,
        byte[] payload,
        string? pin,
        OperatorCredential expected,
        string? presentedSerial,
        string? presentedName)
    {
        if (!SerialsMatch(expected.Serial, presentedSerial))
        {
            return CredentialSignResult.Failed(SameBadgeRequired);
        }

        var signed = PivSigner.Sign(channel, payload, pin);
        if (!signed.Succeeded)
        {
            return signed;
        }

        var identity = new OperatorCredential
        {
            DisplayName = string.IsNullOrWhiteSpace(presentedName) ? expected.DisplayName : presentedName,
            Serial = presentedSerial!,
            Transport = expected.Transport,
            ReaderName = expected.ReaderName,
            Thumbprint = signed.Thumbprint ?? expected.Thumbprint,
            CapturedAt = expected.CapturedAt,
        };
        return CredentialSignResult.Signed(
            signed.Signature!,
            signed.Algorithm ?? AttestationAlgorithm.PivRsaPkcs1Sha256,
            signed.CertificateDer,
            signed.Thumbprint,
            identity);
    }
}
