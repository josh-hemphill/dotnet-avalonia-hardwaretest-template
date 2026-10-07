using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using HardwareTest.Core.Credentials;
using HardwareTest.Core.Reporting;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using HardwareTest.Core.Time;
using HardwareTest.Tests.Fixtures;
using HardwareTest.Tests.Reporting;
using HardwareTest.Tests.Time;
using PCSC;
using PCSC.Exceptions;
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
        Assert.False(result.Attestation.EmbeddedInPdf);
        Assert.Equal(AttestationSignatureFormat.DetachedSidecar, result.Attestation.SignatureFormat);
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
        var issuedPath = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification);
        Assert.False(string.IsNullOrWhiteSpace(issuedPath));
        await File.WriteAllBytesAsync(issuedPath!, "%PDF-changed"u8.ToArray());
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
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "s",
            Reports = [new RunReportArtifact { Role = ReportArtifactRoles.Working, Kind = ReportKinds.Status, PdfPath = "s.pdf" }],
        };
        Assert.False(service.NeedsAttestation(run, ReportKinds.Status));
        Assert.False(service.NeedsAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public void SettingsBackedBroker_switches_with_UseMockOperatorCredential()
    {
        var settings = new AppSettings { UseMockOperatorCredential = true };
        var mock = new MockOperatorCredentialBroker();
        var physical = new Pkcs11OperatorCredentialBroker(settings, presence: new PcscOperatorCredentialBroker());
        var broker = new SettingsBackedCredentialBroker(settings, mock, physical);
        Assert.True(broker.IsMock);
        Assert.False(broker.CanSignPdf);
        settings.UseMockOperatorCredential = false;
        Assert.False(broker.IsMock);
        Assert.True(broker.CanSign);
        Assert.True(broker.CanSignPdf);
    }

    [Fact]
    public void Contactless_reader_names_are_detected()
    {
        Assert.True(PivCardIdentity.IsContactlessReader("ACS ACR122U PICC Interface"));
        Assert.True(PivCardIdentity.IsContactlessReader("Broadcom NFC Contactless"));
        Assert.False(PivCardIdentity.IsContactlessReader("SCM Microsystems Contact Reader"));
    }

    [Fact]
    public void Pcsc_cleanup_ignores_hot_removal_errors()
    {
        var resource = new ThrowingPcscDisposable();

        PcscOperatorCredentialBroker.DisposeBestEffort(resource);

        Assert.True(resource.DisposeAttempted);
    }

    [Fact]
    public async Task Attest_pin_required_then_signed_with_piv_rsa()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var originalCertBytes = await File.ReadAllBytesAsync(WorkingCertificationPath(run));
        using var broker = new SoftwarePdfBroker();
        var reports = new RecordingReportService();
        var service = new ReportAttestationService(
            broker,
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = false,
            },
            reports: new Lazy<IReportService>(() => reports));

        var first = await service.AttestAsync(run, ReportKinds.Certification);
        Assert.True(first.PinRequired);
        Assert.False(first.Succeeded);
        Assert.Empty(run.Attestations);
        Assert.Equal(originalCertBytes, await File.ReadAllBytesAsync(WorkingCertificationPath(run)));
        Assert.Null(ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification));
        Assert.Equal(0, reports.GenerateCount);

        var signed = await service.AttestAsync(
            run,
            ReportKinds.Certification,
            first.Credential,
            SoftwarePdfBroker.Pin);
        Assert.True(signed.Succeeded);
        Assert.Equal(AttestationKind.Signed, signed.Attestation!.Kind);
        Assert.Equal(AttestationAlgorithm.PivRsaPkcs1Sha256, signed.Attestation.Algorithm);
        Assert.True(signed.Attestation.EmbeddedInPdf);
        Assert.Equal(AttestationSignatureFormat.PadesBasic, signed.Attestation.SignatureFormat);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
        Assert.Equal(1, reports.GenerateCount);
        Assert.Equal(originalCertBytes, await File.ReadAllBytesAsync(WorkingCertificationPath(run)));
        var issuedPath = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification);
        Assert.False(string.IsNullOrWhiteSpace(issuedPath));
        var stamped = await File.ReadAllBytesAsync(issuedPath!);
        Assert.NotEqual(originalCertBytes, stamped);
        Assert.Contains(SoftwarePdfBroker.DisplayName, Encoding.UTF8.GetString(stamped), StringComparison.Ordinal);
        Assert.True(ITextPadesSignature.TryVerify(stamped, broker.Credential.Thumbprint, out var verifyError), verifyError);
        var sidecar = await File.ReadAllTextAsync(signed.Attestation.SidecarPath!);
        Assert.Contains("certificateBase64", sidecar, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HasValidAttestation_false_when_new_pades_revision_sidecar_missing()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var service = new ReportAttestationService(
            broker,
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = false,
            });
        var signed = await service.AttestAsync(
            run,
            ReportKinds.Certification,
            pin: SoftwarePdfBroker.Pin);
        Assert.True(signed.Succeeded);
        var path = signed.Attestation!.SidecarPath!;
        File.Delete(path);
        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task HasValidAttestation_false_when_new_pades_revision_sidecar_tampered()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var service = new ReportAttestationService(
            broker,
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = false,
            });
        var signed = await service.AttestAsync(
            run,
            ReportKinds.Certification,
            pin: SoftwarePdfBroker.Pin);
        Assert.True(signed.Succeeded);
        var path = signed.Attestation!.SidecarPath!;
        var json = await File.ReadAllTextAsync(path);
        var bad = json.Replace(
            "\"signatureBase64\":",
            "\"signatureBase64\":\"AAAA\", \"_was\":",
            StringComparison.Ordinal);
        Assert.NotEqual(json, bad);
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
        var original = await File.ReadAllBytesAsync(run.Reports[0].PdfPath);
        using var broker = new SoftwarePdfBroker { Mode = "failure" };
        var reports = new RecordingReportService();
        var service = new ReportAttestationService(
            broker,
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = true,
            },
            reports: new Lazy<IReportService>(() => reports));
        var result = await service.AttestAsync(
            run,
            ReportKinds.Certification,
            pin: SoftwarePdfBroker.Pin);
        Assert.False(result.Succeeded);
        Assert.Contains("Signing failed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(run.Attestations);
        Assert.Equal(original, await File.ReadAllBytesAsync(run.Reports[0].PdfPath));
        Assert.Null(ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification));
        Assert.False(Directory.Exists(Path.Combine(store.GetRunDirectory(run.RunId), ReportArtifactRoles.DirectoryName)));
    }

    [Fact]
    public async Task Attest_unavailable_embedded_signing_after_pin_records_presence_when_policy_allows()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var reports = new RecordingReportService();
        var service = new ReportAttestationService(
            new UnavailableEmbeddedBroker(),
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = true,
            },
            reports: new Lazy<IReportService>(() => reports));

        var result = await service.AttestAsync(
            run,
            ReportKinds.Certification,
            pin: "123456");

        Assert.True(result.Succeeded);
        Assert.Equal(AttestationKind.Presence, result.Attestation!.Kind);
        Assert.Equal(AttestationKind.Presence, reports.LastIdentity!.Kind);
    }

    [Theory]
    [InlineData("malformed-pdf")]
    [InlineData("cms-only")]
    [InlineData("missing-pdf")]
    [InlineData("missing-credential")]
    [InlineData("wrong-serial")]
    [InlineData("wrong-credential-thumbprint")]
    [InlineData("wrong-thumbprint")]
    [InlineData("missing-thumbprint")]
    [InlineData("wrong-certificate")]
    [InlineData("missing-certificate")]
    [InlineData("missing-signature")]
    [InlineData("malformed-certificate")]
    [InlineData("different-signer")]
    [InlineData("appended-bytes")]
    public async Task Attest_malformed_embedded_success_cannot_downgrade_or_replace_prior_issue(string mode)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var service = new ReportAttestationService(broker, store,
            new AppSettings { AllowPresenceInLieuOfSigning = true });
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin)).Succeeded);
        var snapshot = await PublicationSnapshot.CaptureAsync(run, store);

        broker.Mode = mode;
        var rejected = await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin);

        Assert.False(rejected.Succeeded);
        Assert.False(rejected.PinRequired);
        await snapshot.AssertUnchangedAsync(run, store);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("retry")]
    [InlineData("cancel")]
    [InlineData("pin-required")]
    public async Task Attest_failure_retry_or_cancel_preserves_existing_publication(string mode)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var service = new ReportAttestationService(broker, store,
            new AppSettings { AllowPresenceInLieuOfSigning = true });
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin)).Succeeded);
        var snapshot = await PublicationSnapshot.CaptureAsync(run, store);
        broker.Mode = mode;

        if (mode == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin));
        else
        {
            var result = await service.AttestAsync(run, ReportKinds.Certification,
                pin: mode == "pin-required" ? null : SoftwarePdfBroker.Pin);
            Assert.False(result.Succeeded);
            Assert.Equal(mode is "retry" or "pin-required", result.PinRequired);
        }

        await snapshot.AssertUnchangedAsync(run, store);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task Attest_save_failure_restores_existing_publication()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var faultingStore = new FaultingRunStore(store);
        var service = new ReportAttestationService(broker, faultingStore, new AppSettings());
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin)).Succeeded);
        var snapshot = await PublicationSnapshot.CaptureAsync(run, store);
        faultingStore.FailSave = true;

        var rejected = await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin);
        Assert.False(rejected.Succeeded);
        Assert.Contains("Could not publish", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Fixture save failure", rejected.Message, StringComparison.Ordinal);

        await snapshot.AssertUnchangedAsync(run, store);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task Attest_cancellation_at_publication_restores_existing_publication()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var faultingStore = new FaultingRunStore(store);
        var service = new ReportAttestationService(broker, faultingStore, new AppSettings());
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin)).Succeeded);
        var snapshot = await PublicationSnapshot.CaptureAsync(run, store);
        faultingStore.CancelSave = true;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin));

        await snapshot.AssertUnchangedAsync(run, store);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task HasValidAttestation_rejects_physical_detached_signature()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var service = new ReportAttestationService(broker, store, new AppSettings());
        var signed = await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin);
        Assert.True(signed.Succeeded);
        // The valid certificate/CMS sidecar cannot authorize the physical detached route.
        signed.Attestation!.EmbeddedInPdf = false;
        signed.Attestation.SignatureFormat = AttestationSignatureFormat.DetachedSidecar;

        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("001122")]
    public async Task HasValidAttestation_rejects_missing_or_different_recorded_signer(string? thumbprint)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var service = new ReportAttestationService(broker, store, new AppSettings());
        var signed = await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin);
        Assert.True(signed.Succeeded);
        signed.Attestation!.Thumbprint = thumbprint;

        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task HasValidAttestation_rejects_appended_bytes_even_when_recorded_hash_matches()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var service = new ReportAttestationService(broker, store, new AppSettings());
        var signed = await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin);
        Assert.True(signed.Succeeded);
        var issued = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification)!;
        var changed = (await File.ReadAllBytesAsync(issued)).Concat("trailing bytes"u8.ToArray()).ToArray();
        await File.WriteAllBytesAsync(issued, changed);
        signed.Attestation!.PdfSha256 = Convert.ToHexString(SHA256.HashData(changed));

        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Attest_empty_candidate_preserves_existing_issue_and_all_metadata(bool presence, bool emptyWorking)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var broker = new MockOperatorCredentialBroker(canSign: true);
        var settings = new AppSettings { AllowPresenceInLieuOfSigning = true };
        var initialService = new ReportAttestationService(broker, store, settings);
        Assert.True((await initialService.AttestAsync(run, ReportKinds.Certification)).Succeeded);
        var reports = new RecordingReportService { ReturnEmptyPdf = true };
        if (emptyWorking)
        {
            await File.WriteAllBytesAsync(WorkingCertificationPath(run), []);
        }
        var snapshot = await PublicationSnapshot.CaptureAsync(run, store);
        var service = new ReportAttestationService(broker, store, settings,
            reports: emptyWorking ? null : new Lazy<IReportService>(() => reports));

        var result = await service.AttestAsync(run, ReportKinds.Certification, skipSigning: presence);

        Assert.False(result.Succeeded);
        Assert.Contains(emptyWorking ? "missing" : "no bytes", result.Message, StringComparison.OrdinalIgnoreCase);
        await snapshot.AssertUnchangedAsync(run, store);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Attestation_evidence_tracks_highest_revision_after_backwards_or_equal_clock(int secondOffsetDays)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var clock = new FakeClock(DateTimeOffset.UnixEpoch.AddDays(10));
        var reports = new RecordingReportService { CompiledTitleSuffix = "first issue" };
        var service = new ReportAttestationService(broker, store, new AppSettings(), clock,
            new Lazy<IReportService>(() => reports));
        var first = await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin);
        Assert.True(first.Succeeded);
        var firstPath = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification)!;
        var firstBytes = await File.ReadAllBytesAsync(firstPath);
        var firstMetadata = JsonSerializer.Serialize(first.Attestation, AppJsonContext.Default.ReportAttestation);
        clock.Advance(TimeSpan.FromDays(secondOffsetDays));
        reports.CompiledTitleSuffix = "second issue";

        var second = await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin);

        Assert.True(second.Succeeded);
        Assert.NotEqual(first.Attestation!.PdfSha256, second.Attestation!.PdfSha256);
        var selected = second.Attestation;
        var evidence = ReportAttestationService.Find(run, ReportKinds.Certification);
        Assert.Equal(selected!.PdfSha256, evidence?.PdfSha256);
        Assert.Equal(selected.SidecarPath, evidence?.SidecarPath);
        var selectedPath = run.Reports.Last().PdfPath;
        Assert.Equal(selectedPath, ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification));
        Assert.Equal(selectedPath, ReportAttestationService.ResolvePrintOrExportPdfPath(run, WorkingCertificationPath(run)));
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
        Assert.Equal(2, run.Attestations.Count);
        Assert.Equal(2, run.Reports.Count(a => ReportArtifactRoles.IsIssued(a.Role)));
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(firstPath));
        Assert.Equal(firstMetadata, JsonSerializer.Serialize(run.Attestations[0], AppJsonContext.Default.ReportAttestation));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    public async Task Identical_pdf_revisions_select_matching_identity_and_sidecar_despite_clock_changes(int secondOffsetDays, bool presence)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var clock = new FakeClock(DateTimeOffset.UnixEpoch.AddDays(10));
        var broker = new MockOperatorCredentialBroker(clock);
        var service = new ReportAttestationService(broker, store,
            new AppSettings { AllowPresenceInLieuOfSigning = true }, clock);
        var firstIdentity = new OperatorCredential
        {
            DisplayName = "First Certifier",
            Serial = MockOperatorCredentialBroker.MockSerial,
            Thumbprint = "mock-thumbprint",
            CapturedAt = clock.UtcNow,
        };
        var first = await service.AttestAsync(run, ReportKinds.Certification, firstIdentity, skipSigning: presence);
        Assert.True(first.Succeeded);
        var firstPath = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification)!;
        var firstBytes = await File.ReadAllBytesAsync(firstPath);
        var firstSidecarBytes = await File.ReadAllBytesAsync(first.Attestation!.SidecarPath!);
        clock.Advance(TimeSpan.FromDays(secondOffsetDays));
        var secondIdentity = new OperatorCredential
        {
            DisplayName = "Second Certifier",
            Serial = MockOperatorCredentialBroker.MockSerial,
            Thumbprint = "mock-thumbprint",
            CapturedAt = clock.UtcNow,
        };

        var second = await service.AttestAsync(run, ReportKinds.Certification, secondIdentity, skipSigning: presence);

        Assert.True(second.Succeeded);
        Assert.Equal(first.Attestation.PdfSha256, second.Attestation!.PdfSha256);
        Assert.NotEqual(first.Attestation.SidecarPath, second.Attestation.SidecarPath);
        var secondPath = run.Reports.Last().PdfPath;
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(secondPath));
        var expected = second.Attestation;
        var expectedPath = secondPath;
        var evidence = ReportAttestationService.Find(run, ReportKinds.Certification);
        Assert.NotNull(evidence);
        Assert.Equal(expected.DisplayName, evidence.DisplayName);
        Assert.Equal(expected.SidecarPath, evidence.SidecarPath);
        Assert.Equal(Path.ChangeExtension(expectedPath, ".attestation.json"), evidence.SidecarPath);
        var selectedSidecar = JsonSerializer.Deserialize(
            await File.ReadAllBytesAsync(evidence.SidecarPath!), AppJsonContext.Default.ReportAttestationSidecar)!;
        Assert.Equal(expected.DisplayName, selectedSidecar.Attestation.DisplayName);
        Assert.Equal(expectedPath, ReportAttestationService.ResolvePrintOrExportPdfPath(run, WorkingCertificationPath(run)));
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(firstPath));
        Assert.Equal(firstSidecarBytes, await File.ReadAllBytesAsync(first.Attestation.SidecarPath!));
        Assert.Equal(2, run.Attestations.Count);
        if (presence)
        {
            Assert.Null(selectedSidecar.SignatureBase64);
        }
        else
        {
            var payload = Encoding.UTF8.GetBytes($"{expected.PdfSha256}:{expected.RunJsonSha256}");
            Assert.True(MockOperatorCredentialBroker.VerifyMockSignature(payload,
                Convert.FromBase64String(selectedSidecar.SignatureBase64!)));
            var other = first.Attestation;
            var otherSidecar = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(other.SidecarPath!),
                AppJsonContext.Default.ReportAttestationSidecar)!;
            Assert.NotEqual(otherSidecar.SignatureBase64, selectedSidecar.SignatureBase64);
        }
        // A matching hash from another issuance cannot replace this issue's evidence.
        evidence.SidecarPath = first.Attestation.SidecarPath;
        Assert.Null(ReportAttestationService.Find(run, ReportKinds.Certification));
        Assert.False(service.HasValidAttestation(run, ReportKinds.Certification));
    }

    [Fact]
    public async Task Attest_reissue_keeps_prior_physical_issue_and_working_bytes_immutable()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        using var broker = new SoftwarePdfBroker();
        var service = new ReportAttestationService(broker, store, new AppSettings());
        Assert.True((await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin)).Succeeded);
        var prior = await PublicationSnapshot.CaptureAsync(run, store);
        var priorAttestationMetadata = JsonSerializer.Serialize(Assert.Single(run.Attestations),
            AppJsonContext.Default.ReportAttestation);
        var priorReport = Assert.Single(run.Reports, r => ReportArtifactRoles.IsIssued(r.Role));
        var priorReportMetadata = JsonSerializer.Serialize(priorReport);

        Assert.True((await service.AttestAsync(run, ReportKinds.Certification, pin: SoftwarePdfBroker.Pin)).Succeeded);

        Assert.NotEqual(prior.IssuedPath, ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification));
        Assert.Equal(prior.WorkingBytes, await File.ReadAllBytesAsync(prior.WorkingPath));
        Assert.Equal(prior.IssuedBytes, await File.ReadAllBytesAsync(prior.IssuedPath));
        Assert.Equal(prior.SidecarBytes, await File.ReadAllBytesAsync(prior.SidecarPath));
        Assert.Equal(2, run.Attestations.Count);
        Assert.Equal(2, run.Reports.Count(r => ReportArtifactRoles.IsIssued(r.Role)));
        var historicAttestation = Assert.Single(run.Attestations, a => a.SidecarPath == prior.SidecarPath);
        Assert.Equal(priorAttestationMetadata, JsonSerializer.Serialize(historicAttestation,
            AppJsonContext.Default.ReportAttestation));
        var historicReport = Assert.Single(run.Reports, r => r.PdfPath == prior.IssuedPath);
        Assert.Equal(priorReportMetadata, JsonSerializer.Serialize(historicReport));
        var newestPath = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification)!;
        Assert.Equal(newestPath, ReportAttestationService.ResolvePrintOrExportPdfPath(run, prior.WorkingPath));
        Assert.Equal(prior.IssuedPath, ReportAttestationService.ResolvePrintOrExportPdfPath(run, prior.IssuedPath));
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification, priorReport.RevisionId));
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
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

    [Fact]
    public async Task HasValidAttestation_true_when_working_pdf_changes_after_issue()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var workingPath = WorkingCertificationPath(run);
        var original = await File.ReadAllBytesAsync(workingPath);
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: true),
            store,
            new AppSettings
            {
                RequireAttestationBeforeExport = true,
                AllowPresenceInLieuOfSigning = true,
            });
        var result = await service.AttestAsync(run, ReportKinds.Certification);
        Assert.True(result.Succeeded);
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));

        await File.WriteAllBytesAsync(workingPath, "%PDF-working-changed"u8.ToArray());
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
        Assert.NotEqual(original, await File.ReadAllBytesAsync(workingPath));
        var issuedPath = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification);
        Assert.False(string.IsNullOrWhiteSpace(issuedPath));
        Assert.True(File.Exists(issuedPath));
    }

    [Fact]
    public async Task Attest_restamps_pdf_with_badge_before_hashing()
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store, includeStatus: true);
        var originalCertBytes = await File.ReadAllBytesAsync(WorkingCertificationPath(run));
        var reports = new RecordingReportService();
        var service = new ReportAttestationService(
            new MockOperatorCredentialBroker(canSign: true),
            store,
            new AppSettings { RequireAttestationBeforeExport = true },
            reports: new Lazy<IReportService>(() => reports));

        var result = await service.AttestAsync(run, ReportKinds.Certification);

        Assert.True(result.Succeeded);
        Assert.Equal(1, reports.GenerateCount);
        Assert.Equal(MockOperatorCredentialBroker.MockDisplayName, reports.LastIdentity?.DisplayName);
        Assert.Equal(ReportKinds.Certification, reports.LastIdentity?.ReportKind);
        Assert.Equal(AttestationKind.Signed, reports.LastIdentity?.Kind);
        Assert.Contains(run.Reports, r => r.Kind == ReportKinds.Status && ReportArtifactRoles.IsWorking(r.Role));
        Assert.True(service.HasValidAttestation(run, ReportKinds.Certification));
        Assert.Equal(originalCertBytes, await File.ReadAllBytesAsync(WorkingCertificationPath(run)));
        var issuedPath = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification);
        Assert.False(string.IsNullOrWhiteSpace(issuedPath));
        var stamped = await File.ReadAllBytesAsync(issuedPath!);
        Assert.NotEqual(originalCertBytes, stamped);
        Assert.Contains(MockOperatorCredentialBroker.MockDisplayName, Encoding.UTF8.GetString(stamped), StringComparison.Ordinal);
        Assert.True(File.Exists(issuedPath));
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(4, true)]
    public async Task AttestAsync_rejected_run_preserves_working_pdf_and_stamps(int version, bool readOnly)
    {
        using var temp = new TempDataDirectory();
        var store = new FileRunStore(temp.RunsDirectory);
        var run = await SeedCertificationRunAsync(store);
        var pdfPath = WorkingCertificationPath(run);
        var priorPdf = await File.ReadAllBytesAsync(pdfPath);
        var dir = store.GetRunDirectory(run.RunId);
        var sidecar = Path.Combine(dir, "certification.attestation.json");
        await File.WriteAllTextAsync(sidecar, "frozen stamp");
        run.Attestations.Add(new ReportAttestation { ReportKind = ReportKinds.Certification, SidecarPath = sidecar });
        run.SchemaVersion = version;
        run.IsSchemaReadOnly = readOnly;
        var service = new ReportAttestationService(new MockOperatorCredentialBroker(), store, new AppSettings());

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => service.AttestAsync(run, ReportKinds.Certification));

        Assert.Equal(priorPdf, await File.ReadAllBytesAsync(pdfPath));
        Assert.Equal("frozen stamp", await File.ReadAllTextAsync(sidecar));
        Assert.Null(ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification));
        Assert.Single(run.Attestations);
    }

    private static string WorkingCertificationPath(TestRunRecord run)
    {
        var path = ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Certification);
        Assert.False(string.IsNullOrWhiteSpace(path));
        return path!;
    }

    private static async Task<TestRunRecord> SeedCertificationRunAsync(
        FileRunStore store,
        bool includeStatus = false)
    {
        var run = new TestRunRecord
        {
            SchemaVersion = SchemaVersions.TestRunRecord,
            RunId = "attest-" + Guid.NewGuid().ToString("N")[..8],
            PlanName = "Cert",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
        };
        await store.SaveAsync(run);
        var dir = store.GetRunDirectory(run.RunId);
        if (includeStatus)
        {
            var statusPdf = Path.Combine(dir, "status.pdf");
            await File.WriteAllBytesAsync(statusPdf, "%PDF-1.4 status"u8.ToArray());
            run.Reports.Add(new RunReportArtifact
            {
                Role = ReportArtifactRoles.Working,
                Kind = ReportKinds.Status,
                Title = "Status Report",
                PdfPath = statusPdf,
                GeneratedAt = DateTimeOffset.UtcNow,
            });
        }
        var pdf = Path.Combine(dir, "certification.pdf");
        await File.WriteAllBytesAsync(pdf, PdfTestFixture.CreateMinimalPdf());
        run.Reports.Add(new RunReportArtifact
        {
            Role = ReportArtifactRoles.Working,
            Kind = ReportKinds.Certification,
            Title = "Certification Report",
            PdfPath = pdf,
            GeneratedAt = DateTimeOffset.UtcNow,
        });
        await store.SaveAsync(run);
        return run;
    }
}

