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

/// <summary>
/// The procedural design of the generator. This file has no game dependencies on purpose,
/// so the same code drives the mod and the offline preview exporter in tools/.
///
/// Layout, looking down (north is -Z):
///   - hull and shaft lining: an octagon 9 blocks across (HullRadius 4)
///   - support frames: paired columns 11 blocks apart, on the four axes
///   - injection pumps: 3x3 towers on the four diagonals
///   - foundation pad: 17 blocks across (PadRadius 8)
/// </summary>
public static class GeneratorBlueprint
{
    public const int Height = 40;
    public const int MaxDepth = 80;
    public const int MinDepth = 24;

    public const int HullRadius = 4;
    public const int FrameRadius = 5;
    public const int PadRadius = 8;
    public const int ClearRadiusUpper = 6;

    /// <summary>Where the control console stands relative to the centre, before rotation: on the pad rim.</summary>
    public const int ConsoleDistance = 8;

    public const int CoreBottom = 3;
    public const int CoreTop = 14;

    public static readonly string[] StageIds =
    {
        "shaft", "valves", "pumps", "core", "frame-a", "frame-b", "hull"
    };

    // Service ladder runs down the north side of the shaft, one cell in from the lining.
    private const int LadderX = 1;
    private const int LadderZ = -3;

    public static int ClampDepth(int depth) => Math.Clamp(depth, MinDepth, MaxDepth);

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

    /// <summary>Offsets of the core heart column, which swaps from cold to lit when the generator is fired.</summary>
    public static IEnumerable<(int X, int Y, int Z)> CoreHeartCells()
    {
        for (int y = CoreBottom; y <= CoreTop; y++) yield return (0, y, 0);
    }

    /// <summary>Top of the exhaust stack, where steam leaves a running generator.</summary>
    public static (int X, int Y, int Z) StackTop => (0, Height, 0);

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

    /// <summary>Walkway cells of a shaft platform: everything inside the lining except the 3x3 well around the riser.</summary>
    private static bool IsWalkway(int x, int z)
    {
        return InInterior(x, z, HullRadius) && Math.Max(Math.Abs(x), Math.Abs(z)) >= 2 && !(x == LadderX && z == LadderZ);
    }

