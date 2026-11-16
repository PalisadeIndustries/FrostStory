using System;
using System.Collections.Generic;

namespace FrostStory.Generator.Blueprint;

/// <summary>
/// One block the blueprint wants at an offset from the generator's centre.
/// Y = 0 is the deck (the layer that replaces the ground surface), Y = 1 is the first layer above ground.
/// A null <see cref="Code"/> means "clear this cell".
/// </summary>
public readonly record struct Placement(int X, int Y, int Z, string? Code);

/// <summary>
/// One piece of a stage: the unit that goes up when its share of the stage's materials has been delivered.
/// <see cref="Key"/> names it (lang key "step-" + Key); <see cref="Number"/> of <see cref="Of"/> counts
/// repeated pieces such as shaft lifts. <see cref="Weight"/> is its share of the stage's cost.
/// </summary>
public sealed class StepBlueprint
{
    public string Key { get; }
    public int Number { get; }
    public int Of { get; }
    public int Weight { get; }
    public IReadOnlyList<Placement> Placements { get; }

    public StepBlueprint(string key, int number, int of, int weight, IReadOnlyList<Placement> placements)
    {
        Key = key;
        Number = number;
        Of = of;
        Weight = weight;
        Placements = placements;
    }
}

/// <summary>A construction stage: its id and its pieces, in the order they are built.</summary>
public sealed class StageBlueprint
{
    public string Id { get; }
    public IReadOnlyList<StepBlueprint> Steps { get; }

    /// <summary>Every placement of every piece, in build order.</summary>
    public IReadOnlyList<Placement> Placements { get; }

    public StageBlueprint(string id, IReadOnlyList<StepBlueprint> steps)
    {
        Id = id;
        Steps = steps;
        var all = new List<Placement>();
        foreach (var step in steps) all.AddRange(step.Placements);
        Placements = all;
    }

    /// <summary>
    /// How much of a stage requirement must have been delivered before piece <paramref name="step"/> can go
    /// up: its running share of the total, with the last piece taking whatever is left.
    /// </summary>
    public int Threshold(int step, int stageQuantity)
    {
        if (step >= Steps.Count - 1) return stageQuantity;

        int total = 0, upTo = 0;
        for (int i = 0; i < Steps.Count; i++)
        {
            total += Steps[i].Weight;
            if (i <= step) upTo += Steps[i].Weight;
        }

        return (int)Math.Ceiling(stageQuantity * (double)upTo / total);
    }
}

/// <summary>A cell whose block changes when the generator is fired or banked.</summary>
public readonly record struct LitSwap(int X, int Y, int Z, string Cold, string Lit);

/// <summary>
/// The design of the generator. This file has no game dependencies on purpose, so the same code drives
/// the mod and the offline preview exporter in tools/.
///
/// What you see is a set of large modelled segments (shapes made by tools/gen_segments.py), each anchored
/// to one cell. What you bump into is a matching set of invisible filler cells. The foundation pad and
/// the rock lining of the shaft are ordinary blocks. Dimensions here and in gen_segments.py go together.
///
/// Looking down (north is -Z):
///   - hull: an octagon 9 blocks across the flats; shaft liner 7 across at the mouth, 11 below the throat
///   - support frames: paired columns just outside the hull, on the four axes
///   - injection pumps: towers on the four diagonals
///   - foundation pad: 17 blocks across
/// </summary>
public static class GeneratorBlueprint
{
    public const int Height = 40;
    public const int MaxDepth = 80;
    public const int MinDepth = 24;

    /// <summary>The shaft is built in lifts of this height; its depth is always a whole number of them.</summary>
    public const int ShaftModule = 8;

    public const int HullRadius = 4;

    /// <summary>
    /// The shaft mouth is the hull's width, because the tower stands on its rim. Three blocks down, under
    /// the pad, the shaft opens out to this radius.
    /// </summary>
    public const int ShaftRadius = 6;
    private const int ThroatBottom = -2;
    public const int PadRadius = 8;
    public const int ClearRadiusUpper = 9;

    /// <summary>Where the control console stands relative to the centre, before rotation: on the pad rim.</summary>
    public const int ConsoleDistance = 8;

    public static readonly string[] StageIds =
    {
        "shaft", "valves", "pumps", "core", "frame-a", "frame-b", "hull", "jacket"
    };

