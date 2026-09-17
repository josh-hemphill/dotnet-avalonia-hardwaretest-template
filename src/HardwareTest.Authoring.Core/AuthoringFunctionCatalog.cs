using HardwareTest.OpenTap.Plugins.Basic;
using OpenTap;

namespace HardwareTest.Authoring;

/// Closed FunctionId / AlgorithmId table (not a reflection scan of every OpenTAP Display).
public static class AuthoringFunctionIds
{
    public const string BasicAcquireVoltage = "Basic.AcquireVoltage";
    public const string BasicMeanGte = "Basic.MeanGte";
    public const string BasicPublishBandScalar = "Basic.PublishBandScalar";
    public const string BasicBitSweepAcquire = "Basic.BitSweepAcquire";
    public const string BasicPublishTimedSample = "Basic.PublishTimedSample";
    public const string BasicPublishSeriesCompliance = "Basic.PublishSeriesCompliance";
    public const string BasicRepeatLoop = "Basic.RepeatLoop";
    public const string BasicReportStationHealth = "Basic.ReportStationHealth";
    public const string BasicSafeShutdown = "Basic.SafeShutdown";
    public const string BasicOperatorPrompt = "Basic.OperatorPrompt";
    public const string BasicOperatorInput = "Basic.OperatorInput";
    public const string BasicIdentityCheck = "Basic.IdentityCheck";
    public const string BasicApplyTransferFunction = "Basic.ApplyTransferFunction";
    public const string IcIdentityQuery = "IC.IdentityQuery";
    public const string IcSafeShutdown = "IC.SafeShutdown";
}

public sealed record AuthoringFunctionSpec(
    string Id,
    string Pack,
    string TypeName,
    bool NeedsInstrument,
    bool IsAlgorithm);

public static class AuthoringFunctionCatalog
{
    private static readonly AuthoringFunctionSpec[] Specs =
    [
        new(AuthoringFunctionIds.BasicAcquireVoltage, "HardwareTest Basic", nameof(AcquireVoltageStep), true, false),
        new(AuthoringFunctionIds.BasicMeanGte, "HardwareTest Basic", nameof(MeanGteStep), true, true),
        new(AuthoringFunctionIds.BasicPublishBandScalar, "HardwareTest Basic", nameof(PublishBandScalarStep), false, true),
        new(AuthoringFunctionIds.BasicBitSweepAcquire, "HardwareTest Basic", nameof(BitSweepAcquireStep), true, false),
        new(AuthoringFunctionIds.BasicPublishTimedSample, "HardwareTest Basic", nameof(PublishTimedSampleStep), false, false),
        new(AuthoringFunctionIds.BasicPublishSeriesCompliance, "HardwareTest Basic", nameof(PublishSeriesComplianceStep), false, true),
        new(AuthoringFunctionIds.BasicApplyTransferFunction, "HardwareTest Basic", nameof(ApplyTransferFunctionStep), false, true),
        new(AuthoringFunctionIds.BasicIdentityCheck, "HardwareTest Basic", nameof(IdentityCheckStep), false, false),
        new(AuthoringFunctionIds.BasicReportStationHealth, "HardwareTest Basic", nameof(ReportStationHealthStep), false, false),
        new(AuthoringFunctionIds.IcIdentityQuery, "InstrumentComponents.OpenTap", "IdentityQueryStep", true, false),
        new(AuthoringFunctionIds.IcSafeShutdown, "InstrumentComponents.OpenTap", "SafeShutdownStep", true, false),
    ];

    public static bool TryGet(string id, out AuthoringFunctionSpec spec)
    {
        foreach (var candidate in Specs)
        {
            if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
            {
                spec = candidate;
                return true;
            }
        }

        spec = null!;
        return false;
    }

    public static bool TryGetByStep(ITestStep step, out AuthoringFunctionSpec spec)
    {
        ArgumentNullException.ThrowIfNull(step);
        var typeName = step.GetType().Name;
        foreach (var candidate in Specs)
        {
            if (!string.Equals(candidate.TypeName, typeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (candidate.Id.StartsWith("IC.", StringComparison.Ordinal)
                && !IsInstrumentComponentsType(step.GetType()))
            {
                continue;
            }

            spec = candidate;
            return true;
        }

        spec = null!;
        return false;
    }

    public static ITestStep CreateStep(string functionId)
    {
        if (!TryGet(functionId, out var spec))
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.UnknownFunction}: '{functionId}' is not in the authoring function catalog.");
        }

        ITestStep? created = spec.Id switch
        {
            AuthoringFunctionIds.BasicAcquireVoltage => new AcquireVoltageStep(),
            AuthoringFunctionIds.BasicMeanGte => new MeanGteStep(),
            AuthoringFunctionIds.BasicPublishBandScalar => new PublishBandScalarStep(),
            AuthoringFunctionIds.BasicBitSweepAcquire => new BitSweepAcquireStep(),
            AuthoringFunctionIds.BasicPublishTimedSample => new PublishTimedSampleStep(),
            AuthoringFunctionIds.BasicPublishSeriesCompliance => new PublishSeriesComplianceStep(),
            AuthoringFunctionIds.BasicApplyTransferFunction => new ApplyTransferFunctionStep(),
            AuthoringFunctionIds.BasicIdentityCheck => new IdentityCheckStep(),
            AuthoringFunctionIds.BasicReportStationHealth => new ReportStationHealthStep(),
            _ => TryCreateFromPluginManager(spec.TypeName),
        };

        if (created is null)
        {
            throw new AuthoringWorkspaceException(
                $"{AuthoringCompileCodes.UnknownFunction}: '{functionId}' ({spec.TypeName}) is not loaded.");
        }

        return created;
    }

    private static ITestStep? TryCreateFromPluginManager(string typeName)
    {
        foreach (var type in PluginManager.GetPlugins<ITestStep>())
        {
            if (!string.Equals(type.Name, typeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (Activator.CreateInstance(type) is ITestStep step)
            {
                return step;
            }
        }

        return null;
    }

    private static bool IsInstrumentComponentsType(Type type)
    {
        var ns = type.Namespace ?? string.Empty;
        return string.Equals(ns, "InstrumentComponents.OpenTap", StringComparison.Ordinal)
               || ns.StartsWith("InstrumentComponents.OpenTap.", StringComparison.Ordinal);
    }
}
