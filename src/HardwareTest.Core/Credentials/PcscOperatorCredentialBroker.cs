using HardwareTest.Core.Time;
using PCSC;
using PCSC.Exceptions;

namespace HardwareTest.Core.Credentials;

/// <summary>PC/SC contact and contactless credential capture backed by pcsc-sharp.</summary>
public sealed class PcscOperatorCredentialBroker : IOperatorCredentialBroker
{
    private readonly IClock _clock;
    private readonly TimeSpan _pollInterval;

    public PcscOperatorCredentialBroker(IClock? clock = null, TimeSpan? pollInterval = null)
    {
        _clock = clock ?? SystemClock.Instance;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public bool IsMock => false;
    public bool CanSign => true;
    public bool ProducesCms => true;
    public string? SigningAlgorithm => AttestationAlgorithm.PivRsaPkcs1Sha256;
    public string StatusText { get; private set; } = "PC/SC not queried yet.";

    public async Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!TryEstablishContext(out var context, out var contextError))
        {
            StatusText = contextError;
            return new CredentialCaptureResult { Error = StatusText };
        }

        try
        {
            var deadline = _clock.UtcNow + timeout;
            var poll = new PivPresenceIdentity.Poll();
            while (_clock.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var completed = poll.Observe(TryCaptureOnce(context), _clock.UtcNow, out var status);
                StatusText = status;
                if (completed is not null)
                {
                    return completed;
                }

                var remaining = deadline - _clock.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                await Task.Delay(remaining < _pollInterval ? remaining : _pollInterval, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (poll.Fallback?.Credential is not null)
            {
                StatusText = $"Credential present ({poll.Fallback.Credential.Transport}).";
                return poll.Fallback;
            }

            StatusText = "No chip or tap detected before timeout.";
            return new CredentialCaptureResult { Error = StatusText };
        }
        finally
        {
            DisposeBestEffort(context);
        }
    }

    public Task<CredentialSignResult> TrySignPayloadAsync(
        byte[] payload,
        OperatorCredential credential,
        string? pin = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (payload.Length == 0)
        {
            return Task.FromResult(CredentialSignResult.Failed("Nothing to sign."));
        }

        return Task.FromResult(WithPresentedCard(
            credential,
            (channel, serial, printedName) => CredentialSignBinding.SignMatching(
                channel,
                payload,
                pin,
                credential,
                serial,
                printedName)));
    }

    public Task<CredentialSignResult> TrySignDocumentAsync(
        byte[] document,
        OperatorCredential credential,
        string? pin = null,
        DateTimeOffset? signingTime = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (document.Length == 0)
        {
            return Task.FromResult(CredentialSignResult.Failed("Nothing to sign."));
        }

        return Task.FromResult(WithPresentedCard(
            credential,
            (channel, serial, printedName) => CredentialSignBinding.SignDocumentMatching(
                channel,
                document,
                pin,
                credential,
                serial,
                printedName,
                signingTime ?? _clock.UtcNow)));
    }

    private CredentialSignResult WithPresentedCard(
        OperatorCredential credential,
        Func<IApduChannel, string?, string?, CredentialSignResult> sign)
    {
        if (!TryEstablishContext(out var context, out var contextError))
        {
            return CredentialSignResult.Failed(contextError);
        }

        try
        {
            var readers = GetReaders(context);
            var ordered = string.IsNullOrWhiteSpace(credential.ReaderName)
                ? readers
                : readers.OrderBy(reader =>
                    string.Equals(reader, credential.ReaderName, StringComparison.Ordinal) ? 0 : 1).ToArray();
            CredentialSignResult? last = null;
            var sawMatchingSerial = false;
            foreach (var readerName in ordered)
            {
                var reader = TryConnect(context, readerName);
                if (reader is null)
                {
                    continue;
                }

                try
                {
                    var channel = new PcscApduChannel(reader);
                    var (serial, printedName) = ReadIdentity(channel, reader);
                    if (!CredentialSignBinding.SerialsMatch(credential.Serial, serial))
                    {
                        continue;
                    }

                    sawMatchingSerial = true;
                    var result = sign(channel, serial, printedName);
                    if (result.Succeeded || result.PinRequired || result.PinRetriesRemaining is not null)
                    {
                        StatusText = result.Succeeded
                            ? $"Signed with {result.Credential?.DisplayName ?? credential.DisplayName}."
                            : (result.Error ?? StatusText);
                        return result;
                    }

                    last = result;
                }
                finally
                {
                    DisposeBestEffort(reader);
                }
            }

            return sawMatchingSerial
                ? last ?? CredentialSignResult.Failed(CredentialSignBinding.SameBadgeRequired)
                : CredentialSignResult.Failed(CredentialSignBinding.SameBadgeRequired);
        }
        finally
        {
            DisposeBestEffort(context);
        }
    }

    private CredentialCaptureResult TryCaptureOnce(ISCardContext context)
    {
        var readers = GetReaders(context);
        if (readers.Count == 0)
        {
            return new CredentialCaptureResult { Error = "No smart-card readers. Connect a chip/tap reader." };
        }

        foreach (var readerName in readers)
        {
            var reader = TryConnect(context, readerName);
            if (reader is null)
            {
                continue;
            }

            try
            {
                var channel = new PcscApduChannel(reader);
                var (serial, printedName) = ReadIdentity(channel, reader);
                var signatureThumbprint = PivCardIdentity.TryReadSignatureCertificateThumbprint(channel);
                if (string.IsNullOrWhiteSpace(serial))
                {
                    continue;
                }

                var transport = PivCardIdentity.IsContactlessReader(readerName)
                    ? CredentialTransport.Contactless
                    : CredentialTransport.Contact;
                return new CredentialCaptureResult
                {
                    Credential = new OperatorCredential
                    {
                        DisplayName = string.IsNullOrWhiteSpace(printedName)
                            ? $"Card {serial[..Math.Min(8, serial.Length)]}"
                            : printedName,
                        Serial = serial,
                        Transport = transport,
                        ReaderName = readerName,
                        Thumbprint = signatureThumbprint,
                        CapturedAt = _clock.UtcNow,
                    },
                };
            }
            finally
            {
                DisposeBestEffort(reader);
            }
        }

        return new CredentialCaptureResult { Error = "Present a badge: insert chip or tap the reader." };
    }

    private static (string? Serial, string? DisplayName) ReadIdentity(
        IApduChannel channel,
        ICardReader reader)
    {
        var identity = PivCardIdentity.TryRead(channel);
        if (!string.IsNullOrWhiteSpace(identity.Serial))
        {
            return identity;
        }

        try
        {
            var atr = reader.GetStatus().GetAtr();
            return atr is { Length: > 0 } ? (Convert.ToHexString(atr), identity.DisplayName) : identity;
        }
        catch (PCSCException)
        {
            return identity;
        }
    }

    private static bool TryEstablishContext(out ISCardContext context, out string error)
    {
        try
        {
            context = ContextFactory.Instance.Establish(SCardScope.User);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is PCSCException or DllNotFoundException or TypeInitializationException)
        {
            context = null!;
            error = $"PC/SC context failed. Is pcscd/winscard available? {ex.Message}";
            return false;
        }
    }

    private static IReadOnlyList<string> GetReaders(ISCardContext context)
    {
        try
        {
            return context.GetReaders() ?? [];
        }
        catch (NoReadersAvailableException)
        {
            return [];
        }
        catch (PCSCException)
        {
            return [];
        }
    }

    private static ICardReader? TryConnect(ISCardContext context, string readerName)
    {
        try
        {
            return context.ConnectReader(readerName, SCardShareMode.Shared, SCardProtocol.Any);
        }
        catch (PCSCException)
        {
            return null;
        }
    }

    internal static void DisposeBestEffort(IDisposable? resource)
    {
        try
        {
            resource?.Dispose();
        }
        catch (PCSCException)
        {
            // A card or reader may disappear between the last operation and disconnect.
        }
    }
}