    // Service ladder runs up the north side, one cell in from the wall.
    private const int LadderX = 1;
    private const int LadderZ = -3;
    private const int Gallery = 21;

    // The temporary headframe is anchored where the core's plinth will later stand, which replaces it.
    private const int HeadframeX = -2;

    // Outside service galleries ring the hull beyond the frames; a ladder on the south frame reaches them.
    private static readonly int[] OuterGalleries = { 12, 24 };
    private const int OuterLadderX = 1;
    private const int OuterLadderZ = 6;

    public static int ClampDepth(int depth)
    {
        depth = Math.Clamp(depth, MinDepth, MaxDepth);
        return depth - depth % ShaftModule;
    }

    public static IReadOnlyList<StageBlueprint> Build(int depth)
    {
        depth = ClampDepth(depth);
        return new[]
        {
            BuildShaft(depth),
            BuildValves(depth),
            BuildPumps(depth),
            BuildCore(),
            BuildFrame("frame-a", 0),
            BuildFrame("frame-b", 1),
            BuildHull(),
            BuildJacket()
        };
    }

    /// <summary>
    /// What changes when the generator is fired: the core and the two hull lifts around it take their lit
    /// models, and the steam ports go live.
    /// </summary>
    public static IReadOnlyList<LitSwap> LitSwaps { get; } = BuildLitSwaps();

    private static LitSwap[] BuildLitSwaps()
    {
        var swaps = new List<LitSwap>
        {
            new(0, 2, 0, Blocks.Segment("corelower"), Blocks.Segment("corelowerlit")),
            new(0, 10, 0, Blocks.Segment("coreupper"), Blocks.Segment("coreupperlit")),
            new(1, 2, 0, Blocks.Segment("hulla"), Blocks.Segment("hullalit")),
            new(1, 12, 0, Blocks.Segment("hullb"), Blocks.Segment("hullblit"))
        };

        foreach (var (x, z, side) in SteamPorts())
            swaps.Add(new LitSwap(x, 1, z, Blocks.Port(side, false), Blocks.Port(side, true)));

        foreach (var cell in MainPortCells())
        {
            if (cell.Code == Blocks.MainPort(cell.Side, false))
                swaps.Add(new LitSwap(cell.X, cell.Y, cell.Z, cell.Code, Blocks.MainPort(cell.Side, true)));
        }

        return swaps.ToArray();
    }

    /// <summary>
    /// Eight steam ports stand on the pad rim, two to a side, each facing outward. Run steam pipes from them.
    /// </summary>
    public static IEnumerable<(int X, int Z, string Side)> SteamPorts()
    {
        string[] sides = { "east", "south", "west", "north" };
        for (int k = 0; k < 4; k++)
        {
            foreach (int offset in new[] { 3, -3 })
            {
                var (x, z) = Rot(7, offset, k);
                yield return (x, z, sides[k]);
            }
        }
    }

    /// <summary>Top of the exhaust stack, where steam leaves a running generator.</summary>
    public static (int X, int Y, int Z) StackTop => (0, Height, 0);

    /// <summary>Height of the lower layer of the large steam ports: their bore is centred 3 m above the first gallery.</summary>
    public const int MainPortY = 15;

    /// <summary>
    /// Four large steam ports, one to a side, each a 2 x 2 nozzle running from the hull out through the frame
    /// to a mouth over the first gallery. The mouth is a port block in its minimum-corner cell plus three
    /// fill cells; the nozzle behind it is plain collision. A steam main set against the mouth lines up with it.
    /// </summary>
    public static IEnumerable<(int X, int Y, int Z, string Code, string Side)> MainPortCells()
    {
        string[] sides = { "east", "south", "west", "north" };
        for (int k = 0; k < 4; k++)
        {
            var mouth = new List<(int X, int Y, int Z)>();
            for (int depth = 5; depth <= 7; depth++)
            {
                for (int across = 0; across <= 1; across++)
                {
                    for (int up = 0; up <= 1; up++)
                    {
                        var (x, z) = Rot(depth, across, k);
                        if (depth == 7) mouth.Add((x, MainPortY + up, z));
                        else yield return (x, MainPortY + up, z, Blocks.FillSolid, sides[k]);
                    }
                }
            }

            int minX = int.MaxValue, minZ = int.MaxValue;
            foreach (var c in mouth) { minX = Math.Min(minX, c.X); minZ = Math.Min(minZ, c.Z); }

            foreach (var c in mouth)
            {
                int dx = c.X - minX, dy = c.Y - MainPortY, dz = c.Z - minZ;
                string code = dx + dy + dz == 0 ? Blocks.MainPort(sides[k], false) : Blocks.MainFill(dx, dy, dz);
                yield return (c.X, c.Y, c.Z, code, sides[k]);
            }
        }
    }

