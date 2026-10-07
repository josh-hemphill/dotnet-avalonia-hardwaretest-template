using System.Diagnostics;
using System.Text.Json;

namespace HardwareTest.Authoring;

/// Launches the authoring executable itself, never the operator worker.
public sealed class AuthoringChildProcessRunner
{
    private readonly string executable;
    private readonly object reapGate = new();
    private readonly List<(AuthoringProcessOwnership Ownership, Process Process)> retained = [];
    internal Action? BeforeExitVerification { get; init; }
    public bool HasPendingReap { get { lock (reapGate) return retained.Count > 0; } }
    public async Task ReapPendingAsync()
    {
        (AuthoringProcessOwnership Ownership, Process Process)[] pending;
        lock (reapGate) pending = retained.ToArray();
        foreach (var scope in pending)
        {
            SignalEndOfInput(scope.Process);
            scope.Ownership.Terminate();
            BeforeExitVerification?.Invoke();
            await scope.Ownership.WaitForExitAsync(scope.Process).ConfigureAwait(false);
            lock (reapGate) retained.Remove(scope);
            scope.Ownership.Dispose(); scope.Process.Dispose();
        }
    }
    private readonly IReadOnlyList<string> prefix;
    public AuthoringChildProcessRunner(string executable, IReadOnlyList<string>? arguments = null)
    { this.executable = executable; prefix = arguments?.ToArray() ?? []; }

    public static AuthoringChildProcessRunner ForExecutable(string assemblyPath)
    {
        if (!assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return new(assemblyPath);
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var host = string.IsNullOrWhiteSpace(root) ? "dotnet" : Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return new(host, [assemblyPath]);
    }

    internal async Task<int> RunAsync(string requestPath, Action<AuthoringOperationLog> log,
        Action<AuthoringOperationProgress> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(requestPath)!
        };
        foreach (var argument in prefix) start.ArgumentList.Add(argument);
        start.ArgumentList.Add(AuthoringOperationHost.Switch);
        start.ArgumentList.Add(requestPath);
        var ownership = new AuthoringProcessOwnership(start.WorkingDirectory);
        var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new IOException("Could not start the authoring child."); }
        catch { ownership.Dispose(); process.Dispose(); throw; }
        try { ownership.Attach(process); }
        catch
        {
            // The host cannot spawn work until the parent's gate opens.
            process.Kill();
            await process.WaitForExitAsync().ConfigureAwait(false);
            ownership.Dispose(); process.Dispose();
            throw;
        }
        using var cancellation = cancellationToken.Register(() => _ = Task.Run(() =>
        {
            try { SignalEndOfInput(process); ownership.Terminate(); }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }));
        var reaped = false;
        using var reading = new CancellationTokenSource();
        var stdout = DrainAsync(process.StandardOutput, "stdout", log, reading.Token);
        var stderr = DrainAsync(process.StandardError, "stderr", log, reading.Token);
        var stages = ReadProgressAsync(Path.Combine(start.WorkingDirectory, "progress.json"), progress, reading.Token);
        try
        {
            await WaitForHostFile("host-ready").WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(Path.Combine(start.WorkingDirectory, "host-start"), "");
            await WaitForHostFile("host-exit.json").WaitAsync(TimeSpan.FromMinutes(30), cancellationToken).ConfigureAwait(false);
            var exit = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(Path.Combine(start.WorkingDirectory, "host-exit.json"), cancellationToken).ConfigureAwait(false),
                AuthoringOperationJsonContext.Default.AuthoringChildExit) ?? throw new InvalidDataException("Empty child exit status.");
            progress(new("Reaping operation processes"));
            cancellationToken.ThrowIfCancellationRequested();
            SignalEndOfInput(process);
            ownership.Terminate();
            BeforeExitVerification?.Invoke();
            await ownership.WaitForExitAsync(process).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return exit.ExitCode;
        }
        finally
        {
            try
            {
                SignalEndOfInput(process);
                ownership.Terminate();
                // Reap the owned process before staging cleanup. Cancellation never waits on the UI thread.
                BeforeExitVerification?.Invoke();
                await ownership.WaitForExitAsync(process).ConfigureAwait(false);
                reaped = true;
            }
            catch
            {
                lock (reapGate) retained.Add((ownership, process));
                throw;
            }
            finally
            {
                reading.Cancel();
                try { await Task.WhenAll(stdout, stderr, stages).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
                finally { if (reaped) { ownership.Dispose(); process.Dispose(); } }
            }
        }
        async Task WaitForHostFile(string name)
        {
            var path = Path.Combine(start.WorkingDirectory, name);
            while (!File.Exists(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException($"Authoring ownership host exited unexpectedly ({process.ExitCode}).");
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void SignalEndOfInput(Process process)
    {
        // Cancellation, successful completion and retained retries share the same durable EOF signal.
        // Closing an already closed pipe must not prevent verification of the original owned scope.
        try { process.StandardInput.Close(); }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    private static async Task DrainAsync(StreamReader reader, string stream, Action<AuthoringOperationLog> log, CancellationToken token)
    {
        var buffer = new char[1024];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) return;
            log(new(stream, new string(buffer, 0, count)));
        }
    }

    private static async Task ReadProgressAsync(string path, Action<AuthoringOperationProgress> progress, CancellationToken token)
    {
        string? last = null;
        while (true)
        {
            try
            {
                if (File.Exists(path))
                {
                    var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
                    if (bytes.Length > 4096) throw new InvalidDataException("Oversized child progress.");
                    var next = JsonSerializer.Deserialize(bytes, AuthoringOperationJsonContext.Default.AuthoringOperationProgress);
                    if (next is not null && next.Stage != last) { last = next.Stage; progress(next); }
                }
            }
            catch (IOException) { }
            await Task.Delay(100, token).ConfigureAwait(false);
        }
    }
}
