namespace EdenDeck.Backends.Eden;

public static class EdenProcess
{
    private static readonly string[] Names = ["eden", "eden-cli"];

    /// <summary>True if an Eden process (Qt or CLI) is running for this user.</summary>
    public static bool IsRunning()
    {
        if (!Directory.Exists("/proc"))
        {
            return false;
        }
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(dir);
            if (name.Length == 0 || !char.IsDigit(name[0]))
            {
                continue;
            }
            try
            {
                var comm = File.ReadAllText(Path.Combine(dir, "comm")).Trim();
                if (Names.Contains(comm))
                {
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return false;
    }
}