    // ---------------------------------------------------------------- geometry

    public static bool InDisc(int x, int z, int r)
    {
        double lim = r + 0.5;
        return x * x + z * z <= lim * lim;
    }

    /// <summary>Outermost ring of a disc: cells with at least one face neighbour outside it. Watertight.</summary>
    public static bool InShell(int x, int z, int r)
    {
        return InDisc(x, z, r)
            && !(InDisc(x + 1, z, r) && InDisc(x - 1, z, r) && InDisc(x, z + 1, r) && InDisc(x, z - 1, r));
    }

    public static bool InInterior(int x, int z, int r) => InDisc(x, z, r) && !InShell(x, z, r);

    private static int ShaftRadiusAt(int y) => y >= ThroatBottom ? HullRadius : ShaftRadius;

    /// <summary>Shaft cells that lie behind the diagonal faces of the modelled liner.</summary>
    private static bool BehindLiner(int x, int y, int z)
    {
        int r = ShaftRadiusAt(y);
        return InInterior(x, z, r) && Math.Abs(x) + Math.Abs(z) >= (r == HullRadius ? 5 : 8);
    }

    /// <summary>Walkway cells of a platform: everything inside the wall except the 3x3 well and the ladder way.</summary>
    private static bool IsWalkway(int x, int z, bool inShaft)
    {
        if (x == LadderX && z == LadderZ) return false;

        // In the shaft the platforms are catwalks round the wall, leaving the middle open; the ladder
        // hangs at their inner edge. In the hull the gallery leaves only a well round the exhaust.
        return inShaft
            ? InInterior(x, z, ShaftRadius) && !BehindLiner(x, ThroatBottom - 1, z) && OctRadius(x, z) >= 3.9
            : InInterior(x, z, HullRadius) && Math.Max(Math.Abs(x), Math.Abs(z)) >= 2;
    }

    /// <summary>Cells under an outside gallery: an octagonal ring between 5.5 and 6.7 blocks from the axis.</summary>
    private static bool InOuterGallery(int x, int z)
    {
        int ax = Math.Abs(x), az = Math.Abs(z);
        double r = Math.Max(Math.Max(ax, az), (ax + az) / Math.Sqrt(2));
        return r >= 5.5 && r <= 6.75;
    }

    /// <summary>Distance from the axis measured the way an octagon measures it: to the nearest flat or diagonal side.</summary>
    private static double OctRadius(int x, int z)
    {
        int ax = Math.Abs(x), az = Math.Abs(z);
        return Math.Max(Math.Max(ax, az), (ax + az) / Math.Sqrt(2));
    }

    // The insulating jacket rings the upper tower outside the galleries, then hoods in at 45 degrees.
    private const int JacketBottom = 20;
    private const int JacketTop = 33;

    private static bool InJacket(int x, int y, int z)
    {
        double r = OctRadius(x, z);
        if (y >= JacketBottom && y <= JacketTop) return r >= 7.6 && r <= 8.55;
        if (y == JacketTop + 1) return r >= 6.9 && r < 7.9;
        if (y == JacketTop + 2) return r >= 5.9 && r < 6.9;
        return false;
    }

    private static int Modules(int depth) => depth / ShaftModule;

    /// <summary>
    /// Height an 8 m shaft lift is anchored at. The three families of lift use three columns, all clear of
    /// the riser and of the valve arms, which can pass at this height.
    /// </summary>
    private static int ModuleAnchorY(int module) => -ShaftModule * module - 4;

    /// <summary>Platforms cap every other lift.</summary>
    public static IEnumerable<int> PlatformLevels(int depth)
    {
        for (int m = 1; m < Modules(depth); m += 2) yield return -ShaftModule * m;
    }

    /// <summary>
    /// Valve stations hang four blocks above every other platform: three clear blocks under the arms, with the
    /// inverted valves' handwheels just overhead.
    /// </summary>
    public static IEnumerable<int> ValveLevels(int depth)
    {
        int i = 0;
        foreach (int y in PlatformLevels(depth))
        {
            if (i++ % 2 == 0) yield return y + 4;
        }
    }

