using System.Runtime.InteropServices;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
using HardwareTest.Tests.Fixtures;
using HardwareTest.Tests.Time;
using Xunit;

namespace HardwareTest.Tests.Credentials;

public sealed class MockOperatorCredentialBrokerTests
{
    [Fact]
    public async Task WaitForPresence_returns_mock_identity()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero));
        var broker = new MockOperatorCredentialBroker(clock, canSign: true, transport: CredentialTransport.Contact);
        var result = await broker.WaitForPresenceAsync(TimeSpan.FromSeconds(1));
        Assert.True(result.Succeeded);
        Assert.Equal(MockOperatorCredentialBroker.MockSerial, result.Credential!.Serial);
        Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, result.Credential.DisplayName);
        Assert.Equal(CredentialTransport.Contact, result.Credential.Transport);
        Assert.Equal(clock.UtcNow, result.Credential.CapturedAt);
        Assert.True(broker.CanSign);
        Assert.Equal(MockOperatorCredentialBroker.MockAlgorithm, broker.SigningAlgorithm);
    }

    [Fact]
    public async Task TrySign_hmac_verifies_when_can_sign()
    {
        var broker = new MockOperatorCredentialBroker(canSign: true);
        var capture = await broker.WaitForPresenceAsync(TimeSpan.FromSeconds(1));
        var payload = "pdf:run"u8.ToArray();
        var signature = await broker.TrySignPayloadAsync(payload, capture.Credential!);
        Assert.True(signature.Succeeded);
        Assert.True(MockOperatorCredentialBroker.VerifyMockSignature(payload, signature.Signature!));
    }

    [Fact]
    public async Task TrySign_returns_null_when_presence_only()
    {
        var broker = new MockOperatorCredentialBroker(canSign: false);
        var capture = await broker.WaitForPresenceAsync(TimeSpan.FromSeconds(1));
        var signature = await broker.TrySignPayloadAsync("x"u8.ToArray(), capture.Credential!);
        Assert.False(signature.Succeeded);
        Assert.Null(broker.SigningAlgorithm);
    }
}