file sealed class ThrowingPcscDisposable : IDisposable
{
    public bool DisposeAttempted { get; private set; }

    public void Dispose()
    {
        DisposeAttempted = true;
        throw new PCSCException(SCardError.RemovedCard);
    }
}

/// Rewrites the certification PDF with the overlay identity so Attest can hash stamped bytes.
internal sealed class RecordingReportService : IReportService
{
    public int GenerateCount { get; private set; }
    public ReportAttestation? LastIdentity { get; private set; }
    public bool ReturnEmptyPdf { get; set; }
    public string CompiledTitleSuffix { get; set; } = string.Empty;

    public Task<string> GeneratePdfAsync(TestRunRecord run, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<RunReportArtifact>> GenerateReportsAsync(
        TestRunRecord run,
        IReadOnlyList<string> kinds,
        DutHistoryReport? history = null,
        CancellationToken cancellationToken = default,
        ReportAttestation? compileIdentity = null)
    {
        GenerateCount++;
        LastIdentity = compileIdentity;
        var artifacts = new List<RunReportArtifact>();
        foreach (var kind in kinds)
        {
            var existing = run.Reports.FirstOrDefault(r =>
                string.Equals(r.Kind, kind, StringComparison.OrdinalIgnoreCase));
            if (existing is null || string.IsNullOrWhiteSpace(existing.PdfPath))
            {
                throw new InvalidOperationException($"Missing PDF for {kind}.");
            }

            var stamp = compileIdentity?.DisplayName ?? "unsigned";
            File.WriteAllBytes(existing.PdfPath, PdfTestFixture.CreateMinimalPdf(stamp));
            artifacts.Add(existing);
        }

        return Task.FromResult((IReadOnlyList<RunReportArtifact>)artifacts);
    }

    public Task<string> GenerateSuitePdfAsync(SuiteRunRecord suiteRun, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<byte[]> CompileTemplateAsync(TestRunRecord run, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public Task<byte[]> CompileReportAsync(
        TestRunRecord run,
        string kind,
        CancellationToken cancellationToken = default,
        ReportAttestation? compileIdentity = null)
    {
        GenerateCount++;
        LastIdentity = compileIdentity;
        _ = run;
        _ = kind;
        var stamp = compileIdentity?.DisplayName ?? "unsigned";
        return Task.FromResult(ReturnEmptyPdf ? Array.Empty<byte>() : PdfTestFixture.CreateMinimalPdf(stamp + CompiledTitleSuffix));
    }
}

internal sealed class UnavailableEmbeddedBroker : IOperatorCredentialBroker, IEmbeddedPdfSigningBroker
{
    private static readonly OperatorCredential Credential = new()
    {
        DisplayName = "Presence Only",
        Serial = "PRESENCE-1",
        Transport = CredentialTransport.Contact,
        Thumbprint = "001122",
        CapturedAt = DateTimeOffset.UtcNow,
    };

    public bool IsMock => false;
    public bool CanSign => true;
    public bool CanSignPdf => true;
    public string? SigningAlgorithm => null;
    public string StatusText => "Signing unavailable";

    public Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        _ = timeout;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CredentialCaptureResult { Credential = Credential });
    }

    public Task<CredentialSignResult> TrySignPayloadAsync(
        byte[] payload,
        OperatorCredential credential,
        string? pin = null,
        CancellationToken cancellationToken = default)
    {
        _ = payload;
        _ = credential;
        _ = pin;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CredentialSignResult.Unavailable("No PIV signing certificate is available."));
    }

