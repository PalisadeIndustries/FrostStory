#!/usr/bin/env python3
"""Designs the wrecked tramp steamer and writes everything the mod needs to lay it down:

    assets/froststory/config/wreck.json            where every block goes, and what is broken up with what
    assets/froststory/shapes/block/generator/ship-*.json   the models
    assets/froststory/blocktypes/generator/shipcell.json, shipmodel.json

    python3 tools/gen_ship.py          (after gen_assets.py and gen_segments.py)

The ship is a three-island tramp of about 1900: 110 blocks long and 15 in the beam, bow to the east
(+X), keel at Y = 0, with a forecastle, a bridge island amidships over the engine and boilers, and a
poop. Two holds forward and two aft, each with a 'tween deck; a cross bunker and side bunkers; cabins
either side of the machinery casings; the master's quarters and the wheelhouse above.

What you see is models: one for each section of the hull with all that is built into it, and one for
each fitting. What you touch is unseen cells: solid ones for the shell, and open ones that carry thin
walls and a floor on their sides, so partitions are a plank thick and rooms are their real size.
"""
import importlib.util
import json
import math
import os
import random

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("gen_segments", os.path.join(HERE, "gen_segments.py"))
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)
Model = g.Model

ASSETS = os.path.join(HERE, "..", "FrostStory", "assets", "froststory")
SHAPES = os.path.join(ASSETS, "shapes", "block", "generator")
T = "block/generator/"
g.TEXTURES.update({
    "shippanel": (T + "shippanel", 16, 16),
    "shipwhite": (T + "shipwhite", 16, 16),
})

# ------------------------------------------------------------------------------------------- the lines
X0, X1, HB = -55, 54, 7
TANK, TWEEN, MAIN, UPPER, BRIDGE, TOP = 2, 6, 9, 12, 15, 18     # the level each deck's floor lies at
WATER = 5                                                       # the ice comes up to the top of this
SECTIONS = [(-55, -47), (-46, -35), (-34, -21), (-20, -9), (-8, 3), (4, 9), (10, 24), (25, 39), (40, 47), (48, 54)]
BULKHEADS = (-46, -34, -20, -8, 4, 10, 25, 40)                  # on the after side of these stations
HATCHES = ((-43, -38), (-30, -25), (14, 19), (29, 34))          # 4, 3, 2, 1: each five wide on the centre line
ISLAND = (-20, 9)
FORECASTLE, POOP = 40, -44
BREACH = {(x, y) for x in range(14, 19) for y in (6, 7, 8)} - {(14, 8), (18, 8), (14, 6)}     # stove in, port side


def island(x):
    return x >= FORECASTLE or x <= POOP or ISLAND[0] <= x <= ISLAND[1]


def skin_top(x):
    return UPPER - 1 if island(x) else MAIN


def half(x, y):
    """Half the breadth at a station and a height, to the middle of the outermost cell; below zero is outside."""
    x = max(X0 - 0.5, min(X1 + 0.5, x))
    if x > 28:
        h = HB * (1 - ((x - 28) / 27.2) ** 1.8)
    elif x < -38:
        h = HB * math.sqrt(max(0.0, 1 - ((-38 - x) / 17.7) ** 2))
    else:
        h = HB
    h -= 3.2 if y == 0 else 1.2 if y == 1 else 0
    if x > 38 and y < 4:
        h -= (4 - y) * (x - 38) / 12.0          # the forefoot
    if x < -45 and y < 6:
        h -= (6 - y) * (-45 - x) / 5.0          # the counter, cut away under the stern
    return h


def reach(x, y):
    h = half(x, min(y, skin_top(x)))
    return round(h) if h >= -0.2 else None


def in_hull(x, y, z):
    if x < X0 or x > X1 or y < 0 or y > skin_top(x):
        return False
    r = reach(x, y)
    return r is not None and abs(z) <= r


def is_skin(x, y, z):
    if not in_hull(x, y, z):
        return False
    if y < 2:
        return True
    return not (in_hull(x + 1, y, z) and in_hull(x - 1, y, z) and in_hull(x, y, z + 1) and in_hull(x, y, z - 1) and in_hull(x, y - 1, z))


# Doorways cut through the solid ends of the islands: out of the forecastle and the poop, and both ends
# of each alleyway.
DOORWAYS = {(x, y, z) for y in (MAIN, MAIN + 1) for x, zs in ((FORECASTLE, (-3, 3)), (POOP, (0,)), (ISLAND[0], (-3, 3)), (ISLAND[1], (-3, 3))) for z in zs}


def interior(x, y, z):
    return in_hull(x, y, z) and ((x, y, z) in DOORWAYS or not is_skin(x, y, z))


def section_of(x):
    for i, (a, b) in enumerate(SECTIONS):
        if a <= x <= b:
            return i
    raise ValueError(x)


# ------------------------------------------------------------------------------------------- the plan
solid = {}        # cell -> group
flags = {}        # cell -> set of 'f', 'n', 'e', 's', 'w'
floors = {}       # (x, Y, z) -> texture
walls = {}        # (x, y, z, side) -> (texture, kind)
real = {}         # cell -> block code
groups = []       # {"name", "drops", "cells"}
models = {}       # name -> Model
instances = []    # (model name, anchor cell, group)
rng = random.Random(1900)

for i in range(len(SECTIONS)):
    groups.append({"name": f"hull-{i}", "drops": [], "cells": 0})


def hull_group(x):
    return section_of(max(X0, min(X1, x)))


def anchor_of(i):
    x = SECTIONS[i][0]
    y = skin_top(x)
    return (x, y, -reach(x, y))


hull = []
for i in range(len(SECTIONS)):
    ax, ay, az = anchor_of(i)
    hull.append(Model(f"ship-hull-{i}", (ax, ay + 1, az)))


def sec(x):
    return hull[hull_group(x)]


def floor(x, Y, z, tex="darkplate"):
    if (x, Y, z) in solid or (x, Y, z) in real:
        return
    if Y <= skin_top(x) and not interior(x, Y, z):
        return
    flags.setdefault((x, Y, z), set()).add("f")
    floors[(x, Y, z)] = tex


def wall(x, y, z, side, tex="shiphull", kind="plain"):
    if (x, y, z) in solid or (x, y, z) in real:
        return
    if y <= skin_top(x) and not interior(x, y, z):
        return
    flags.setdefault((x, y, z), set()).add(side)
    walls[(x, y, z, side)] = (tex, kind)


def wall_run(axis, at, side, a, b, y0, y1, tex="shiphull", doors=(), windows=(), window_y=None):
    """A wall along a line of cells: axis 'z' runs athwartships at station `at`, 'x' fore and aft at
    breadth `at`. Doors are two high; windows sit in the middle course."""
    for c in range(a, b + 1):
        for y in range(y0, y1 + 1):
            if c in doors and y <= y0 + 1:
                continue
            kind = "window" if c in windows and y == (window_y if window_y is not None else y0 + 1) else "plain"
            if axis == "z":
                wall(at, y, c, side, tex, kind)
            else:
                wall(c, y, at, side, tex, kind)


