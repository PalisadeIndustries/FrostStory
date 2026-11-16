using System;
using System.Collections.Generic;

namespace FrostStory.Generator.Blueprint;

/// <summary>
/// The wreck of a steam freighter, frozen in. Pure geometry: cells relative to the middle of the keel,
/// bow to the east (+X), keel at Y = 0. The hull is real plate, so it can be broken up for its iron;
/// the boilers, engine, funnel and masts are single models that are broken down whole.
///
/// From the stern: aft hold, engine room, boiler room with its coal bunker, fore hold, fore peak. A
/// deckhouse and wheelhouse stand amidships with the funnel behind them. Hatches over the holds and a
/// hole stove in the port side are the ways in.
/// </summary>
public static class ShipBlueprint
{
    public const int HalfLength = 26;
    public const int HalfBeam = 5;
    public const int Deck = 7;            // the main deck, in blocks above the keel
    public const int Waterline = 3;       // the ice comes up to here

    private const string D = "froststory:";
    public const string Plate = D + "shipplate";
    public const string Planks = D + "shipdeck";
    public const string Crate = D + "shipcrate";
    public const string Ladder = D + "serviceladder-north";
    public static string Part(string type) => D + "shippart-" + type;
    public static string Coal(int layers) => D + "coalheap-" + layers;

    private static readonly int[] Bulkheads = { -14, -4, 6, 16 };

    /// <summary>Half the hull's width at a station along it, on deck.</summary>
    private static double HalfWidth(int x)
    {
        if (x > 10) return HalfBeam * (1 - Math.Pow((x - 10) / 16.2, 1.7));            // the bow fines away to a stem
        if (x < -18) return HalfBeam * Math.Sqrt(Math.Max(0, 1 - Math.Pow((-18 - x) / 8.6, 2)));   // a rounded stern
        return HalfBeam;
    }

    /// <summary>The top of the hull at a station: the forecastle and the poop stand a deck higher.</summary>
    public static int TopAt(int x) => x >= 17 || x <= -21 ? Deck + 1 : Deck;

    /// <summary>Whether a cell is inside the hull's outer skin.</summary>
    public static bool InHull(int x, int y, int z)
    {
        if (x < -HalfLength || x > HalfLength || y < 0 || y > TopAt(x)) return false;
        double half = HalfWidth(x) - (y == 0 ? 2.6 : y == 1 ? 1.0 : 0);              // the turn of the bilge
        if (x > 10 && y < 3) half -= (3 - y) * (x - 10) / 9.0;                        // the forefoot cut away
        return half >= -0.2 && Math.Abs(z) <= Math.Round(half);
    }

    private static bool Skin(int x, int y, int z)
    {
        return !InHull(x + 1, y, z) || !InHull(x - 1, y, z) || !InHull(x, y, z + 1) || !InHull(x, y, z - 1) || !InHull(x, y - 1, z);
    }