    public Task<CredentialSignResult> TrySignPdfAsync(
        byte[] pdf,
        OperatorCredential credential,
        string? pin = null,
        DateTimeOffset? signingTime = null,
        CancellationToken cancellationToken = default)
    {
        _ = pdf;
        _ = credential;
        _ = pin;
        _ = signingTime;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CredentialSignResult.Unavailable("No PIV signing certificate is available."));
    }
}


/// Physical embedded signing fixture: software key, production iText, no card/APDU I/O.
file sealed class SoftwarePdfBroker : IOperatorCredentialBroker, IEmbeddedPdfSigningBroker, IDisposable
{
    public const string Pin = "123456";
    public const string DisplayName = "Software Certifier";
    private readonly RSA _key = RSA.Create(2048);
    private readonly RSA _otherKey = RSA.Create(2048);
    private readonly X509Certificate2 _certificate;
    private readonly X509Certificate2 _otherCertificate;
    public string Mode { get; set; } = "success";
    public OperatorCredential Credential { get; }

    public SoftwarePdfBroker()
    {
        _certificate = CreateCertificate(_key);
        _otherCertificate = CreateCertificate(_otherKey);
        Credential = Identity(_certificate.Thumbprint);
    }

    private static X509Certificate2 CreateCertificate(RSA key)
    {
        var request = new CertificateRequest("CN=" + DisplayName, key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
    }

    private static OperatorCredential Identity(string? thumbprint, string serial = "SOFTWARE-1") => new()
    {
        DisplayName = DisplayName,
        Serial = serial,
        Transport = CredentialTransport.Contact,
        Thumbprint = thumbprint,
        CapturedAt = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero),
    };

    public bool IsMock => false;
    public bool CanSign => true;
    public bool CanSignPdf => true;
    public string? SigningAlgorithm => AttestationAlgorithm.PivRsaPkcs1Sha256;
    public string StatusText => "Software physical signing fixture";

    public Task<CredentialCaptureResult> WaitForPresenceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CredentialCaptureResult { Credential = Credential });
    }

    public Task<CredentialSignResult> TrySignPayloadAsync(byte[] payload, OperatorCredential credential,
        string? pin = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(pin))
            return Task.FromResult(CredentialSignResult.NeedPin("Enter badge PIN to sign."));
        return Task.FromResult(CredentialSignResult.Signed(
            _key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            SigningAlgorithm!, _certificate.RawData, _certificate.Thumbprint, Credential));
    }

    public Task<CredentialSignResult> TrySignPdfAsync(byte[] pdf, OperatorCredential credential,
        string? pin = null, DateTimeOffset? signingTime = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Mode == "cancel")
            throw new OperationCanceledException("Fixture canceled during PDF signing.");
        if (Mode == "failure")
            return Task.FromResult(CredentialSignResult.Failed("Signing failed after PIN."));
        if (Mode == "retry" || pin != Pin)
            return Task.FromResult(CredentialSignResult.Failed("Incorrect PIN; 2 retries remain.", 2));
        var actualCertificate = Mode == "different-signer" ? _otherCertificate : _certificate;
        var actualKey = Mode == "different-signer" ? _otherKey : _key;
        Assert.True(ITextPadesSignature.TrySign(pdf, actualCertificate, actualKey, DisplayName,
            signingTime ?? Credential.CapturedAt, out var signedPdf, out var cms, out var error), error);
        var returnedCredential = Mode switch
        {
            "missing-credential" => null,
            "wrong-serial" => Identity(_certificate.Thumbprint, "OTHER-SERIAL"),
            "wrong-credential-thumbprint" => Identity(_otherCertificate.Thumbprint),
            _ => Credential,
        };
        return Task.FromResult(new CredentialSignResult
        {
            SignedPdf = Mode switch
            {
                "cms-only" => null,
                "missing-pdf" => [],
                "malformed-pdf" => "not a signed PDF"u8.ToArray(),
                "appended-bytes" => signedPdf.Concat("unsigned trailing bytes"u8.ToArray()).ToArray(),
                _ => signedPdf,
            },
            Signature = Mode == "missing-signature" ? null : cms,
            Algorithm = SigningAlgorithm,
            CertificateDer = Mode switch
            {
                "missing-certificate" => null,
                "malformed-certificate" => [0x01],
                "wrong-certificate" => _otherCertificate.RawData,
                _ => _certificate.RawData,
            },
            Thumbprint = Mode switch
            {
                "missing-thumbprint" => null,
                "wrong-thumbprint" => _otherCertificate.Thumbprint,
                _ => _certificate.Thumbprint,
            },
            Credential = returnedCredential,
            // Even a broker that incorrectly marks malformed success as unavailable cannot downgrade.
            PresenceFallbackAllowed = Mode != "success",
        });
    }

    public void Dispose()
    {
        _certificate.Dispose();
        _otherCertificate.Dispose();
        _key.Dispose();
        _otherKey.Dispose();
    }
}

