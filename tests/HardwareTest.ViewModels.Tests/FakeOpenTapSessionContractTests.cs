using HardwareTest.OpenTap.Host;
using HardwareTest.Session.Contracts;
using HardwareTest.ViewModels.Tests.Fakes;
using Xunit;

namespace HardwareTest.ViewModels.Tests;

public sealed class FakeOpenTapSessionContractTests : OpenTapSessionContractTests
{
    protected override Task<IOpenTapSession> CreateUnloadedSessionAsync()
        => Task.FromResult<IOpenTapSession>(new FakeOpenTapSession(preloadSample: false));

    protected override async Task<IOpenTapSession> CreateLoadedSessionAsync(ContractPlan plan)
    {
        var session = new FakeOpenTapSession(preloadSample: false)
        {
            Delay = TimeSpan.FromMilliseconds(400),
        };

        if (ReferenceEquals(plan, ContractPlan.Simple))
        {
            await session.LoadPlanShapeAsync(PlanShapeFixtures.FlatLeavesName);
        }
        else if (ReferenceEquals(plan, ContractPlan.WithLoop))
        {
            session.EmitLoopProgress = true;
            await session.LoadSweepDemoProgramAsync();
        }
        else if (ReferenceEquals(plan, ContractPlan.WithInteraction))
        {
            session.EmitOperatorInteractionDuringRun = true;
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
            new DutIdentity("DUT-CONTRACT-FAKE", Family: "demo"));
}