def ladder(x, z, y0, y1, side):
    for y in range(y0, y1 + 1):
        cell = (x, y, z)
        flags.pop(cell, None)
        floors.pop((x, y, z), None)
        for s in "nesw":
            walls.pop((x, y, z, s), None)
        real[cell] = f"froststory:serviceladder-{side}"


def prop(name, model, origin, cells, drops):
    """A fitting: one model hung from `origin`, solid in `cells` (offsets from it), broken up whole."""
    index = len(groups)
    groups.append({"name": f"{name}-{sum(1 for gr in groups if gr['name'].startswith(name + '-'))}", "drops": drops, "cells": 0})
    for dx, dy, dz in cells:
        cell = (origin[0] + dx, origin[1] + dy, origin[2] + dz)
        flags.pop(cell, None)
        floors.pop(cell, None)
        real.pop(cell, None)
        solid[cell] = index
    instances.append((model, origin, index))


# ---- the shell
for x in range(X0, X1 + 1):
    for y in range(0, skin_top(x) + 1):
        r = reach(x, y)
        if r is None:
            continue
        for z in range(-r, r + 1):
            if is_skin(x, y, z) and (x, y, z) not in DOORWAYS and not (z == -r and (x, y) in BREACH):
                solid[(x, y, z)] = hull_group(x)


def hatch_at(x, z):
    return abs(z) <= 2 and any(a <= x <= b for a, b in HATCHES)


# ---- decks
for x in range(X0, X1 + 1):
    r = reach(x, MAIN) or 0
    for z in range(-r, r + 1):
        engine_room = -20 <= x <= -9
        boiler_room = -8 <= x <= 3
        bunker = 4 <= x <= 9
        # the 'tween deck: through the holds and the peaks; gratings round the engine; bunker floors beside the boilers
        if not hatch_at(x, z):
            if engine_room:
                if abs(z) >= 4 or x in (-20, -9):
                    floor(x, TWEEN, z, "grating")
            elif boiler_room:
                if abs(z) >= 4:
                    floor(x, TWEEN, z)
            elif not bunker:
                floor(x, TWEEN, z, "shipdeck")
        # the main deck: open over the hatches and down the middle of the machinery casings
        if not hatch_at(x, z):
            casing_well = (-17 <= x <= -12 and abs(z) <= 1)
            if not casing_well:
                inside_casing = ISLAND[0] <= x <= ISLAND[1] and abs(z) <= 2 and x <= 3
                floor(x, MAIN, z, "grating" if inside_casing else "shipdeck")
    if island(x):
        r = reach(x, UPPER - 1) or 0
        for z in range(-r, r + 1):
            floors_tex = "grating" if (-17 <= x <= -12 and abs(z) <= 1) else "shipdeck"
            floor(x, UPPER, z, floors_tex)

# ---- watertight bulkheads, tank top to main deck
for x in BULKHEADS:
    doors = {-8: (0,), 4: (-3, 3)}.get(x, ())
    wall_run("z", x, "w", -HB, HB, TANK, MAIN - 1, "shiphull", doors=doors)
# the break of the forecastle, the poop and the bridge island
wall_run("z", FORECASTLE, "w", -HB, HB, MAIN, UPPER - 1, "shipwhite", doors=(-3, 3))
wall_run("z", POOP, "e", -HB, HB, MAIN, UPPER - 1, "shipwhite", doors=(0,))
wall_run("z", ISLAND[0], "w", -HB, HB, MAIN, UPPER - 1, "shipwhite", doors=(-3, 3))
wall_run("z", ISLAND[1], "e", -HB, HB, MAIN, UPPER - 1, "shipwhite", doors=(-3, 3))

# ---- the bridge island, main deck: casings down the middle, an alleyway each side, cabins outboard
P = "shippanel"
wall_run("x", -2, "n", -20, 3, MAIN, UPPER - 1, "shiphull", doors=(-15, -2))          # casing sides
wall_run("x", 2, "s", -20, 3, MAIN, UPPER - 1, "shiphull", doors=(-15, -2))
wall_run("z", -8, "w", -2, 2, MAIN, UPPER - 1, "shiphull")                             # between engine and boiler casings
wall_run("z", 3, "e", -2, 2, MAIN, UPPER - 1, "shiphull")                              # casing front
PORT_ROOMS = [(-20, -18, "bath"), (-17, -14, "cabin"), (-13, -11, "cabin"), (-10, -8, "cabin"), (-7, -4, "mess"), (-3, 0, "galley"), (1, 4, "store"), (5, 9, "saloon")]
STBD_ROOMS = [(-20, -18, "store"), (-17, -15, "cabin"), (-14, -12, "cabin"), (-11, -9, "cabin"), (-8, -5, "cabin"), (-4, -1, "cabin"), (0, 4, "mess"), (5, 9, "saloon")]
for rooms, inner, side_in, side_out in ((PORT_ROOMS, -4, "s", "n"), (STBD_ROOMS, 4, "n", "s")):
    for a, b, kind in rooms:
        mid = (a + b) // 2
        wall_run("x", inner, side_in, a, b, MAIN, UPPER - 1, P, doors=(mid,))            # the cabin's side of the alleyway
        if b < ISLAND[1]:
            z0, z1 = (-6, -4) if inner < 0 else (4, 6)
            wall_run("z", b, "e", z0, z1, MAIN, UPPER - 1, P)                            # partition to the next room
# the forward lobby, with the ladder to the bridge
wall_run("z", 4, "w", -2, 2, MAIN, UPPER - 1, P, doors=(0,))

# ---- forecastle: seamen to port, firemen to starboard, stores in the eyes of her
wall_run("x", 0, "n", 40, 47, MAIN, UPPER - 1, P)
wall_run("z", 48, "w", -HB, HB, MAIN, UPPER - 1, P, doors=(-2, 2))
wall_run("x", 0, "n", 48, 54, MAIN, UPPER - 1, P)
# ---- poop: the steering engine in the middle, a hospital to port, stores to starboard
wall_run("x", -2, "n", -52, -45, MAIN, UPPER - 1, P, doors=(-46,))
wall_run("x", 2, "s", -52, -45, MAIN, UPPER - 1, P, doors=(-46,))

# ---- the bridge house on the island: master to port, chart room and a spare cabin to starboard, a lobby between
HX0, HX1, HZ = -1, 8, 4
W = "shipwhite"
wall_run("z", HX0, "w", -HZ, HZ, UPPER, BRIDGE - 1, W, doors=(0,), windows=(-3, 3))
wall_run("z", HX1, "e", -HZ, HZ, UPPER, BRIDGE - 1, W, windows=(-3, -2, 2, 3))
for z, side in ((-HZ, "n"), (HZ, "s")):
    wall_run("x", z, side, HX0, HX1, UPPER, BRIDGE - 1, W, windows=(0, 2, 5, 7))
