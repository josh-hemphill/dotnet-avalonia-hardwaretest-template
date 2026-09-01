using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HardwareTest.Core.IO;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;

namespace HardwareTest.Core.Credentials;

/// Gates certification export/print on chip/tap presence, with optional signing.
public interface IReportAttestationService
{
    TimeSpan PresenceTimeout { get; }

    /// True when site policy requires a credential before exporting or opening this kind.
    bool NeedsAttestation(TestRunRecord run, string reportKind);

    bool HasValidAttestation(TestRunRecord run, string reportKind);

    Task<ReportAttestationResult> AttestAsync(
        TestRunRecord run,
        string reportKind,
        OperatorCredential? credential = null,
        string? pin = null,
        bool skipSigning = false,
        CancellationToken cancellationToken = default);
}

/// Captures a badge, writes a sidecar, and stamps the run record.
public sealed class ReportAttestationService : IReportAttestationService
{
    public const string PackageKind = "package";
    public static readonly TimeSpan DefaultPresenceTimeout = TimeSpan.FromSeconds(20);

    private readonly IOperatorCredentialBroker _broker;
    private readonly IRunStore _runStore;
    private readonly AppSettings _settings;
    private readonly IClock _clock;

    public ReportAttestationService(
        IOperatorCredentialBroker broker,
        IRunStore runStore,
        AppSettings settings,
        IClock? clock = null)
    {
        _broker = broker;
        _runStore = runStore;
        _settings = settings;
        _clock = clock ?? SystemClock.Instance;
        PresenceTimeout = DefaultPresenceTimeout;
    }

    public TimeSpan PresenceTimeout { get; init; }

    public bool NeedsAttestation(TestRunRecord run, string reportKind)
    {
        if (!_settings.RequireAttestationBeforeExport)
        {
            return false;
        }

        if (string.Equals(reportKind, ReportKinds.Certification, StringComparison.OrdinalIgnoreCase)
            || string.Equals(reportKind, PackageKind, StringComparison.OrdinalIgnoreCase))
        {
            return run.Reports.Any(r =>
                string.Equals(r.Kind, ReportKinds.Certification, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    public bool HasValidAttestation(TestRunRecord run, string reportKind)
    {
        var lookupKind = string.Equals(reportKind, PackageKind, StringComparison.OrdinalIgnoreCase)
            ? ReportKinds.Certification
            : reportKind;
        var attestation = Find(run, lookupKind);
        if (attestation is null)
        {
            return false;
        }

        var pdfPath = ResolvePdfPath(run, lookupKind);
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            return false;
        }

        var pdfHash = HashFile(pdfPath);
        if (!string.Equals(pdfHash, attestation.PdfSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(attestation.Kind, AttestationKind.Signed, StringComparison.OrdinalIgnoreCase))
        {
            return SignatureMatches(attestation, pdfHash);
        }

        return string.Equals(attestation.Kind, AttestationKind.Presence, StringComparison.OrdinalIgnoreCase)
            && _settings.AllowPresenceInLieuOfSigning;
    }

    public async Task<ReportAttestationResult> AttestAsync(
        TestRunRecord run,
        string reportKind,
        OperatorCredential? credential = null,
        string? pin = null,
        bool skipSigning = false,
        CancellationToken cancellationToken = default)
    {
        var targetKind = string.Equals(reportKind, PackageKind, StringComparison.OrdinalIgnoreCase)
            ? ReportKinds.Certification
            : reportKind;
        var captured = credential;
        if (captured is null)
        {
            var capture = await _broker.WaitForPresenceAsync(PresenceTimeout, cancellationToken).ConfigureAwait(false);
            if (!capture.Succeeded || capture.Credential is null)
            {
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    Message = capture.Error ?? "Present a badge to certify this report.",
                };
            }

            captured = capture.Credential;
        }

        var pdfPath = ResolvePdfPath(run, targetKind);
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            return new ReportAttestationResult
            {
                Succeeded = false,
                Message = "Certification PDF is missing. Generate reports first.",
                Credential = captured,
            };
        }

        var pdfHash = HashFile(pdfPath);
        var runJson = JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord);
        var runHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(runJson)));
        var payload = Encoding.UTF8.GetBytes($"{pdfHash}:{runHash}");
        CredentialSignResult? sign = null;
        if (!skipSigning)
        {
            sign = await _broker.TrySignPayloadAsync(payload, captured, pin, cancellationToken)
                .ConfigureAwait(false);
            if (sign.PinRequired)
            {
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    PinRequired = true,
                    Credential = captured,
                    Message = sign.Error ?? "Enter badge PIN to sign.",
                };
            }

