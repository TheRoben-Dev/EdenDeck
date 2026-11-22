namespace EdenDeck.Core.Library;

public enum ContentKind
{
    Unknown,
    Base,
    Update,
    Dlc,
    Homebrew,
}

/// <summary>A game file found by a backend scanner.</summary>
public sealed record GameEntry
{
    public required string BackendId { get; init; }

    /// <summary>Backend-specific title id (Switch: 16 uppercase hex digits). Empty if unknown.</summary>
    public required string TitleId { get; init; }

    public required string Title { get; init; }
    public required string Path { get; init; }
    public ContentKind Kind { get; init; } = ContentKind.Base;
    public string? Version { get; init; }
    public string? IconPath { get; init; }

    /// <summary>How the title id was determined, for diagnostics ("nca-header", "filename", ...).</summary>
    public string TitleIdSource { get; init; } = "unknown";

    public string GameId => GameIds.Make(BackendId, string.IsNullOrEmpty(TitleId) ? System.IO.Path.GetFileName(Path) : TitleId);
}

public static class GameIds
{
    public static string Make(string backendId, string titleId) => $"{backendId}:{titleId}";

    public static bool TryParse(string gameId, out string backendId, out string titleId)
    {
        var i = gameId.IndexOf(':');
        if (i <= 0 || i == gameId.Length - 1)
        {
            backendId = titleId = "";
            return false;
        }
        backendId = gameId[..i];
        titleId = gameId[(i + 1)..];
        return true;
    }
}

/// <summary>
/// A game in the library: the base file plus any updates/DLC files found for the same title.
/// </summary>
public sealed record LibraryGame(GameEntry Base, IReadOnlyList<GameEntry> Updates, IReadOnlyList<GameEntry> Dlc)
{
    public string GameId => Base.GameId;
}