    private static (int X, int Z) Rot(int x, int z, int quarterTurns)
    {
        for (int i = 0; i < (quarterTurns & 3); i++) (x, z) = (-z, x);
        return (x, z);
    }

    /// <summary>The shaft's fittings go in from the bottom, two lifts (16 m) to a piece.</summary>
    private static List<(int Low, int High)> ShaftSections(int depth)
    {
        var sections = new List<(int, int)>();
        for (int m = Modules(depth) - 1; m >= 0; m -= 2)
        {
            int top = Math.Max(0, m - 1);
            sections.Add((-ShaftModule * m - (ShaftModule - 1), Math.Min(-1, -ShaftModule * top)));
        }

        return sections;
    }

    // ---------------------------------------------------------------- stage 1

    private static StageBlueprint BuildShaft(int depth)
    {
        var b = new StageBuilder("shaft");
        var platforms = new HashSet<int>(PlatformLevels(depth));
        int modules = Modules(depth);

        // Clear the site from the top down so nothing is left hanging over the works, and lay the pad.
        b.Step("site", weight: 2);
        for (int y = Height; y >= 1; y--)
        {
            int r = y > 12 ? ClearRadiusUpper : PadRadius;
            for (int x = -r; x <= r; x++)
                for (int z = -r; z <= r; z++)
                    if (InDisc(x, z, r)) b.Set(x, y, z, null);
        }

        for (int y = 0; y >= -3; y--)
        {
            int open = ShaftRadiusAt(y);
            for (int x = -PadRadius; x <= PadRadius; x++)
                for (int z = -PadRadius; z <= PadRadius; z++)
                    if (InDisc(x, z, PadRadius) && !InDisc(x, z, open)) b.Set(x, y, z, Blocks.Foundation);
        }

        // A headframe stands over the dig until the core takes its place.
        b.Set(HeadframeX, 2, 0, Blocks.Segment("headframe"));

        // The shaft goes down one lift at a time. Within a layer the lining goes in before the rock comes out.
        for (int m = 0; m < modules; m++)
        {
            b.Step("shaftlift", number: m + 1, of: modules);
            for (int y = -ShaftModule * m; y > -ShaftModule * (m + 1); y--)
            {
                // the top course is part of the pad; below it the rock is lined
                string ring = y == 0 ? Blocks.Foundation : Blocks.Lining;
                int r = ShaftRadiusAt(y);
                ForDisc(r, (x, z) => { if (InShell(x, z, r)) b.Set(x, y, z, ring); });

                ForDisc(r, (x, z) =>
                {
                    if (!InInterior(x, z, r)) return;
                    if (x == LadderX && z == LadderZ) b.Set(x, y, z, Blocks.FillLadderBack);
                    else if (BehindLiner(x, y, z)) b.Set(x, y, z, Blocks.FillSolid);
                    else if (y == 0) b.Set(x, y, z, Blocks.FillDeck);
                    else if (platforms.Contains(y) && IsWalkway(x, z, true)) b.Set(x, y, z, Blocks.FillDeck);
                    else b.Set(x, y, z, null);
                });

                if (y == -1) b.Set(-2, y, 0, Blocks.Segment("collar"));
                if (y == ModuleAnchorY(m)) b.Set(-2, y, -1, Blocks.Segment(m == 0 ? "shafttop" : m % 2 == 1 ? "shaftplatform" : "shaftplain"));
            }

            // Floor plug under the last lift.
            if (m == modules - 1) ForDisc(ShaftRadius, (x, z) => b.Set(x, -depth, z, Blocks.Foundation));
        }

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 2

    private static StageBlueprint BuildValves(int depth)
    {
        var b = new StageBuilder("valves");
        var stations = new HashSet<int>(ValveLevels(depth));
        int modules = Modules(depth);
        var sections = ShaftSections(depth);

        // Main riser, bottom up, with a valve station wherever one is due.
        for (int i = 0; i < sections.Count; i++)
        {
            b.Step("riser", number: i + 1, of: sections.Count);
            for (int y = sections[i].Low; y <= sections[i].High; y++)
            {
                ForDisc(1, (x, z) => b.Set(x, y, z, Blocks.FillSolid));     // the riser is three cells across

                for (int m = 0; m < modules; m++)
                {
                    if (y == ModuleAnchorY(m)) b.Set(0, y, 0, Blocks.Segment(m == modules - 1 ? "riserbase" : "riser"));
                }

                if (!stations.Contains(y)) continue;

                for (int k = 0; k < 4; k++)
                {
                    for (int dist = 2; dist <= ShaftRadius - 1; dist++)
                    {
                        var (x, z) = Rot(dist, 0, k);
                        b.Set(x, y, z, Blocks.FillSolid);
                    }
                }

                // anchored in the south arm: the axis cell at this height can belong to a riser lift
                b.Set(0, y, 1, Blocks.Segment("valvestation"));
            }
        }

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 3

    private static StageBlueprint BuildPumps(int depth)
    {
        var b = new StageBuilder("pumps");
        var sections = ShaftSections(depth);

        // Injection lines up the shaft wall from the bottom.
        for (int i = 0; i < sections.Count; i++)
        {
            b.Step("injectors", number: i + 1, of: sections.Count);
            for (int y = sections[i].Low; y <= sections[i].High; y++)
            {
                // the lines run down the wall of the wide shaft, and come out of its roof under the pad
                if (y < ThroatBottom)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        var (x, z) = Rot(ShaftRadius - 1, -1, k);
                        b.Set(x, y, z, Blocks.FillSolid);
                    }
                }

                for (int m = 0; m < Modules(depth); m++)
                {
                    if (y == ModuleAnchorY(m)) b.Set(2, y, 1, Blocks.Segment(m == 0 ? "injectorstop" : "injectors"));
                }
            }
        }

        // Feed-water ring main round the foot of the tower.
        b.Step("ringmain", weight: 2);
        b.Set(6, 1, 0, Blocks.Segment("ringmain"));

        // Pump towers on the diagonals, one at a time.
        for (int k = 0; k < 4; k++)
        {
            b.Step("pump", number: k + 1, of: 4, weight: 3);
            const int c = 5;
            for (int y = 1; y <= 10; y++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (y > 8 && (dx != 0 || dz != 0)) continue;
                        var (x, z) = Rot(c + dx, c + dz, k);
                        b.Set(x, y, z, Blocks.FillSolid);
                    }
                }
            }