    public static List<Placement> Build()
    {
        var cells = new Dictionary<(int, int, int), string?>();
        void Set(int x, int y, int z, string? code) => cells[(x, y, z)] = code;

        // ---- the hull: plate skin, a tank top to walk on, the decks, a rail round the top
        for (int x = -HalfLength; x <= HalfLength; x++)
        {
            int top = TopAt(x);
            for (int z = -HalfBeam; z <= HalfBeam; z++)
            {
                for (int y = 0; y <= top; y++)
                {
                    if (!InHull(x, y, z)) continue;
                    bool skin = Skin(x, y, z);
                    if (y == top) Set(x, y, z, skin ? Plate : Planks);
                    else if (skin || y == 1) Set(x, y, z, Plate);
                    else Set(x, y, z, null);
                }

                if (InHull(x, top, z) && Skin(x, top, z)) Set(x, top + 1, z, Plate);      // bulwark
            }

            // the step up to the forecastle and the poop
            if (x == 17 || x == -21)
            {
                for (int z = -HalfBeam; z <= HalfBeam; z++)
                {
                    if (InHull(x, Deck, z) && z != 0) Set(x, Deck, z, Plate);
                }
            }
        }

        // ---- bulkheads, each with a doorway on the centre line
        foreach (int x in Bulkheads)
        {
            for (int z = -HalfBeam; z <= HalfBeam; z++)
            {
                for (int y = 2; y < Deck; y++)
                {
                    if (InHull(x, y, z) && !Skin(x, y, z)) Set(x, y, z, z == 0 && y <= 3 ? null : Plate);
                }
            }
        }

        // ---- hatches over the holds, with a ladder down each
        void Hatch(int x0, int x1)
        {
            for (int x = x0; x <= x1; x++)
                for (int z = -1; z <= 1; z++)
                    Set(x, Deck, z, null);
            for (int y = 2; y <= Deck; y++) Set(x0, y, 2, Ladder);
        }

        Hatch(9, 12);
        Hatch(-19, -17);

        // ---- stove in on the port side, at the level of the ice
        foreach (var (x, y) in new[] { (8, 4), (9, 4), (10, 4), (11, 4), (9, 5), (10, 5), (11, 5), (10, 3), (9, 3), (10, 6) })
        {
            Set(x, y, -HalfBeam, null);
        }

        // ---- cargo
        var crates = new (int X, int Z, int High)[]
        {
            (8, -3, 2), (8, -2, 1), (9, 3, 2), (10, 3, 3), (11, 3, 1), (13, -3, 2), (14, -3, 2), (14, -2, 1), (14, 2, 1), (13, 3, 2), (7, 3, 1),
            (-16, -3, 2), (-17, -3, 1), (-16, 3, 2), (-15, 3, 1), (-19, -2, 2), (-20, 2, 1), (-15, -3, 3),
            (19, 0, 1), (18, 1, 2)
        };
        foreach (var (x, z, high) in crates)
        {
            for (int y = 2; y < 2 + high; y++)
            {
                if (InHull(x, y, z) && !Skin(x, y, z)) Set(x, y, z, Crate);
            }
        }

        // ---- boiler room: two boilers side by side, and the bunker across the after end of it
        Set(1, 2, -3, Part("boiler"));
        Set(1, 2, 2, Part("boiler"));
        for (int z = -4; z <= 4; z++)
        {
            if (z == 0) continue;      // the way through to the engine room
            Set(-3, 2, z, Coal(8));
            Set(-3, 3, z, Coal(Math.Abs(z) > 2 ? 8 : 5));
            Set(-2, 2, z, Coal(Math.Abs(z) > 1 ? 6 : 3));
        }

        // ---- engine room
        Set(-11, 2, -1, Part("engine"));

        // ---- deckhouse amidships, wheelhouse on top of it
        void House(int x0, int x1, int halfZ, int y0, int y1, int window, (int X, int Z) door)
        {
            for (int x = x0; x <= x1; x++)
            {
                for (int z = -halfZ; z <= halfZ; z++)
                {
                    bool wall = x == x0 || x == x1 || Math.Abs(z) == halfZ;
                    for (int y = y0; y <= y1; y++)
                    {
                        bool glass = y == window && ((x + z) & 1) == 0 && !((x == x0 || x == x1) && Math.Abs(z) == halfZ);
                        bool way = (x, z) == door && y <= y0 + 1;
                        Set(x, y, z, wall && !glass && !way ? Plate : null);
                    }

                    Set(x, y1 + 1, z, Planks);
                }
            }
        }

        House(-1, 6, 3, Deck + 1, Deck + 3, Deck + 2, (-1, 0));
        House(3, 6, 2, Deck + 5, Deck + 7, Deck + 6, (3, 0));

        // ---- funnel and masts
        Set(-3, Deck + 1, -1, Part("funnel"));
        Set(14, Deck + 1, 0, Part("mast"));
        Set(-13, Deck + 1, 0, Part("mast"));

        var list = new List<Placement>(cells.Count);
        foreach (var entry in cells)
        {
            if (entry.Value != null) list.Add(new Placement(entry.Key.Item1, entry.Key.Item2, entry.Key.Item3, entry.Value));
        }

        list.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
        return list;
    }

    /// <summary>The cells of ice packed round the hull: a few blocks out from the skin, up to the waterline.</summary>
    public static IEnumerable<(int X, int Y, int Z)> Ice(int reach = 5)
    {
        for (int x = -HalfLength - reach; x <= HalfLength + reach; x++)
        {
            for (int z = -HalfBeam - reach; z <= HalfBeam + reach; z++)
            {
                for (int y = Waterline - 1; y <= Waterline; y++)
                {
                    if (InHull(x, y, z)) continue;
                    bool near = false;
                    for (int d = 1; d <= reach && !near; d++)
                    {
                        near = InHull(x + d, Waterline, z) || InHull(x - d, Waterline, z) || InHull(x, Waterline, z + d) || InHull(x, Waterline, z - d)
                            || (d <= reach * 2 / 3 && (InHull(x + d, Waterline, z + d) || InHull(x - d, Waterline, z + d) || InHull(x + d, Waterline, z - d) || InHull(x - d, Waterline, z - d)));
                    }

                    if (near) yield return (x, y, z);
                }
            }
        }
    }
}
