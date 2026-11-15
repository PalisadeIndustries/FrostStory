using FrostStory.Generator.Blueprint;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Generator;

/// <summary>Admin commands for testing and for worlds where the generator should simply exist.</summary>
public static class GeneratorCommands
{
    public static void Register(ICoreServerAPI api, FrostStoryModSystem mod)
    {
        var parsers = api.ChatCommands.Parsers;
        int stageCount = GeneratorBlueprint.StageIds.Length;

        api.ChatCommands.Create("frostgen")
            .WithDescription("FrostStory generator tools")
            .RequiresPrivilege(Privilege.controlserver)

            .BeginSubCommand("place")
                .WithDescription("Instantly build a generator centred on a position (deck level). Stages 1-7, default all.")
                .WithArgs(parsers.OptionalWorldPosition("centre"), parsers.OptionalIntRange("stages", 1, stageCount, stageCount), parsers.OptionalBool("lit"))
                .HandleWith(args => Place(api, args))
            .EndSubCommand()

            .BeginSubCommand("check")
                .WithDescription("Compare the world against the blueprint at a position.")
                .WithArgs(parsers.OptionalWorldPosition("centre"), parsers.OptionalIntRange("stages", 1, stageCount, stageCount), parsers.OptionalBool("lit"))
                .HandleWith(args => Check(api, args))
            .EndSubCommand()

            .BeginSubCommand("demolish")
                .WithDescription("Remove a generator centred on a position, leaving the open shaft. Its blocks cannot be broken by hand.")
                .WithArgs(parsers.OptionalWorldPosition("centre"))
                .HandleWith(args => Demolish(api, args))
            .EndSubCommand()

            .BeginSubCommand("console")
                .WithDescription("Put a control console at a position. Side is the way its front faces; the generator rises behind it.")
                .WithArgs(parsers.OptionalWorldPosition("position"), parsers.OptionalWordRange("side", "north", "east", "south", "west"))
                .HandleWith(args => PlaceConsole(api, args))
            .EndSubCommand()

            .BeginSubCommand("start")
                .WithDescription("Waive the materials and start building the current stage of a console (looked at, or at a position).")
                .WithArgs(parsers.OptionalWorldPosition("console"))
                .HandleWith(args => WithConsole(api, args, console =>
                    console.StartCurrentStage()
                        ? TextCommandResult.Success($"Construction of stage {console.FinishedStages + 1} started.")
                        : TextCommandResult.Error("Nothing to start: the stage is already under construction or the generator is complete.")))
            .EndSubCommand()

            .BeginSubCommand("advance")
                .WithDescription("Finish the current stage of a console at once (looked at, or at a position).")
                .WithArgs(parsers.OptionalWorldPosition("console"))
                .HandleWith(args => WithConsole(api, args, console =>
                {
                    if (console.IsComplete) return TextCommandResult.Error("This generator is already complete.");
                    return console.CompleteCurrentStage()
                        ? TextCommandResult.Success($"Stage finished ({console.FinishedStages}/{console.StageCount}).")
                        : TextCommandResult.Error("Part of the site is in unloaded chunks.");
                }))
            .EndSubCommand()

            .BeginSubCommand("fire")
                .WithDescription("Fire or bank a finished generator from its console (looked at, or at a position).")
                .WithArgs(parsers.OptionalWorldPosition("console"), parsers.OptionalBool("on"))
                .HandleWith(args => WithConsole(api, args, console =>
                {
                    if (!console.IsComplete) return TextCommandResult.Error("The generator is not finished yet.");
                    bool on = args.Parsers[1].IsMissing ? !console.IsLit : (bool)args[1];
                    console.SetLit(on);
                    return TextCommandResult.Success(on ? "The generator is running." : "The generator is cold.");
                }))
            .EndSubCommand()

            .BeginSubCommand("status")
                .WithDescription("Report the state of a console (looked at, or at a position).")
                .WithArgs(parsers.OptionalWorldPosition("console"))
                .HandleWith(args => WithConsole(api, args, console => TextCommandResult.Success(console.Summary())))
            .EndSubCommand();
    }

