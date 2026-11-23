using EdenDeck.Core.Library;
using Godot;

namespace EdenDeck.App.Ui;

/// <summary>
/// The game's own square icon (read from its control data with the player's keys and cached by
/// the Eden backend), shrunk to 32x32 so it's drawn as chunky pixels on the cartridge label, the
/// same look as the Steam art. Loaded off the main thread; nothing is downloaded.
/// </summary>
public static class GameIcons
{
    public const int Pixels = 32;
    private static readonly Dictionary<string, Task<Image?>> Cache = new(StringComparer.Ordinal);

    /// <summary>The icon for <paramref name="game"/>, or null when there isn't a readable one (no keys, compressed file, …).</summary>
    public static Task<Image?> Load(GameEntry game)
    {
        // Switch and PS3 games carry an icon; other systems keep their generated cartridge label.
        if (Main.Instance.Host is not { } host || string.IsNullOrEmpty(game.TitleId) || host.Backends.All(b => b.Id != game.BackendId))
        {
            return Task.FromResult<Image?>(null);
        }
        // One read per game, shared by the library and the game card (they may ask at the same time).
        if (!Cache.TryGetValue(game.GameId, out var task))
        {
            Cache[game.GameId] = task = Read(game);
        }
        return task;
    }

    private static async Task<Image?> Read(GameEntry game)
    {
        var host = Main.Instance.Host!;
        var bytes = await Task.Run(() =>
        {
            try
            {
                return host.Backend(game.BackendId).IconFor(game) is { } path ? File.ReadAllBytes(path) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        });
        Image? img = null;
        if (bytes != null)
        {
            img = new Image();
            if (img.LoadJpgFromBuffer(bytes) != Error.Ok && img.LoadPngFromBuffer(bytes) != Error.Ok)
            {
                img = null;
            }
            else
            {
                // Wide icons (PS3's ICON0 is 320x176): keep the middle square instead of squashing.
                if (img.GetWidth() != img.GetHeight())
                {
                    var side = Math.Min(img.GetWidth(), img.GetHeight());
                    img = img.GetRegion(new Rect2I((img.GetWidth() - side) / 2, (img.GetHeight() - side) / 2, side, side));
                }
                img.Resize(Pixels, Pixels, Image.Interpolation.Bilinear);
            }
        }
        if (img == null)
        {
            Cache.Remove(game.GameId);  // ask again later (keys may be added)
        }
        return img;
    }
}
