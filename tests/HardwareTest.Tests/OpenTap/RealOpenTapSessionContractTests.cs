using HardwareTest.OpenTap.Host;
using HardwareTest.Session.Contracts;
using Xunit;

namespace HardwareTest.Tests.OpenTap;

[Collection("OpenTapSerial")]
public sealed class RealOpenTapSessionContractTests : OpenTapSessionContractTests
{
    protected override Task<IOpenTapSession> CreateUnloadedSessionAsync()
        => Task.FromResult<IOpenTapSession>(new OpenTapSession());

    protected override async Task<IOpenTapSession> CreateLoadedSessionAsync(ContractPlan plan)
    {
        var session = new OpenTapSession();
        if (ReferenceEquals(plan, ContractPlan.Simple))
        {
            await session.LoadPlanShapeAsync(PlanShapeFixtures.FlatLeavesName);
        }
        else if (ReferenceEquals(plan, ContractPlan.WithLoop))
        {
            await session.LoadSweepDemoProgramAsync();
        }
        else if (ReferenceEquals(plan, ContractPlan.WithInteraction))
        {
            await session.LoadSampleProgramAsync();
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(plan), plan.Id, "Unknown contract plan.");
        }

        return session;
    }

    protected override Task ApplyDefaultStationAsync(IOpenTapSession session)
        => session.ApplyStationAndDutAsync(
            new StationProfile(new Dictionary<string, string> { ["dmm"] = "MOCK::INSTR0" }),
            new DutIdentity("DUT-CONTRACT-REAL", Family: "demo"));
}
