using System.Diagnostics;
using Xunit;

namespace HardwareTest.Authoring.Tests;

[Collection("AuthoringOpenTap")]
public sealed class AuthoringCreationOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-ownership-" + Guid.NewGuid().ToString("N"));
    public AuthoringCreationOwnershipTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private AuthoringDocumentDto Document(string id) => AuthoringDocumentDto.FromDraft(
        new AuthoringPlanInitializer().Construct(new PlanInitializationRequest(id) { WorkspaceRoot = _root }).Draft);

    [Theory]
    [InlineData("rail")]
    [InlineData("RAIL")]
    public async Task Overlapping_independent_creators_publish_only_one_case_equivalent_identity(string secondId)
    {
        using var validated = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var firstDocument = Document("rail");
        var secondDocument = Document(secondId);
        var first = Task.Run(() =>
        {
            var validations = 0;
            new AuthoringDocumentStore(_root).CreateNew(firstDocument, validateWorkspace: () =>
            {
                if (++validations != 2) return;
                validated.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
            });
        });
        try
        {
            Assert.True(validated.Wait(TimeSpan.FromSeconds(30)));
            // First is inside its final validation, not merely staged or scheduled.
            var error = await Task.Run(() => Record.Exception(() => new AuthoringDocumentStore(_root).CreateNew(secondDocument)))
                .WaitAsync(TimeSpan.FromSeconds(30));
            Assert.IsType<IOException>(error);
        }
        finally { release.Set(); await first.WaitAsync(TimeSpan.FromSeconds(30)); }
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.Equal("rail", new AuthoringDocumentStore(_root).Load("rail").Document!.PlanId);
    }

    [Fact]
    public async Task Other_identity_can_publish_while_first_identity_is_owned()
    {
        using var validated = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var document = Document("rail");
        var first = Task.Run(() =>
        {
            var validations = 0;
            new AuthoringDocumentStore(_root).CreateNew(document, validateWorkspace: () =>
            {
                if (++validations == 2) { validated.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(30))); }
            });
        });
        try
        {
            Assert.True(validated.Wait(TimeSpan.FromSeconds(30)));
            new AuthoringDocumentStore(_root).CreateNew(Document("other"));
            Assert.True(new AuthoringDocumentStore(_root).Load("other").Exists);
        }
        finally { release.Set(); await first.WaitAsync(TimeSpan.FromSeconds(30)); }
        Assert.Equal(2, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Reentrant_creator_cannot_borrow_the_first_creators_native_mutex()
    {
        var first = Document("rail");
        var second = Document("RAIL");
        var validations = 0;
        new AuthoringDocumentStore(_root).CreateNew(first, validateWorkspace: () =>
        {
            if (++validations == 2)
                Assert.Throws<IOException>(() => new AuthoringDocumentStore(_root).CreateNew(second));
        });
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.True(new AuthoringDocumentStore(_root).Load("rail").Exists);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failure_or_cancellation_releases_ownership_without_removing_external_bytes(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var document = Document("rail");
        var unrelated = Path.Combine(_root, "unrelated.bin");
        byte[] bytes = [0, 255, 13, 10];
        File.WriteAllBytes(unrelated, bytes);
        var validations = 0;
        var error = Record.Exception(() => new AuthoringDocumentStore(_root).CreateNew(document, cancellation.Token, () =>
        {
            if (++validations != 2) return;
            if (cancel) cancellation.Cancel();
            else throw new IOException("Injected final validation failure.");
        }));
        if (cancel) Assert.IsType<OperationCanceledException>(error);
        else Assert.IsType<IOException>(error);
        new AuthoringDocumentStore(_root).CreateNew(Document("RAIL"));
        Assert.Equal(bytes, File.ReadAllBytes(unrelated));
        Assert.Equal(2, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "AuthoringIntegration")]
    public async Task Independent_process_ownership_is_exclusive_and_released_after_exit(bool crash)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "HardwareTest.Authoring.ProcessFixture.dll"));
        start.ArgumentList.Add("--create-held"); start.ArgumentList.Add(_root);
        using var process = Process.Start(start)!;
        try
        {
            Assert.Equal("validated", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Throws<IOException>(() => new AuthoringDocumentStore(_root).CreateNew(Document("RAIL")));
            new AuthoringDocumentStore(_root).CreateNew(Document("other"));
            if (crash) process.Kill(entireProcessTree: true);
            else { await process.StandardInput.WriteLineAsync("release"); await process.StandardInput.FlushAsync(); }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if (crash)
            {
                Assert.False(new AuthoringDocumentStore(_root).Load("rail").Exists);
                new AuthoringDocumentStore(_root).CreateNew(Document("RAIL"));
                Assert.True(new AuthoringDocumentStore(_root).Load("RAIL").Exists);
            }
            else
            {
                Assert.Equal(0, process.ExitCode);
                Assert.Throws<IOException>(() => new AuthoringDocumentStore(_root).CreateNew(Document("RAIL")));
            }
            Assert.True(new AuthoringDocumentStore(_root).Load("other").Exists);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, "authoring-drafts"), "*.authoring.json").Length);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
}
