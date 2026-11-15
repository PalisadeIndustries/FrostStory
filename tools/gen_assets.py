#!/usr/bin/env python3
"""Generates FrostStory's placeholder textures and block shapes.

Everything here is procedural so the look can be tuned in one place and regenerated:

    python3 tools/gen_assets.py

Outputs go to FrostStory/assets/froststory/{textures,shapes}/block/generator/.
Hand-painted replacements can simply overwrite the PNGs (and be removed from TEXTURES below).
"""
import json
import math
import os
import random

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "FrostStory", "assets", "froststory")
TEX_DIR = os.path.join(ROOT, "textures", "block", "generator")
SHAPE_DIR = os.path.join(ROOT, "shapes", "block", "generator")
S = 32


# --------------------------------------------------------------------------- texture toolkit

class Tex:
    def __init__(self, name, color, grain=7, blotch=10, alpha=255):
        self.name = name
        self.rng = random.Random(name)
        self.px = [[(0, 0, 0, alpha)] * S for _ in range(S)]
        field = self._field(4)
        for y in range(S):
            for x in range(S):
                d = (field[y][x] - 0.5) * 2 * blotch + self.rng.uniform(-grain, grain)
                self.px[y][x] = tuple(clamp(c + d) for c in color) + (alpha,)

    def _field(self, cells):
        """Tileable low-frequency noise, 0..1."""
        g = [[self.rng.random() for _ in range(cells)] for _ in range(cells)]
        out = [[0.0] * S for _ in range(S)]
        for y in range(S):
            for x in range(S):
                fx, fy = x * cells / S, y * cells / S
                x0, y0 = int(fx), int(fy)
                tx, ty = smooth(fx - x0), smooth(fy - y0)
                x1, y1 = (x0 + 1) % cells, (y0 + 1) % cells
                top = g[y0][x0] * (1 - tx) + g[y0][x1] * tx
                bot = g[y1][x0] * (1 - tx) + g[y1][x1] * tx
                out[y][x] = top * (1 - ty) + bot * ty
        return out

    def shade(self, x, y, d):
        if 0 <= x < S and 0 <= y < S:
            r, g, b, a = self.px[y][x]
            self.px[y][x] = (clamp(r + d), clamp(g + d), clamp(b + d), a)

    def tint(self, x, y, color, amount):
        if 0 <= x < S and 0 <= y < S:
            r, g, b, a = self.px[y][x]
            self.px[y][x] = tuple(clamp(o * (1 - amount) + c * amount) for o, c in zip((r, g, b), color)) + (a,)

    def fill(self, x0, y0, x1, y1, color, amount=1.0):
        for y in range(y0, y1):
            for x in range(x0, x1):
                self.tint(x, y, color, amount)

    def rect_shade(self, x0, y0, x1, y1, d):
        for y in range(y0, y1):
            for x in range(x0, x1):
                self.shade(x, y, d)

    def clear(self, x0, y0, x1, y1):
        for y in range(y0, y1):
            for x in range(x0, x1):
                if 0 <= x < S and 0 <= y < S:
                    self.px[y][x] = (0, 0, 0, 0)

    def panel(self, x0, y0, x1, y1, light=16, dark=-22):
        """A raised plate: lit top/left edge, shadowed bottom/right edge."""
        for x in range(x0, x1):
            self.shade(x, y0, light)
            self.shade(x, y1 - 1, dark)
        for y in range(y0, y1):
            self.shade(x0, y, light)
            self.shade(x1 - 1, y, dark)

    def groove(self, x0, y0, x1, y1, d=-30):
        self.rect_shade(x0, y0, x1, y1, d)

    def rivet(self, x, y):
        self.shade(x, y, 34)
        self.shade(x + 1, y, 8)
        self.shade(x, y + 1, 8)
        self.shade(x + 1, y + 1, -34)

    def rivet_row(self, y, step=6, offset=2):
        for x in range(offset, S, step):
            self.rivet(x, y)

    def rivet_col(self, x, step=6, offset=2):
        for y in range(offset, S, step):
            self.rivet(x, y)

    def round_shade(self, strength=38, axis="x"):
        """Cylinder shading across one axis: dark edges, highlight off-centre."""
        for y in range(S):
            for x in range(S):
                t = ((x if axis == "x" else y) + 0.5) / S
                d = math.cos((t - 0.38) * math.pi * 1.25) * strength - strength * 0.55
                self.shade(x, y, int(d))

    def streaks(self, amount=6, vertical=True):
        for i in range(S):
            d = self.rng.uniform(-amount, amount)
            for j in range(S):
                self.shade(i if vertical else j, j if vertical else i, int(d))

    def rust(self, count=10, color=(120, 66, 40)):
        for _ in range(count):
            x, y = self.rng.randrange(S), self.rng.randrange(S)
            for _ in range(self.rng.randrange(2, 6)):
                self.tint(x, y, color, self.rng.uniform(0.15, 0.4))
                x, y = x + self.rng.choice((-1, 0, 1)), y + self.rng.choice((0, 1))

    def disc(self, cx, cy, r, color, amount=1.0):
        for y in range(S):
            for x in range(S):
                if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r:
                    self.tint(x, y, color, amount)

    def ring(self, cx, cy, r0, r1, d):
        for y in range(S):
            for x in range(S):
                dist = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
                if r0 <= dist <= r1:
                    self.shade(x, y, d)

    def save(self):
        img = Image.new("RGBA", (S, S))
        img.putdata([p for row in self.px for p in row])
        img.save(os.path.join(TEX_DIR, self.name + ".png"))