wall_run("x", -1, "s", HX0, HX1, UPPER, BRIDGE - 1, P, doors=(1, 6))                    # lobby, port side
wall_run("x", 1, "n", HX0, HX1, UPPER, BRIDGE - 1, P, doors=(1, 6))                     # lobby, starboard side
wall_run("z", 3, "e", -HZ, -2, UPPER, BRIDGE - 1, P)                                   # master's bedroom | day room
wall_run("z", 3, "e", 2, HZ, UPPER, BRIDGE - 1, P)                                     # spare cabin | chart room
for x in range(HX0, HX1 + 1):                                                          # the deck over the house, and the bridge wings
    for z in range(-HZ, HZ + 1):
        floor(x, BRIDGE, z, "shipdeck")
for x in (5, 6, 7):
    for z in list(range(-HB, -HZ)) + list(range(HZ + 1, HB + 1)):
        floor(x, BRIDGE, z, "grating")
# ---- the wheelhouse
WX0, WX1, WZ = 4, 8, 3
wall_run("z", WX0, "w", -WZ, WZ, BRIDGE, TOP - 1, "pole", doors=(-2, 2), windows=(-1, 0, 1))
wall_run("z", WX1, "e", -WZ, WZ, BRIDGE, TOP - 1, "pole", windows=(-3, -2, -1, 0, 1, 2, 3))
for z, side in ((-WZ, "n"), (WZ, "s")):
    wall_run("x", z, side, WX0, WX1, BRIDGE, TOP - 1, "pole", windows=(5, 6, 7))
for x in range(WX0, WX1 + 1):
    for z in range(-WZ, WZ + 1):
        floor(x, TOP, z, "shipdeck")

# ---- the shaft tunnel, aft from the engine room along the tank top
tunnel = len(groups)
groups.append({"name": "tunnel-0", "drops": [["game:metalplate-iron", 10], ["game:metalbit-iron", 30]], "cells": 0})
for x in range(-46, -20):
    for y in (TANK, TANK + 1):
        if interior(x, y, 0):
            flags.pop((x, y, 0), None)
            solid[(x, y, 0)] = tunnel

# ---- ladders
for (a, b) in HATCHES:
    ladder(a, 0, TANK, MAIN, "west")
ladder(-9, 2, TANK, MAIN, "east")                 # engine room, up into its casing
ladder(3, 2, TANK, MAIN, "east")                  # stokehold, up into the boiler casing
ladder(8, 0, MAIN, BRIDGE + 1, "east")            # island lobby to the master's lobby to the wheelhouse
ladder(41, 0, TANK, MAIN, "west")                 # fore peak
ladder(-47, 0, TWEEN, MAIN, "east")               # steering flat
for z in (-5, 5):
    ladder(39, z, MAIN, UPPER - 1, "east")        # well deck up to the forecastle head
    ladder(-43, z, MAIN, UPPER - 1, "west")       # and to the poop
    ladder(-21, z, MAIN, UPPER - 1, "east")       # and to the boat deck
    ladder(10, z, MAIN, UPPER - 1, "west")        # and to the bridge deck

# ---- cargo: packing cases stowed out to the sides of the holds, on both levels
CRATE = "froststory:shipcrate"
for (a, b), keep in ((HATCHES[0], 0.2), (HATCHES[1], 0.25), (HATCHES[2], 0.3), (HATCHES[3], 0.22)):
    for x in range(a - 3, b + 5):
        for z in range(-6, 7):
            if abs(z) < 3:
                continue
            for base in (TANK, TWEEN):
                height = rng.choice((0, 0, 1, 1, 2, 3)) if rng.random() < keep else 0
                for y in range(base, base + min(height, 3 if base == TANK else 2)):
                    cell = (x, y, z)
                    if interior(*cell) and cell not in solid and cell not in real and (y > base or True):
                        if y == base:
                            fl = flags.get(cell, set())
                            if fl - {"f"}:
                                break                 # leave bulkheads and doors clear
                        flags.pop(cell, None)
                        floors.pop(cell, None)
                        real[cell] = CRATE
for cell in [(44, TWEEN, -3), (44, TWEEN, -2), (45, TWEEN, 3), (46, TWEEN, 3), (46, TWEEN + 1, 3), (-50, TWEEN, 3), (-49, TWEEN, -3), (2, MAIN, -5), (3, MAIN, -5), (-19, MAIN, 5)]:
    if (interior(*cell) or cell[1] >= MAIN) and cell not in solid and cell not in real:
        flags.pop(cell, None)
        floors.pop(cell, None)
        real[cell] = CRATE

# ---- coal: the cross bunker forward of the boilers, and what is left in the side bunkers
for x in range(4, 10):
    for z in range(-6, 7):
        cell = (x, TANK, z)
        if interior(*cell) and cell not in real and not (flags.get(cell, set()) - {"f"}):
            layers = rng.choice((2, 3, 4, 5, 6, 8)) if abs(z) > 1 or x > 5 else rng.choice((1, 2))
            real[cell] = f"froststory:coalheap-{layers}"
            flags.pop(cell, None)
            if layers == 8 and rng.random() < 0.5:
                real[(x, TANK + 1, z)] = f"froststory:coalheap-{rng.choice((2, 4, 6))}"
for x in range(-7, 3):
    for z in (-6, -5, 5, 6):
        cell = (x, TWEEN, z)
        if interior(*cell) and cell not in real and rng.random() < 0.7:
            flags.pop(cell, None)
            floors.pop(cell, None)
            solid_under = True
            real[cell] = f"froststory:coalheap-{rng.choice((1, 2, 3, 4))}"
wall_run("x", -4, "s", -8, 3, TWEEN, MAIN - 1, "shiphull", doors=(-3,))                  # side bunker fronts
wall_run("x", 4, "n", -8, 3, TWEEN, MAIN - 1, "shiphull", doors=(-3,))


# =========================================================================================== fittings
def local(name):
    return Model("ship-" + name, (0, 1, 0))


def box_cells(nx, ny, nz):
    return [(x, y, z) for x in range(nx) for y in range(ny) for z in range(nz)]


def iron(plates, ingots=0, bits=0, extra=()):
    drops = []
    if plates:
        drops.append(["game:metalplate-iron", plates])
    if ingots:
        drops.append(["game:ingot-iron", ingots])
    if bits:
        drops.append(["game:metalbit-iron", bits])
    return drops + [list(e) for e in extra]


def engine_model():
    """A triple-expansion engine, six blocks long: the bed plate, three cylinders of growing size on
    their columns, the crankshaft below them, the condenser along one side."""
    m = local("engine")
    m.box((2.5, 0.2, 1.0), (5.8, 0.4, 2.6), "girder")
    m.hprism((-0.4, 0.8, 1.0), (5.4, 0.8, 1.0), 0.16, "steel")
    for x, a in ((0.6, 0.6), (2.5, 0.8), (4.4, 1.0)):
        for dz in (-0.9, 0.9):
            for dx in (-0.45, 0.45):
                m.member((x + dx, 0.4, 1.0 + dz), (x + dx * 0.5, 3.2, 1.0 + dz * 0.55), 0.16, 0.16, "band", chunk=3.0)
        m.vprism(x, 1.0, 3.2, 4.9, a, "green", caps=(True, True), captex="band", chunk=2.0)
        m.vprism(x, 1.0, 4.9, 5.1, a * 0.55, "brass", caps=(False, True), captex="brass")
        m.vprism(x, 1.0, 1.4, 3.2, 0.07, "steel")
        m.box((x, 0.85, 1.0), (0.2, 0.8, 0.7), "band")
        m.box((x, 2.3, 1.0), (0.7, 0.2, 0.9), "brass")
    m.hprism((0.2, 2.2, 2.25), (4.8, 2.2, 2.25), 0.42, "darkplate", caps=(True, True), captex="band")     # condenser
    m.hprism((-0.3, 4.2, -0.1), (5.3, 4.2, -0.1), 0.14, "pipe")                                           # main steam pipe
    m.wheel((-0.45, 0.8, 1.0), 0.8, 0.14, 0.12, "girder", axis="x")
    m.box((2.5, 3.1, -0.45), (5.6, 0.08, 0.5), "grating")                                                 # cylinder platform
    return m


