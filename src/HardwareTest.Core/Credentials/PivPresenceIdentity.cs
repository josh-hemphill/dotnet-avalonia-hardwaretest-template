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
        return nextQuality >= currentQuality ? next : current;
    }

    /// Accumulates UID-only reads and returns when a name settles or the settle window elapses.
    internal sealed class Poll
    {
        public CredentialCaptureResult? Fallback { get; private set; }
        public DateTimeOffset? CardSeenAt { get; private set; }

        /// Completed capture, or null to keep polling.
        public CredentialCaptureResult? Observe(
            CredentialCaptureResult captured,
            DateTimeOffset now,
            out string status)
        {
            if (captured.Succeeded && captured.Credential is not null)
            {
                if (IsSettled(captured.Credential.DisplayName))
                {
                    status = $"Credential present ({captured.Credential.Transport}).";
                    return captured;
                }

                var best = Better(Fallback?.Credential, captured.Credential);
                Fallback = new CredentialCaptureResult { Credential = best };
                CardSeenAt ??= now;
                status = "Reading badge identity…";
            }
            else if (Fallback is null)
            {
                status = captured.Error ?? "Present a badge: insert chip or tap the reader.";
            }
            else
            {
                status = "Reading badge identity…";
            }

            if (Fallback?.Credential is not null
                && CardSeenAt is not null
                && now - CardSeenAt.Value >= Settle)
            {
                status = $"Credential present ({Fallback.Credential.Transport}).";
                return Fallback;
            }

            return null;
        }
    }
}