    private static BlockPos CentreFrom(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if (!args.Parsers[0].IsMissing && args[0] is Vec3d given) return given.AsBlockPos;
        if (args.Caller.Pos != null) return args.Caller.Pos.AsBlockPos;
        return api.World.DefaultSpawnPosition.AsBlockPos;
    }

    private static int DepthAt(BlockPos centre) => GeneratorBlueprint.ClampDepth(centre.Y - 8);

    private static TextCommandResult Place(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var centre = CentreFrom(api, args);
        int count = (int)args[1];
        bool lit = (bool)args[2];

        if (centre.Y - 8 < GeneratorBlueprint.MinDepth)
            return TextCommandResult.Error($"Centre is too low: the deck must be at y={GeneratorBlueprint.MinDepth + 8} or higher.");

        var stages = GeneratorBlueprint.Build(DepthAt(centre));
        var builder = new GeneratorBuilder(api.World);

        bool complete = true;
        for (int i = 0; i < count; i++) complete &= builder.PlaceStage(centre, stages[i]);
        if (lit && count == stages.Count) builder.SetCoreLit(centre, true);

        string where = $"{centre.X} {centre.Y} {centre.Z}";
        return complete
            ? TextCommandResult.Success($"Built {count} stage(s) at {where}, shaft depth {DepthAt(centre)}.")
            : TextCommandResult.Error($"Built what was loaded at {where}, but part of the site is in unloaded chunks. Stand closer and run it again.");
    }

    private static TextCommandResult Demolish(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var centre = CentreFrom(api, args);
        var stages = GeneratorBlueprint.Build(DepthAt(centre));
        bool complete = new GeneratorBuilder(api.World).Demolish(centre, stages);

        return complete
            ? TextCommandResult.Success($"Generator at {centre.X} {centre.Y} {centre.Z} removed.")
            : TextCommandResult.Error("Removed what was loaded, but part of the site is in unloaded chunks. Stand closer and run it again.");
    }

    private static TextCommandResult Check(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var centre = CentreFrom(api, args);
        int count = (int)args[1];
        bool lit = (bool)args[2];

        var stages = GeneratorBlueprint.Build(DepthAt(centre));
        var builder = new GeneratorBuilder(api.World);
        int mismatches = builder.CountMismatches(centre, stages, count, lit, null, out int cells);

        return TextCommandResult.Success($"Checked {cells} cells against {count} stage(s) at {centre.X} {centre.Y} {centre.Z}: {mismatches} differ.");
    }

    private static TextCommandResult PlaceConsole(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var pos = CentreFrom(api, args);
        string side = args.Parsers[1].IsMissing ? "north" : (string)args[1];

        var block = api.World.GetBlock(new AssetLocation(Blocks.Console + "-" + side));
        if (block == null) return TextCommandResult.Error("The console block is not registered.");
        if (api.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) return TextCommandResult.Error("That position is not loaded.");

        api.World.BlockAccessor.SetBlock(block.Id, pos);
        if (api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGeneratorConsole console)
            return TextCommandResult.Error("The console was placed but has no block entity.");

        var c = console.Center;
        return TextCommandResult.Success($"Console placed at {pos.X} {pos.Y} {pos.Z}; the generator centre is {c.X} {c.Y} {c.Z}.");
    }

    private static TextCommandResult WithConsole(ICoreServerAPI api, TextCommandCallingArgs args, System.Func<BlockEntityGeneratorConsole, TextCommandResult> action)
    {
        BlockPos? pos = !args.Parsers[0].IsMissing && args[0] is Vec3d given
            ? given.AsBlockPos
            : args.Caller.Player?.CurrentBlockSelection?.Position;

        if (pos == null || api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGeneratorConsole console)
            return TextCommandResult.Error("Look at a generator console, or give its position.");
        if (!console.SiteIsDeepEnough) return TextCommandResult.Error("This site is too close to the bottom of the world.");

        return action(console);
    }
}
