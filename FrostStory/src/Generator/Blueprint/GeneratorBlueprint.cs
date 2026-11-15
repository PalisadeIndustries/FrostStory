using System;
using System.Collections.Generic;

namespace FrostStory.Generator.Blueprint;

/// <summary>
/// One block the blueprint wants at an offset from the generator's centre.
/// Y = 0 is the deck (the layer that replaces the ground surface), Y = 1 is the first layer above ground.
/// A null <see cref="Code"/> means "clear this cell".
/// </summary>
public readonly record struct Placement(int X, int Y, int Z, string? Code);

/// <summary>A construction stage: its id and the blocks it adds, in the order they should be built.</summary>
public sealed class StageBlueprint
{
    public string Id { get; }
    public IReadOnlyList<Placement> Placements { get; }

    public StageBlueprint(string id, IReadOnlyList<Placement> placements)
    {
        Id = id;
        Placements = placements;
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
///   - hull: an octagon 9 blocks across the flats, shaft liner 7 across
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
    public const int PadRadius = 8;
    public const int ClearRadiusUpper = 7;

    /// <summary>Where the control console stands relative to the centre, before rotation: on the pad rim.</summary>
    public const int ConsoleDistance = 8;

    public static readonly string[] StageIds =
    {
        "shaft", "valves", "pumps", "core", "frame-a", "frame-b", "hull"
    };

    // Service ladder runs up the north side, one cell in from the wall.
    private const int LadderX = 1;
    private const int LadderZ = -3;
    private const int Gallery = 21;

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
            BuildHull()
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

    /// <summary>Shaft cells that lie behind the diagonal faces of the modelled liner.</summary>
    private static bool BehindLiner(int x, int z) => InInterior(x, z, HullRadius) && Math.Abs(x) + Math.Abs(z) >= 5;

    /// <summary>Walkway cells of a platform: everything inside the wall except the 3x3 well and the ladder way.</summary>
    private static bool IsWalkway(int x, int z, bool inShaft)
    {
        return InInterior(x, z, HullRadius)
            && !(inShaft && BehindLiner(x, z))
            && Math.Max(Math.Abs(x), Math.Abs(z)) >= 2
            && !(x == LadderX && z == LadderZ);
    }

    /// <summary>Cells under an outside gallery: an octagonal ring between 5.5 and 6.7 blocks from the axis.</summary>
    private static bool InOuterGallery(int x, int z)
    {
        int ax = Math.Abs(x), az = Math.Abs(z);
        double r = Math.Max(Math.Max(ax, az), (ax + az) / Math.Sqrt(2));
        return r >= 5.5 && r <= 6.75;
    }

    private static int Modules(int depth) => depth / ShaftModule;

    /// <summary>Cell an 8 m shaft lift is anchored at; the three families of lift use three columns.</summary>
    private static int ModuleAnchorY(int module) => -ShaftModule * module - 4;

    /// <summary>Platforms cap every other lift.</summary>
    public static IEnumerable<int> PlatformLevels(int depth)
    {
        for (int m = 1; m < Modules(depth); m += 2) yield return -ShaftModule * m;
    }

    /// <summary>Valve stations hang three blocks above every other platform, clear of a walker's head.</summary>
    public static IEnumerable<int> ValveLevels(int depth)
    {
        int i = 0;
        foreach (int y in PlatformLevels(depth))
        {
            if (i++ % 2 == 0) yield return y + 3;
        }
    }

    private static (int X, int Z) Rot(int x, int z, int quarterTurns)
    {
        for (int i = 0; i < (quarterTurns & 3); i++) (x, z) = (-z, x);
        return (x, z);
    }

    // ---------------------------------------------------------------- stage 1

    private static StageBlueprint BuildShaft(int depth)
    {
        var b = new StageBuilder("shaft");
        var platforms = new HashSet<int>(PlatformLevels(depth));

        // Clear the site from the top down so nothing is left hanging over the works.
        for (int y = Height; y >= 1; y--)
        {
            int r = y > 12 ? ClearRadiusUpper : PadRadius;
            for (int x = -r; x <= r; x++)
                for (int z = -r; z <= r; z++)
                    if (InDisc(x, z, r)) b.Set(x, y, z, null);
        }

        // Shaft, top down. Within a layer the lining goes in before the rock comes out.
        for (int y = 0; y > -depth; y--)
        {
            if (y >= -3)
            {
                for (int x = -PadRadius; x <= PadRadius; x++)
                    for (int z = -PadRadius; z <= PadRadius; z++)
                        if (InDisc(x, z, PadRadius) && !InDisc(x, z, HullRadius)) b.Set(x, y, z, Blocks.Foundation);
            }

            // the top course is part of the pad; below it the rock is lined
            string ring = y == 0 ? Blocks.Foundation : Blocks.Lining;
            ForDisc(HullRadius, (x, z) => { if (InShell(x, z, HullRadius)) b.Set(x, y, z, ring); });

            ForDisc(HullRadius, (x, z) =>
            {
                if (!InInterior(x, z, HullRadius)) return;
                if (x == LadderX && z == LadderZ) b.Set(x, y, z, Blocks.FillLadder);
                else if (BehindLiner(x, z)) b.Set(x, y, z, Blocks.FillSolid);
                else if (y == 0) b.Set(x, y, z, Blocks.FillDeck);
                else if (platforms.Contains(y) && IsWalkway(x, z, true)) b.Set(x, y, z, Blocks.FillDeck);
                else b.Set(x, y, z, null);
            });

            // The modelled liner follows the dig down, one lift at a time.
            if (y == -1) b.Set(-1, y, 0, Blocks.Segment("collar"));
            for (int m = 0; m < Modules(depth); m++)
            {
                if (y == ModuleAnchorY(m)) b.Set(-1, y, 0, Blocks.Segment(m % 2 == 1 ? "shaftplatform" : "shaftplain"));
            }
        }

        // Floor plug.
        ForDisc(HullRadius, (x, z) => b.Set(x, -depth, z, Blocks.Foundation));

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 2

    private static StageBlueprint BuildValves(int depth)
    {
        var b = new StageBuilder("valves");
        var stations = new HashSet<int>(ValveLevels(depth));
        int modules = Modules(depth);

        // Main riser, bottom up, with a valve station wherever one is due.
        for (int y = -depth + 1; y <= -1; y++)
        {
            b.Set(0, y, 0, Blocks.FillSolid);

            for (int m = 0; m < modules; m++)
            {
                if (y == ModuleAnchorY(m)) b.Set(0, y, 0, Blocks.Segment(m == modules - 1 ? "riserbase" : "riser"));
            }

            if (!stations.Contains(y)) continue;

            b.Set(0, y, 0, Blocks.Segment("valvestation"));
            for (int k = 0; k < 4; k++)
            {
                for (int dist = 1; dist <= 3; dist++)
                {
                    var (x, z) = Rot(dist, 0, k);
                    b.Set(x, y, z, Blocks.FillSolid);
                }
            }
        }

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 3

    private static StageBlueprint BuildPumps(int depth)
    {
        var b = new StageBuilder("pumps");

        // Injection lines down the shaft wall, one lift at a time from the bottom.
        for (int y = -depth + 1; y <= -1; y++)
        {
            for (int k = 0; k < 4; k++)
            {
                var (x, z) = Rot(3, -1, k);
                b.Set(x, y, z, Blocks.FillSolid);
            }

            for (int m = 0; m < Modules(depth); m++)
            {
                if (y == ModuleAnchorY(m)) b.Set(1, y, 0, Blocks.Segment("injectors"));
            }
        }

        // Pump towers on the diagonals.
        for (int k = 0; k < 4; k++)
        {
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

        // Feed-water ring main round the foot of the tower.
        b.Set(6, 1, 0, Blocks.Segment("ringmain"));

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 4

    private static StageBlueprint BuildCore()
    {
        var b = new StageBuilder("core");

        for (int y = 1; y <= 34; y++)
        {
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
                if (y <= Gallery) b.Set(LadderX, y, LadderZ, Blocks.FillLadder);
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

    // ---------------------------------------------------------------- helpers

    private static void ForDisc(int r, Action<int, int> cell)
    {
        for (int x = -r; x <= r; x++)
            for (int z = -r; z <= r; z++)
                if (InDisc(x, z, r)) cell(x, z);
    }

    /// <summary>Collects placements in build order; writing a cell twice keeps its first slot but the last code.</summary>
    private sealed class StageBuilder
    {
        private readonly string id;
        private readonly List<Placement> list = new();
        private readonly Dictionary<(int, int, int), int> index = new();

        public StageBuilder(string id) => this.id = id;

        public void Set(int x, int y, int z, string? code)
        {
            var key = (x, y, z);
            if (index.TryGetValue(key, out int i)) list[i] = new Placement(x, y, z, code);
            else
            {
                index[key] = list.Count;
                list.Add(new Placement(x, y, z, code));
            }
        }

        public StageBlueprint Build() => new(id, list);
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
