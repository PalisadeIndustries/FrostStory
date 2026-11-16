using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FrostStory.Climate;

/// <summary>Server side, once a second: players out in a storm lose body heat on top of what the vanilla
/// body temperature takes, scaled by storm strength and cut by windproof gear. A player who is
/// hypothermic walks slower and is told so.</summary>
public class StormExposure : IDisposable
{
    public const string SlowCode = "froststory-hypothermia";

    private readonly FrostClimateSystem sys;
    private readonly ICoreServerAPI sapi;
    private readonly HashSet<string> hypothermic = new();
    private readonly RoomProbe rooms;

    public StormExposure(FrostClimateSystem sys, ICoreServerAPI sapi)
    {
        this.sys = sys;
        this.sapi = sapi;
        rooms = new RoomProbe(sapi);
    }

    /// <summary>How exposed a player is right now, 0 (sheltered) to 1 (in the open), and why.</summary>
    public float ExposureOf(IPlayer player, out string why)
    {
        var entity = player.Entity;
        var cfg = sys.Config;
        var bt = entity.GetBehavior<EntityBehaviorBodyTemperature>();
        if (bt != null && NearHeat(bt) >= cfg.ShelteringHeat) { why = "heat"; return 0; }

        var pos = entity.Pos.AsBlockPos;
        if (rooms.InClosedRoom(pos)) { why = "room"; return 0; }

        int rainHeight = sapi.World.BlockAccessor.GetRainMapHeightAt(pos.X, pos.Z);
        if (rainHeight > pos.Y + 1) { why = "roof"; return cfg.UnderRoofExposure; }
        why = "open";
        return 1;
    }

    public void Tick(float dt)
    {
        var cfg = sys.Config;
        float strength = sys.Strength;
        foreach (IServerPlayer player in sapi.World.AllOnlinePlayers)
        {
            var entity = player.Entity;
            if (entity == null || !entity.Alive || player.ConnectionState != EnumClientState.Playing) continue;
            var bt = entity.GetBehavior<EntityBehaviorBodyTemperature>();
            if (bt == null) continue;
            bool survival = player.WorldData.CurrentGameMode == EnumGameMode.Survival;

            if (survival && strength > 0.01f)
            {
                float exposure = ExposureOf(player, out _);
                if (exposure > 0)
                {
                    float windproof = FrostClimateSystem.WornSum(player, FrostClimateSystem.WindproofAttr);
                    float loss = cfg.StormHeatLossPerMinute / 60f * dt * strength * exposure * (1 - windproof);
                    if (loss > 0 && bt.CurBodyTemperature > cfg.StormBodyTempFloor)
                        bt.CurBodyTemperature = Math.Max(cfg.StormBodyTempFloor, bt.CurBodyTemperature - loss);
                }
            }

            UpdateHypothermia(player, bt, survival);
        }
    }

    private void UpdateHypothermia(IServerPlayer player, EntityBehaviorBodyTemperature bt, bool survival)
    {
        var cfg = sys.Config;
        float temp = bt.CurBodyTemperature;
        bool cold = survival && temp < cfg.HypothermiaBelow;
        bool was = hypothermic.Contains(player.PlayerUID);

        if (cold)
        {
            float severity = GameMath.Clamp((cfg.HypothermiaBelow - temp) / 4f, 0, 1);
            player.Entity.Stats.Set("walkspeed", SlowCode, -cfg.HypothermiaSlow * (1 + severity), true);
            if (!was)
            {
                hypothermic.Add(player.PlayerUID);
                player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "froststory:climate-hypothermia"), EnumChatType.Notification);
                player.SendIngameError("froststory-hypothermia", Lang.GetL(player.LanguageCode, "froststory:climate-hypothermia-short"));
            }
        }
        else if (was)
        {
            hypothermic.Remove(player.PlayerUID);
            player.Entity.Stats.Remove("walkspeed", SlowCode);
            if (survival) player.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(player.LanguageCode, "froststory:climate-warmedup"), EnumChatType.Notification);
        }
        else if (player.Entity.Stats["walkspeed"]?.ValuesByKey.ContainsKey(SlowCode) == true)
        {
            // left over from before a restart: the stat is saved with the player, our list is not
            player.Entity.Stats.Remove("walkspeed", SlowCode);
        }
    }

    private static readonly PropertyInfo? nearHeatProp =
        typeof(EntityBehaviorBodyTemperature).GetProperty("nearHeatSourceStrength", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    /// <summary>The vanilla body temperature's own sum of nearby heat sources (not public, so read by reflection).</summary>
    public static float NearHeat(EntityBehaviorBodyTemperature bt)
    {
        try { return nearHeatProp?.GetValue(bt) is float f ? f : 0; }
        catch { return 0; }
    }

    public bool IsHypothermic(IPlayer player) => hypothermic.Contains(player.PlayerUID);

    public void Dispose() { }

    /// <summary>Asks the vanilla room registry whether a position is inside a closed room. The registry
    /// lives in VSEssentials, which this project does not reference, so it is reached by reflection.</summary>
    private class RoomProbe
    {
        private readonly object? registry;
        private readonly MethodInfo? getRoom;
        private FieldInfo? exitCount;

        public RoomProbe(ICoreAPI api)
        {
            foreach (var system in api.ModLoader.Systems)
            {
                if (system.GetType().FullName != "Vintagestory.GameContent.RoomRegistry") continue;
                registry = system;
                getRoom = system.GetType().GetMethod("GetRoomForPosition", new[] { typeof(BlockPos) });
                break;
            }
        }

        public bool InClosedRoom(BlockPos pos)
        {
            if (registry == null || getRoom == null) return false;
            try
            {
                object? room = getRoom.Invoke(registry, new object[] { pos });
                if (room == null) return false;
                exitCount ??= room.GetType().GetField("ExitCount");
                return exitCount != null && (int)exitCount.GetValue(room)! == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
