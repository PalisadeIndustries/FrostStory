using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FrostStory.Steam;

/// <summary>
/// A steam main stand: set it on the ground and it raises a post with a main fitting on top.
///
/// Set in line with a large port or the end of a steam main, the fitting comes up at that conduit's
/// height and lined up with its bore, however the ground lies, so the main can be run out to it level.
/// With nothing to line up with, the fitting stands at the height of the generator's large ports above
/// the spot it was placed. The stand itself never exists in the world: it turns into posts and a fitting.
/// </summary>
public class BlockSteamMainStand : Block
{
    /// <summary>Tallest stand: blocks from the ground to the underside of the fitting.</summary>
    public const int MaxRise = 40;

    /// <summary>With nothing to line up with, the fitting sits this far above the block the stand was set on:
    /// the height of the generator's large ports above its pad.</summary>
    public const int DefaultRise = 14;

    /// <summary>How far along the ground the stand looks for a main or port to line up with.</summary>
    public const int Reach = 48;

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        return Raise(world, blockSel.Position, byPlayer, out failureCode, out _);
    }

    /// <summary>
    /// Raises a stand whose post starts in <paramref name="ground"/>. On the client this only says whether it
    /// would succeed; the server does the building.
    /// </summary>
    public static bool Raise(IWorldAccessor world, BlockPos ground, IPlayer? byPlayer, out string failureCode, out BlockPos fittingPos)
    {
        failureCode = "";
        var ba = world.BlockAccessor;
        var post = world.GetBlock(new AssetLocation("froststory", "mainpost")) as BlockSteamConduit;
        var fitting = world.GetBlock(new AssetLocation("froststory", "mainjunction")) as BlockSteamConduit;
        fittingPos = FittingPosition(ba, ground);

        if (post == null || fitting == null)
        {
            failureCode = "missingblock";
            return false;
        }

        int rise = fittingPos.Y - ground.Y;
        if (rise < 1 || rise > MaxRise || !ba.IsValidPos(fittingPos.UpCopy(fitting.Size.Y)))
        {
            failureCode = "froststory-stand-height";
            return false;
        }

        // Everything the post and the fitting will fill has to be free, and the player's to build in.
        var cells = new List<BlockPos>();
        var postFoot = new BlockPos(fittingPos.X, ground.Y, fittingPos.Z, ground.dimension);
        for (int y = 0; y < rise; y++) cells.AddRange(post.Cells(postFoot.UpCopy(y)));
        cells.AddRange(fitting.Cells(fittingPos));

        foreach (var cell in cells)
        {
            if (!ba.IsValidPos(cell) || ba.GetChunkAtBlockPos(cell) == null || !ba.GetBlock(cell).IsReplacableBy(fitting))
            {
                failureCode = "notreplaceable";
                return false;
            }

            if (byPlayer != null && !world.Claims.TryAccess(byPlayer, cell, EnumBlockAccessFlags.BuildOrBreak))
            {
                failureCode = "claimed";
                return false;
            }
        }

        if (world.Side != EnumAppSide.Server) return true;

        for (int y = 0; y < rise; y++) ba.SetBlock(post.Id, postFoot.UpCopy(y));
        ba.SetBlock(fitting.Id, fittingPos);
        world.PlaySoundAt(new AssetLocation("game:sounds/block/chute"), fittingPos, 0, byPlayer);
        return true;
    }

    /// <summary>
    /// Where the fitting's own cell goes for a stand set in <paramref name="ground"/>: level and in line with
    /// the nearest large conduit that faces this spot along a compass line, or else straight up.
    /// </summary>
    public static BlockPos FittingPosition(IBlockAccessor ba, BlockPos ground)
    {
        BlockPos? best = null;
        int bestDistance = int.MaxValue;
        var probe = ground.Copy();

        foreach (var face in BlockFacing.HORIZONTALS)
        {
            bool alongX = face.Axis == EnumAxis.X;

            // A two-wide conduit covers this column if its near edge is here or one cell back.
            for (int offset = 0; offset >= -1; offset--)
            {
                for (int distance = 2; distance <= Reach && distance < bestDistance; distance++)
                {
                    for (int up = 2; up <= MaxRise; up++)
                    {
                        probe.Set(
                            ground.X + face.Normali.X * distance + (alongX ? 0 : offset),
                            ground.Y + up,
                            ground.Z + face.Normali.Z * distance + (alongX ? offset : 0));

                        if (ba.GetChunkAtBlockPos(probe) == null) continue;
                        if (!SteamNetwork.Resolve(ba, probe, out var conduit, out var anchor)) continue;
                        if (!conduit.IsBig || conduit.IsStructural || !conduit.ConnectsOn(face.Opposite)) continue;

                        // Line the fitting's bore up with this conduit's: same height, same sideways position.
                        var lined = alongX
                            ? new BlockPos(ground.X, anchor.Y, anchor.Z, ground.dimension)
                            : new BlockPos(anchor.X, anchor.Y, ground.Z, ground.dimension);
                        int sideways = alongX ? ground.Z - anchor.Z : ground.X - anchor.X;
                        if (sideways < 0 || sideways > 1 || anchor.Y <= ground.Y) continue;

                        best = lined;
                        bestDistance = distance;
                        break;
                    }
                }
            }
        }

        return best ?? ground.UpCopy(DefaultRise);
    }
}
