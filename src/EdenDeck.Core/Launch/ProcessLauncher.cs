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
            // SIGTERM first (to the AppImage runtime and the emulator it started) so the emulator
            // can save its config; then kill whatever is left.
            var pids = Descendants(p.Id).Prepend(p.Id).Select(i => i.ToString()).ToList();
            Process.Start("kill", ["-TERM", .. pids])?.WaitForExit(1000);
            if (!p.WaitForExit((int)(grace ?? TimeSpan.FromSeconds(5)).TotalMilliseconds))
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>All descendant process ids of a process (Linux /proc), children first.</summary>
    public static List<int> Descendants(int root)
    {
        var parents = new Dictionary<int, int>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(dir), out var pid))
                {
                    continue;
                }
                try
                {
                    // /proc/<pid>/stat: "pid (comm) state ppid ..."; comm may contain spaces, so split after ')'.
                    var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                    var rest = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                    parents[pid] = int.Parse(rest[1]);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException)
                {
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        var result = new List<int>();
        var queue = new Queue<int>([root]);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var (pid, ppid) in parents)
            {
                if (ppid == cur && !result.Contains(pid))
                {
                    result.Add(pid);
                    queue.Enqueue(pid);
                }
            }
        }
        return result;
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