    public static IEnumerable<int> PlatformLevels(int depth)
    {
        for (int y = -8; y >= -depth + 6; y -= 16) yield return y;
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

    /// <summary>Rotates the axis suffix of an oriented block code (…-we / …-ns) with the layout.</summary>
    private static string? RotCode(string? code, int quarterTurns)
    {
        if (code == null || (quarterTurns & 1) == 0) return code;
        if (code.EndsWith("-we", StringComparison.Ordinal)) return code[..^3] + "-ns";
        if (code.EndsWith("-ns", StringComparison.Ordinal)) return code[..^3] + "-we";
        return code;
    }

    // ---------------------------------------------------------------- stage 1

    private static StageBlueprint BuildShaft(int depth)
    {
        var b = new StageBuilder("shaft");

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
            bool collar = y >= -3;
            bool platform = false;
            foreach (int p in PlatformLevels(depth)) platform |= p == y;

            if (collar)
            {
                for (int x = -PadRadius; x <= PadRadius; x++)
                    for (int z = -PadRadius; z <= PadRadius; z++)
                        if (InDisc(x, z, PadRadius) && !InDisc(x, z, HullRadius)) b.Set(x, y, z, Blocks.Foundation);
            }

            string lining = y == 0 || y % 8 == 0 ? Blocks.LiningRibbed : Blocks.LiningPlain;
            ForDisc(HullRadius, (x, z) => { if (InShell(x, z, HullRadius)) b.Set(x, y, z, lining); });

            ForDisc(HullRadius, (x, z) =>
            {
                if (!InInterior(x, z, HullRadius)) return;
                if (x == LadderX && z == LadderZ) b.Set(x, y, z, Blocks.Ladder);
                else if (y == 0) b.Set(x, y, z, Blocks.Grating);
                else if (platform && IsWalkway(x, z)) b.Set(x, y, z, Blocks.Grating);
                else b.Set(x, y, z, null);
            });
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

        // Wellhead and main riser, bottom up.
        b.Set(0, -depth + 1, 0, Blocks.ValveBody);
        for (int y = -depth + 2; y <= 0; y++)
        {
            if (!stations.Contains(y))
            {
                b.Set(0, y, 0, y == 0 ? Blocks.Junction : Blocks.PipeUD);
                continue;
            }

            b.Set(0, y, 0, Blocks.Junction);
            for (int k = 0; k < 4; k++)
            {
                void Arm(int dist, int dy, string code)
                {
                    var (x, z) = Rot(dist, 0, k);
                    b.Set(x, y + dy, z, RotCode(code, k));
                }

                Arm(1, 0, Blocks.PipeWE);
                Arm(2, 0, Blocks.ValveBody);
                Arm(3, 0, Blocks.PipeWE);
                Arm(4, 0, Blocks.HullVent);   // relief port set into the lining
                Arm(2, 1, Blocks.ValveWheel);
            }
        }

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 3

    private static StageBlueprint BuildPumps(int depth)
    {
        var b = new StageBuilder("pumps");

        for (int k = 0; k < 4; k++)
        {
            void Put(int x, int y, int z, string code)
            {
                var (rx, rz) = Rot(x, z, k);
                b.Set(rx, y, rz, RotCode(code, k));
            }

            // Injection line: down through the pad, in through the lining, then down the shaft wall.
            Put(2, -depth + 1, 3, Blocks.Junction);
            for (int y = -depth + 2; y <= -2; y++) Put(2, y, 3, Blocks.PipeUD);
            Put(2, -1, 3, Blocks.Junction);
            Put(2, -1, 4, Blocks.Junction);
            Put(3, -1, 4, Blocks.PipeWE);
            Put(4, -1, 4, Blocks.Junction);
            Put(4, 0, 4, Blocks.PipeUD);

            // Pump tower, 3x3 centred on the diagonal.
            const int c = 5;
            for (int y = 1; y <= 10; y++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        bool centre = dx == 0 && dz == 0;
                        bool corner = dx != 0 && dz != 0;
                        if (y <= 2 || y == 8) Put(c + dx, y, c + dz, Blocks.PumpCasing);
                        else if (y <= 7 && centre) Put(c + dx, y, c + dz, Blocks.PumpCylinder);
                        else if (y <= 7 && corner) Put(c + dx, y, c + dz, Blocks.GirderUD);
                        else if (y >= 9 && centre) Put(c + dx, y, c + dz, Blocks.PipeUD);
                    }
                }
            }
        }

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 4

    private static StageBlueprint BuildCore()
    {
        var b = new StageBuilder("core");
        const int vesselR = 2;
        const int capTop = CoreTop + 2;
        const int header = capTop + 3;

        for (int y = 1; y <= capTop; y++)
        {
            bool solid = y < CoreBottom || y > CoreTop;
            ForDisc(vesselR, (x, z) =>
            {
                if (solid) { b.Set(x, y, z, Blocks.CoreCasing); return; }
                if (x == 0 && z == 0) { b.Set(x, y, z, Blocks.CoreHeartCold); return; }
                if (!InShell(x, z, vesselR)) return;

                bool onAxis = x == 0 || z == 0;
                bool window = onAxis && y > CoreBottom && y < CoreTop;
                b.Set(x, y, z, window ? Blocks.CoreGrille : Blocks.CoreCasing);
            });

            // Standpipes sit in the four notches of the vessel.
            for (int k = 0; k < 4; k++)
            {
                var (x, z) = Rot(2, 2, k);
                b.Set(x, y, z, Blocks.PipeUD);
            }
        }

        // Exhaust and standpipes up to the distribution header.
        for (int y = capTop + 1; y < header; y++)
        {
            b.Set(0, y, 0, Blocks.PipeUD);
            for (int k = 0; k < 4; k++)
            {
                var (x, z) = Rot(2, 2, k);
                b.Set(x, y, z, Blocks.PipeUD);
            }
        }

        // Distribution header: a square ring over the vessel with four spokes to the exhaust.
        b.Set(0, header, 0, Blocks.Junction);
        for (int k = 0; k < 4; k++)
        {
            void Put(int x, int z, string code)
            {
                var (rx, rz) = Rot(x, z, k);
                b.Set(rx, header, rz, RotCode(code, k));
            }

            Put(1, 0, Blocks.PipeWE);
            Put(2, 0, Blocks.Junction);
            Put(2, 1, Blocks.PipeNS);
            Put(2, 2, Blocks.Junction);
            Put(1, 2, Blocks.PipeWE);
        }

        // Exhaust line up to where the stack will start.
        for (int y = header + 1; y <= 34; y++) b.Set(0, y, 0, Blocks.PipeUD);

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 5 (twice)

    private static StageBlueprint BuildFrame(string id, int quarterTurn)
    {
        var b = new StageBuilder(id);
        const int top = 36;

        void Put(int x, int y, int z, string code)
        {
            var (rx, rz) = Rot(x, z, quarterTurn);
            b.Set(rx, y, rz, RotCode(code, quarterTurn));
        }

        for (int y = 1; y <= top; y++)
        {
            foreach (int sx in new[] { 1, -1 })
            {
                foreach (int sz in new[] { 1, -1 })
                {
                    int x = sx * FrameRadius, z = sz * 2;

                    // Main column, with a stepped buttress outboard of it.
                    Put(x, y, z, y == top ? Blocks.GirderNode : Blocks.GirderUD);
                    if (y <= 10) Put(x + sx, y, z, y == 10 ? Blocks.GirderNode : Blocks.GirderUD);
                    if (y <= 5) Put(x + 2 * sx, y, z, y == 5 ? Blocks.GirderNode : Blocks.GirderUD);
                }

                // Cross ties between the paired columns.
                if (y % 4 == 0 && y < top)
                    for (int z = -1; z <= 1; z++) Put(sx * FrameRadius, y, z, Blocks.GirderNS);
            }
        }

        // Head beams spanning the tower.
        foreach (int sz in new[] { 1, -1 })
            for (int x = -FrameRadius + 1; x <= FrameRadius - 1; x++)
                Put(x, top, sz * 2, Math.Abs(x) == 2 ? Blocks.GirderNode : Blocks.GirderWE);

        return b.Build();
    }

    // ---------------------------------------------------------------- stage 6

    private static StageBlueprint BuildHull()
    {
        var b = new StageBuilder("hull");
        const int drumTop = 28;
        const int shoulder = 29;
        const int crownTop = 33;
        const int roof = 34;
        const int gallery = 21;

        for (int y = 1; y <= Height; y++)
        {
            if (y <= drumTop)
            {
                bool band = y <= 2 || y % 8 == 0 || y == drumTop;
                ForDisc(HullRadius, (x, z) =>
                {
                    if (!InShell(x, z, HullRadius)) return;
                    bool onAxis = x == 0 || z == 0;
                    if (onAxis && y <= 2) return;                       // doorway between the frame columns
                    if (onAxis && !band && y >= 5) b.Set(x, y, z, Blocks.HullGrille);
                    else b.Set(x, y, z, band ? Blocks.HullRiveted : Blocks.HullPlain);
                });

                if (y <= gallery) b.Set(LadderX, y, LadderZ, Blocks.Ladder);
                if (y == gallery)
                    ForDisc(HullRadius, (x, z) => { if (IsWalkway(x, z)) b.Set(x, y, z, Blocks.Grating); });
            }
            else if (y == shoulder)
            {
                ForDisc(HullRadius, (x, z) => { if (!InInterior(x, z, 3)) b.Set(x, y, z, Blocks.HullRiveted); });
            }
            else if (y <= crownTop)
            {
                ForDisc(3, (x, z) =>
                {
                    if (InShell(x, z, 3)) b.Set(x, y, z, y == crownTop ? Blocks.HullRiveted : Blocks.HullPlain);
                });
            }
            else if (y == roof)
            {
                ForDisc(3, (x, z) => { if (x != 0 || z != 0) b.Set(x, y, z, Blocks.HullRiveted); });
            }
            else
            {
                ForDisc(1, (x, z) =>
                {
                    if (x != 0 || z != 0) b.Set(x, y, z, y == Height ? Blocks.HullRiveted : Blocks.HullPlain);
                });
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
    public const string LiningPlain = D + "shaftlining-plain";
    public const string LiningRibbed = D + "shaftlining-ribbed";
    public const string Grating = D + "grating";
    public const string Ladder = D + "serviceladder-north";

    public const string PipeUD = D + "steampipe-ud";
    public const string PipeNS = D + "steampipe-ns";
    public const string PipeWE = D + "steampipe-we";
    public const string Junction = D + "pipejunction";
    public const string ValveBody = D + "valvebody";
    public const string ValveWheel = D + "valvewheel";

    public const string PumpCasing = D + "pumpcasing";
    public const string PumpCylinder = D + "pumpcylinder";

    public const string CoreCasing = D + "corecasing";
    public const string CoreGrille = D + "coregrille";
    public const string CoreHeartCold = D + "coreheart-cold";
    public const string CoreHeartLit = D + "coreheart-lit";

    public const string GirderUD = D + "girder-ud";
    public const string GirderNS = D + "girder-ns";
    public const string GirderWE = D + "girder-we";
    public const string GirderNode = D + "girdernode";

    public const string HullPlain = D + "hullplate-plain";
    public const string HullRiveted = D + "hullplate-riveted";
    public const string HullVent = D + "hullplate-vent";
    public const string HullGrille = D + "hullgrille";

    public const string Console = D + "generatorconsole";
}