def boiler_model():
    """A Scotch boiler, five blocks long and four across: a riveted drum in its saddles, three furnace
    mouths in the forward end, the uptake rising from it."""
    m = local("boiler")
    z, y = 1.5, 2.0
    m.hprism((-0.4, y, z), (4.4, y, z), 1.95, "shiphull", caps=(True, True), captex="shiphull", chunk=1.0)
    for x in (-0.3, 1.0, 2.0, 3.0, 4.3):
        m.hprism((x - 0.06, y, z), (x + 0.06, y, z), 2.02, "band")
    for x in (0.4, 3.6):
        m.box((x, 0.22, z), (0.4, 0.44, 3.4), "girder")
    for dz in (-1.0, 0.0, 1.0):
        m.hprism((4.4, 1.0, z + dz), (4.5, 1.0, z + dz), 0.36, "band", caps=(True, True), captex="black")
    m.vprism(0.6, z, 3.9, 4.0, 0.7, "band", caps=(False, True), captex="black")
    m.vprism(3.4, z, 3.9, 4.0, 0.3, "brass", caps=(False, True), captex="brass")
    return m


def funnel_model():
    """The funnel: an uptake from the boiler tops through the casing, then a tall raked stack with brass
    bands, a flared top, a waste-steam pipe and the whistle."""
    m = local("funnel")
    c = 1.0
    m.vprism(c, c, 0.0, 6.2, 1.3, "darkplate", chunk=2.0)                                  # uptake, to the boat deck
    m.vprism(c, c, 6.2, 6.7, 1.75, "band", caps=(True, True), captex="band")
    for i in range(11):
        m.vprism(c - i * 0.05, c, 6.7 + i, 7.75 + i, 1.35, "shiphull", chunk=1.0)
    for y in (9.0, 13.0, 16.2):
        m.vprism(c - (y - 6.7) * 0.05, c, y, y + 0.3, 1.42, "brass")
    m.vprism(c - 0.55, c, 17.7, 18.0, 1.6, "brass", caps=(True, True), captex="black")
    m.vprism(c + 1.6, c, 6.2, 15.5, 0.12, "pipe")
    m.vprism(c + 1.6, c, 15.5, 16.0, 0.2, "brass", caps=(False, True), captex="black")
    m.vprism(c + 1.45, c + 0.7, 6.2, 12.0, 0.07, "brass")
    m.hprism((c + 1.2, 12.1, c + 0.7), (c + 1.7, 12.1, c + 0.7), 0.12, "brass", caps=(True, True), captex="black")   # whistle
    for dz in (-1, 1):
        m.member((c + 0.2, 6.3, c + dz * 3.4), (c - 0.5, 15.0, c + dz * 1.3), 0.04, 0.04, "steel", chunk=12.0)
    return m


def mast_model():
    """A pole mast in a tabernacle, with a crosstree, a crow's nest and a derrick boom topped up over
    the hatch on each side of it."""
    m = local("mast")
    m.vprism(0, 0, 0.0, 19.0, 0.24, "pole", chunk=1.0)
    m.vprism(0, 0, 0.0, 1.2, 0.42, "band", caps=(False, True), captex="band")
    m.vprism(0, 0, 19.0, 19.4, 0.1, "brass", caps=(False, True), captex="brass")
    m.box((0, 13.0, 0), (0.18, 0.18, 6.0), "pole")
    m.vprism(0, 0, 13.6, 14.5, 0.6, "shipdeck", caps=(True, False), captex="shipdeck")
    for sx in (-1, 1):
        m.member((sx * 0.4, 1.6, 0), (sx * 7.0, 5.2, 0), 0.2, 0.2, "pole", chunk=2.0)
        m.member((sx * 7.0, 5.2, 0), (sx * 0.2, 12.6, 0), 0.03, 0.03, "steel", chunk=12.0)
        m.box((sx * 7.0, 4.6, 0), (0.2, 1.0, 0.2), "band")
    for dz in (-3, 3):
        m.member((0, 13.0, dz), (0, 0.4, dz * 2.2), 0.04, 0.04, "steel", chunk=16.0)
        m.member((0, 13.0, dz), (-1.6, 0.4, dz * 2.2), 0.04, 0.04, "steel", chunk=16.0)
        m.member((0, 13.0, dz), (1.6, 0.4, dz * 2.2), 0.04, 0.04, "steel", chunk=16.0)
    return m


def winch_model():
    """A steam cargo winch: a bed, two small cylinders, a barrel between cheeks, warping ends."""
    m = local("winch")
    m.box((0.5, 0.1, 0), (1.9, 0.2, 0.9), "girder")
    m.hprism((0.5, 0.6, -0.45), (0.5, 0.6, 0.45), 0.34, "band", caps=(True, True), captex="band")
    for z in (-0.5, 0.5):
        m.box((0.5, 0.55, z), (0.9, 0.9, 0.08), "darkplate")
    for z in (-0.72, 0.72):
        a, b = sorted((z, z * 1.25))
        m.hprism((0.5, 0.6, a), (0.5, 0.6, b), 0.2, "brass", caps=(True, True), captex="brass")
    for x in (-0.15, 1.15):
        m.hprism((x - 0.2, 0.45, -0.25), (x + 0.2, 0.45, -0.25), 0.16, "green", caps=(True, True), captex="brass")
    m.box((1.3, 0.9, 0.3), (0.06, 0.7, 0.06), "steel")
    m.box((1.3, 1.25, 0.3), (0.1, 0.1, 0.1), "red")
    return m


def windlass_model():
    """The anchor windlass on the forecastle head: two gypsies on a shaft between heavy cheeks, with
    its warping drums and brake wheels."""
    m = local("windlass")
    m.box((0.5, 0.12, 1.0), (1.8, 0.24, 3.6), "girder")
    m.hprism((0.5, 0.75, -0.9), (0.5, 0.75, 2.9), 0.12, "steel")
    for z in (0.2, 1.8):
        m.hprism((0.5, 0.75, z - 0.25), (0.5, 0.75, z + 0.25), 0.5, "band", caps=(True, True), captex="band")
        m.box((0.5, 0.6, z - 0.4), (1.2, 1.1, 0.1), "darkplate")
        m.box((0.5, 0.6, z + 0.4), (1.2, 1.1, 0.1), "darkplate")
    for z in (-0.8, 2.8):
        a, b = sorted((z - 0.15, z + 0.15))
        m.hprism((0.5, 0.75, a), (0.5, 0.75, b), 0.3, "brass", caps=(True, True), captex="brass")
    m.handwheel(1.2, 1.3, 1.0, a=0.3)
    m.vprism(1.2, 1.0, 0.24, 1.3, 0.05, "steel")
    for z in (0.2, 1.8):                                                                   # chain, leading forward to the hawse
        m.member((0.9, 0.9, z), (3.4, 0.2, z * 1.6 - 0.6), 0.14, 0.1, "band", chunk=4.0)
    return m


