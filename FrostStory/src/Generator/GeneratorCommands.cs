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
                        ? TextCommandResult.Success($"Materials for stage {console.FinishedStages + 1} waived; its pieces will go up in turn.")
                        : TextCommandResult.Error("Nothing to start: the generator is complete.")))
            .EndSubCommand()

            .BeginSubCommand("deliver")
                .WithDescription("Credit a console's current stage with materials: which requirement (1 is the first listed), how many, then the console's position if you are not looking at it.")
                .WithArgs(parsers.Int("requirement"), parsers.Int("amount"), parsers.OptionalWorldPosition("console"))
                .HandleWith(args => WithConsole(api, args, console =>
                {
                    int now = console.Credit((int)args[0], (int)args[1]);
                    return now < 0
                        ? TextCommandResult.Error("The current stage has no such requirement.")
                        : TextCommandResult.Success($"Requirement {(int)args[0]} now stands at {now}. {console.Summary()}");
                }, 2))
            .EndSubCommand()

            .BeginSubCommand("piece")
                .WithDescription("Put up the next piece of a console's current stage at once (looked at, or at a position).")
                .WithArgs(parsers.OptionalWorldPosition("console"))
                .HandleWith(args => WithConsole(api, args, console =>
                {
                    if (console.IsComplete) return TextCommandResult.Error("This generator is already complete.");
                    return console.CompleteCurrentPiece()
                        ? TextCommandResult.Success(console.Summary())
                        : TextCommandResult.Error("Part of the site is in unloaded chunks.");
                }))
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

            .BeginSubCommand("run")
                .WithDescription("String a pipe run between two fittings or ports, or take down the one that is there. Add 'true' for a steam main.")
                .WithArgs(parsers.WorldPosition("from"), parsers.WorldPosition("to"), parsers.OptionalBool("main"))
                .HandleWith(args =>
                {
                    var from = ((Vec3d)args[0]).AsBlockPos;
                    var to = ((Vec3d)args[1]).AsBlockPos;
                    string key = FrostStory.Steam.SteamRuns.Toggle(api.World, from, to, (bool)args[2], int.MaxValue, out int cost, out bool built);
                    string text = Vintagestory.API.Config.Lang.Get(key, System.Math.Abs(cost));
                    return built || cost < 0 ? TextCommandResult.Success(text) : TextCommandResult.Error(text);
                })
            .EndSubCommand()

            .BeginSubCommand("cloth")
                .WithDescription("Stretch a tent cloth panel from one tent rod to another, or take down the one that is there. Add 'true' for a triangle to the nearer end of the other pole.")
                .WithArgs(parsers.WorldPosition("rod"), parsers.WorldPosition("otherRod"), parsers.OptionalBool("triangle"))
                .HandleWith(args =>
                {
                    var rod = ((Vec3d)args[0]).AsBlockPos;
                    var other = ((Vec3d)args[1]).AsBlockPos;
                    if (api.World.BlockAccessor.GetBlock(other).FirstCodePart() != "tentrod") return TextCommandResult.Error("The second position is not a tent rod.");
                    var edge = (bool)args[2]
                        ? FrostStory.Tent.TentPanels.RodEnd(api.World.BlockAccessor, other, rod.ToVec3d().Add(0.5, 0.5, 0.5))
                        : FrostStory.Tent.TentPanels.RodEdge(api.World.BlockAccessor, other);
                    string key = FrostStory.Tent.TentPanels.Toggle(api.World, rod, other, edge, int.MaxValue, out int cost, out bool built);
                    string text = Vintagestory.API.Config.Lang.Get(key, System.Math.Abs(cost));
                    return built || cost < 0 ? TextCommandResult.Success(text) : TextCommandResult.Error(text);
                })
            .EndSubCommand()

            .BeginSubCommand("coal")
                .WithDescription("Report on a generator console, coal heap or steam borer at a position. Optionally add coal to it and let that many hours pass for it.")
                .WithArgs(parsers.WorldPosition("position"), parsers.OptionalInt("add"), parsers.OptionalFloat("hours"))
                .HandleWith(args =>
                {
                    var be = api.World.BlockAccessor.GetBlockEntity(((Vec3d)args[0]).AsBlockPos);
                    int add = args.Parsers[1].IsMissing ? 0 : (int)args[1];
                    double hours = args.Parsers[2].IsMissing ? 0 : (float)args[2];
                    switch (be)
                    {
                        case BlockEntityGeneratorConsole console:
                            console.AddCoal(add);
                            console.AdvanceFuel(hours);
                            return TextCommandResult.Success($"Bunker {console.Coal:0.#}. {(console.IsLit ? $"Running: sending {console.SteamOut():0.#} steam, burning {console.BurnPerHour:0.#} an hour." : "Not lit.")}");
                        case FrostStory.Coal.BlockEntityCoalHeap heap:
                            heap.Add(add);
                            return TextCommandResult.Success($"Heap holds {heap.Count}.");
                        case FrostStory.Coal.BlockEntityBorer borer:
                            if (borer.OreCount < 0) borer.Survey();
                            borer.Advance(hours);
                            return TextCommandResult.Success(borer.Summary());
                        default:
                            return TextCommandResult.Error("There is no generator console, coal heap or steam borer there.");
                    }
                })
            .EndSubCommand()

            .BeginSubCommand("valve")
                .WithDescription("Set a stop valve: 0 is shut, 8 fully open.")
                .WithArgs(parsers.WorldPosition("position"), parsers.IntRange("setting", 0, 8))
                .HandleWith(args =>
                {
                    if (api.World.BlockAccessor.GetBlockEntity(((Vec3d)args[0]).AsBlockPos) is not FrostStory.Steam.BlockEntitySteamValve valve)
                        return TextCommandResult.Error("There is no stop valve there.");
                    valve.Set((int)args[1]);
                    return TextCommandResult.Success(valve.Describe());
                })
            .EndSubCommand()

            .BeginSubCommand("stove")
                .WithDescription("Report on a tent stove; with a number, add that many seconds of fuel and light it.")
                .WithArgs(parsers.WorldPosition("position"), parsers.OptionalFloat("fuelSeconds"))
                .HandleWith(args =>
                {
                    if (api.World.BlockAccessor.GetBlockEntity(((Vec3d)args[0]).AsBlockPos) is not FrostStory.Stove.BlockEntityTentStove stove)
                        return TextCommandResult.Error("There is no tent stove there.");
                    string lit = "";
                    if (!args.Parsers[1].IsMissing)
                    {
                        stove.AddFuelSeconds((float)args[1]);
                        string? problem = stove.TryLight();
                        lit = problem == null ? "Lit. " : "Not lit: " + Vintagestory.API.Config.Lang.Get("froststory:" + problem) + " ";
                    }

                    return TextCommandResult.Success(lit + stove.Summary());
                })
            .EndSubCommand()

            .BeginSubCommand("stand")
                .WithDescription("Raise a steam main stand with its post starting at a position (or where you stand). Add 'true' for a steam hub on its tower.")
                .WithArgs(parsers.OptionalWorldPosition("position"), parsers.OptionalBool("hub"))
                .HandleWith(args =>
                {
                    var ground = CentreFrom(api, args);
                    bool hub = !args.Parsers[1].IsMissing && (bool)args[1];
                    string why;
                    BlockPos fitting;
                    return (hub ? FrostStory.Steam.BlockSteamMainStand.RaiseHub(api.World, ground, null, out why, out fitting)
                                : FrostStory.Steam.BlockSteamMainStand.Raise(api.World, ground, null, out why, out fitting))
                        ? TextCommandResult.Success($"Stand raised: fitting at {fitting.X} {fitting.Y} {fitting.Z}, {fitting.Y - ground.Y} blocks up.")
                        : TextCommandResult.Error($"Could not raise a stand there ({why}).");
                })
            .EndSubCommand()

            .BeginSubCommand("steam")
                .WithDescription("Say whether the pipe, fitting or port at a position (or the one you look at) is carrying steam.")
                .WithArgs(parsers.OptionalWorldPosition("position"))
                .HandleWith(args => Steam(api, mod, args))
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

    private static TextCommandResult Steam(ICoreServerAPI api, FrostStoryModSystem mod, TextCommandCallingArgs args)
    {
        BlockPos? pos = !args.Parsers[0].IsMissing && args[0] is Vec3d given
            ? given.AsBlockPos
            : args.Caller.Player?.CurrentBlockSelection?.Position;

        if (pos != null && api.World.BlockAccessor.GetBlock(pos) is FrostStory.Steam.BlockPipeWarmth warmth)
        {
            return TextCommandResult.Success(warmth.GetHeatStrength(api.World, pos, pos) > 0
                ? "Warm air from a pipe run or steam hub that is carrying steam."
                : "Air round a pipe run or steam hub that is cold.");
        }

        if (pos == null || !FrostStory.Steam.SteamNetwork.Resolve(api.World.BlockAccessor, pos, out var conduit, out _))
            return TextCommandResult.Error("Look at a steam pipe, main, fitting or port, or give its position.");

        var steam = mod.SteamFor(api.World);
        bool live = conduit.IsLiveSource || steam.IsLive(pos);
        if (!live) return TextCommandResult.Success("Cold: no live steam port on this run.");

        var (available, demand, ratio) = steam.Reading(pos);
        string fed = conduit.IsLiveSource || ratio > 0.995f ? "" : $" Starved: fed {ratio * 100:0}%.";
        string gauge = conduit.IsGauge ? $" Gauge: {available:0.#} available, {demand:0.#} drawn beyond." : "";
        return TextCommandResult.Success("Carrying steam." + fed + gauge);
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

    private static TextCommandResult WithConsole(ICoreServerAPI api, TextCommandCallingArgs args, System.Func<BlockEntityGeneratorConsole, TextCommandResult> action, int posArg = 0)
    {
        BlockPos? pos = !args.Parsers[posArg].IsMissing && args[posArg] is Vec3d given
            ? given.AsBlockPos
            : args.Caller.Player?.CurrentBlockSelection?.Position;

        if (pos == null || api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGeneratorConsole console)
            return TextCommandResult.Error("Look at a generator console, or give its position.");
        if (!console.SiteIsDeepEnough) return TextCommandResult.Error("This site is too close to the bottom of the world.");

        return action(console);
    }
}
