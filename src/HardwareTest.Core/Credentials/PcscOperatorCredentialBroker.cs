using HardwareTest.Core.Time;

namespace HardwareTest.Core.Credentials;

/// PC/SC contact (chip) and contactless (tap) capture for Windows, Linux, and macOS.
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
    public string? SigningAlgorithm => AttestationAlgorithm.PivRsaPkcs1Sha256;
    public string StatusText { get; private set; } = "PC/SC not queried yet.";

    public async Task<CredentialCaptureResult> WaitForPresenceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!PcscNative.TryLoad(out var loadError))
        {
            StatusText = "PC/SC library not found (install pcscd / winscard).";
            return new CredentialCaptureResult { Error = StatusText + (loadError is null ? string.Empty : " " + loadError) };
        }

        var deadline = _clock.UtcNow + timeout;
        nint context = 0;
        var rc = PcscNative.EstablishContext(out context);
        if (rc != PcscNative.Success)
        {
            StatusText = "PC/SC context failed. Is pcscd running?";
            return new CredentialCaptureResult { Error = StatusText };
        }

        try
        {
            while (_clock.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var captured = TryCaptureOnce(context);
                if (captured.Succeeded)
                {
                    StatusText = $"Credential present ({captured.Credential!.Transport}).";
                    return captured;
                }

                StatusText = captured.Error ?? "Present a badge: insert chip or tap the reader.";
                var remaining = deadline - _clock.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var wait = remaining < _pollInterval ? remaining : _pollInterval;
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            StatusText = "No chip or tap detected before timeout.";
            return new CredentialCaptureResult { Error = StatusText };
        }
        finally
        {
            _ = PcscNative.ReleaseContext(context);
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

        if (!PcscNative.TryLoad(out var loadError))
        {
            return Task.FromResult(CredentialSignResult.Failed(
                loadError ?? "PC/SC library not found (install pcscd / winscard)."));
        }

        nint context = 0;
        if (PcscNative.EstablishContext(out context) != PcscNative.Success)
        {
            return Task.FromResult(CredentialSignResult.Failed("PC/SC context failed. Is pcscd running?"));
        }

        try
        {
            return Task.FromResult(SignOnPresentedCard(context, credential, payload, pin));
        }
        finally
        {
            _ = PcscNative.ReleaseContext(context);
        }
    }

    private CredentialSignResult SignOnPresentedCard(
        nint context,
        OperatorCredential credential,
        byte[] payload,
        string? pin)
    {
        var readers = PcscNative.ListReaders(context);
        IEnumerable<string> ordered = readers;
        if (!string.IsNullOrWhiteSpace(credential.ReaderName)
            && readers.Any(r => string.Equals(r, credential.ReaderName, StringComparison.Ordinal)))
        {
            ordered = readers.OrderBy(r =>
                string.Equals(r, credential.ReaderName, StringComparison.Ordinal) ? 0 : 1);
        }

        CredentialSignResult? last = null;
        var sawMatchingSerial = false;
        foreach (var reader in ordered)
        {
            if (PcscNative.Connect(context, reader, out var card, out var protocol) != PcscNative.Success)
            {
                continue;
            }

            try
            {
                var channel = new PcscApduChannel(card, protocol);
                var (serial, printedName) = PivCardIdentity.TryRead(channel);
                if (string.IsNullOrWhiteSpace(serial))
                {
                    var atr = PcscNative.ReadAtr(card);
                    if (atr is { Length: > 0 })
                    {
                        serial = Convert.ToHexString(atr);
                    }
                }

                if (!CredentialSignBinding.SerialsMatch(credential.Serial, serial))
                {
                    continue;
                }

                sawMatchingSerial = true;
                var result = CredentialSignBinding.SignMatching(
                    channel,
                    payload,
                    pin,
                    credential,
                    serial,
                    printedName);
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
                _ = PcscNative.Disconnect(card);
            }
        }

        if (!sawMatchingSerial)
        {
            return CredentialSignResult.Failed(CredentialSignBinding.SameBadgeRequired);
        }

        return last ?? CredentialSignResult.Failed(CredentialSignBinding.SameBadgeRequired);
    }

    private sealed class PcscApduChannel : IApduChannel
    {
        private readonly nint _card;
        private readonly int _protocol;

        public PcscApduChannel(nint card, int protocol)
        {
            _card = card;
            _protocol = protocol;
        }

        public byte[]? Transmit(byte[] command) => PcscNative.Transmit(_card, _protocol, command);
    }

    private CredentialCaptureResult TryCaptureOnce(nint context)
    {
        var readers = PcscNative.ListReaders(context);
        if (readers.Count == 0)
        {
            return new CredentialCaptureResult { Error = "No smart-card readers. Connect a chip/tap reader." };
        }

        foreach (var reader in readers)
        {
            if (PcscNative.Connect(context, reader, out var card, out var protocol) != PcscNative.Success)
            {
                continue;
            }

            try
            {
                var atr = PcscNative.ReadAtr(card);
                var (serial, printedName) = PivCardIdentity.TryRead(card, protocol);
                if (string.IsNullOrWhiteSpace(serial) && atr is { Length: > 0 })
                {
                    serial = Convert.ToHexString(atr);
                }

                if (string.IsNullOrWhiteSpace(serial))
                {
                    continue;
                }

                var transport = PivCardIdentity.IsContactlessReader(reader)
                    ? CredentialTransport.Contactless
                    : CredentialTransport.Contact;
                var display = string.IsNullOrWhiteSpace(printedName)
                    ? $"Card {serial[..Math.Min(8, serial.Length)]}"
                    : printedName;
                return new CredentialCaptureResult
                {
                    Credential = new OperatorCredential
                    {
                        DisplayName = display,
                        Serial = serial,
                        Transport = transport,
                        ReaderName = reader,
                        CapturedAt = _clock.UtcNow,
                    },
                };
            }
            finally
            {
                _ = PcscNative.Disconnect(card);
            }
        }

        return new CredentialCaptureResult { Error = "Present a badge: insert chip or tap the reader." };
    }
}