def lifeboat_model():
    """A clinker lifeboat on its chocks under radial davits, canvas cover laced over it."""
    m = local("lifeboat")
    for i, (w, h) in enumerate(((0.5, 0.5), (1.0, 0.8), (1.3, 0.9), (1.3, 0.9), (1.0, 0.8), (0.5, 0.5))):
        x = -0.1 + i * 0.84
        m.box((x, 0.75 + (0.9 - h) / 2, 0.3), (0.86, h, w), "shipwhite")
        m.box((x, 1.24, 0.3), (0.86, 0.08, w + 0.06), "canvas")
    m.box((2.0, 0.25, 0.3), (5.0, 0.1, 0.12), "pole")
    for x in (0.6, 3.4):
        m.box((x, 0.18, 0.3), (0.2, 0.36, 1.2), "pole")
        m.vprism(x, 1.15, 0.0, 2.1, 0.07, "steel")
        m.member((x, 2.1, 1.15), (x, 2.5, 0.3), 0.07, 0.07, "steel", chunk=2.0)
        m.box((x, 1.9, 0.3), (0.03, 1.2, 0.03), "steel")
    return m


def steering_model():
    """The steering engine and its quadrant, on the flat under the poop."""
    m = local("steering")
    m.box((1.0, 0.1, 1.0), (2.8, 0.2, 2.6), "girder")
    for z in (0.4, 1.6):
        m.hprism((0.2, 0.6, z), (1.4, 0.6, z), 0.26, "green", caps=(True, True), captex="brass")
    m.vprism(2.0, 1.0, 0.2, 0.5, 0.9, "band", caps=(True, True), captex="band")
    m.vprism(2.0, 1.0, 0.5, 1.6, 0.14, "steel")
    m.wheel((0.0, 0.9, 1.0), 0.5, 0.1, 0.1, "brass", axis="x")
    return m


for name, fn in (("engine", engine_model), ("boiler", boiler_model), ("funnel", funnel_model), ("mast", mast_model),
                 ("winch", winch_model), ("windlass", windlass_model), ("lifeboat", lifeboat_model), ("steering", steering_model)):
    models["ship-" + name] = fn()

prop("engine", "ship-engine", (-17, TANK, -1), box_cells(6, 5, 3), iron(14, 24, 20, [("game:metalnailsandstrips-iron", 8)]))
SECTION_DROP = ["froststory:boilersection", 6]
prop("boiler", "ship-boiler", (-6, TANK, -6), box_cells(5, 4, 4), [SECTION_DROP])
prop("boiler", "ship-boiler", (-6, TANK, 2), box_cells(5, 4, 4), [SECTION_DROP])
prop("funnel", "ship-funnel", (-4, TWEEN, -1), [(x, y, z) for x in range(3) for z in range(3) for y in range(0, 17)], iron(20, 0, 24))
for mx in (24, -34):
    prop("mast", "ship-mast", (mx, MAIN, 0), [(0, y, 0) for y in range(0, 12)], [["game:plank-aged", 20], ["game:rope", 10], ["game:metalnailsandstrips-iron", 4]])
    for wx in (mx - 3, mx + 2):
        for z in (-3, 3):
            prop("winch", "ship-winch", (wx, MAIN, z), [(0, 0, 0), (1, 0, 0)], iron(1, 5, 6))
prop("windlass", "ship-windlass", (45, UPPER, -1), box_cells(1, 1, 3), iron(3, 10, 10))
for bx in (-19, -11):
    for z, mirror in ((-6, 0), (5, 0)):
        prop("lifeboat", "ship-lifeboat", (bx, UPPER, z), box_cells(5, 1, 1), [["game:plank-aged", 14], ["game:rope", 4], ["game:linen-normal-down", 2]])
prop("steering", "ship-steering", (-52, MAIN, -1), box_cells(3, 1, 3), iron(3, 8, 8))


# =========================================================================================== the hull's models
def edge(x, y):
    return max(0.0, half(x, min(y, skin_top(round(max(X0, min(X1, x)))))) + 0.5)


def colour(x, y):
    if y == WATER + 1:
        return "red"
    if y >= MAIN + 1 and island(x):
        return "shipwhite"
    return "shiphull"


for x in range(X0, X1 + 1):
    m = sec(x)
    top = skin_top(x)
    xa, xb = x - 0.5, x + 0.5
    for sign in (-1, 1):
        runs, y = [], 0
        while y <= top:
            if reach(x, y) is None or (sign < 0 and (x, y) in BREACH):
                y += 1
                continue
            key = (round(edge(xa, y), 3), round(edge(xb, y), 3), colour(x, y))
            y1 = y
            while y1 + 1 <= top and not (sign < 0 and (x, y1 + 1) in BREACH) and reach(x, y1 + 1) is not None \
                    and (round(edge(xa, y1 + 1), 3), round(edge(xb, y1 + 1), 3), colour(x, y1 + 1)) == key:
                y1 += 1
            runs.append((y, y1, key))
            y = y1 + 1
        for ya, yb, (ea, eb, tex) in runs:
            m.member((xa, (ya + yb + 1) / 2, sign * ea), (xb, (ya + yb + 1) / 2, sign * eb), yb - ya + 1, 0.14, tex, chunk=2.0)

        e = edge(x, top)
        if x % 3 == 0 and e > 1.0:                                                         # a frame showing through the plating
            m.box((x, (WATER + top + 1) / 2 + 0.5, sign * (e + 0.09)), (0.14, top - WATER, 0.1), "band")
        cap = "pole" if not island(x) else "brass"                                         # rail cap on the bulwark; a rubbing strake on the islands
        m.member((xa, top + 1.05, sign * edge(xa, top)), (xb, top + 1.05, sign * edge(xb, top)), 0.1, 0.24, cap, chunk=2.0)
        if island(x) and x % 3 == 1 and e > 4:                                             # portholes to the accommodation
            z0, z1 = sorted((sign * (e + 0.02), sign * (e + 0.12)))
            m.hprism((x, MAIN + 1.6, z0), (x, MAIN + 1.6, z1), 0.2, "brass", caps=(True, True), captex="black")
        if island(x) and e > 2:                                                            # rails round the island decks
            ze = sign * (edge(x, top) - 0.1)
            if x % 2 == 0:
                m.box((x, UPPER + 0.6, ze), (0.05, 1.0, 0.05), "steel")
            for dy in (0.55, 1.05):
                m.member((xa, UPPER + dy, sign * (edge(xa, top) - 0.1)), (xb, UPPER + dy, sign * (edge(xb, top) - 0.1)), 0.04, 0.04, "steel" if dy < 1 else "brass", chunk=2.0)

    # the keel and the garboards, for when she is dug out
    r0 = reach(x, 0)
    if r0 is not None:
        m.box((x, 0.06, 0), (1.0, 0.12, 2 * (half(x, 0) + 0.5)), "shiphull")

