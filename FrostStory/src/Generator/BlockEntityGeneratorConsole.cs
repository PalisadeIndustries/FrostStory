using System;
using System.Collections.Generic;
using System.Text;
using FrostStory.Generator.Blueprint;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FrostStory.Generator;

/// <summary>
/// The control console that owns a generator project. It takes deliveries of materials, raises the
/// current stage block by block once they are in, and fires the finished generator.
/// </summary>
public class BlockEntityGeneratorConsole : BlockEntity
{
    private const int TickMs = 100;
    private const int HighlightSlot = 5471;

    // Synced state
    private int stage;                       // number of finished stages
    private int depth;                       // shaft depth chosen when the project was founded
    private bool building;
    private int cursor;                      // next placement of the stage being built
    private int total;                       // placements in the stage being built
    private bool lit;
    private int[] delivered = Array.Empty<int>();
    private int[] required = Array.Empty<int>();

    // Server only
    private IReadOnlyList<StageBlueprint>? stages;
    private GeneratorBuilder? builder;
    private BlockPos? scratch;
    private int ticksSinceSound;
    private readonly HashSet<string> highlightedFor = new();

    private FrostStoryModSystem Mod => Api.ModLoader.GetModSystem<FrostStoryModSystem>();

    public int StageCount => GeneratorBlueprint.StageIds.Length;
    public int FinishedStages => stage;
    public bool IsComplete => stage >= StageCount;
    public bool IsBuilding => building;
    public bool IsLit => lit;
    public int Depth => depth;

    /// <summary>Centre of the generator at deck level. The console stands on the pad rim with its back to it.</summary>
    public BlockPos Center
    {
        get
        {
            var facing = BlockFacing.FromCode(Block?.Variant?["side"]) ?? BlockFacing.NORTH;
            var n = facing.Normali;
            return Pos.AddCopy(-n.X * GeneratorBlueprint.ConsoleDistance, -1, -n.Z * GeneratorBlueprint.ConsoleDistance);
        }
    }