            if (!sign.Succeeded && sign.PinRetriesRemaining is not null)
            {
                return new ReportAttestationResult
                {
                    Succeeded = false,
                    PinRequired = true,
                    Credential = captured,
                    Message = sign.Error ?? "Incorrect PIN.",
                };
            }
        }

        var signature = sign is { Succeeded: true } ? sign.Signature : null;
        var kind = signature is { Length: > 0 } ? AttestationKind.Signed : AttestationKind.Presence;
        if (kind == AttestationKind.Presence && !_settings.AllowPresenceInLieuOfSigning)
        {
            return new ReportAttestationResult
            {
                Succeeded = false,
                Credential = captured,
                Message = sign?.Error ?? "This badge cannot sign, and presence-only attestation is disabled.",
            };
        }

        var sidecarName = $"{targetKind}.attestation.json";
        var sidecarPath = Path.Combine(_runStore.GetRunDirectory(run.RunId), sidecarName);
        var document = new ReportAttestation
        {
            Kind = kind,
            ReportKind = targetKind,
            DisplayName = captured.DisplayName,
            Serial = captured.Serial,
            Transport = captured.Transport,
            Thumbprint = sign?.Thumbprint ?? captured.Thumbprint,
            PdfSha256 = pdfHash,
            RunJsonSha256 = runHash,
            SidecarPath = sidecarPath,
            Algorithm = signature is { Length: > 0 }
                ? (sign?.Algorithm ?? _broker.SigningAlgorithm ?? AttestationAlgorithm.MockHmac)
                : AttestationAlgorithm.Presence,
            CapturedAt = captured.CapturedAt == default ? _clock.UtcNow : captured.CapturedAt,
        };

        var sidecar = new ReportAttestationSidecar
        {
            Attestation = document,
            SignatureBase64 = signature is { Length: > 0 } ? Convert.ToBase64String(signature) : null,
            CertificateBase64 = sign?.CertificateDer is { Length: > 0 }
                ? Convert.ToBase64String(sign.CertificateDer)
                : null,
        };
        await AtomicFile.WriteJsonAsync(sidecarPath, sidecar, AppJsonContext.Default.ReportAttestationSidecar, cancellationToken)
            .ConfigureAwait(false);

        run.Attestations.RemoveAll(a => string.Equals(a.ReportKind, targetKind, StringComparison.OrdinalIgnoreCase));
        run.Attestations.Add(document);
        if (string.IsNullOrWhiteSpace(run.OperatorName))
        {
            run.OperatorName = captured.DisplayName;
        }

        await _runStore.SaveAsync(run, cancellationToken).ConfigureAwait(false);

        var verb = kind == AttestationKind.Signed ? "Signed" : "Recorded presence";
        return new ReportAttestationResult
        {
            Succeeded = true,
            Credential = captured,
            Message = $"{verb} for {captured.DisplayName} ({captured.Transport}).",
            Attestation = document,
        };
    }

    /// Drops stamps and sidecars for kinds that are about to be regenerated (PDF bytes will change).
    public static void InvalidateForKinds(TestRunRecord run, string runDirectory, IEnumerable<string> kinds)
    {
        foreach (var kind in kinds)
        {
            var existing = run.Attestations
                .Where(a => string.Equals(a.ReportKind, kind, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var attestation in existing)
            {
                TryDeleteSidecar(attestation.SidecarPath);
            }

            run.Attestations.RemoveAll(a =>
                string.Equals(a.ReportKind, kind, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(runDirectory))
            {
                TryDeleteSidecar(Path.Combine(runDirectory, $"{kind}.attestation.json"));
            }
        }
    }

    public static ReportAttestation? Find(TestRunRecord run, string reportKind)
        => run.Attestations.LastOrDefault(a =>
            string.Equals(a.ReportKind, reportKind, StringComparison.OrdinalIgnoreCase));

    public static string? ResolvePdfPath(TestRunRecord run, string reportKind)
    {
        var match = run.Reports.FirstOrDefault(r =>
            string.Equals(r.Kind, reportKind, StringComparison.OrdinalIgnoreCase));
        if (match is not null && !string.IsNullOrWhiteSpace(match.PdfPath))
        {
            return match.PdfPath;
        }

        return string.Equals(reportKind, ReportKinds.Certification, StringComparison.OrdinalIgnoreCase)
            ? null
            : run.ReportPdfPath;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool SignatureMatches(ReportAttestation attestation, string pdfHash)
    {
        if (string.IsNullOrWhiteSpace(attestation.SidecarPath) || !File.Exists(attestation.SidecarPath))
        {
            return false;
        }

        using var stream = File.OpenRead(attestation.SidecarPath);
        var sidecar = JsonSerializer.Deserialize(stream, AppJsonContext.Default.ReportAttestationSidecar);
        if (sidecar?.SignatureBase64 is null
            || !string.Equals(pdfHash, attestation.PdfSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(sidecar.SignatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = Encoding.UTF8.GetBytes($"{attestation.PdfSha256}:{attestation.RunJsonSha256}");
        var algorithm = attestation.Algorithm ?? sidecar.Attestation.Algorithm;
        if (string.Equals(algorithm, AttestationAlgorithm.MockHmac, StringComparison.Ordinal))
        {
            return MockOperatorCredentialBroker.VerifyMockSignature(payload, signature);
        }

        if (string.IsNullOrWhiteSpace(sidecar.CertificateBase64))
        {
            return false;
        }

        byte[] cert;
        try
        {
            cert = Convert.FromBase64String(sidecar.CertificateBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        return PivSigner.Verify(payload, signature, cert, algorithm ?? string.Empty);
    }

    private static void TryDeleteSidecar(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reprint must still proceed; a leftover sidecar is dropped from the run record.
        }
    }
}

/// Disk sidecar next to the PDF (signature bytes stay off the run JSON).
public sealed class ReportAttestationSidecar
{
    public ReportAttestation Attestation { get; set; } = new();
    public string? SignatureBase64 { get; set; }
    public string? CertificateBase64 { get; set; }
}
