namespace HardwareTest.Core.Credentials;

/// Presence polling: a UID-only `Card {hex}` read is not done until the applet has had time to answer.
internal static class PivPresenceIdentity
{
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    public static bool IsSettled(string? displayName)
        => PivCertificateName.Classify(displayName).Quality >= PivIdentityQuality.DirectoryEmail;

    public static OperatorCredential Better(OperatorCredential? current, OperatorCredential next)
    {
        if (current is null)
        {
            return next;
        }

        var currentQuality = PivCertificateName.Classify(current.DisplayName).Quality;
        var nextQuality = PivCertificateName.Classify(next.DisplayName).Quality;
        return nextQuality > currentQuality ? next : current;
    }
}