    public string? CurrentStageId => IsComplete ? null : GeneratorBlueprint.StageIds[stage];

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        if (api.Side == EnumAppSide.Server)
        {
            if (depth == 0) depth = GeneratorBlueprint.ClampDepth(MaxDepthAt(Center));
            SyncRequirements();
            RegisterGameTickListener(OnServerTick, TickMs);
        }
        else
        {
            RegisterGameTickListener(OnClientTick, 250);
        }
    }

    /// <summary>Deepest shaft the world allows under this centre, leaving a margin above the map floor.</summary>
    private static int MaxDepthAt(BlockPos center) => center.Y - 8;

    public bool SiteIsDeepEnough => MaxDepthAt(Center) >= GeneratorBlueprint.MinDepth;

    private IReadOnlyList<StageBlueprint> Stages => stages ??= GeneratorBlueprint.Build(depth);

    private GeneratorBuilder Builder => builder ??= new GeneratorBuilder(Api.World);

    // ------------------------------------------------------------ interaction

    public bool OnInteract(IPlayer byPlayer)
    {
        if (Api.Side != EnumAppSide.Server) return true;
        var player = (IServerPlayer)byPlayer;

        if (!SiteIsDeepEnough)
        {
            Tell(player, Lang.Get("froststory:console-tooshallow", GeneratorBlueprint.MinDepth + 8));
            return true;
        }

        if (IsComplete)
        {
            SetLit(!lit);
            Tell(player, Lang.Get(lit ? "froststory:console-fired" : "froststory:console-banked"));
            return true;
        }

        if (building)
        {
            Tell(player, Lang.Get("froststory:console-building", StageName(CurrentStageId!), Percent()));
            return true;
        }

        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var reqs = Mod.RequirementsFor(CurrentStageId!);

        if (slot?.Itemstack != null && TryDeliver(slot, reqs))
        {
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/plate"), Pos, 0, null, true, 16f);
        }
        else if (slot?.Itemstack == null && byPlayer.Entity.Controls.ShiftKey && byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            for (int i = 0; i < delivered.Length; i++) delivered[i] = required[i];
        }
        else
        {
            if (stage == 0) ShowFootprint(player);
            Tell(player, StatusText());
        }

        if (RequirementsMet()) BeginStage();
        MarkDirty(true);
        return true;
    }

    private bool TryDeliver(ItemSlot slot, StageRequirement[] reqs)
    {
        for (int i = 0; i < reqs.Length && i < delivered.Length; i++)
        {
            int missing = reqs[i].Quantity - delivered[i];
            if (missing <= 0 || !FrostStoryModSystem.Matches(reqs[i].Code, slot.Itemstack!.Collectible)) continue;

            int taken = Math.Min(missing, slot.StackSize);
            slot.TakeOut(taken);
            slot.MarkDirty();
            delivered[i] += taken;
            return true;
        }

        return false;
    }

    private bool RequirementsMet()
    {
        for (int i = 0; i < required.Length; i++)
        {
            if (delivered[i] < required[i]) return false;
        }

        return true;
    }

    // ------------------------------------------------------------ construction

    private void BeginStage()
    {
        building = true;
        cursor = 0;
        total = Stages[stage].Placements.Count;
        ClearFootprints();
    }

    private void OnServerTick(float dt)
    {
        if (!building || IsComplete) return;

        var placements = Stages[stage].Placements;
        float seconds = Mod.BuildSecondsFor(CurrentStageId!);
        int perTick = Math.Max(1, (int)Math.Ceiling(placements.Count / (seconds * 1000f / TickMs)));

        var center = Center;
        scratch ??= Pos.Copy();
        BlockPos? lastPlaced = null;

        for (int i = 0; i < perTick && cursor < placements.Count; i++)
        {
            var result = Builder.Place(center, placements[cursor], Pos, scratch);
            if (result == PlaceResult.NotLoaded) break;   // wait for the chunk rather than leave a hole
            if (result == PlaceResult.Placed) lastPlaced = scratch.Copy();
            cursor++;
        }

        if (lastPlaced != null && ++ticksSinceSound >= 4)
        {
            ticksSinceSound = 0;
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/anvilhit"), lastPlaced, 0, null, true, 48f, 0.6f);
        }

        if (cursor >= placements.Count) FinishStage();
        MarkDirty();
    }

    private void FinishStage()
    {
        string finished = StageName(CurrentStageId!);
        stage++;
        building = false;
        cursor = 0;
        total = 0;
        SyncRequirements();

        string message = IsComplete
            ? Lang.Get("froststory:console-allcomplete", finished)
            : Lang.Get("froststory:console-stagecomplete", finished, StageName(CurrentStageId!));
        ((ICoreServerAPI)Api).BroadcastMessageToAllGroups(message, EnumChatType.Notification);
    }

    /// <summary>Starts building the current stage without its materials. Used by the admin command.</summary>
    public bool StartCurrentStage()
    {
        if (IsComplete || building) return false;

        for (int i = 0; i < delivered.Length; i++) delivered[i] = required[i];
        BeginStage();
        MarkDirty(true);
        return true;
    }

    /// <summary>One-line state for the admin command.</summary>
    public string Summary()
    {
        var c = Center;
        string state = IsComplete ? (lit ? "running" : "complete, cold") : building ? $"building {CurrentStageId} {Percent()}%" : $"waiting for materials for {CurrentStageId}";
        return $"Stages finished: {stage}/{StageCount}; {state}; centre {c.X} {c.Y} {c.Z}; shaft depth {depth}.";
    }

    /// <summary>Finishes the stage in progress (or the next one) immediately. Used by the admin command.</summary>
    public bool CompleteCurrentStage()
    {
        if (IsComplete) return false;
        if (!Builder.PlaceStage(Center, Stages[stage], Pos)) return false;

        FinishStage();
        MarkDirty(true);
        return true;
    }

    public void SetLit(bool value)
    {
        if (lit == value || !IsComplete) return;
        lit = value;
        Builder.SetCoreLit(Center, lit);
        MarkDirty(true);
    }

    private void SyncRequirements()
    {
        var reqs = IsComplete ? Array.Empty<StageRequirement>() : Mod.RequirementsFor(CurrentStageId!);
        required = new int[reqs.Length];
        for (int i = 0; i < reqs.Length; i++) required[i] = reqs[i].Quantity;
        if (delivered.Length != reqs.Length) delivered = new int[reqs.Length];
    }

    // ------------------------------------------------------------ footprint preview

    private void ShowFootprint(IServerPlayer player)
    {
        var center = Center;
        var cells = new List<BlockPos>();
        var colors = new List<int>();
        int pad = ColorUtil.ToRgba(90, 80, 170, 255);
        int hull = ColorUtil.ToRgba(140, 60, 110, 255);

        const int r = GeneratorBlueprint.PadRadius;
        for (int x = -r; x <= r; x++)
        {
            for (int z = -r; z <= r; z++)
            {
                bool onPadRim = GeneratorBlueprint.InShell(x, z, r);
                bool onHull = GeneratorBlueprint.InShell(x, z, GeneratorBlueprint.HullRadius);
                if (!onPadRim && !onHull) continue;

                cells.Add(center.AddCopy(x, 0, z));
                colors.Add(onHull ? hull : pad);
            }
        }

        Api.World.HighlightBlocks(player, HighlightSlot, cells, colors);
        highlightedFor.Add(player.PlayerUID);
    }

    private void ClearFootprints()
    {
        foreach (var player in Api.World.AllOnlinePlayers)
        {
            if (highlightedFor.Contains(player.PlayerUID))
                Api.World.HighlightBlocks(player, HighlightSlot, new List<BlockPos>(), new List<int>());
        }

        highlightedFor.Clear();
    }

    public override void OnBlockRemoved()
    {
        if (Api?.Side == EnumAppSide.Server) ClearFootprints();
        base.OnBlockRemoved();
    }

    // ------------------------------------------------------------ client effects

    private void OnClientTick(float dt)
    {
        if (!lit) return;

        var (sx, sy, sz) = GeneratorBlueprint.StackTop;
        var center = Center;
        var min = new Vec3d(center.X + sx + 0.2, center.Y + sy + 0.6, center.Z + sz + 0.2);

        var steam = new SimpleParticleProperties(
            2, 3,
            ColorUtil.ToRgba(110, 235, 235, 240),
            min, min.AddCopy(0.6, 0.2, 0.6),
            new Vec3f(-0.15f, 0.9f, -0.15f), new Vec3f(0.15f, 1.6f, 0.15f),
            6f, -0.01f, 1.2f, 2.2f,
            EnumParticleModel.Quad)
        {
            SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 5f),
            OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.QUADRATIC, -16f),
            WindAffectednes = 0.6f,
            WithTerrainCollision = false
        };

        Api.World.SpawnParticles(steam);
    }

    // ------------------------------------------------------------ text

    private static string StageName(string id) => Lang.Get("froststory:stage-" + id);

    private int Percent() => total <= 0 ? 0 : (int)(100L * cursor / total);

    private string RequirementName(StageRequirement req)
    {
        if (req.Name != null) return Lang.Get(req.Name);
        var stacks = Mod.StacksFor(Api.World, req.Code);
        return stacks.Length > 0 ? stacks[0].GetName() : req.Code;
    }

    private string StatusText()
    {
        var sb = new StringBuilder();
        AppendStatus(sb);
        return sb.ToString();
    }

    private void AppendStatus(StringBuilder sb)
    {
        if (IsComplete)
        {
            sb.AppendLine(Lang.Get(lit ? "froststory:console-running" : "froststory:console-ready"));
            return;
        }

        string id = CurrentStageId!;
        sb.AppendLine(Lang.Get("froststory:console-stage", stage + 1, StageCount, StageName(id)));

        if (building)
        {
            sb.AppendLine(Lang.Get("froststory:console-progress", Percent()));
            return;
        }

        var reqs = Mod.Stages.Find(id)?.Requirements ?? Array.Empty<StageRequirement>();
        for (int i = 0; i < required.Length && i < reqs.Length; i++)
        {
            sb.AppendLine(Lang.Get("froststory:console-requirement", delivered[i], required[i], RequirementName(reqs[i])));
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        AppendStatus(dsc);
        if (!IsComplete && !building) dsc.AppendLine(Lang.Get("froststory:stagedesc-" + CurrentStageId));
    }

    /// <summary>Materials still owed for the current stage, for the interaction hint.</summary>
    public ItemStack[] OutstandingStacks()
    {
        if (IsComplete || building) return Array.Empty<ItemStack>();

        var reqs = Mod.Stages.Find(CurrentStageId!)?.Requirements ?? Array.Empty<StageRequirement>();
        var stacks = new List<ItemStack>();
        for (int i = 0; i < required.Length && i < reqs.Length; i++)
        {
            if (delivered[i] < required[i]) stacks.AddRange(Mod.StacksFor(Api.World, reqs[i].Code));
        }

        return stacks.ToArray();
    }

    private static void Tell(IServerPlayer player, string message)
    {
        player.SendMessage(GlobalConstants.CurrentChatGroup, message.TrimEnd(), EnumChatType.Notification);
    }

    // ------------------------------------------------------------ persistence

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("stage", stage);
        tree.SetInt("depth", depth);
        tree.SetBool("building", building);
        tree.SetInt("cursor", cursor);
        tree.SetInt("total", total);
        tree.SetBool("lit", lit);
        tree["delivered"] = new IntArrayAttribute(delivered);
        tree["required"] = new IntArrayAttribute(required);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        stage = tree.GetInt("stage");
        depth = tree.GetInt("depth");
        building = tree.GetBool("building");
        cursor = tree.GetInt("cursor");
        total = tree.GetInt("total");
        lit = tree.GetBool("lit");
        delivered = (tree["delivered"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
        required = (tree["required"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
        if (delivered.Length != required.Length) delivered = new int[required.Length];
    }
}
