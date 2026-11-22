namespace EdenDeck.Core.Profiles;

/// <summary>
/// Product rule: frame generation only exists to reach 60 fps. 2x only, output capped at 60,
/// and titles that already run at 60 get none. Every frame-gen path goes through this.
/// </summary>
public static class FrameGenPolicy
{
    public const int TargetFps = 60;
    public const int Multiplier = 2;

    public sealed record Decision(bool Enabled, int Multiplier, int TargetFps, string Reason);

    public static Decision Decide(FrameGenMode requested, int? nativeFps)
    {
        if (requested == FrameGenMode.Off)
        {
            return new(false, 1, 0, "off");
        }
        if (nativeFps is >= TargetFps)
        {
            return new(false, 1, 0, $"title already runs at {nativeFps} fps");
        }
        return new(true, Multiplier, TargetFps, nativeFps is null ? "2x, capped at 60 fps" : $"{nativeFps} -> 60 fps (2x)");
    }
}
