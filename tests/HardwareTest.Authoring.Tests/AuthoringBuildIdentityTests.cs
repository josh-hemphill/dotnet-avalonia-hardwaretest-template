using HardwareTest.Authoring;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringBuildIdentityTests : IDisposable
{
    public void Dispose() => AuthoringBuildSnapshotTests.CleanupOwnedFixtures();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Streamed_identity_recheck_detects_same_size_timestamp_replacement_and_added_inputs(bool added)
    {
        var root = AuthoringBuildSnapshotTests.Temp();
        var path = Path.Combine(root, "sdk.dll"); File.WriteAllText(path, "original");
        var timestamp = File.GetLastWriteTimeUtc(path);
        var tree = AuthoringBuildService.CaptureTree(root, "sdk-identity", true, materialize: false);
        Assert.Empty(Assert.Single(tree.Files).Bytes);
        var request = new AuthoringBuildRequest(root, new PackOptions(), [tree], AuthoringBuildService.CaptureEnvironment());
        AuthoringBuildService.Recheck(request);
        if (added) File.WriteAllText(Path.Combine(root, "additional-sdk.dll"), "added");
        else
        {
            File.WriteAllText(path, "changed!"); File.SetLastWriteTimeUtc(path, timestamp);
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        }
        var error = Assert.Throws<AuthoringWorkspaceException>(() => AuthoringBuildService.Recheck(request));
        Assert.Contains("BUILD_INPUT_CHANGED", error.Message);
    }
}