def clamp(v):
    return max(0, min(255, int(round(v))))


def smooth(t):
    return t * t * (3 - 2 * t)


# --------------------------------------------------------------------------- textures

STEEL = (92, 99, 108)
DARK_STEEL = (66, 70, 78)
CORE = (54, 52, 60)
IRON = (82, 78, 76)
STONE = (106, 101, 96)
CONCRETE = (88, 87, 86)
BRASS = (158, 122, 62)
PAINT_RED = (150, 44, 36)
PAINT_GREEN = (66, 88, 82)


def tex_steel():
    t = Tex("steel", STEEL)
    t.streaks(5)
    t.panel(0, 0, S, S, 10, -14)
    return t


def tex_hull_plain():
    t = Tex("hullplate-plain", STEEL)
    t.streaks(4, vertical=False)
    for y0 in (0, 16):
        split = 20 if y0 == 0 else 10
        t.panel(0, y0, split, y0 + 16)
        t.panel(split, y0, S, y0 + 16)
    t.rust(5)
    return t


def tex_hull_riveted():
    t = Tex("hullplate-riveted", DARK_STEEL)
    t.streaks(4, vertical=False)
    t.panel(0, 0, S, 8)
    t.panel(0, 8, S, 24, 12, -18)
    t.panel(0, 24, S, S)
    t.rivet_row(3, 5, 1)
    t.rivet_row(27, 5, 1)
    t.rust(6)
    return t


def tex_hull_vent():
    t = Tex("hullplate-vent", DARK_STEEL)
    t.panel(0, 0, S, S)
    for y in range(5, 28, 5):
        t.groove(4, y, 28, y + 2, -44)
        t.rect_shade(4, y + 2, 28, y + 3, 22)
    for x, y in ((1, 1), (29, 1), (1, 29), (29, 29)):
        t.rivet(x, y)
    return t


def tex_foundation():
    t = Tex("foundation", CONCRETE, grain=9, blotch=12)
    for y0 in (0, 16):
        offset = 0 if y0 == 0 else 16
        t.groove(0, y0, S, y0 + 1, -20)
        t.groove(offset, y0, offset + 1, y0 + 16, -20)
        t.rect_shade(0, y0 + 1, S, y0 + 2, 8)
    return t


def tex_lining(name, ribbed):
    t = Tex(name, STONE, grain=8, blotch=14)
    for row, y0 in enumerate(range(0, S, 8)):
        t.groove(0, y0, S, y0 + 1, -24)
        off = 6 if row % 2 else 22
        t.groove(off, y0, off + 1, y0 + 8, -24)
        t.rect_shade(0, y0 + 1, S, y0 + 2, 9)
    if ribbed:
        t.fill(0, 10, S, 22, DARK_STEEL, 0.95)
        t.panel(0, 10, S, 22, 18, -26)
        t.rivet_row(15, 6, 2)
    return t


def tex_lining_top():
    t = Tex("shaftlining-top", STONE, grain=8, blotch=12)
    t.panel(0, 0, S, S, 8, -18)
    return t


def tex_pipe():
    t = Tex("pipe", IRON, grain=5, blotch=6)
    t.round_shade(34)
    t.rust(8)
    return t


def tex_pipe_end():
    t = Tex("pipe-end", IRON)
    t.ring(16, 16, 9, 13, 14)
    t.disc(16, 16, 9, (20, 18, 18), 0.9)
    for i in range(8):
        a = i * math.pi / 4 + math.pi / 8
        t.rivet(int(16 + math.cos(a) * 14) - 1, int(16 + math.sin(a) * 14) - 1)
    return t


