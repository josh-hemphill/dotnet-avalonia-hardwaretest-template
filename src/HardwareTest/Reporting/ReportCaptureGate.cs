using System.Runtime.CompilerServices;
using HardwareTest.Core.Credentials;

namespace HardwareTest.Reporting;

internal static class ReportCaptureGate
{
    private static readonly ConditionalWeakTable<IReportAttestationService, SemaphoreSlim> Gates = new();
    public const string WaitingMessage = "A previous badge operation is still finishing. Close its prompt, then try again.";

    public static IDisposable? TryEnter(IReportAttestationService service)
    {
        var gate = Gates.GetValue(service, _ => new SemaphoreSlim(1, 1));
        return gate.Wait(0) ? new Lease(gate) : null;
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
