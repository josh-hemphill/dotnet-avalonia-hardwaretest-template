using System.Net.Mail;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HardwareTest.Core.Credentials;

internal enum PivIdentityQuality
{
    None = 0,
    WeakIdentifier = 1,
    DirectoryEmail = 2,
    PersonName = 3,
}

internal readonly record struct PivIdentityCandidate(string? Value, PivIdentityQuality Quality)
{
    public static PivIdentityCandidate None { get; } = new(null, PivIdentityQuality.None);

    public bool HasValue
        => Quality != PivIdentityQuality.None && !string.IsNullOrWhiteSpace(Value);
}

/// Display names from PIV X.509 subject / SAN (NIST SP 800-73, FIPS 201).
internal static class PivCertificateName
{
    private const string OidSurname = "2.5.4.4";
    private const string OidGivenName = "2.5.4.42";

    private static readonly string[] GenericSlotLabels =
    [
        "Card Authentication",
        "PIV Card Authentication",
        "PIV Authentication",
        "Digital Signature",
        "Key Management",
    ];

    /// Prefers a useful subject CN, then rfc822 SAN / subject email, then UPN-as-email.
    public static string? TryDisplayName(byte[] certificateDer)
        => TryCandidate(certificateDer).Value;

    /// Best identity on one certificate; PersonName beats email, email beats digit UPN/hex.
    public static PivIdentityCandidate TryCandidate(byte[] certificateDer)
    {
        try
        {
            using var cert = X509CertificateLoader.LoadCertificate(certificateDer);
            var best = PivIdentityCandidate.None;
            best = Prefer(best, Classify(TryGivenNameSurname(cert)));
            best = Prefer(best, Classify(cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false)));
            best = Prefer(best, Classify(cert.GetNameInfo(X509NameType.EmailName, forIssuer: false)));
            best = Prefer(best, Classify(cert.GetNameInfo(X509NameType.UpnName, forIssuer: false)));
            return best;
        }
        catch (CryptographicException)
        {
            return PivIdentityCandidate.None;
        }
    }

    public static PivIdentityCandidate Classify(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return PivIdentityCandidate.None;
        }

        if (IsEmail(normalized))
        {
            var at = normalized.IndexOf('@');
            var local = at > 0 ? normalized[..at] : normalized;
            var weak = local.Length > 0 && local.All(char.IsAsciiDigit);
            return new PivIdentityCandidate(
                normalized,
                weak ? PivIdentityQuality.WeakIdentifier : PivIdentityQuality.DirectoryEmail);
        }

        if (IsUsefulPersonName(normalized))
        {
            return new PivIdentityCandidate(normalized, PivIdentityQuality.PersonName);
        }

        return PivIdentityCandidate.None;
    }

    public static PivIdentityCandidate Prefer(PivIdentityCandidate current, PivIdentityCandidate next)
        => next.Quality > current.Quality ? next : current;

    public static bool IsUsefulPersonName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
        {
            return false;
        }

        var trimmed = value.Trim();
        foreach (var label in GenericSlotLabels)
        {
            if (trimmed.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (trimmed.StartsWith("Card ", StringComparison.OrdinalIgnoreCase)
            && IsCardIdToken(trimmed[5..].Trim()))
        {
            return false;
        }

        return !IsCardIdToken(trimmed);
    }

    /// True for rfc822 addresses and UPN-style values including DoD `edipi@mil`.
    public static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return false;
        }

        try
        {
            var parsed = new MailAddress(value.Trim());
            if (string.IsNullOrWhiteSpace(parsed.User) || string.IsNullOrWhiteSpace(parsed.Host))
            {
                return false;
            }

            if (parsed.Host.Contains('.', StringComparison.Ordinal))
            {
                return !parsed.Host.StartsWith('.') && !parsed.Host.EndsWith('.');
            }

            return IsSingleDnsLabel(parsed.Host);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? TryGivenNameSurname(X509Certificate2 cert)
    {
        string? given = null;
        string? surname = null;
        foreach (var rdn in cert.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            if (rdn.HasMultipleElements)
            {
                continue;
            }

            var oid = rdn.GetSingleElementType().Value;
            var text = Normalize(rdn.GetSingleElementValue());
            if (text is null)
            {
                continue;
            }

            switch (oid)
            {
                case OidGivenName:
                    given = text;
                    break;
                case OidSurname:
                    surname = text;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(given) || string.IsNullOrWhiteSpace(surname))
        {
            return null;
        }

        return $"{given} {surname}";
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 64 ? trimmed : trimmed[..64];
    }

    private static bool IsCardIdToken(string value)
    {
        if (value.Length < 8)
        {
            return false;
        }

        var hex = 0;
        foreach (var c in value)
        {
            if (c is '-' or ' ' or ':')
            {
                continue;
            }

            if (!IsHexDigit(c))
            {
                return false;
            }

            hex++;
        }

        return hex >= 8;
    }

    private static bool IsSingleDnsLabel(string host)
    {
        if (host.Length is < 2 or > 63)
        {
            return false;
        }

        if (!char.IsAsciiLetter(host[0]) || host[^1] == '-')
        {
            return false;
        }

        foreach (var c in host)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHexDigit(char c)
        => char.IsAsciiHexDigit(c);
}
