using System.Collections.Generic;
using FrostStory.Generator.Blueprint;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FrostStory.Generator;

public enum PlaceResult
{
    Placed,
    Unchanged,
    Skipped,
    NotLoaded
}

/// <summary>Writes blueprint placements into the world.</summary>
public sealed class GeneratorBuilder
{
    private readonly IWorldAccessor world;
    private readonly Dictionary<string, int> ids = new();

    public GeneratorBuilder(IWorldAccessor world)
    {
        this.world = world;
    }

    public int IdOf(string? code)
    {
        if (code == null) return 0;
        if (ids.TryGetValue(code, out int id)) return id;

        var block = world.GetBlock(new AssetLocation(code));
        if (block == null) world.Logger.Error("[froststory] Blueprint refers to unknown block {0}", code);

        id = block?.Id ?? -1;
        ids[code] = id;
        return id;
    }

    /// <summary>
    /// Puts one blueprint cell into the world. <paramref name="keep"/> is a position that must never be
    /// overwritten (the console running the build). <paramref name="scratch"/> receives the world position.
    /// </summary>
    public PlaceResult Place(BlockPos center, Placement p, BlockPos? keep, BlockPos scratch)
    {
        scratch.Set(center.X + p.X, center.Y + p.Y, center.Z + p.Z);
        scratch.SetDimension(center.dimension);
        if (keep != null && scratch.Equals(keep)) return PlaceResult.Skipped;

        var ba = world.BlockAccessor;
        if (!ba.IsValidPos(scratch)) return PlaceResult.Skipped;
        if (ba.GetChunkAtBlockPos(scratch) == null) return PlaceResult.NotLoaded;

        int id = IdOf(p.Code);
        if (id < 0) return PlaceResult.Skipped;

        // Ground water must not stay behind inside the works.
        if (ba.GetBlock(scratch, BlockLayersAccess.Fluid).Id != 0) ba.SetBlock(0, scratch, BlockLayersAccess.Fluid);

        if (ba.GetBlock(scratch, BlockLayersAccess.Solid).Id == id) return PlaceResult.Unchanged;

        ba.SetBlock(id, scratch);
        return PlaceResult.Placed;
    }

    /// <summary>Places a whole stage at once. Returns false if part of it lies in unloaded chunks.</summary>
    public bool PlaceStage(BlockPos center, StageBlueprint stage, BlockPos? keep = null)
    {
        var scratch = center.Copy();
        bool complete = true;
        foreach (var p in stage.Placements)
        {
            if (Place(center, p, keep, scratch) == PlaceResult.NotLoaded) complete = false;
        }

        return complete;
    }

    /// <summary>Places one piece of a stage at once. Returns false if part of it lies in unloaded chunks.</summary>
    public bool PlaceStep(BlockPos center, StepBlueprint step, BlockPos? keep = null)
    {
        var scratch = center.Copy();
        bool complete = true;
        foreach (var p in step.Placements)
        {
            if (Place(center, p, keep, scratch) == PlaceResult.NotLoaded) complete = false;
        }

        return complete;
    }

    /// <summary>Swaps the segments that have one between their cold and lit models.</summary>
    public void SetCoreLit(BlockPos center, bool lit)
    {
        var scratch = center.Copy();
        foreach (var swap in GeneratorBlueprint.LitSwaps)
        {
            // only where the segment has actually been built
            scratch.Set(center.X + swap.X, center.Y + swap.Y, center.Z + swap.Z);
            int found = world.BlockAccessor.GetBlock(scratch, BlockLayersAccess.Solid).Id;
            if (found != IdOf(swap.Cold) && found != IdOf(swap.Lit)) continue;

            Place(center, new Placement(swap.X, swap.Y, swap.Z, lit ? swap.Lit : swap.Cold), null, scratch);
        }
    }

    /// <summary>
    /// Removes everything the given stages built, leaving the open shaft. The generator's own blocks cannot
    /// be broken by hand, so this is the way to take one down.
    /// </summary>
    public bool Demolish(BlockPos center, IReadOnlyList<StageBlueprint> stages, BlockPos? keep = null)
    {
        var scratch = center.Copy();
        bool complete = true;
        foreach (var stage in stages)
        {
            foreach (var p in stage.Placements)
            {
                if (p.Code == null) continue;
                if (Place(center, p with { Code = null }, keep, scratch) == PlaceResult.NotLoaded) complete = false;
            }
        }

        return complete;
    }

    /// <summary>
    /// Counts cells that differ from what the first <paramref name="stageCount"/> stages should have left behind.
    /// </summary>
    public int CountMismatches(BlockPos center, IReadOnlyList<StageBlueprint> stages, int stageCount, bool lit, BlockPos? keep, out int checkedCells)
    {
        var expected = new Dictionary<(int, int, int), string?>();
        for (int i = 0; i < stageCount && i < stages.Count; i++)
        {
            foreach (var p in stages[i].Placements) expected[(p.X, p.Y, p.Z)] = p.Code;
        }

        if (lit && stageCount > 3)
        {
            foreach (var swap in GeneratorBlueprint.LitSwaps)
            {
                // a later stage may have put its own segment in this cell
                if (expected.TryGetValue((swap.X, swap.Y, swap.Z), out var code) && code == swap.Cold)
                    expected[(swap.X, swap.Y, swap.Z)] = swap.Lit;
            }
        }

        var ba = world.BlockAccessor;
        var pos = center.Copy();
        int mismatches = 0;
        checkedCells = 0;
        foreach (var (cell, code) in expected)
        {
            pos.Set(center.X + cell.Item1, center.Y + cell.Item2, center.Z + cell.Item3);
            if (keep != null && pos.Equals(keep)) continue;
            if (!ba.IsValidPos(pos) || ba.GetChunkAtBlockPos(pos) == null) continue;

            var found = ba.GetBlock(pos, BlockLayersAccess.Solid);
            if (found is BlockGeneratorConsole) continue;   // the console stands on the site and is never overwritten

            checkedCells++;
            if (found.Id != IdOf(code)) mismatches++;
        }

        return mismatches;
    }
}