            var (ax, az) = Rot(c, c, k);
            b.Set(ax, 1, az, Blocks.Segment("pump", k));
        }

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 4

    private static StageBlueprint BuildCore()
    {
        var b = new StageBuilder("core");

        for (int y = 1; y <= 34; y++)
        {
            switch (y)
            {
                case 1: b.Step("corelower", weight: 3); break;
                case 9: b.Step("coreupper", weight: 3); break;
                case 17: b.Step("manifold", weight: 2); break;
                case 23: b.Step("exhaust"); break;
            }

            if (y <= 16) ForDisc(2, (x, z) => b.Set(x, y, z, Blocks.FillSolid));
            else b.Set(0, y, 0, Blocks.FillSolid);

            switch (y)
            {
                case 2: b.Set(0, y, 0, Blocks.Segment("corelower")); break;
                case 10: b.Set(0, y, 0, Blocks.Segment("coreupper")); break;
                case 18: b.Set(0, y, 0, Blocks.Segment("manifold")); break;
                case 24: b.Set(0, y, 0, Blocks.Segment("exhaust")); break;
            }

            // The working lines come up through the pad as soon as the core has a foot.
            if (y == 2)
            {
                foreach (var (x, z, side) in SteamPorts()) b.Set(x, 1, z, Blocks.Port(side, false));
            }
        }

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 5 (twice)

    private static StageBlueprint BuildFrame(string id, int quarterTurn)
    {
        var b = new StageBuilder(id);
        const int top = 36;
        bool lapped = quarterTurn % 2 == 1;   // the second frame's head beams sit on the first's

        for (int y = 1; y <= top; y++)
        {
            if (y % 12 == 1) b.Step("framelift", number: y / 12 + 1, of: 3);

            foreach (int half in new[] { 0, 2 })
            {
                int turn = quarterTurn + half;

                void Fill(int x, int z)
                {
                    var (rx, rz) = Rot(x, z, turn);
                    b.Set(rx, y, rz, Blocks.FillSolid);
                }

                foreach (int sz in new[] { 2, -2 })
                {
                    Fill(5, sz);                                   // column
                    if (y <= 2) Fill(8, sz);                       // raking buttress, stepping in as it rises
                    if (y >= 3 && y <= 7) Fill(7, sz);
                    if (y >= 7 && y <= 11) Fill(6, sz);
                    if (y == top) for (int x = 0; x <= 4; x++) Fill(x, sz);   // head beam
                }

                if (y % 4 == 0 && y <= 32) for (int z = -1; z <= 1; z++) Fill(5, z);   // tie between the columns

                string? part = y switch
                {
                    2 => "framebase",
                    14 => "framemid",
                    26 => lapped ? "frametopb" : "frametop",
                    _ => null
                };
                if (part != null)
                {
                    var (ax, az) = Rot(5, 0, turn);
                    b.Set(ax, y, az, Blocks.Segment(part, turn));
                }
            }
        }

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 6

    private static StageBlueprint BuildHull()
    {
        var b = new StageBuilder("hull");

        for (int y = 1; y <= Height; y++)
        {
            switch (y)
            {
                case 1: b.Step("hulla", weight: 3); break;
                case 11: b.Step("hullb", weight: 4); break;
                case 20: b.Step("hullc", weight: 3); break;
                case 30: b.Step("shoulder", weight: 2); break;
                case 35: b.Step("stack", weight: 2); break;
            }

            if (y <= 29)
            {
                ForDisc(HullRadius, (x, z) =>
                {
                    if (!InShell(x, z, HullRadius)) return;
                    if ((x == 0 || z == 0) && y <= 2) return;     // doorway
                    b.Set(x, y, z, Blocks.FillSolid);
                });

                if (Array.IndexOf(OuterGalleries, y) >= 0)
                {
                    for (int x = -7; x <= 7; x++)
                        for (int z = -7; z <= 7; z++)
                            if (InOuterGallery(x, z)) b.Set(x, y, z, Blocks.FillDeck);
                }

                if (y == MainPortY)
                {
                    foreach (var cell in MainPortCells()) b.Set(cell.X, cell.Y, cell.Z, cell.Code);
                }

                if (y <= OuterGalleries[^1]) b.Set(OuterLadderX, y, OuterLadderZ, Blocks.FillLadder);
                if (y <= Gallery) b.Set(LadderX, y, LadderZ, Blocks.FillLadderBack);
                if (y == Gallery)
                    ForDisc(HullRadius, (x, z) => { if (IsWalkway(x, z, false)) b.Set(x, y, z, Blocks.FillDeck); });
            }
            else if (y == 30)
            {
                ForDisc(HullRadius, (x, z) => { if (!InDisc(x, z, 1)) b.Set(x, y, z, Blocks.FillSolid); });   // shoulder
            }
            else if (y <= 33)
            {
                ForDisc(2, (x, z) => { if (InShell(x, z, 2)) b.Set(x, y, z, Blocks.FillSolid); });            // crown
            }
            else if (y == 34)
            {
                ForDisc(2, (x, z) => { if (x != 0 || z != 0) b.Set(x, y, z, Blocks.FillSolid); });
            }
            else
            {
                ForDisc(1, (x, z) => { if (x != 0 || z != 0) b.Set(x, y, z, Blocks.FillSolid); });            // stack
            }

            string? part = y switch
            {
                2 => "hulla",
                12 => "hullb",
                22 => "hullc",
                30 => "shoulder",
                36 => "stack",
                _ => null
            };
            if (part != null) b.Set(1, y, 0, Blocks.Segment(part));
        }

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 7

    /// <summary>Which of the jacket's eight panels a cell belongs to: 0 is east, counting round towards north.</summary>
    private static int JacketPanel(int x, int z)
    {
        double turns = Math.Atan2(-z, x) / (Math.PI / 4);
        return ((int)Math.Round(turns) % 8 + 8) % 8;
    }

    /// <summary>
    /// The insulating jacket goes up as four pieces, each a pair of opposite panels with the beams that
    /// carry them: east and west, north and south, then the two diagonal pairs.
    /// </summary>
    private static StageBlueprint BuildJacket()
    {
        var b = new StageBuilder("jacket");

        // (panel, panel, part, turn, turn): the axis panel is modelled facing east, the diagonal one north-east
        var pairs = new[]
        {
            (0, 4, "jacketaxis", 0, 2),
            (2, 6, "jacketaxis", 3, 1),
            (1, 5, "jacketdiag", 0, 2),
            (3, 7, "jacketdiag", 3, 1)
        };

        for (int i = 0; i < pairs.Length; i++)
        {
            var (first, second, part, turnA, turnB) = pairs[i];
            b.Step("jacketpanels", number: i + 1, of: pairs.Length);

            for (int y = JacketBottom; y <= JacketTop + 2; y++)
            {
                for (int x = -9; x <= 9; x++)
                    for (int z = -9; z <= 9; z++)
                    {
                        if (!InJacket(x, y, z)) continue;
                        int panel = JacketPanel(x, z);
                        if (panel == first || panel == second) b.Set(x, y, z, Blocks.FillSolid);
                    }
            }

            foreach (int turn in new[] { turnA, turnB })
            {
                var (ax, az) = part == "jacketaxis" ? Rot(8, 0, turn) : Rot(6, -6, turn);
                b.Set(ax, 22, az, Blocks.Segment(part, turn));
            }
        }

        return b.Build();
    }

    // ---------------------------------------------------------------- helpers

    private static void ForDisc(int r, Action<int, int> cell)
    {
        for (int x = -r; x <= r; x++)
            for (int z = -r; z <= r; z++)
                if (InDisc(x, z, r)) cell(x, z);
    }

    /// <summary>
    /// Collects placements piece by piece, in build order. Within a piece, writing a cell twice keeps its
    /// first slot but the last code; a later piece that rewrites a cell adds its own placement, so every
    /// intermediate state is what the earlier pieces left.
    /// </summary>
    private sealed class StageBuilder
    {
        private readonly string id;
        private readonly List<StepBlueprint> steps = new();
        private readonly Dictionary<(int, int, int), int> index = new();
        private List<Placement> list = new();
        private (string Key, int Number, int Of, int Weight) current;
        private bool open;

        public StageBuilder(string id) => this.id = id;

        public void Step(string key, int number = 0, int of = 0, int weight = 1)
        {
            Close();
            current = (key, number, of, weight);
            list = new List<Placement>();
            index.Clear();
            open = true;
        }

        public void Set(int x, int y, int z, string? code)
        {
            if (!open) throw new InvalidOperationException("Begin a step before placing blocks.");

            var key = (x, y, z);
            if (index.TryGetValue(key, out int i)) list[i] = new Placement(x, y, z, code);
            else
            {
                index[key] = list.Count;
                list.Add(new Placement(x, y, z, code));
            }
        }

        private void Close()
        {
            if (open) steps.Add(new StepBlueprint(current.Key, current.Number, current.Of, current.Weight, list));
            open = false;
        }

        public StageBlueprint Build()
        {
            Close();
            return new StageBlueprint(id, steps);
        }
    }
}

/// <summary>Block codes used by the blueprint.</summary>
public static class Blocks
{
    private const string D = "froststory:";

    public const string Foundation = D + "foundation";
    public const string Lining = D + "shaftlining-plain";

    /// <summary>Invisible cells that give the modelled segments something to stand on and bump into.</summary>
    public const string FillSolid = D + "genfill-solid";
    public const string FillDeck = D + "genfill-deck";
    public const string FillLadder = D + "genfill-ladder";

    /// <summary>A ladder cell climbed from the wall side: its plate is on the far side of the cell.</summary>
    public const string FillLadderBack = D + "genfill-ladderback";

    public const string Console = D + "generatorconsole";

    /// <summary>A steam port on the pad; side is the way its outlet faces.</summary>
    public static string Port(string side, bool live) => $"{D}steamport-{side}-{(live ? "live" : "cold")}";

    /// <summary>The mouth of a large steam port; side is the way it faces.</summary>
    public static string MainPort(string side, bool live) => $"{D}mainport-{side}-{(live ? "live" : "cold")}";

    /// <summary>A fill cell of a two-block-wide conduit, offset from the cell its owner lives in.</summary>
    public static string MainFill(int dx, int dy, int dz) => $"{D}mainfill-o{dx}{dy}{dz}";

    /// <summary>A modelled segment, turned by quarter turns about the generator's axis (east to south is one).</summary>
    public static string Segment(string part, int turn = 0) => $"{D}gensegment-{part}-r{turn & 3}";
}
