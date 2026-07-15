using System.Text.Json;
using HardwareTest.Core.Runs;
using HardwareTest.Core.Serialization;
using HardwareTest.Core.Settings;
using Xunit;

namespace HardwareTest.Tests.Serialization;

public sealed class AppJsonContextTests
{
    [Fact]
    public void TestRunRecord_round_trips()
    {
        var record = new TestRunRecord
        {
            RunId = "abc123",
            PlanName = "Demo",
            StartedAt = DateTimeOffset.UtcNow,
            Result = RunResult.Passed,
            Samples = [new StoredSample { Channel = "VDC", Timestamp = DateTimeOffset.UtcNow, Value = 1.23 }],
        };

        var json = JsonSerializer.Serialize(record, AppJsonContext.Default.TestRunRecord);
        var loaded = JsonSerializer.Deserialize(json, AppJsonContext.Default.TestRunRecord);
        Assert.NotNull(loaded);
        Assert.Equal(record.RunId, loaded!.RunId);
        Assert.Equal(RunResult.Passed, loaded.Result);
        Assert.Single(loaded.Samples);
    }

    [Fact]
    public void AppSettings_and_UiState_round_trip()
    {
        var settings = new AppSettings { DefaultVisaResource = "X", PlotRefreshHz = 12 };
        var ui = new UiState { SelectedPageId = "Home", Width = 800 };

        var sJson = JsonSerializer.Serialize(settings, AppJsonContext.Default.AppSettings);
        var uJson = JsonSerializer.Serialize(ui, AppJsonContext.Default.UiState);
        var s2 = JsonSerializer.Deserialize(sJson, AppJsonContext.Default.AppSettings);
        var u2 = JsonSerializer.Deserialize(uJson, AppJsonContext.Default.UiState);
        Assert.Equal("X", s2!.DefaultVisaResource);
        Assert.Equal("Home", u2!.SelectedPageId);
    }
}
