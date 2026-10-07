namespace HardwareTest.Core.Credentials;

/// Binds mock payload signing to the captured mock badge identity.
internal static class CredentialSignBinding
{
    public const string SameBadgeRequired = "Present the same badge to sign.";

    public static bool SerialsMatchMock(string expected, string? presented)
        => !string.IsNullOrWhiteSpace(expected)
           && string.Equals(expected, presented, StringComparison.OrdinalIgnoreCase);
}