def tex_junction():
    t = Tex("pipejunction", IRON)
    t.panel(0, 0, S, S, 14, -22)
    t.ring(16, 16, 8, 11, 16)
    t.ring(16, 16, 0, 8, -10)
    for x, y in ((3, 3), (27, 3), (3, 27), (27, 27), (15, 2), (15, 28), (2, 15), (28, 15)):
        t.rivet(x, y)
    t.rust(6)
    return t


def tex_valve_body():
    t = Tex("valvebody", PAINT_RED, grain=6, blotch=10)
    t.panel(0, 0, S, S, 16, -26)
    t.fill(0, 0, S, 4, DARK_STEEL, 0.9)
    t.fill(0, 28, S, S, DARK_STEEL, 0.9)
    t.rivet_row(1, 6, 2)
    t.rivet_row(29, 6, 2)
    t.ring(16, 16, 6, 8, 20)
    t.ring(16, 16, 0, 6, -14)
    t.rust(8, (60, 30, 24))
    return t


def tex_paint_red():
    t = Tex("paint-red", PAINT_RED, grain=6, blotch=8)
    t.rust(5, (70, 34, 26))
    return t


def tex_pump_casing():
    t = Tex("pumpcasing", PAINT_GREEN, grain=6, blotch=9)
    t.panel(0, 0, S, S, 14, -24)
    t.panel(6, 6, 26, 26, -18, 14)
    t.panel(7, 7, 25, 25, 12, -16)
    for x, y in ((2, 2), (28, 2), (2, 28), (28, 28), (15, 2), (15, 28), (2, 15), (28, 15)):
        t.rivet(x, y)
    t.rust(7)
    return t


def tex_pump_cylinder():
    t = Tex("pumpcylinder", BRASS, grain=5, blotch=8)
    t.round_shade(42)
    for y in (4, 15, 26):
        t.rect_shade(0, y, S, y + 1, 26)
        t.rect_shade(0, y + 1, S, y + 2, -30)
    return t


def tex_core_casing():
    t = Tex("corecasing", CORE, grain=5, blotch=9)
    t.panel(0, 0, 16, S)
    t.panel(16, 0, S, S)
    t.rivet_col(2, 6, 2)
    t.rivet_col(13, 6, 2)
    t.rivet_col(18, 6, 2)
    t.rivet_col(29, 6, 2)
    # heat bluing towards the seams
    for y in range(S):
        for x in (15, 16):
            t.tint(x, y, (96, 60, 110), 0.35)
    return t


def tex_core_heart(name, lit):
    base = (255, 150, 48) if lit else (62, 48, 42)
    t = Tex(name, base, grain=10 if lit else 5, blotch=26 if lit else 8)
    for y in range(1, S, 4):
        if lit:
            t.fill(0, y, S, y + 1, (255, 244, 190), 0.85)
            t.fill(0, y + 1, S, y + 2, (255, 208, 96), 0.6)
            t.fill(0, y + 3, S, y + 4, (214, 84, 22), 0.5)
        else:
            t.rect_shade(0, y, S, y + 1, 22)
            t.rect_shade(0, y + 2, S, y + 3, -18)
    if not lit:
        t.round_shade(26)
    return t


def tex_grating():
    t = Tex("grating", DARK_STEEL)
    for y in range(S):
        for x in range(S):
            if x % 8 in (2, 3, 4, 5, 6) and y % 8 in (2, 3, 4, 5, 6):
                t.px[y][x] = (0, 0, 0, 0)
            elif x % 8 == 1 or y % 8 == 1:
                t.shade(x, y, 18)
            elif x % 8 == 7 or y % 8 == 7:
                t.shade(x, y, -18)
    return t


def tex_girder():
    t = Tex("girder", (74, 72, 76), grain=6, blotch=9)
    t.streaks(5)
    t.rivet_col(3, 7, 3)
    t.rivet_col(26, 7, 3)
    t.rust(12)
    return t


def tex_girder_node():
    t = Tex("girdernode", (70, 68, 72), grain=6, blotch=9)
    t.panel(0, 0, S, S, 16, -24)
    t.panel(5, 5, 27, 27, 10, -14)
    for i in range(2, 30, 5):
        t.rivet(i, 2)
        t.rivet(i, 28)
        t.rivet(2, i)
        t.rivet(28, i)
    for i in range(8, 24, 5):
        t.rivet(i, i)
        t.rivet(i, 30 - i)
    t.rust(8)
    return t


