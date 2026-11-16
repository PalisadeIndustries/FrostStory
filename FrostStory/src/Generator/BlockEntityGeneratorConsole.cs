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
    private int step;                        // pieces of the current stage that are finished
    private int depth;                       // shaft depth chosen when the project was founded
    private bool building;                   // a piece is going up
    private int cursor;                      // next placement of the piece being built
    private int total;                       // placements in the piece being built
    private bool lit;
    private bool released;                   // someone has told a stage that needs no materials to go ahead
    private int[] delivered = Array.Empty<int>();    // towards the whole stage
    private int[] required = Array.Empty<int>();     // for the whole stage
    private int[] pieceNeed = Array.Empty<int>();    // what must have been delivered before the next piece goes up

    // Synced description of the next piece, for the client's hover text
    private int stepCount;
    private string stepKey = "";
    private int stepNumber;
    private int stepOf;

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
            SyncPiece();
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
            var held = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (held?.Itemstack != null && FrostStory.Coal.CoalItems.IsCoal(held.Itemstack.Collectible))
            {
                int taken = AddCoal(held.StackSize);
                if (taken > 0 && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) { held.TakeOut(taken); held.MarkDirty(); }
                Tell(player, Lang.Get(taken > 0 ? "froststory:console-coaled" : "froststory:console-bunkerfull", taken, (int)coal));
                return true;
            }

            if (!lit && Mod.Config.RequireCoal && coal <= 0f)
            {
                Tell(player, Lang.Get("froststory:console-nocoal"));
                return true;
            }

            SetLit(!lit);
            Tell(player, Lang.Get(lit ? "froststory:console-fired" : "froststory:console-banked"));
            return true;
        }

        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var reqs = Mod.RequirementsFor(CurrentStageId!);

        if (slot?.Itemstack != null && TryDeliver(slot, reqs))
        {
            released = true;
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/plate"), Pos, 0, null, true, 16f);
        }
        else if (slot?.Itemstack == null && byPlayer.Entity.Controls.ShiftKey && byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            for (int i = 0; i < delivered.Length; i++) delivered[i] = required[i];
            released = true;
        }
        else
        {
            if (stage == 0 && step == 0 && !building) ShowFootprint(player);
            if (required.Length == 0) released = true;      // nothing to deliver: asking is the go-ahead
            Tell(player, StatusText());
        }

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

    /// <summary>The next piece can go up once the stage has received its running share of every material.</summary>
    private bool CanStartPiece()
    {
        if (required.Length == 0) return released;

        for (int i = 0; i < required.Length && i < pieceNeed.Length; i++)
        {
            if (delivered[i] < pieceNeed[i]) return false;
        }

        return true;
    }

    // ------------------------------------------------------------ construction

    // ------------------------------------------------------------ coal

    /// <summary>Coal in the bunker.</summary>
    public float Coal => coal;

    private float coal;
    private double lastFuelHours = -1;
    private float fuelWait;
    private float conveyorOwed;

    /// <summary>Steam the generator's ports are sending out now, each pipe run counted once.</summary>
    public float SteamOut()
    {
        if (!lit) return 0f;
        var steam = Mod.SteamFor(Api.World);
        var center = Center;
        var counted = new HashSet<BlockPos>();
        float sum = 0f;
        foreach (var swap in GeneratorBlueprint.LitSwaps)
        {
            if (!swap.Lit.Contains("port")) continue;
            var run = steam.RunOf(center.AddCopy(swap.X, swap.Y, swap.Z));
            if (run != null && counted.Add(run.Value.Key)) sum += run.Value.Delivered;
        }

        return sum;
    }

    /// <summary>Coal burnt an hour at the present load.</summary>
    public float BurnPerHour => lit ? Mod.Config.IdleCoalPerHour + Mod.Config.CoalPerSteamHour * SteamOut() : 0f;

    public int AddCoal(int amount)
    {
        int room = Math.Max(0, Mod.Config.BunkerCapacity - (int)Math.Ceiling(coal));
        int taken = Math.Clamp(amount, 0, room);
        coal += taken;
        if (taken > 0) MarkDirty(true);
        return taken;
    }

    /// <summary>Burns coal for the time that has passed and lets the conveyor bring more.</summary>
    public void AdvanceFuel(double hours)
    {
        if (hours <= 0 || !IsComplete) return;
        var config = Mod.Config;

        conveyorOwed = Math.Min(conveyorOwed + (float)(config.ConveyorCoalPerHour * hours), config.BunkerCapacity);
        int want = Math.Min((int)conveyorOwed, config.BunkerCapacity - (int)Math.Ceiling(coal));
        if (want > 0)
        {
            int got = FrostStory.Coal.Conveyors.Collect(Api.World, Pos, want);
            coal += got;
            conveyorOwed -= got;
        }

        conveyorOwed = Math.Min(conveyorOwed, 64f);      // an idle conveyor does not save its turns up

        if (lit && config.RequireCoal)
        {
            coal -= (float)(BurnPerHour * hours);
            if (coal <= 0f)
            {
                coal = 0f;
                SetLit(false);      // the fire is out: it has to be fired again once there is coal
            }
        }

        MarkDirty();
    }

    private void FuelTick(float dt)
    {
        fuelWait += dt;
        if (fuelWait < 2f) return;
        fuelWait = 0f;

        double now = Api.World.Calendar.TotalHours;
        if (lastFuelHours >= 0) AdvanceFuel(Math.Clamp(now - lastFuelHours, 0, 1));
        lastFuelHours = now;
    }

    private void OnServerTick(float dt)
    {
        if (IsComplete) { FuelTick(dt); return; }

        var steps = Stages[stage].Steps;
        if (step >= steps.Count) { FinishStage(); MarkDirty(true); return; }

        if (!building)
        {
            if (!CanStartPiece()) return;
            building = true;
            cursor = 0;
            total = steps[step].Placements.Count;
            ClearFootprints();
        }

        var placements = steps[step].Placements;
        float seconds = Mod.BuildSecondsFor(CurrentStageId!);
        int perTick = Math.Max(1, (int)Math.Ceiling(Stages[stage].Placements.Count / (seconds * 1000f / TickMs)));

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

        if (cursor >= placements.Count) FinishPiece();
        MarkDirty();
    }

    private void FinishPiece()
    {
        step++;
        building = false;
        cursor = 0;
        total = 0;

        if (step >= Stages[stage].Steps.Count) FinishStage();
        else SyncPiece();
    }

    private void FinishStage()
    {
        string finished = StageName(CurrentStageId!);
        stage++;
        step = 0;
        building = false;
        released = false;
        cursor = 0;
        total = 0;
        delivered = Array.Empty<int>();
        SyncRequirements();
        SyncPiece();

        string message = IsComplete
            ? Lang.Get("froststory:console-allcomplete", finished)
            : Lang.Get("froststory:console-stagecomplete", finished, StageName(CurrentStageId!));
        ((ICoreServerAPI)Api).BroadcastMessageToAllGroups(message, EnumChatType.Notification);
    }

    /// <summary>Starts building the current stage without its materials. Used by the admin command.</summary>
    public bool StartCurrentStage()
    {
        if (IsComplete) return false;

        for (int i = 0; i < delivered.Length; i++) delivered[i] = required[i];
        released = true;
        MarkDirty(true);
        return true;
    }

    /// <summary>
    /// Credits the current stage with materials as if they had been delivered: requirement is 1-based, in the
    /// order the console lists them. Used by the admin command. Returns the new total, or -1 if there is no such requirement.
    /// </summary>
    public int Credit(int requirement, int amount)
    {
        if (IsComplete || requirement < 1 || requirement > required.Length) return -1;

        int i = requirement - 1;
        delivered[i] = Math.Clamp(delivered[i] + amount, 0, required[i]);
        released = true;
        MarkDirty(true);
        return delivered[i];
    }

    /// <summary>Puts up the next piece of the current stage immediately. Used by the admin command.</summary>
    public bool CompleteCurrentPiece()
    {
        if (IsComplete) return false;
        if (!Builder.PlaceStep(Center, Stages[stage].Steps[step], Pos)) return false;

        for (int i = 0; i < delivered.Length && i < pieceNeed.Length; i++) delivered[i] = Math.Max(delivered[i], pieceNeed[i]);
        FinishPiece();
        MarkDirty(true);
        return true;
    }

    /// <summary>One-line state for the admin command.</summary>
    public string Summary()
    {
        var c = Center;
        string piece = IsComplete ? "" : $" piece {step + 1}/{Stages[stage].Steps.Count} ({stepKey})";
        string state = IsComplete ? (lit ? "running" : "complete, cold") : building ? $"building {CurrentStageId}{piece} {Percent()}%" : $"waiting for materials for {CurrentStageId}{piece}";
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

    /// <summary>Works out what the next piece is and what the stage must have received before it goes up.</summary>
    private void SyncPiece()
    {
        if (IsComplete)
        {
            stepCount = 0;
            stepKey = "";
            pieceNeed = Array.Empty<int>();
            return;
        }

        var blueprint = Stages[stage];
        step = Math.Clamp(step, 0, blueprint.Steps.Count - 1);
        var piece = blueprint.Steps[step];
        stepCount = blueprint.Steps.Count;
        stepKey = piece.Key;
        stepNumber = piece.Number;
        stepOf = piece.Of;

        pieceNeed = new int[required.Length];
        for (int i = 0; i < required.Length; i++) pieceNeed[i] = blueprint.Threshold(step, required[i]);
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

    private string PieceName() => Lang.Get("froststory:step-" + stepKey, stepNumber, stepOf);

    private void AppendStatus(StringBuilder sb)
    {
        if (IsComplete)
        {
            sb.AppendLine(Lang.Get(lit ? "froststory:console-running" : "froststory:console-ready"));
            sb.AppendLine(Lang.Get("froststory:console-coal", (int)coal, Mod.Config.BunkerCapacity));
            if (lit && Api.Side == EnumAppSide.Server)
            {
                float burn = BurnPerHour;
                sb.AppendLine(Lang.Get("froststory:console-burn", Math.Round(burn, 1), Math.Round(SteamOut(), 1), burn > 0 ? Math.Round(coal / burn, 1) : 0));
            }

            return;
        }

        string id = CurrentStageId!;
        sb.AppendLine(Lang.Get("froststory:console-stage", stage + 1, StageCount, StageName(id)));
        if (stepCount > 0) sb.AppendLine(Lang.Get("froststory:console-piece", step + 1, stepCount, PieceName()));
        if (building) sb.AppendLine(Lang.Get("froststory:console-progress", Percent()));

        var reqs = Mod.Stages.Find(id)?.Requirements ?? Array.Empty<StageRequirement>();
        var owed = new List<string>();
        for (int i = 0; i < required.Length && i < reqs.Length; i++)
        {
            string name = RequirementName(reqs[i]);
            sb.AppendLine(Lang.Get("froststory:console-requirement", delivered[i], required[i], name));
            if (!building && i < pieceNeed.Length && delivered[i] < pieceNeed[i])
                owed.Add(Lang.Get("froststory:console-owed", pieceNeed[i] - delivered[i], name));
        }

        if (owed.Count > 0) sb.AppendLine(Lang.Get("froststory:console-pieceneeds", string.Join(", ", owed)));
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        AppendStatus(dsc);
        if (!IsComplete) dsc.AppendLine(Lang.Get("froststory:stagedesc-" + CurrentStageId));
    }

    /// <summary>Materials still owed for the current stage, for the interaction hint.</summary>
    public ItemStack[] OutstandingStacks()
    {
        if (IsComplete) return Array.Empty<ItemStack>();

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
        tree.SetInt("step", step);
        tree.SetBool("released", released);
        tree.SetInt("stepCount", stepCount);
        tree.SetString("stepKey", stepKey);
        tree.SetInt("stepNumber", stepNumber);
        tree.SetInt("stepOf", stepOf);
        tree["pieceNeed"] = new IntArrayAttribute(pieceNeed);
        tree.SetInt("depth", depth);
        tree.SetBool("building", building);
        tree.SetInt("cursor", cursor);
        tree.SetInt("total", total);
        tree.SetBool("lit", lit);
        tree.SetFloat("coal", coal);
        tree["delivered"] = new IntArrayAttribute(delivered);
        tree["required"] = new IntArrayAttribute(required);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        stage = tree.GetInt("stage");
        step = tree.GetInt("step");
        released = tree.GetBool("released");
        stepCount = tree.GetInt("stepCount");
        stepKey = tree.GetString("stepKey", "");
        stepNumber = tree.GetInt("stepNumber");
        stepOf = tree.GetInt("stepOf");
        pieceNeed = (tree["pieceNeed"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
        depth = tree.GetInt("depth");
        building = tree.GetBool("building");
        cursor = tree.GetInt("cursor");
        total = tree.GetInt("total");
        lit = tree.GetBool("lit");
        coal = tree.GetFloat("coal");
        delivered = (tree["delivered"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
        required = (tree["required"] as IntArrayAttribute)?.value ?? Array.Empty<int>();
        if (delivered.Length != required.Length) delivered = new int[required.Length];
    }
}