file sealed record PublicationSnapshot(string Metadata, string WorkingPath, byte[] WorkingBytes,
    string IssuedPath, byte[] IssuedBytes, string SidecarPath, byte[] SidecarBytes, byte[] RunJson,
    string[] PublishedFiles)
{
    public static async Task<PublicationSnapshot> CaptureAsync(TestRunRecord run, FileRunStore store)
    {
        var working = ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Certification)!;
        var issued = ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification)!;
        var sidecar = Assert.Single(run.Attestations).SidecarPath!;
        var dir = store.GetRunDirectory(run.RunId);
        return new(JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord),
            working, await File.ReadAllBytesAsync(working), issued, await File.ReadAllBytesAsync(issued),
            sidecar, await File.ReadAllBytesAsync(sidecar), await File.ReadAllBytesAsync(Path.Combine(dir, "run.json")),
            Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
    }

    public async Task AssertUnchangedAsync(TestRunRecord run, FileRunStore store)
    {
        Assert.Equal(Metadata, JsonSerializer.Serialize(run, AppJsonContext.Default.TestRunRecord));
        Assert.Equal(WorkingPath, ReportAttestationService.ResolveWorkingPdfPath(run, ReportKinds.Certification));
        Assert.Equal(IssuedPath, ReportAttestationService.ResolveIssuedPdfPath(run, ReportKinds.Certification));
        Assert.Equal(WorkingBytes, await File.ReadAllBytesAsync(WorkingPath));
        Assert.Equal(IssuedBytes, await File.ReadAllBytesAsync(IssuedPath));
        Assert.Equal(SidecarBytes, await File.ReadAllBytesAsync(SidecarPath));
        var dir = store.GetRunDirectory(run.RunId);
        Assert.Equal(RunJson, await File.ReadAllBytesAsync(Path.Combine(dir, "run.json")));
        Assert.Equal(PublishedFiles,
            Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
    }
}

file sealed class FaultingRunStore(FileRunStore inner) : IRunStore
{
    public bool FailSave { get; set; }
    public bool CancelSave { get; set; }
    public Task SaveAsync(TestRunRecord run, CancellationToken cancellationToken = default)
        => CancelSave ? throw new OperationCanceledException("Fixture canceled at publication.")
            : FailSave ? throw new IOException("Fixture save failure.") : inner.SaveAsync(run, cancellationToken);
    public Task<TestRunRecord?> LoadAsync(string runId, CancellationToken cancellationToken = default)
        => inner.LoadAsync(runId, cancellationToken);
    public Task<IReadOnlyList<TestRunSummary>> ListAsync(CancellationToken cancellationToken = default)
        => inner.ListAsync(cancellationToken);
    public string GetRunDirectory(string runId) => inner.GetRunDirectory(runId);
}
