using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// For other code mods: what FrostStory's steam network says about a position. Every call is cheap: a
/// run is traced once and the answer kept for a few seconds. All of it works on either side, but only
/// the server's answer is authoritative.
///
/// <code>
/// var fed = FrostStory.Steam.SteamApi.Fed(world, pos);      // 0 (cold) to 1 (all it draws)
/// </code>
///
/// To draw steam, a block does not need code: give it the class <c>FrostStorySteamConduit</c> (or
/// <c>FrostStorySteamEngine</c> to turn an axle) and a <c>steamDraw</c> attribute.
/// </summary>
public static class SteamApi
{
    private static SteamNetwork? NetworkOf(IWorldAccessor world) =>
        world.Api.ModLoader.GetModSystem<FrostStoryModSystem>()?.SteamFor(world);

    /// <summary>True when a FrostStory pipe, fitting, port or steam machine occupies this cell.</summary>
    public static bool IsSteamBlock(IWorldAccessor world, BlockPos pos) => SteamNetwork.Resolve(world.BlockAccessor, pos, out _, out _);

    /// <summary>How well the steam block here is fed: 0 is cold, 1 is all the steam it draws.</summary>
    public static float Fed(IWorldAccessor world, BlockPos pos) => NetworkOf(world)?.Ratio(pos) ?? 0f;

    /// <summary>What a gauge would read here: steam that can reach it, what everything beyond draws, and how well that is met.</summary>
    public static (float Available, float Demand, float Ratio) Reading(IWorldAccessor world, BlockPos pos) =>
        NetworkOf(world)?.Reading(pos) ?? (0f, 0f, 0f);

    /// <summary>Steam the block here draws from its line when fully fed.</summary>
    public static float DrawOf(IWorldAccessor world, BlockPos pos) =>
        SteamNetwork.Resolve(world.BlockAccessor, pos, out var conduit, out _) ? conduit.Draw : 0f;

    /// <summary>Call after changing a steam block in a way the network cannot see (an attribute, a setting).</summary>
    public static void Invalidate(IWorldAccessor world) => NetworkOf(world)?.Invalidate();
}
