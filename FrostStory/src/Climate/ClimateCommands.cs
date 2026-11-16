using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FrostStory.Climate;

/// <summary>/frostclimate: status, storm start/stop and the day offset, for testing.</summary>
public static class ClimateCommands
{
    public static void Register(ICoreServerAPI api, FrostClimateSystem sys)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("frostclimate")
            .WithDescription("FrostStory Long Winter climate tools")
            .RequiresPrivilege(Privilege.controlserver)

            .BeginSubCommand("status")
                .WithDescription("Show the Long Winter state: days, cold, storm phase and strength, and your exposure.")
                .HandleWith(args => TextCommandResult.Success(Status(api, sys, args.Caller.Player)))
            .EndSubCommand()

            .BeginSubCommand("storm")
                .WithDescription("Start or stop a storm.")
                .BeginSubCommand("start")
                    .WithDescription("Start a storm now at the given peak strength (0..1, default 1) for the given in-game hours (default as the day would give).")
                    .WithArgs(parsers.OptionalFloat("strength", 1f), parsers.OptionalFloat("hours", -1f))
                    .HandleWith(args =>
                    {
                        if (!sys.Enabled) return TextCommandResult.Error(Off);
                        float peak = Math.Clamp((float)args[0], 0.05f, 1f);
                        float hours = (float)args[1];
                        var cycle = sys.Cycle;
                        cycle.Started = true;
                        cycle.Peak = peak;
                        cycle.WindAngle = (float)(Random.Shared.NextDouble() * Math.PI * 2);
                        cycle.Begin(StormPhase.Storm, api.World.Calendar.TotalHours,
                            hours > 0 ? hours : StormCycle.StormHours(sys.Days, sys.Config, Random.Shared));
                        sys.Mod.Logger.Notification("Storm started by command: peak {0:0.00}, {1:0.0} h.", peak, cycle.PhaseEnd - cycle.PhaseStart);
                        return TextCommandResult.Success($"Storm started: peak {peak:0.00} for {cycle.PhaseEnd - cycle.PhaseStart:0.0} h.");
                    })
                .EndSubCommand()
                .BeginSubCommand("stop")
                    .WithDescription("End any storm now and start a calm.")
                    .HandleWith(args =>
                    {
                        if (!sys.Enabled) return TextCommandResult.Error(Off);
                        var cycle = sys.Cycle;
                        cycle.Started = true;
                        cycle.Begin(StormPhase.Calm, api.World.Calendar.TotalHours,
                            StormCycle.CalmHours(sys.Days, sys.StormFrequency, sys.Config, Random.Shared));
                        sys.Mod.Logger.Notification("Storm stopped by command; calm for {0:0.0} h.", cycle.PhaseEnd - cycle.PhaseStart);
                        return TextCommandResult.Success($"Calm for {cycle.PhaseEnd - cycle.PhaseStart:0.0} h.");
                    })
                .EndSubCommand()
            .EndSubCommand()

            .BeginSubCommand("days")
                .WithDescription("Set the day offset added to the days survived (storms and cold scale with days).")
                .WithArgs(parsers.Double("offset"))
                .HandleWith(args =>
                {
                    if (!sys.Enabled) return TextCommandResult.Error(Off);
                    sys.Cycle.DayOffset = (double)args[0];
                    return TextCommandResult.Success($"Day offset {sys.Cycle.DayOffset:0.0}; days survived now {sys.Cycle.DaysSurvived(api.World.Calendar.TotalDays):0.0}.");
                })
            .EndSubCommand();
    }

    private const string Off = "The Long Winter is off in this world (world config froststoryLongWinter).";

    public static string Status(ICoreServerAPI api, FrostClimateSystem sys, IPlayer? player)
    {
        if (!sys.Enabled) return Off;
        var c = sys.Cycle;
        double now = api.World.Calendar.TotalHours;
        var sb = new StringBuilder();
        sb.AppendLine($"Long Winter: day {sys.Days:0.0} (offset {c.DayOffset:0.0}), cold offset {sys.ColdOffset:0} °C, now {sys.ColdNow:0.0} °C in all, storm frequency {sys.StormFrequency:0.##}.");
        sb.AppendLine($"Storm: {c.Phase}, strength {sys.Strength:0.00}, peak {c.Peak:0.00}, {Math.Max(0, c.PhaseEnd - now):0.0} h left in phase, wind from {FrostClimateSystem.CompassFrom(c.WindAngle)}.");
        if (player?.Entity != null)
        {
            var bt = player.Entity.GetBehavior<EntityBehaviorBodyTemperature>();
            var exposure = sys.Exposure;
            string why = "-";
            float exp = exposure?.ExposureOf(player, out why) ?? 0;
            sb.Append($"You: body {bt?.CurBodyTemperature:0.0} °C, exposure {exp:0.00} ({why}), windproof {FrostClimateSystem.WornSum(player, FrostClimateSystem.WindproofAttr):0.00}, storm vision {FrostClimateSystem.WornSum(player, FrostClimateSystem.StormVisionAttr):0.00}, hypothermic {(exposure?.IsHypothermic(player) == true ? "yes" : "no")}.");
        }
        return sb.ToString().TrimEnd();
    }
}
