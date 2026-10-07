using System.Text;
using Xunit;

namespace HardwareTest.Authoring.Tests;

public sealed class AuthoringProgressFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ht-progress-sharing-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Atomic_publication_succeeds_while_the_parent_holds_the_previous_record_open()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "progress.json");
        var oldBytes = Encoding.UTF8.GetBytes("{\"stage\":\"Preparing\"}");
        var newBytes = Encoding.UTF8.GetBytes("{\"stage\":\"Validating\"}");
        AuthoringOperationChild.PublishProgress(path, oldBytes);
        using (var reader = AuthoringChildProcessRunner.OpenProgressReadStream(path))
        {
            AuthoringOperationChild.PublishProgress(path, newBytes);
            var observed = new byte[oldBytes.Length];
            await reader.ReadExactlyAsync(observed);
            Assert.Equal(oldBytes, observed);
            Assert.Equal(oldBytes.Length, reader.Length);
            Assert.Equal(-1, reader.ReadByte());
            Assert.Equal(newBytes, await AuthoringChildProcessRunner.ReadProgressBytesAsync(path, CancellationToken.None));
        }
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task Progress_reads_enforce_the_existing_size_bound_and_cancellation()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "progress.json");
        var limit = new byte[4096];
        await File.WriteAllBytesAsync(path, limit);
        Assert.Equal(limit, await AuthoringChildProcessRunner.ReadProgressBytesAsync(path, CancellationToken.None));
        await File.WriteAllBytesAsync(path, new byte[4097]);
        await Assert.ThrowsAsync<InvalidDataException>(() => AuthoringChildProcessRunner.ReadProgressBytesAsync(path, CancellationToken.None));
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("{}"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AuthoringChildProcessRunner.ReadProgressBytesAsync(path, cancelled.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