for (x, y, z) in DOORWAYS:
    if y == MAIN:
        sec(x).box((x, MAIN + 2.06, z), (1.1, 0.12, 1.0), "brass")
        for dz in (-0.47, 0.47):
            sec(x).box((x, MAIN + 1.06, z + dz), (1.1, 1.9, 0.06), "brass")

# stem and stern
bow, stern = hull[-1], hull[0]
bow.box((X1 + 0.5, (skin_top(X1) + 1 + 4) / 2, 0), (0.24, skin_top(X1) - 3, 0.5), "band")
stern.box((X0 - 0.5, (6 + skin_top(X0) + 1) / 2, 0), (0.14, skin_top(X0) - 5, 2 * edge(X0 - 0.5, 9)), "shiphull")
stern.box((X0 + 1.0, 2.5, 0), (0.3, 5.0, 0.16), "band")                                    # rudder post
stern.box((X0 + 0.2, 2.6, 0), (1.6, 4.2, 0.12), "shiphull")                                # rudder
stern.wheel((X0 + 2.6, 2.4, 0), 1.9, 0.5, 0.3, "brass", axis="x")                          # the screw

# decks: one plank or plate for each run of floor across the ship
by_row = {}
for (x, Y, z), tex in floors.items():
    by_row.setdefault((x, Y, tex), []).append(z)
for (x, Y, tex), zs in by_row.items():
    zs.sort()
    start = prev = zs[0]
    for z in zs[1:] + [None]:
        if z is not None and z == prev + 1:
            prev = z
            continue
        sec(x).box((x, Y + 0.06, (start + prev) / 2), (1.0, 0.12, prev - start + 1), tex)
        if z is not None:
            start = prev = z

# walls: one panel for each run of wall up a cell's side
by_edge = {}
for (x, y, z, side), (tex, kind) in walls.items():
    by_edge.setdefault((x, z, side, tex, kind), []).append(y)


def panel(m, x, z, side, ya, yb, tex, inset=0.0):
    off = 0.5 - 0.06
    if side in "we":
        m.box((x + (off if side == "e" else -off), (ya + yb) / 2, z), (0.12, yb - ya, 1.0 - inset), tex)
    else:
        m.box((x, (ya + yb) / 2, z + (off if side == "s" else -off)), (1.0 - inset, yb - ya, 0.12), tex)


for (x, z, side, tex, kind), ys in by_edge.items():
    ys.sort()
    m = sec(x)
    if kind == "window":
        for y in ys:
            panel(m, x, z, side, y, y + 0.22, tex)
            panel(m, x, z, side, y + 0.86, y + 1.0, tex)
            panel(m, x, z, side, y + 0.22, y + 0.3, "brass")
            panel(m, x, z, side, y + 0.78, y + 0.86, "brass")
            for q in (-0.44, 0.44):                                                        # the frame's stiles
                if side in "we":
                    m.box((x + (0.44 if side == "e" else -0.44), y + 0.54, z + q), (0.12, 0.48, 0.1), "brass")
                else:
                    m.box((x + q, y + 0.54, z + (0.44 if side == "s" else -0.44)), (0.1, 0.48, 0.12), "brass")
        continue
    start = prev = ys[0]
    for y in ys[1:] + [None]:
        if y is not None and y == prev + 1:
            prev = y
            continue
        panel(m, x, z, side, start, prev + 1, tex)
        if y is not None:
            start = prev = y

# hatch coamings, with the hatch beams left across
for a, b in HATCHES:
    for x in range(a, b + 1):
        for sign in (-1, 1):
            sec(x).box((x, MAIN + 0.4, sign * 2.56), (1.0, 0.8, 0.12), "band")
        if (x - a) % 2 == 1:
            sec(x).box((x, MAIN + 0.74, 0), (0.14, 0.14, 5.0), "girder")
    for x, off in ((a, -0.56), (b, 0.56)):
        sec(x).box((x + off, MAIN + 0.4, 0), (0.12, 0.8, 5.24), "band")


def cowl(x, z, base, facing=1, a=0.26, height=1.6):
    m = sec(x)
    m.vprism(x, z, base, base + height, a, "shipwhite")
    m.vprism(x, z, base, base + 0.12, a + 0.12, "band", caps=(False, True), captex="band")
    m.box((x, base + height + 0.2, z), (a * 2.4, a * 2.2, a * 2.4), "shipwhite")
    p, q = sorted((x, x + facing * a * 2.0))
    m.hprism((p, base + height + 0.24, z), (q, base + height + 0.24, z), a * 1.5, "shipwhite", caps=(True, True), captex="red")


for x, z in ((12, 5), (12, -5), (21, 5), (21, -5), (27, 5), (27, -5), (37, 4), (37, -4), (-23, 5), (-23, -5), (-32, 5), (-32, -5), (-41, 4), (-41, -4)):
    cowl(x, z, MAIN + 0.12, 1 if x > 0 else -1)
for x, z in ((-8, 5), (-8, -5), (1, 5), (1, -5), (-18, 3), (-18, -3)):
    cowl(x, z, UPPER + 0.12, 1, a=0.36, height=2.6)                                        # the big stokehold ventilators

# the engine room skylight, on the boat deck
sky = sec(-15)
sky.box((-14.5, UPPER + 0.5, 0), (6.4, 0.7, 3.4), "pole", faces=["north", "south", "east", "west"])
for sign in (-1, 1):
    sky.box((-14.5, UPPER + 1.15, sign * 0.85), (6.2, 0.08, 1.9), "canvas", (sign * -22, 0, 0))
sky.box((-14.5, UPPER + 1.52, 0), (6.3, 0.1, 0.16), "brass")

# the wheelhouse: wheel, binnacle, telegraphs; the standard compass and a searchlight on its top
wh = sec(6)
wh.wheel((6.2, BRIDGE + 1.25, 0), 0.6, 0.08, 0.08, "pole", axis="x")
wh.box((6.5, BRIDGE + 0.7, 0), (0.3, 1.2, 0.3), "pole")
wh.vprism(7.3, 0, BRIDGE + 0.12, BRIDGE + 1.1, 0.2, "pole")
wh.vprism(7.3, 0, BRIDGE + 1.1, BRIDGE + 1.4, 0.26, "brass", caps=(False, True), captex="brass")
for z in (-2, 2):
    wh.vprism(7.2, z, BRIDGE + 0.12, BRIDGE + 1.1, 0.1, "brass")
    wh.hprism((7.1, BRIDGE + 1.25, z), (7.3, BRIDGE + 1.25, z), 0.26, "brass", caps=(True, True), captex="canvas")
