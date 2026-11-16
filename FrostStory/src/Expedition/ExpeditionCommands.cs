using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Expedition;

/// <summary>/frostexp: admin commands for the expedition start.</summary>
public static class ExpeditionCommands
{
    public static void Register(ICoreServerAPI api, ExpeditionSystem system)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("frostexp")
            .WithDescription("FrostStory expedition start: the starting kit and the start wreck")
            .RequiresPrivilege(Privilege.controlserver)

            .BeginSubCommand("kit")
                .WithDescription("Give the starting kit to a player (or yourself), whether or not they have had it")
                .WithArgs(parsers.OptionalWord("player"))
                .HandleWith(args =>
                {
                    var player = Find(api, args, 0, out string? error);
                    if (player == null) return TextCommandResult.Error(error!);
                    int given = system.GiveKit(player);
                    return TextCommandResult.Success($"Gave {player.PlayerName} the kit: {given} stacks.");
                })
            .EndSubCommand()

            .BeginSubCommand("resetkit")
                .WithDescription("Forget that a player (or you) has had the starting kit, so it is given again on their next join")
                .WithArgs(parsers.OptionalWord("player"))
                .HandleWith(args =>
                {
                    var player = Find(api, args, 0, out string? error);
                    if (player == null) return TextCommandResult.Error(error!);
                    ExpeditionSystem.ResetKit(player);
                    return TextCommandResult.Success($"{player.PlayerName} will get the kit again on their next join.");
                })
            .EndSubCommand()

            .BeginSubCommand("wreck")
                .WithDescription("Set the start wreck down: at a position on the ice amidships, or with no position on the best site near the world spawn. Moves the world spawn beside her hole.")
                .WithArgs(parsers.OptionalWorldPosition("position"))
                .HandleWith(args =>
                {
                    var plan = api.ModLoader.GetModSystem<FrostStoryModSystem>().Wreck;
                    if (args.Parsers[0].IsMissing)
                    {
                        var caller = args.Caller.Player as IServerPlayer;
                        system.BeginWreck(text =>
                        {
                            if (caller != null) caller.SendMessage(args.Caller.FromChatGroupId, text, EnumChatType.CommandSuccess);
                            else api.Logger.Notification("[froststory] /frostexp wreck: {0}", text);
                        });
                        return TextCommandResult.Success("Looking for a site near the world spawn; the wreck goes down once the ground is loaded.");
                    }

                    var at = ((Vec3d)args[0]).AsBlockPos;
                    var keel = at.DownCopy(plan.Waterline + 1);
                    return TextCommandResult.Success(system.Place(keel, false));
                })
            .EndSubCommand()

            .BeginSubCommand("status")
                .WithDescription("Report the expedition start's state in this world")
                .HandleWith(_ =>
                {
                    var keel = system.Keel;
                    var spawn = api.World.DefaultSpawnPosition?.AsBlockPos;
                    int found = ExpeditionKit.Resolve(api.World, system.Kit, api.Logger).Count;
                    return TextCommandResult.Success(
                        $"Long Winter: {system.LongWinter}. Wreck: {system.State}" + (keel != null ? $", keel at {keel.X} {keel.Y} {keel.Z}" : "") +
                        (spawn != null ? $". World spawn at {spawn.X} {spawn.Y} {spawn.Z}" : "") +
                        $". Kit: {found} of {system.Kit.Items.Length} entries exist (missing ones are in the server log). Kit {(system.Config.Kit ? "on" : "off")}, wreck {(system.Config.Wreck ? "on" : "off")}, distance {system.Config.WreckDistance}.");
                })
            .EndSubCommand();
    }

    private static IServerPlayer? Find(ICoreServerAPI api, TextCommandCallingArgs args, int index, out string? error)
    {
        error = null;
        if (args.Parsers[index].IsMissing)
        {
            if (args.Caller.Player is IServerPlayer self) return self;
            error = "Name a player.";
            return null;
        }

        string name = (string)args[index];
        var player = api.World.AllOnlinePlayers.OfType<IServerPlayer>()
            .FirstOrDefault(p => string.Equals(p.PlayerName, name, StringComparison.OrdinalIgnoreCase));
        if (player == null) error = $"No player called '{name}' is online.";
        return player;
    }
}
