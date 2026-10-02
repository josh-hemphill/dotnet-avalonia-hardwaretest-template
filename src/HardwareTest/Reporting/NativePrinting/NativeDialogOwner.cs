using Avalonia.Controls;
using Avalonia.Threading;
using HardwareTest.Core.Credentials;

namespace HardwareTest.Reporting.NativePrinting;

/// <summary>Resolves the current desktop owner lazily on Avalonia's dispatcher.</summary>
public sealed class NativeDialogOwner(Func<Window?> window) : INativeSigningDialogOwner
{
    public async Task<nint> GetWindowHandleAsync(CancellationToken cancellationToken = default)
    {
        try { return await GetHandleAsync(cancellationToken).ConfigureAwait(false); }
        catch (InvalidOperationException) { return 0; }
    }

    public async Task<nint> GetHandleAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = window() ?? throw new InvalidOperationException("No desktop window is available.");
            var handle = owner.TryGetPlatformHandle();
            if (handle is null || handle.Handle == 0 || !string.Equals(handle.HandleDescriptor, "HWND", StringComparison.Ordinal))
                throw new InvalidOperationException("The desktop window has no native Windows handle.");
            return handle.Handle;
        });
    }
}
