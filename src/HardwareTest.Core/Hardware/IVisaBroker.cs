namespace HardwareTest.Core.Hardware;

/// Process-wide VISA open. Instruments queries and OpenTAP plugin steps share this path.
public interface IVisaBroker
{
    Task<IVisaSession> OpenAsync(string resourceName, CancellationToken cancellationToken = default);
}