def tex_console_front():
    t = Tex("console-front", (58, 62, 70))
    t.panel(0, 0, S, S, 14, -22)
    for cx in (9, 23):
        t.disc(cx, 11, 6.5, BRASS, 1.0)
        t.disc(cx, 11, 5, (226, 220, 200), 1.0)
        t.ring(cx, 11, 4.2, 5, -30)
    # needles
    for i in range(4):
        t.tint(9 + i, 11 - i, (150, 30, 24), 1.0)
        t.tint(23 - i, 11 - i // 2, (150, 30, 24), 1.0)
    # switch row
    for x in range(4, 28, 5):
        t.fill(x, 22, x + 3, 27, (30, 30, 34))
        t.fill(x + 1, 22, x + 2, 25, BRASS)
    t.rivet(1, 1)
    t.rivet(29, 1)
    t.rivet(1, 29)
    t.rivet(29, 29)
    return t


def tex_console_side():
    t = Tex("console-side", (58, 62, 70))
    t.panel(0, 0, S, S, 14, -22)
    t.panel(4, 4, 28, 28, -16, 12)
    t.rivet_row(1, 7, 2)
    t.rivet_row(29, 7, 2)
    return t


def tex_console_top():
    t = Tex("console-top", (70, 58, 46), grain=6, blotch=10)
    t.streaks(6, vertical=False)
    t.panel(0, 0, S, S, 12, -20)
    t.fill(5, 8, 19, 24, (214, 204, 176), 0.95)   # a ledger sheet
    for y in range(11, 23, 3):
        t.fill(7, y, 17, y + 1, (90, 84, 76), 0.8)
    t.disc(25, 14, 3, BRASS, 1.0)
    return t


TEXTURES = [
    tex_steel, tex_hull_plain, tex_hull_riveted, tex_hull_vent, tex_foundation,
    lambda: tex_lining("shaftlining-plain", False), lambda: tex_lining("shaftlining-ribbed", True), tex_lining_top,
    tex_pipe, tex_pipe_end, tex_junction, tex_valve_body, tex_paint_red, tex_pump_casing, tex_pump_cylinder,
    tex_core_casing, lambda: tex_core_heart("coreheart-cold", False), lambda: tex_core_heart("coreheart-lit", True),
    tex_grating, tex_girder, tex_girder_node, tex_console_front, tex_console_side, tex_console_top,
]


# --------------------------------------------------------------------------- shapes

def box(name, frm, to, tex, faces=None, rotation=None, origin=None):
    """An element whose UVs follow its own position, so textures line up across neighbouring boxes."""
    x0, y0, z0 = frm
    x1, y1, z1 = to

    def uv(face):
        if face in ("north", "south"):
            return [x0, 16 - y1, x1, 16 - y0]
        if face in ("east", "west"):
            return [z0, 16 - y1, z1, 16 - y0]
        return [x0, z0, x1, z1]

    el = {"name": name, "from": list(frm), "to": list(to), "faces": {}}
    for face in ("north", "east", "south", "west", "up", "down"):
        code = (faces or {}).get(face, tex)
        if code is None:
            el["faces"][face] = {"texture": "#" + tex, "uv": uv(face), "enabled": False}
        else:
            el["faces"][face] = {"texture": "#" + code, "uv": [clampuv(v) for v in uv(face)]}
    if rotation:
        el["rotationOrigin"] = list(origin or (8, 8, 8))
        for axis, deg in rotation.items():
            el["rotation" + axis.upper()] = deg
    return el


def clampuv(v):
    return max(0.0, min(16.0, float(v)))


def shape(name, textures, elements):
    data = {
        "editor": {"allAngles": True},
        "textureWidth": 16,
        "textureHeight": 16,
        "textures": {k: "froststory:block/generator/" + v for k, v in textures.items()},
        "elements": elements,
    }
    with open(os.path.join(SHAPE_DIR, name + ".json"), "w") as f:
        json.dump(data, f, indent="\t")
        f.write("\n")


def shape_pipe():
    ends = {"up": "end", "down": "end"}
    shape("pipe", {"side": "pipe", "end": "pipe-end", "flange": "steel"}, [
        box("Tube", (2, 2, 2), (14, 14, 14), "side", {"up": None, "down": None}),
        box("FlangeLow", (1, 0, 1), (15, 2, 15), "flange", ends),
        box("FlangeHigh", (1, 14, 1), (15, 16, 15), "flange", ends),
    ])


def shape_girder():
    els = []
    for i, (x, z) in enumerate(((0, 0), (13, 0), (0, 13), (13, 13))):
        els.append(box(f"Post{i}", (x, 0, z), (x + 3, 16, z + 3), "girder"))
    # A top tie and one diagonal brace on each side.
    els += [
        box("TieN", (3, 14, 0.5), (13, 16, 1.5), "girder"),
        box("TieS", (3, 14, 14.5), (13, 16, 15.5), "girder"),
        box("TieW", (0.5, 14, 3), (1.5, 16, 13), "girder"),
        box("TieE", (14.5, 14, 3), (15.5, 16, 13), "girder"),
        box("BraceN", (-1, 7.25, 0.75), (17, 8.75, 1.25), "girder", rotation={"z": 45}, origin=(8, 8, 1)),
        box("BraceS", (-1, 7.25, 14.75), (17, 8.75, 15.25), "girder", rotation={"z": -45}, origin=(8, 8, 15)),
        box("BraceW", (0.75, 7.25, -1), (1.25, 8.75, 17), "girder", rotation={"x": 45}, origin=(1, 8, 8)),
        box("BraceE", (14.75, 7.25, -1), (15.25, 8.75, 17), "girder", rotation={"x": -45}, origin=(15, 8, 8)),
    ]
    shape("girder", {"girder": "girder"}, els)


def shape_valvewheel():
    shape("valvewheel", {"steel": "steel", "paint": "paint-red"}, [
        box("Stem", (7, 0, 7), (9, 9.5, 9), "steel"),
        box("Hub", (6, 9, 6), (10, 11.5, 10), "paint"),
        box("RimN", (1, 9.5, 1), (15, 11, 2.5), "paint"),
        box("RimS", (1, 9.5, 13.5), (15, 11, 15), "paint"),
        box("RimW", (1, 9.5, 2.5), (2.5, 11, 13.5), "paint"),
        box("RimE", (13.5, 9.5, 2.5), (15, 11, 13.5), "paint"),
        box("SpokeX", (2.5, 9.75, 7.25), (13.5, 10.75, 8.75), "paint"),
        box("SpokeZ", (7.25, 9.75, 2.5), (8.75, 10.75, 13.5), "paint"),
    ])


def shape_grating():
    shape("grating", {"grating": "grating"}, [
        box("Deck", (0, 14, 0), (16, 16, 16), "grating"),
    ])


def shape_grille():
    els = [
        box("SillLow", (0, 0, 0), (16, 2, 16), "steel"),
        box("SillHigh", (0, 14, 0), (16, 16, 16), "steel"),
    ]
    for i, (x, z) in enumerate(((0, 0), (14, 0), (0, 14), (14, 14))):
        els.append(box(f"Post{i}", (x, 2, z), (x + 2, 14, z + 2), "steel"))
    for i, a in enumerate((3.75, 7.25, 10.75)):
        els += [
            box(f"BarN{i}", (a, 2, 0.25), (a + 1.5, 14, 1.75), "steel"),
            box(f"BarS{i}", (a, 2, 14.25), (a + 1.5, 14, 15.75), "steel"),
            box(f"BarW{i}", (0.25, 2, a), (1.75, 14, a + 1.5), "steel"),
            box(f"BarE{i}", (14.25, 2, a), (15.75, 14, a + 1.5), "steel"),
        ]
    shape("grille", {"steel": "steel"}, els)


def shape_ladder():
    els = [
        box("RailW", (2, 0, 0.5), (3.5, 16, 2), "steel"),
        box("RailE", (12.5, 0, 0.5), (14, 16, 2), "steel"),
    ]
    for i, y in enumerate((1.5, 5.5, 9.5, 13.5)):
        els.append(box(f"Rung{i}", (3.5, y, 0.75), (12.5, y + 1, 1.75), "steel"))
    shape("ladder", {"steel": "steel"}, els)


def shape_console():
    side = {"north": "side", "south": "side", "east": "side", "west": "side", "up": "top", "down": "side"}
    shape("console", {"side": "console-side", "top": "console-top", "front": "console-front", "paint": "paint-red"}, [
        box("Cabinet", (1, 0, 4), (15, 9, 15), "side", side),
        box("Desk", (0.5, 9, 3), (15.5, 11, 15.5), "side", side),
        box("Panel", (1, 11, 11), (15, 16, 15), "side", dict(side, north="front", up="side")),
        box("Lever", (12, 11, 6), (13, 14.5, 7), "paint"),
        box("LeverBase", (11, 11, 5), (14, 11.75, 8), "side", dict(side, up="side")),
    ])


SHAPES = [shape_pipe, shape_girder, shape_valvewheel, shape_grating, shape_grille, shape_ladder, shape_console]


def main():
    os.makedirs(TEX_DIR, exist_ok=True)
    os.makedirs(SHAPE_DIR, exist_ok=True)
    for make in TEXTURES:
        make().save()
    for make in SHAPES:
        make()
    print(f"Wrote {len(TEXTURES)} textures and {len(SHAPES)} shapes.")


if __name__ == "__main__":
    main()