wh.box((4.6, BRIDGE + 0.9, -1.8), (0.9, 0.1, 1.6), "pole")                                 # chart table
wh.vprism(6.0, 0, TOP + 0.12, TOP + 1.0, 0.16, "pole")
wh.vprism(6.0, 0, TOP + 1.0, TOP + 1.3, 0.24, "brass", caps=(False, True), captex="brass")
wh.vprism(7.6, 2, TOP + 0.12, TOP + 0.9, 0.08, "steel")
wh.hprism((7.3, TOP + 1.1, 2), (8.0, TOP + 1.1, 2), 0.3, "brass", caps=(True, True), captex="canvas")
for x in range(HX0, HX1 + 1):                                                              # rails round the bridge and its wings
    for sign in (-1, 1):
        zr = sign * (HB + 0.4 if x in (5, 6, 7) else HZ + 0.4)
        sec(x).box((x, BRIDGE + 1.1, zr), (1.0, 0.06, 0.06), "brass")
        if x % 2 == 0:
            sec(x).box((x, BRIDGE + 0.6, zr), (0.05, 1.0, 0.05), "steel")
for sign in (-1, 1):                                                                       # sidelights on the wing ends
    wh.box((6, BRIDGE + 0.7, sign * (HB + 0.2)), (0.5, 0.6, 0.3), "red" if sign < 0 else "green")


# ---- furniture, built into the sections
def bunk(x, z, y, along="x", upper=False):
    m = sec(x)
    size = (1.9, 0.14, 0.8) if along == "x" else (0.8, 0.14, 1.9)
    for dy in ((0.5, 1.6) if upper else (0.5,)):
        m.box((x + (0.45 if along == "x" else 0), y + dy, z + (0.45 if along == "z" else 0)), size, "pole")
        m.box((x + (0.45 if along == "x" else 0), y + dy + 0.12, z + (0.45 if along == "z" else 0)), (size[0] - 0.1, 0.12, size[2] - 0.1), "canvas")


def table(x, z, y, lx=1.8, lz=0.9):
    m = sec(x)
    m.box((x, y + 0.82, z), (lx, 0.08, lz), "pole")
    for dx in (-lx / 2 + 0.1, lx / 2 - 0.1):
        m.box((x + dx, y + 0.45, z), (0.08, 0.7, lz - 0.2), "pole")


def bench(x, z, y, lx=1.8):
    sec(x).box((x, y + 0.5, z), (lx, 0.08, 0.36), "pole")
    for dx in (-lx / 2 + 0.1, lx / 2 - 0.1):
        sec(x).box((x + dx, y + 0.3, z), (0.08, 0.4, 0.3), "pole")


def stove(x, z, y):
    m = sec(x)
    m.box((x, y + 0.55, z), (1.6, 0.9, 0.8), "darkplate")
    m.box((x, y + 1.03, z), (1.7, 0.06, 0.9), "band")
    m.vprism(x + 0.5, z, y + 1.0, y + 2.9, 0.1, "pipe")
    for dx in (-0.4, 0.3):
        m.box((x + dx, y + 0.55, z - 0.41 if z < 0 else z + 0.41), (0.4, 0.3, 0.03), "brass")


def desk(x, z, y):
    m = sec(x)
    m.box((x, y + 0.82, z), (0.9, 0.08, 0.6), "pole")
    m.box((x, y + 0.45, z), (0.8, 0.7, 0.5), "pole", faces=["north", "south", "east", "west"])
    m.box((x, y + 0.98, z), (0.14, 0.24, 0.14), "brass")                                   # a lamp


F = MAIN + 0.12
for rooms, outer in ((PORT_ROOMS, -6), (STBD_ROOMS, 6)):
    inner = outer + (1 if outer < 0 else -1)
    for a, b, kind in rooms:
        if kind == "cabin":
            bunk(a, outer, F)
            desk(b, outer, F)
        elif kind == "mess":
            table((a + b) / 2, outer + (0.6 if outer < 0 else -0.6), F, lx=b - a)
            bench((a + b) / 2, outer - (0.25 if outer < 0 else -0.25), F, lx=b - a)
        elif kind == "saloon":
            table((a + b) / 2, outer + (1.0 if outer < 0 else -1.0), F, lx=b - a - 0.6, lz=1.2)
            bench((a + b) / 2, outer - (0.2 if outer < 0 else -0.2), F, lx=b - a - 0.4)
        elif kind == "galley":
            stove((a + b) / 2, outer - (0.1 if outer < 0 else -0.1), F)
            table(a + 0.5, inner, F, lx=1.0, lz=0.7)
        elif kind == "bath":
            sec(a).box((a + 0.6, F + 0.35, outer), (1.8, 0.6, 0.8), "shipwhite")
# the forecastle: two tiers of bunks down each side, a mess table in each half
for x in (41, 43, 45):
    for outer in (-5, 5):
        if interior(x, MAIN, outer) and interior(x + 1, MAIN, outer):
            bunk(x, outer, F, upper=True)
for z in (-2, 2):
    table(43.5, z, F, lx=3.0)
    bench(43.5, z - 0.8, F, lx=3.0)
# the master's quarters and the chart room
U = UPPER + 0.12
bunk(-1, -4, U)
desk(1, -4, U)
table(6, -3, U, lx=2.4, lz=1.1)
bench(6, -3.9, U, lx=2.4)
table(6, 3.4, U, lx=3.0, lz=1.0)                                                           # the chart table
bunk(-1, 4, U)
# hospital in the poop
bunk(-50, -5, F)
bunk(-47, -5, F)

# forecastle head: bitts, the bell, the anchors at the hawse
fo = sec(50)
for x, z in ((42, 4), (42, -4), (49, 2), (49, -2)):
    for dz in (-0.3, 0.3):
        sec(x).vprism(x, z + dz, UPPER + 0.12, UPPER + 0.8, 0.14, "band", caps=(False, True), captex="band")
    sec(x).box((x, UPPER + 0.2, z), (0.5, 0.16, 1.0), "band")
fo.vprism(48, 0, UPPER + 0.12, UPPER + 1.5, 0.06, "steel")
fo.vprism(48, 0, UPPER + 1.2, UPPER + 1.5, 0.2, "brass", caps=(False, True), captex="brass")
for sign in (-1, 1):
    e = edge(50, UPPER - 1) + 0.14
    fo.box((50, UPPER - 1.4, sign * e), (0.24, 1.6, 0.18), "band")
    fo.box((50, UPPER - 2.2, sign * e), (1.3, 0.24, 0.18), "band")
    fo.hprism((49.6, UPPER - 0.6, sign * (e - 0.1)), (50.4, UPPER - 0.6, sign * (e - 0.1)), 0.24, "band", caps=(True, True), captex="black")