public sealed class ReportAttestationServiceTests
{
    [Fact]
    public async Task Attest_signed_when_mock_can_sign()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = true,
        };
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: true),
            store,
            settings);
        Assert.True(service.NeedsAttestation(run, ReportAttestationService.PackageKind));
        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));

        var result = await service.AttestAsync(run, ReportAttestationService.PackageKind);
        Assert.True(result.Succeeded);
        Assert.Equal(AttestationKind.Signed, result.Attestation!.Kind);
        Assert.Equal(AttestationAlgorithm.MockHmac, result.Attestation.Algorithm);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
        Assert.True(File.Exists(result.Attestation.SidecarPath));
        Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, run.OperatorName);
    }

    [Fact]
    public async Task Attest_presence_when_signing_unavailable_and_flag_on()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = true,
        };
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: false),
            store,
            settings);
        var result = await service.AttestAsync(run, ReportKinds.Certification);
        Assert.True(result.Succeeded);
        Assert.Equal(AttestationKind.Presence, result.Attestation!.Kind);
        Assert.Equal(AttestationAlgorithm.Presence, result.Attestation.Algorithm);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task Attest_fails_when_presence_flag_off_and_cannot_sign()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = false,
        };
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: false),
            store,
            settings);
        var result = await service.AttestAsync(run, ReportKinds.Certification);
        Assert.False(result.Succeeded);
        Assert.Contains("presence-only", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task HasValidAttestation_false_after_pdf_bytes_change()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = true,
        };
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: true),
            store,
            settings);
        var result = await service.AttestAsync(run, ReportKinds.Certification);
        Assert.True(result.Succeeded);
        await File.WriteAllBytesAsync(run.Reports[0].PdfPath, "%PDF-changed"u8.ToArray());
        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public void NeedsAttestation_false_for_status_only_runs()
    {
        var settings = new AppSettings { RequireAttestationBeforeExport = true };
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(),
            new FileRunStore(Path.Combine(Path.GetTempPath(), "unused-attest")),
            settings);
        var run = new TestRunRecord
        {
            RunId = "s",
            Reports = [new RunReportArtifact { Kind = ReportKinds.Status, PdfPath = "s.pdf" }],
        };
        Assert.False(service.NeedsAttestation(run, ReportKinds.Status));
        Assert.False(service.NeedsAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public void SettingsBackedBroker_switches_with_UseMockOperatorCredential()
    {
        var settings = new AppSettings { UseMockOperatorCredential = true };
        var mock = new MockOperatorCredentialBroker();
        var pcsc = new PcscOperatorCredentialBroker();
        var broker = new SettingsBackedCredentialBroker(settings, mock, pcsc);
        Assert.True(broker.IsMock);
        settings.UseMockOperatorCredential = false;
        Assert.False(broker.IsMock);
        Assert.True(broker.CanSign);
    }

    [Fact]
    public void Contactless_reader_names_are_detected()
    {
        Assert.True(PivCardIdentity.IsContactlessReader("ACS ACR122U PICC Interface"));
        Assert.True(PivCardIdentity.IsContactlessReader("Broadcom NFC Contactless"));
        Assert.False(PivCardIdentity.IsContactlessReader("SCM Microsystems Contact Reader"));
    }

    [Fact]
    public void ScardIoRequest_layouts_match_platform_headers()
    {
        Assert.Equal(8, Marshal.SizeOf<PcscNative.ScardIoRequestDword>());
        Assert.Equal(IntPtr.Size * 2, Marshal.SizeOf<PcscNative.ScardIoRequestULong>());
    }

    [Fact]
    public async Task InvalidateForKinds_drops_stamp_and_sidecar_for_regenerated_kind()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var settings = new AppSettings
        {
            RequireAttestationBeforeExport = true,
            AllowPresenceInLieuOfSigning = true,
        };
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: false),
            store,
            settings);
        var attested = await service.AttestAsync(run, ReportKinds.Certification);
        Assert.True(attested.Succeeded);
        Assert.True(File.Exists(attested.Attestation!.SidecarPath));

        ReportAttestationService.InvalidateForKinds(
            run,
            store.GetRunDirectory(run.RunId),
            [ReportKinds.Certification]);

        Assert.Empty(run.Attestations);
        Assert.False(File.Exists(attested.Attestation.SidecarPath));
        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task InvalidateForKinds_leaves_other_kinds_in_place()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        run.Attestations.Add(new ReportAttestation
        {
            Kind = AttestationKind.Presence,
            ReportKind = ReportKinds.Status,
            DisplayName = "Other",
            Serial = "KEEP",
            PdfSha256 = "abc",
        });
        ReportAttestationService.InvalidateForKinds(
            run,
            store.GetRunDirectory(run.RunId),
            [ReportKinds.Certification]);
        Assert.Single(run.Attestations);
        Assert.Equal(ReportKinds.Status, run.Attestations[0].ReportKind);
    }

    [Fact]
    public async Task Attest_pin_required_then_signed_with_piv_rsa()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var card = FakePivCard.CreateRsa2048();
        var broker = new ScriptedPivBroker(card);
        var service = new ReportAttestationService(
            broker,
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = false,
            });

        var first = await service.AttestAsync(run, ReportKinds.Certification);
        Assert.True(first.PinRequired);
        Assert.False(first.Succeeded);
        Assert.Empty(run.Attestations);

        var signed = await service.AttestAsync(
            run,
            ReportKinds.Certification,
            first.Credential,
            FakePivCard.DefaultPin);
        Assert.True(signed.Succeeded);
        Assert.Equal(AttestationKind.Signed, signed.Attestation!.Kind);
        Assert.Equal(AttestationAlgorithm.PivRsaPkcs1Sha256, signed.Attestation.Algorithm);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
        var sidecar = await File.ReadAllTextAsync(signed.Attestation.SidecarPath!);
        Assert.Contains("certificateBase64", sidecar, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HasValidAttestation_false_when_signed_sidecar_tampered()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var card = FakePivCard.CreateRsa2048();
        var service = new ReportAttestationService(
            new ScriptedPivBroker(card),
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = false,
            });
        var signed = await service.AttestAsync(
            run,
            ReportKinds.Certification,
            pin: FakePivCard.DefaultPin);
        Assert.True(signed.Succeeded);
        var path = signed.Attestation!.SidecarPath!;
        var json = await File.ReadAllTextAsync(path);
        var bad = json.Replace(
            "\"signatureBase64\":",
            "\"signatureBase64\":\"AAAA\", \"_was\":",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, bad);
        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task Attest_skip_signing_records_presence_when_flag_on()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: true),
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = true,
            });
        var result = await service.AttestAsync(run, ReportKinds.Certification, skipSigning: true);
        Assert.True(result.Succeeded);
        Assert.Equal(AttestationKind.Presence, result.Attestation!.Kind);
    }

    [Fact]
    public async Task Attest_failed_sign_after_pin_does_not_record_presence()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var card = FakePivCard.CreateRsa2048();
        card.FailVerifyAsSecurityStatus = true;
        var service = new ReportAttestationService(
            new ScriptedPivBroker(card),
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = true,
            });
        var result = await service.AttestAsync(
            run,
            ReportKinds.Certification,
            pin: FakePivCard.DefaultPin);
        Assert.False(result.Succeeded);
        Assert.Contains("Insert the chip", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(run.Attestations);
    }

    [Fact]
    public async Task Attest_same_badge_mismatch_does_not_record_presence()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: true),
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = true,
            });
        var foreign = new OperatorCredential
        {
            DisplayName = "Other Badge",
            Serial = "OTHER-SERIAL",
            Transport = CredentialTransport.Contact,
        };
        var result = await service.AttestAsync(run, ReportKinds.Certification, foreign);
        Assert.False(result.Succeeded);
        Assert.Equal(CredentialSignBinding.SameBadgeRequired, result.Message);
        Assert.Empty(run.Attestations);
    }

    [Fact]
    public async Task Attest_keeps_session_operator_distinct_from_certifier()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        run.OperatorName = "Session Technician";
        await store.SaveAsync(run);
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: true),
            store,
            new AppSettings { RequireAttestationBeforeExport = true });
        var result = await service.AttestAsync(run, ReportKinds.Certification);
        Assert.True(result.Succeeded);
        Assert.Equal("Session Technician", run.OperatorName);
        Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, result.Attestation!.DisplayName);
    }

    private static async Task<TestRunRecord> SeedCertificationRunAsync(FileRunStore store)
    {
        var run = new TestRunRecord
        {
            RunId = "attest-" + Guid.NewGuid().ToString("N")[..8],
            PlanName = "Cert",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        };
        await store.SaveAsync(run);
        var pdf = Path.Combine(store.GetRunDirectory(run.RunId), "certification.pdf");
        await File.WriteAllBytesAsync(pdf, "%PDF-1.4 test"u8.ToArray());
        run.Reports.Add(new RunReportArtifact
        {
            Kind = ReportKinds.Certification,
            Title = "Certification Report",
            PdfPath = pdf,
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await store.SaveAsync(run);
        return run;
    }
}

internal sealed class ScriptedPivBroker : IOperatorCredentialBroker
{
    private readonly FakePivCard _card;
    private readonly MockOperatorCredentialBroker _identity = new(canSign: false);

    public ScriptedPivBroker(FakePivCard card) => _card = card;

    public bool IsMock => true;
    public bool CanSign => true;
    public string? SigningAlgorithm => AttestationAlgorithm.PivRsaPkcs1Sha256;
    public string StatusText => "Fake PIV";

    public Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => _identity.WaitForPresenceAsync(timeout, cancellationToken);

    public Task<CredentialSignResult> TrySignPayloadAsync(
        byte[] payload,
        OperatorCredential credential,
        string? pin = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = credential;
        return Task.FromResult(PivSigner.Sign(_card, payload, pin));
    }
}
