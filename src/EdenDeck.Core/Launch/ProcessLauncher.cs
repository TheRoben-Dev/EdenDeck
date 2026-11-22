using System.Diagnostics;

namespace EdenDeck.Core.Launch;

public sealed class ProcessLauncher
{
    private readonly Action<string> _log;

    public ProcessLauncher(Action<string>? log = null) => _log = log ?? (_ => { });

    public Process Start(LaunchPlan plan)
    {
        foreach (var step in plan.Prepare)
        {
            step();
        }
        var psi = new ProcessStartInfo(plan.Executable)
        {
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
        };
        foreach (var a in plan.Arguments)
        {
            psi.ArgumentList.Add(a);
        }
        foreach (var (k, v) in plan.Environment)
        {
            psi.Environment[k] = v;
        }
        foreach (var n in plan.Notes)
        {
            _log(n);
        }
        _log($"exec: {plan.Executable} {string.Join(' ', plan.Arguments.Select(Quote))} (cwd {plan.WorkingDirectory})");
        try
        {
            return Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
        }
        catch
        {
            RunCleanup(plan);
            throw;
        }
    }

    /// <summary>Starts the plan, waits for exit, always runs cleanup. Returns the exit code.</summary>
    public async Task<int> RunAsync(LaunchPlan plan, CancellationToken ct = default)
    {
        using var p = Start(plan);
        try
        {
            await p.WaitForExitAsync(ct);
            return p.ExitCode;
        }
        catch (OperationCanceledException)
        {
            Terminate(p);
            throw;
        }
        finally
        {
            RunCleanup(plan);
        }
    }

    public static void Terminate(Process p, TimeSpan? grace = null)
    {
        if (p.HasExited)
        {
            return;
        }
        try
        {
            // SIGTERM first so the emulator can save its config; then kill.
            Process.Start("kill", ["-TERM", p.Id.ToString()])?.WaitForExit(1000);
            if (!p.WaitForExit((int)(grace ?? TimeSpan.FromSeconds(5)).TotalMilliseconds))
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void RunCleanup(LaunchPlan plan)
    {
        foreach (var step in plan.Cleanup)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                _log($"cleanup failed: {e.Message}");
            }
        }
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;
}