# poop: the emergency wheel, a capstan, the flagstaff
po = sec(-50)
po.wheel((-49.0, UPPER + 1.2, 0), 0.7, 0.08, 0.08, "pole", axis="x")
po.box((-49.3, UPPER + 0.7, 0), (0.3, 1.2, 0.3), "pole")
po.vprism(-46, 0, UPPER + 0.12, UPPER + 0.9, 0.3, "band", caps=(False, True), captex="brass")
sec(-54).vprism(-54, 0, UPPER + 0.12, UPPER + 4.0, 0.05, "pole")
# steam pipes along the well decks to the winches
for x in list(range(10, 40)) + list(range(-43, -20)):
    sec(x).hprism((x - 0.5, MAIN + 0.3, 6.0), (x + 0.5, MAIN + 0.3, 6.0), 0.1, "pipe")
    if x % 4 == 0:
        sec(x).box((x, MAIN + 0.2, 6.0), (0.14, 0.16, 0.3), "band")

for m in hull:
    models[m.name] = m

# =========================================================================================== write it out
ORDER = "fnesw"
codes, code_index = [], {}


def index_of(code):
    if code not in code_index:
        code_index[code] = len(codes)
        codes.append(code)
    return code_index[code]


cells = []
placed = set()


def put(cell, code, group):
    cells.extend([cell[0], cell[1], cell[2], index_of(code), group])
    placed.add(cell)
    if group >= 0:
        groups[group]["cells"] += 1


# models first: each takes the place of one solid cell
for i in range(len(SECTIONS)):
    put(anchor_of(i), f"froststory:shipmodel-hull{i}", i)
for model, origin, group in instances:
    put(origin, f"froststory:shipmodel-{model[5:]}", group)
for cell, group in solid.items():
    if cell not in placed:
        put(cell, "froststory:shipcell-solid", group)
for cell, fl in flags.items():
    if cell in placed or cell in real or not fl:
        continue
    put(cell, "froststory:shipcell-" + "".join(c for c in ORDER if c in fl), hull_group(cell[0]))
for cell, code in real.items():
    if cell not in placed:
        put(cell, code, -1)

# what a section of hull yields: its plate as plates and offcuts, its decks and panelling as planks
for i in range(len(SECTIONS)):
    n = groups[i]["cells"]
    a, b = SECTIONS[i]
    wood = sum(1 for (x, Y, z), tex in floors.items() if a <= x <= b and tex == "shipdeck")
    groups[i]["drops"] = [["game:metalplate-iron", max(4, n // 30)], ["game:metalbit-iron", n // 6], ["game:plank-aged", wood // 4]]

clear = []
for x in range(X0 - 1, X1 + 2):
    clear.extend([x, -HB - 1, HB + 1, 34])
ice = []
REACH = 7
for x in range(X0 - REACH, X1 + REACH + 1):
    for z in range(-HB - REACH, HB + REACH + 1):
        if in_hull(x, WATER, z):
            continue
        near = any(in_hull(x + dx, WATER, z + dz) for dx in range(-REACH, REACH + 1) for dz in range(-REACH, REACH + 1) if dx * dx + dz * dz <= REACH * REACH)
        if near:
            for y in (WATER - 1, WATER):
                if not in_hull(x, y, z):
                    ice.extend([x, y, z])

os.makedirs(os.path.join(ASSETS, "config"), exist_ok=True)
with open(os.path.join(ASSETS, "config", "wreck.json"), "w") as f:
    json.dump({
        "minX": X0, "maxX": X1, "halfBeam": HB, "height": 34, "waterline": WATER,
        "codes": codes, "cells": cells,
        "groups": [{"name": gr["name"], "drops": [{"code": c, "quantity": q} for c, q in gr["drops"] if q > 0]} for gr in groups],
        "clear": clear, "ice": ice,
    }, f, separators=(",", ":"))
    f.write("\n")

total = 0
for m in models.values():
    total += m.save(SHAPES)

# ---- the block types
BOXES = {"f": (0, 0, 0, 1, 0.125, 1), "n": (0, 0, 0, 1, 1, 0.125), "s": (0, 0, 0.875, 1, 1, 1), "w": (0, 0, 0, 0.125, 1, 1), "e": (0.875, 0, 0, 1, 1, 1)}


def cuboid(b):
    return dict(zip(("x1", "y1", "z1", "x2", "y2", "z2"), b))


masks = ["".join(c for i, c in enumerate(ORDER) if bits >> i & 1) for bits in range(1, 32)]
SOUND = {"place": "game:block/chute", "break": "game:block/chute", "hit": "game:block/chute", "walk": "game:walk/stone"}
shipcell = {
    "code": "shipcell", "class": "FrostStoryShipCell",
    "variantgroups": [{"code": "type", "states": ["solid"] + masks}],
    "attributes": {"handbook": {"exclude": True}},
    "drawtype": "json", "shape": {"base": "game:block/basic/nothing"},
    "blockmaterial": "Metal",
    "sidesolidByType": {"*-solid": {"all": True}, "*": {"all": False}},
    "sideopaque": {"all": False}, "emitSideAo": {"all": False}, "faceCullMode": "NeverCull", "lightAbsorption": 0,
    "replaceable": 90, "resistance": 30, "requiredMiningTier": 2, "drops": [],
    "collisionSelectionBoxesByType": {"*-solid": [cuboid((0, 0, 0, 1, 1, 1))], **{f"*-{mask}": [cuboid(BOXES[c]) for c in mask] for mask in masks}},
    "sounds": SOUND,
}
TEX = {k: {"base": v[0]} for k, v in g.TEXTURES.items() if k in ("shiphull", "shipdeck", "shippanel", "shipwhite", "band", "girder", "brass", "pipe", "black", "steel", "green", "red", "pole", "darkplate", "grating", "canvas")}
names = sorted(n[5:].replace("-", "") if n.startswith("ship-hull") else n[5:] for n in models)
shipmodel = {
    "code": "shipmodel", "class": "FrostStoryShipCell",
    "variantgroups": [{"code": "type", "states": names}],
    "attributes": {"handbook": {"exclude": True}},
    "drawtype": "json",
    "shapeByType": {f"*-{n}": {"base": T + ("ship-hull-" + n[4:] if n.startswith("hull") else "ship-" + n)} for n in names},
    "textures": TEX, "renderPass": "OpaqueNoCull", "faceCullMode": "NeverCull",
    "blockmaterial": "Metal", "sidesolid": {"all": True}, "sideopaque": {"all": False}, "emitSideAo": {"all": False}, "lightAbsorption": 0,
    "replaceable": 90, "resistance": 30, "requiredMiningTier": 2, "drops": [], "sounds": SOUND,
}
for name, data in (("shipcell", shipcell), ("shipmodel", shipmodel)):
    with open(os.path.join(ASSETS, "blocktypes", "generator", name + ".json"), "w") as f:
        json.dump(data, f, indent="\t")
        f.write("\n")

kinds = {}
for code_i in cells[3::5]:
    k = codes[code_i].split(":")[1].split("-")[0]
    kinds[k] = kinds.get(k, 0) + 1
coal = sum(int(codes[c].rsplit("-", 1)[1]) * 64 for c in cells[3::5] if "coalheap" in codes[c])
print(f"Wreck: {len(cells) // 5} blocks {kinds}, {len(groups)} groups, {len(models)} models ({total} elements), {coal} coal, {len(ice) // 3} ice")
for m in sorted(models.values(), key=lambda m: -len(m.elements))[:4]:
    print(f"  {m.name}: {len(m.elements)} elements")
