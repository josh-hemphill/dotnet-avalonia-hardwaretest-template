using System.Net.Mail;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace HardwareTest.Core.Credentials;

/// Display names from PIV X.509 subject / SAN (NIST SP 800-73, FIPS 201).
internal static class PivCertificateName
{
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
    {
        try
        {
            using var cert = X509CertificateLoader.LoadCertificate(certificateDer);
            var subject = Normalize(cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
            if (IsUsefulPersonName(subject))
            {
                return subject;
            }

            var email = Normalize(cert.GetNameInfo(X509NameType.EmailName, forIssuer: false));
            if (IsEmail(email))
            {
                return email;
            }

            var upn = Normalize(cert.GetNameInfo(X509NameType.UpnName, forIssuer: false));
            if (IsEmail(upn))
            {
                return upn;
            }

            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

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

    public static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return false;
        }

        try
        {
            var parsed = new MailAddress(value.Trim());
            return parsed.Address.Contains('.', StringComparison.Ordinal)
                   && parsed.Host.Contains('.', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
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

    private static bool IsHexDigit(char c)
        => char.IsAsciiHexDigit(c);
}
