using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace FrostStory.Generator;

public class BlockGeneratorConsole : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityGeneratorConsole console)
        {
            return console.OnInteract(byPlayer);
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var help = base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is not BlockEntityGeneratorConsole console) return help;

        WorldInteraction own;
        if (console.IsComplete)
        {
            own = new WorldInteraction
            {
                ActionLangCode = console.IsLit ? "froststory:blockhelp-console-bank" : "froststory:blockhelp-console-fire",
                MouseButton = EnumMouseButton.Right
            };
        }
        else
        {
            var stacks = console.OutstandingStacks();
            if (stacks.Length == 0 && console.IsBuilding) return help;
            own = new WorldInteraction
            {
                ActionLangCode = "froststory:blockhelp-console-deliver",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = stacks.Length > 0 ? stacks : null
            };
        }

        return help == null ? new[] { own } : help.Append(own);
    }
}
