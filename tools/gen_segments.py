#!/usr/bin/env python3
"""Generates the generator's modelled segments: large block shapes that the blueprint anchors in the world.

    python3 tools/gen_segments.py

Geometry is written in "tower space", in blocks: the generator's axis is x = z = 0, y = 0 is the top of the
deck, +x is east and +z is south. Each segment is anchored to one block cell; the shape file stores its
elements relative to that cell, and the engine draws the whole thing as that block's model.

Elements are boxes turned about their own centre. The engine applies an element's rotations as
Rx * Ry * Rz, so everything here is built from "yaw about the tower axis, then tilt" (Ry * Rz), which is
all an octagonal tower needs.

Outputs: shapes/block/generator/segment/*.json and blocktypes/generator/gensegment.json.
"""
import json
import math
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "FrostStory", "assets", "froststory")
SHAPE_DIR = os.path.join(ROOT, "shapes", "block", "generator", "segment")
BLOCKTYPE = os.path.join(ROOT, "blocktypes", "generator", "gensegment.json")

T22 = math.tan(math.radians(22.5))
C22 = math.cos(math.radians(22.5))
CAP_STEP = 0.006   # crossing slabs end at slightly different heights, so their caps never share a plane

# texture code -> (asset path, width, height) with sizes in shape units (16 per block)
TEXTURES = {
    "plating": ("block/generator/segment/plating", 64, 80),
    "darkplate": ("block/generator/segment/darkplate", 32, 32),
    "band": ("block/generator/segment/band", 64, 16),
    "girder": ("block/generator/segment/girder", 16, 64),
    "pipe": ("block/generator/segment/pipe", 16, 64),
    "brass": ("block/generator/segment/brass", 16, 64),
    "tubbing": ("block/generator/segment/tubbing", 48, 32),
    "grating": ("block/generator/segment/grating", 32, 32),
    "checker": ("block/generator/segment/checker", 16, 16),
    "coil": ("block/generator/segment/coil-cold", 16, 32),
    "coillit": ("block/generator/segment/coil-lit", 16, 32),
    "green": ("block/generator/segment/green", 32, 32),
    "black": ("block/generator/segment/black", 16, 16),
    "red": ("block/generator/paint-red", 16, 16),
    "steel": ("block/generator/steel", 16, 16),
}

ALL = ("north", "east", "south", "west", "up", "down")


def side(apothem):
    """Side length of a regular octagon."""
    return 2 * apothem * T22


def yaw(theta, x, z):
    """Rotate a horizontal offset about the vertical axis the way the engine's rotationY does."""
    a = math.radians(theta)
    return x * math.cos(a) + z * math.sin(a), -x * math.sin(a) + z * math.cos(a)


class Model:
    def __init__(self, name, anchor, turn=0):
        """turn swings the whole model by quarter turns about the tower axis (east to south is one)."""
        self.name = name
        self.anchor = anchor
        self.turn = turn
        self.elements = []
        self.used = set()

    # ------------------------------------------------------------------ primitives

    def box(self, center, size, tex, rot=(0, 0, 0), faces=ALL, glow=0, uvoff=(0, 0), facetex=None):
        """A box turned about its own centre by rot = (rx, ry, rz) degrees."""
        if self.turn:
            assert not rot[0], "a turned model cannot hold boxes rotated about X"
            x, z = yaw(-90 * self.turn, center[0], center[2])
            center = (x, center[1], z)
            rot = (0, rot[1] - 90 * self.turn, rot[2])
        ax, ay, az = self.anchor
        cx = (center[0] - (ax - 0.5)) * 16
        cy = (center[1] - (ay - 1)) * 16
        cz = (center[2] - (az - 0.5)) * 16
        sx, sy, sz = (s * 16 for s in size)

        el = {
            "name": f"e{len(self.elements)}",
            "from": [round(cx - sx / 2, 3), round(cy - sy / 2, 3), round(cz - sz / 2, 3)],
            "to": [round(cx + sx / 2, 3), round(cy + sy / 2, 3), round(cz + sz / 2, 3)],
            "faces": {},
        }
        if any(rot):
            el["rotationOrigin"] = [round(cx, 3), round(cy, 3), round(cz, 3)]
            for axis, deg in zip("XYZ", rot):
                if deg:
                    el["rotation" + axis] = round(deg, 3)

        dims = {"north": (sx, sy), "south": (sx, sy), "east": (sz, sy), "west": (sz, sy), "up": (sx, sz), "down": (sx, sz)}
        for face in faces:
            code = (facetex or {}).get(face, tex)
            _, tw, th = TEXTURES[code]
            self.used.add(code)
            fw, fh = dims[face]
            u0 = uvoff[0] % tw if fw < tw else 0
            v0 = uvoff[1] % th if fh < th else 0
            if u0 + fw > tw:
                u0 = max(0, tw - fw)
            if v0 + fh > th:
                v0 = max(0, th - fh)
            spec = {"texture": "#" + code, "uv": [round(u0, 3), round(v0, 3), round(min(tw, u0 + fw), 3), round(min(th, v0 + fh), 3)]}
            if glow:
                spec["glow"] = glow
            el["faces"][face] = spec
        self.elements.append(el)

    def east(self, theta, center, size, tex, tilt=0, origin=(0, 0), **kw):
        """A box given in the east-facing frame (x outward, z along the wall), swung by theta about the axis.

        tilt leans it about its own long horizontal axis: positive tips the top inward."""
        x, z = yaw(theta, center[0], center[2])
        self.box((origin[0] + x, center[1], origin[1] + z), size, tex, (0, theta, tilt), **kw)

    def member(self, p0, p1, w, d, tex, caps=False, chunk=4.0):
        """A straight structural member of section w x d between two points; its texture runs along it."""
        dx, dy, dz = (p1[i] - p0[i] for i in range(3))
        length = math.sqrt(dx * dx + dy * dy + dz * dz)
        ux, uy, uz = dx / length, dy / length, dz / length
        phi = math.degrees(math.acos(max(-1, min(1, uy))))
        theta = 0 if math.hypot(ux, uz) < 1e-6 else math.degrees(math.atan2(uz, -ux))
        n = max(1, math.ceil(length / chunk - 1e-6))
        step = length / n
        for i in range(n):
            mid = (i + 0.5) * step
            faces = ["north", "east", "south", "west"]
            if caps and i == 0:
                faces.append("down")
            if caps and i == n - 1:
                faces.append("up")
            self.box((p0[0] + ux * mid, p0[1] + uy * mid, p0[2] + uz * mid), (w, step, d), tex, (0, theta, phi), faces=faces)

    def vprism(self, cx, cz, y0, y1, a, tex, caps=(False, False), captex="black", phase=0, chunk=4.0, glow=0):
        """A solid upright octagonal prism, as four crossing slabs."""
        s = side(a)
        n = max(1, math.ceil((y1 - y0) / chunk - 1e-6))
        step = (y1 - y0) / n
        for i in range(n):
            for k in range(4):
                faces = ["north", "south"]
                lo, hi = y0 + i * step, y0 + (i + 1) * step
                if caps[0] and i == 0:
                    faces.append("down")
                    lo -= k * CAP_STEP
                if caps[1] and i == n - 1:
                    faces.append("up")
                    hi += k * CAP_STEP
                self.box((cx, (lo + hi) / 2, cz), (s, hi - lo, 2 * a), tex, (0, k * 45 + phase, 0), faces=faces,
                         glow=glow, facetex={"up": captex, "down": captex})

    def hprism(self, p0, p1, a, tex, caps=(False, False), captex="black", chunk=4.0):
        """A solid level octagonal prism (a round pipe) between two points at the same height."""
        dx, dz = p1[0] - p0[0], p1[2] - p0[2]
        length = math.hypot(dx, dz)
        theta = math.degrees(math.atan2(dx, dz))
        s = side(a)
        n = max(1, math.ceil(length / chunk - 1e-6))
        step = length / n
        for i in range(n):
            for k in range(4):
                faces = ["up", "down"]
                lo, hi = i * step, (i + 1) * step
                if caps[0] and i == 0:
                    faces.append("north")
                    lo -= k * CAP_STEP
                if caps[1] and i == n - 1:
                    faces.append("south")
                    hi += k * CAP_STEP
                mid = (lo + hi) / 2 / length
                self.box((p0[0] + dx * mid, p0[1], p0[2] + dz * mid), (s, 2 * a, hi - lo), tex, (0, theta, k * 45), faces=faces,
                         facetex={"north": captex, "south": captex})

    def ring(self, y0, y1, a, t, tex, sides=range(8), inside=True, outside=True, rims=True, origin=(0, 0), phase=0, intex=None, chunk=None, split=1):
        """An octagonal wall: one panel per side (or `split` panels, where a side is wider than its texture),
        outer faces meeting at the corners."""
        s = side(a)
        _, _, th = TEXTURES[tex]
        chunk = chunk or th / 16
        n = max(1, math.ceil((y1 - y0) / chunk - 1e-6))
        step = (y1 - y0) / n
        for i in range(n):
            for k in sides:
                faces = ["east"] if outside else []
                if inside:
                    faces.append("west")
                if rims and i == 0:
                    faces.append("down")
                if rims and i == n - 1:
                    faces.append("up")
                for j in range(split):
                    zc = -s / 2 + (j + 0.5) * s / split
                    self.east(k * 45 + phase, (a - t / 2, y0 + (i + 0.5) * step, zc), (t, step, s / split), tex, origin=origin,
                              faces=faces, facetex={"west": intex or tex}, uvoff=(k * 24 + j * 16, 0))

    def wall(self, k, a, t, y0, y1, z0, z1, tex, inside=True, intex=None):
        """Part of one octagon side, between tangential offsets z0..z1, e.g. the piers either side of a door."""
        s = side(a)
        _, _, th = TEXTURES[tex]
        n = max(1, math.ceil((y1 - y0) / (th / 16) - 1e-6))
        step = (y1 - y0) / n
        for i in range(n):
            faces = ["east", "north", "south", "up", "down"] + (["west"] if inside else [])
            self.east(k * 45, (a - t / 2, y0 + (i + 0.5) * step, (z0 + z1) / 2), (t, step, z1 - z0), tex, faces=faces,
                      facetex={"west": intex or tex}, uvoff=((z0 + s / 2) * 16, 0))

    def cone(self, y0, y1, a0, a1, t, tex, ribtex="band", courses=3, origin=(0, 0), ribs=True, inside=True, sides=range(8)):
        """A faceted taper between two octagons: plated courses with a rib over each hip."""
        tilt = math.degrees(math.atan2(a0 - a1, y1 - y0))
        for i in range(courses):
            f0, f1 = i / courses, (i + 1) / courses
            yb, yt = y0 + (y1 - y0) * f0, y0 + (y1 - y0) * f1
            ab, at = a0 + (a1 - a0) * f0, a0 + (a1 - a0) * f1
            length = math.hypot(ab - at, yt - yb)
            for k in sides:
                self.east(k * 45, ((ab + at) / 2 - t / 2, (yb + yt) / 2, 0), (t, length, side((ab + at) / 2)), tex, tilt=tilt,
                          origin=origin, faces=["east"] + (["west"] if inside else []), uvoff=(k * 24, i * 20))
        if ribs:
            rb, rt = a0 / C22, a1 / C22
            rib_tilt = math.degrees(math.atan2(rb - rt, y1 - y0))
            length = math.hypot(rb - rt, y1 - y0)
            for k in sides:
                self.east(k * 45 + 22.5, ((rb + rt) / 2 - 0.04, (y0 + y1) / 2, 0), (0.2, length, 0.5), ribtex, tilt=rib_tilt,
                          origin=origin, faces=["east", "north", "south"])

    def flange(self, y0, y1, a, tex="band", t=0.45, origin=(0, 0), phase=0):
        self.ring(y0, y1, a, t, tex, origin=origin, phase=phase, chunk=99)

    def wheel(self, center, a, rim, width, tex, axis="x"):
        """An upright octagonal wheel with four spokes, turning about a level axis."""
        cx, cy, cz = center
        s = side(a)
        for k in range(8):
            ang = math.radians(k * 45)
            r = a - rim / 2
            if axis == "x":
                self.box((cx, cy + r * math.cos(ang), cz + r * math.sin(ang)), (width, rim, s), tex, (k * 45, 0, 0))
            else:
                self.box((cx + r * math.sin(ang), cy + r * math.cos(ang), cz), (s, rim, width), tex, (0, 0, -k * 45))
        for k in range(4):
            if axis == "x":
                self.box(center, (width * 0.6, 2 * a - rim, rim * 0.7), tex, (k * 45, 0, 0), faces=["north", "south", "east", "west"])
            else:
                self.box(center, (rim * 0.7, 2 * a - rim, width * 0.6), tex, (0, 0, k * 45), faces=["north", "south", "east", "west"])

    def handwheel(self, cx, y, cz, a=0.55, tex="red"):
        self.ring(y - 0.06, y + 0.06, a, 0.12, tex, origin=(cx, cz), chunk=99)
        self.box((cx, y, cz), (2 * a - 0.12, 0.08, 0.1), tex)
        self.box((cx, y, cz), (0.1, 0.08, 2 * a - 0.12), tex)

    def deck(self, y, a, tex="grating", hole=0.0, gap=None):
        """A grated floor filling an octagon: a cross of grating with checker-plate gussets in the corners.

        hole leaves a square well in the middle; gap = (x0, x1) leaves a ladder way through the north arm."""
        s, t = side(a) / 2, 0.1
        yc = y - t / 2
        if hole:
            for sign in (1, -1):
                self.box((sign * (hole + a) / 2, yc, 0), (a - hole, t, 2 * s), tex)
            self.box((0, yc, (hole + a) / 2), (2 * s, t, a - hole), tex)
            if gap:
                self.box(((-s + gap[0]) / 2, yc, -(hole + a) / 2), (gap[0] + s, t, a - hole), tex)
                if s - gap[1] > 0.05:
                    self.box(((gap[1] + s) / 2, yc, -(hole + a) / 2), (s - gap[1], t, a - hole), tex)
            else:
                self.box((0, yc, -(hole + a) / 2), (2 * s, t, a - hole), tex)
        else:
            self.box((0, yc, s + (a - s) / 2), (2 * s, t, a - s), tex)
            self.box((0, yc, 0), (2 * a, t, 2 * s), tex)
            if gap:
                self.box(((-s + gap[0]) / 2, yc, -(s + a) / 2), (gap[0] + s, t, a - s), tex)
                if s - gap[1] > 0.05:
                    self.box(((gap[1] + s) / 2, yc, -(s + a) / 2), (s - gap[1], t, a - s), tex)
            else:
                self.box((0, yc, -(s + a) / 2), (2 * s, t, a - s), tex)
        inner = max(hole * math.sqrt(2), s * math.sqrt(2) * 0.9) if hole else s * 1.25
        for k in range(4):
            self.east(k * 90 + 45, ((inner + a) / 2, yc - 0.03, 0), (a - inner, t, 2 * s), "checker", faces=["up", "down", "west"])

    def ladder(self, x, z, y0, y1, standoff=0.0, ties=None):
        """A steel ladder up a cell column; its rungs run east-west. standoff ties it back to a wall that far
        away in z, with a pair of stays at each height in ties (every 2 m by default). The stays run from
        the rails, so they pass either side of whoever is climbing."""
        zr = z
        for dx in (-0.36, 0.36):
            self.member((x + dx, y0, zr), (x + dx, y1, zr), 0.07, 0.07, "steel", chunk=1.0)
        y = y0 + 0.2
        while y < y1:
            self.box((x, y, zr), (0.66, 0.05, 0.05), "steel", faces=["north", "south", "up", "down"])
            y += 0.4
        if standoff:
            if ties is None:
                ties = []
                y = y0 + 1
                while y < y1:
                    ties.append(y)
                    y += 2
            for y in ties:
                for dx in (-0.36, 0.36):
                    self.box((x + dx, y, z + standoff / 2), (0.08, 0.08, abs(standoff)), "steel", faces=["east", "west", "up", "down"])

    # ------------------------------------------------------------------ output

    def save(self, folder=None):
        data = {
            "editor": {"allAngles": True},
            "textureWidth": 16,
            "textureHeight": 16,
            "textures": {code: "froststory:" + TEXTURES[code][0] for code in sorted(self.used)},
            "textureSizes": {code: [TEXTURES[code][1], TEXTURES[code][2]] for code in sorted(self.used)},
            "elements": self.elements,
        }
        with open(os.path.join(folder or SHAPE_DIR, self.name + ".json"), "w") as f:
            json.dump(data, f, separators=(",", ":"))
            f.write("\n")
        return len(self.elements)


# =========================================================================== the design
#
# Radii are octagon apothems (centre to the middle of a flat side).

HULL = 4.5        # hull drum, 9 m across the flats
CROWN = 2.5       # upper drum
STACK = 1.5
VESSEL = 2.5      # core vessel
LINER = 3.5       # liner of the shaft mouth and throat, inside the block lining
WIDE = 5.5        # liner of the shaft below the throat, two blocks wider
THROAT = -3.0     # the throat runs from the deck down to here
COLUMN_X = 5.1    # frame columns stand just outside the hull
COLUMN_Z = 2.0
FRAME_TOP = 35.0
MODULE = 8        # shaft module height


# ----------------------------------------------------------------------------- shaft

CATWALK_IN = 3.5                    # inner edge of the shaft catwalks
LADDER_X = 1.0                      # every inside ladder is in this column of cells, east of the axis
THROAT_LADDER_Z = -LINER + 0.14     # on the throat wall, from the deck hatch
WALL_LADDER_Z = -WIDE + 0.14        # on the wall of the wide shaft, through a hatch in every catwalk


def catwalk(m, y, a0=CATWALK_IN, a1=WIDE - 0.15, landing=False):
    """A shaft platform: a grated catwalk round the wall, leaving the middle of the shaft open, with a guard
    rail along its inner edge. The wall ladder passes through a hatch on its north side.

    The first catwalk also has a landing: a bay standing out from its inner edge, where the ladder that
    hangs from the throat comes down."""
    mid, width = (a0 + a1) / 2, a1 - a0
    half = side(a1) / 2
    hatch = 4.5      # the hatch runs from here out to the wall
    faces = ["up", "down", "east", "west"]
    for k in range(8):
        yy = y - 0.05 - (0.012 if k % 2 else 0)      # neighbours overlap at the corners, so they sit a hair apart
        if k == 2:
            # north side (its tangential axis is world x): deck everywhere but the hatch cell at x 0.5..1.5
            m.east(90, ((a0 + hatch) / 2, yy, 0), (hatch - a0, 0.1, 2 * half), "grating", faces=faces)
            m.east(90, ((hatch + a1) / 2, yy, (0.5 - half) / 2), (a1 - hatch, 0.1, half + 0.5), "grating", faces=faces)
            m.east(90, ((hatch + a1) / 2, yy, (1.5 + half) / 2), (a1 - hatch, 0.1, half - 1.5), "grating", faces=faces)
            for x in (LADDER_X - 0.5, LADDER_X + 0.5):
                m.box((x, y + 0.07, -(hatch + a1) / 2), (0.08, 0.14, a1 - hatch), "band")       # coaming
            m.box((LADDER_X, y + 0.07, -hatch), (1.08, 0.14, 0.08), "band")
        else:
            m.east(k * 45, (mid, yy, 0), (width, 0.1, 2 * half), "grating", faces=faces)
        m.east(k * 45, (a0 + 0.06, y - 0.2, 0), (0.12, 0.3, side(a0)), "girder", faces=["east", "west", "down"])    # inner stringer
        wx, wz = yaw(k * 45 + 22.5, a1 / C22, 0)
        ix, iz = yaw(k * 45 + 22.5, (a0 + 0.2) / C22, 0)
        m.member((wx, y - 1.7, wz), (ix, y - 0.3, iz), 0.16, 0.16, "girder")                                         # bracket off the wall

    def post(x, z):
        m.member((x, y, z), (x, y + 1.1, z), 0.09, 0.09, "steel", caps=True, chunk=1.2)

    def rail(p0, p1):
        if math.hypot(p1[0] - p0[0], p1[1] - p0[1]) < 0.3:
            return
        for h in (0.55, 1.05):
            m.member((p0[0], y + h, p0[1]), (p1[0], y + h, p1[1]), 0.06, 0.06, "steel", chunk=6)

    corners = [yaw(k * 45 + 22.5, (a0 + 0.08) / C22, 0) for k in range(8)]
    for x, z in corners:
        post(x, z)
    bay = (LADDER_X - 1.5, LADDER_X + 0.5)      # the landing is two cells wide: under the ladder and beside it
    for k in range(8):
        p0, p1 = corners[k - 1], corners[k]
        if k == 2 and landing:
            # the inner rail opens where the landing joins: p0 is the east corner, p1 the west
            edge = (p0[1] + p1[1]) / 2
            rail((bay[0], edge), p1)
            post(bay[0], edge)
        else:
            rail(p0, p1)

    if landing:
        z0, z1 = -a0, -a0 + 1.0
        m.box(((bay[0] + bay[1]) / 2, y - 0.07, (z0 + z1) / 2), (bay[1] - bay[0], 0.1, 1.0), "grating")
        m.box(((bay[0] + bay[1]) / 2, y - 0.22, z1 - 0.06), (bay[1] - bay[0], 0.3, 0.12), "girder", faces=["north", "south", "down"])
        for x in bay:
            post(x, z1)
            m.member((x, y - 1.3, z0 - 0.1), (x, y - 0.3, z1 - 0.1), 0.14, 0.14, "girder")         # knee brace off the catwalk
        rail((bay[0], z1), (bay[1], z1))
        rail((bay[0], z0), (bay[0], z1))
        rail((bay[1], z0), (bay[1], z1))


def shaft(kind):
    """One 8 m lift of the shaft: liner, ring beams and ladder.

    'top' is the first lift: a 3 m throat the width of the shaft mouth, which carries the tower, opening
    out to the full bore beneath it. Its ladder runs down the throat wall from the deck hatch and then
    hangs free to the landing on the first catwalk. From there down the ladder is on the shaft wall.
    'platform' lifts carry a catwalk at their top; 'landing' is the first of them."""
    m = Model("shaft" + kind, (-2, -4, -1))
    y0, y1 = -MODULE, 0.0
    wide_top = THROAT if kind == "top" else y1

    m.ring(y0, wide_top, WIDE - 0.02, 0.12, "tubbing", outside=False, rims=False, split=2)
    m.flange(y0, y0 + 0.35, WIDE - 0.02, t=0.4)
    m.flange(y0 + 4, y0 + 4.2, WIDE - 0.02, t=0.25)

    if kind == "top":
        m.ring(THROAT, y1, LINER - 0.02, 0.12, "tubbing", outside=False, rims=False)
        m.flange(THROAT - 0.3, THROAT + 0.1, LINER + 0.35, t=0.5)                          # rim of the throat
        m.cone(THROAT - 0.6, THROAT, WIDE - 0.02, WIDE - 0.62, 0.14, "darkplate", courses=1)   # cove under the roof
        # brackets carrying the throat rim back to the wall, one over each corner
        for k in range(8):
            m.east(k * 45 + 22.5, ((LINER + WIDE) / 2 / C22, THROAT - 0.25, 0), ((WIDE - LINER) / C22 - 0.3, 0.3, 0.2), "girder")
        m.ladder(LADDER_X, THROAT_LADDER_Z, THROAT, y1)
        # below the roof it hangs free, stayed to the wall above head height, down to the landing
        m.ladder(LADDER_X, THROAT_LADDER_Z, y0, THROAT, standoff=-(WIDE - LINER), ties=[y0 + 3.2, y0 + 4.6])
        m.ladder(LADDER_X, WALL_LADDER_Z, y0, y0 + 1.1)                                     # the wall ladder stands a rung above the first catwalk
    else:
        m.ladder(LADDER_X, WALL_LADDER_Z, y0, y1)

    if kind in ("platform", "landing"):
        catwalk(m, y1, landing=kind == "landing")
    return m


def collar():
    """The shaft mouth: a steel curb and the grated deck the core will stand on."""
    m = Model("collar", (-2, -1, 0))
    m.flange(-0.5, 0.0, LINER + 0.25, t=0.5)
    m.deck(0.0, LINER - 0.02, gap=(0.5, 1.5))
    # The ladder way is a hatch one block square against the north wall, with a raised coaming round it.
    a = LINER - 0.02
    inner = side(a) / 2
    m.box((1.0, -0.05, -(2.5 + inner) / 2), (1.0, 0.1, 2.5 - inner), "grating")
    for x in (0.5, 1.5):
        m.box((x, 0.07, -3.0), (0.08, 0.14, 1.0), "band")
    m.box((1.0, 0.07, -2.5), (1.08, 0.14, 0.08), "band")
    return m


def headframe():
    """A temporary headframe over the dig: four raking legs to a crown, a winding sheave, a hoist rope down
    the shaft and a winch on the pad. It stands from the first piece of the shaft until the core replaces it."""
    m = Model("headframe", (-2, 2, 0))
    top = 10.5
    feet = [(4.3, 0.0), (0.0, 4.3), (-4.3, 0.0), (0.0, -4.3)]
    heads = [(1.0, 0.0), (0.0, 1.0), (-1.0, 0.0), (0.0, -1.0)]
    for (fx, fz), (hx, hz) in zip(feet, heads):
        m.member((fx, 0.0, fz), (hx, top, hz), 0.38, 0.38, "girder")
        m.box((fx, 0.12, fz), (0.9, 0.24, 0.9), "band")
    for y in (3.6, 7.2):
        f = y / top
        ring = [(fx + (hx - fx) * f, fz + (hz - fz) * f) for (fx, fz), (hx, hz) in zip(feet, heads)]
        for i in range(4):
            (ax, az), (bx, bz) = ring[i], ring[(i + 1) % 4]
            m.member((ax, y, az), (bx, y, bz), 0.2, 0.2, "girder")
    m.box((0, top + 0.1, 0), (2.6, 0.2, 2.6), "checker")                         # crown
    axle = top + 1.75
    for sz in (0.45, -0.45):
        m.member((0, top + 0.2, sz), (0, axle + 0.15, sz), 0.18, 0.18, "girder", caps=True)
    m.wheel((0, axle, 0), 1.25, 0.2, 0.22, "band", axis="z")                     # winding sheave
    m.member((0, axle, -0.62), (0, axle, 0.62), 0.12, 0.12, "steel", caps=True)
    # hoist rope: down the shaft on one side with a kibble on it, out to the winch on the other
    m.member((1.2, axle, 0), (1.2, -1.5, 0), 0.05, 0.05, "steel", chunk=1.0)
    m.box((1.2, 0.9, 0), (0.7, 0.8, 0.7), "band")
    m.member((-1.2, axle, 0), (-6.2, 0.75, 0), 0.05, 0.05, "steel", chunk=1.0)
    m.hprism((-6.3, 0.75, -0.6), (-6.3, 0.75, 0.6), 0.45, "band", caps=(True, True), captex="band")
    for sz in (0.75, -0.75):
        m.box((-6.3, 0.5, sz), (1.1, 1.0, 0.16), "girder")
    m.box((-6.3, 0.08, 0), (1.4, 0.16, 1.9), "checker")
    return m


# ----------------------------------------------------------------------------- riser and valves

RISER = 0.95      # main riser: just under 2 m across


def riser(base):
    m = Model("riserbase" if base else "riser", (0, -4, 0))
    y0, y1 = -MODULE, 0.0
    if base:
        # wellhead: a heavy footing with a 45 degree bell where the bore comes up
        m.vprism(0, 0, y0, y0 + 0.4, 2.3, "band", caps=(False, True), captex="checker")
        m.cone(y0 + 0.4, y0 + 1.6, 2.15, RISER, 0.14, "darkplate", courses=2, inside=False)
        for k in range(4):
            m.east(k * 90 + 45, (1.75, y0 + 1.0, 0), (1.2, 1.2, 0.14), "band", faces=["north", "south", "east", "up"])
        m.vprism(0, 0, y0 + 1.6, y1, RISER, "pipe")
        # finned heat exchanger where the riser leaves the bore
        y = y0 + 1.9
        while y < y0 + 5.0:
            m.vprism(0, 0, y, y + 0.08, RISER + 0.55, "band", caps=(True, True), captex="band")
            y += 0.34
    else:
        m.vprism(0, 0, y0, y1, RISER, "pipe")
    for y in (y0 + 1.9, y0 + 3.9, y0 + 5.9, y0 + 7.8):
        if base and y < y0 + 5.0:
            continue
        m.vprism(0, 0, y, y + 0.22, RISER + 0.25, "band", caps=(True, True), captex="band")
    return m


def valvestation():
    """Four gate valves on arms from the riser to relief ports in the liner, hung four blocks above a
    platform. The valves are inverted: bonnet and handwheel hang below the arm, just overhead, so they
    can be worked from the platform and walked under. Anchored in the south arm, at the arms' level."""
    m = Model("valvestation", (0, 0, 1))
    yc = -0.5
    m.vprism(0, 0, yc - 0.6, yc + 0.6, RISER + 0.4, "band", caps=(True, True), captex="band")
    valve = 4.4      # over the catwalk, so the handwheel can be reached
    for k in range(4):
        ox, oz = yaw(k * 90, 1, 0)

        def at(r, y=yc):
            return (ox * r, y, oz * r)

        m.hprism(at(RISER + 0.3), at(WIDE - 0.1), 0.3, "pipe", chunk=2.5)
        m.vprism(*at(valve)[::2], yc - 0.6, yc + 0.6, 0.55, "red", caps=(True, True), captex="band")   # valve body
        m.vprism(*at(valve)[::2], yc - 1.0, yc - 0.6, 0.3, "band", caps=(True, False), captex="band")   # bonnet, underneath
        m.vprism(*at(valve)[::2], yc - 1.38, yc - 1.0, 0.06, "steel")                                   # stem
        m.handwheel(at(valve)[0], yc - 1.33, at(valve)[2])                                                # 2.1 m above the platform
        for r in (1.9, 3.3):
            m.east(k * 90, (r, yc, 0), (0.14, 0.9, 0.9), "band")                                      # pipe flanges
        m.east(k * 90, (WIDE - 0.2, yc, 0), (0.2, 1.3, 1.0), "band")                                  # relief port, clear of the ladder
    return m


# ----------------------------------------------------------------------------- pumps

INJECTOR = (5.15, -1.2)


def injectors(top=False):
    """Four injection lines down one lift of the shaft. In the top lift they come out of the roof under the pad."""
    m = Model("injectorstop" if top else "injectors", (2, -4, 1))
    y1 = THROAT if top else 0.0
    for k in range(4):
        x, z = yaw(k * 90, *INJECTOR)
        m.vprism(x, z, -MODULE, y1, 0.25, "pipe")
        if top:
            m.vprism(x, z, y1 - 0.2, y1, 0.45, "band", caps=(True, False), captex="band")     # where it leaves the roof
        for y in (-MODULE + 0.1, -MODULE + 4.1):
            m.vprism(x, z, y, y + 0.16, 0.36, "band", caps=(True, True), captex="band")
            m.east(k * 90, (WIDE - 0.14, y + 0.08, INJECTOR[1]), (0.3, 0.1, 0.5), "steel")   # wall bracket
    return m


def pump():
    """A pump tower on the south-east diagonal; the other three are this one turned."""
    m = Model("pump", (5, 1, 5))
    c = (5.0, 5.0)
    m.vprism(*c, 0.0, 0.3, 1.6, "band", caps=(False, True), captex="checker")
    m.vprism(*c, 0.3, 1.9, 1.45, "green", caps=(False, True), captex="checker")
    m.vprism(*c, 1.9, 7.1, 0.75, "brass")
    for y in (2.0, 4.4, 6.8):
        m.vprism(*c, y, y + 0.22, 0.86, "band", caps=(True, True), captex="band")
    for sx in (1, -1):
        for sz in (1, -1):
            m.vprism(c[0] + sx * 1.05, c[1] + sz * 1.05, 1.9, 7.1, 0.1, "steel", chunk=1.0)
    m.vprism(*c, 7.1, 7.8, 1.45, "green", caps=(True, True), captex="checker")
    m.vprism(*c, 7.8, 9.5, 0.3, "pipe")
    m.vprism(*c, 9.5, 9.75, 0.45, "band", caps=(True, True), captex="black")

    # flywheel on the outboard (east) face, with its shaft and bearing
    wx = c[0] + 1.75
    m.wheel((wx, 3.3, c[1]), 1.5, 0.28, 0.3, "band")
    m.member((c[0] + 1.4, 3.3, c[1]), (wx + 0.3, 3.3, c[1]), 0.22, 0.22, "steel", caps=True)
    m.box((wx, 3.3, c[1]), (0.4, 0.5, 0.5), "band")
    m.box((c[0] + 1.55, 1.0, c[1]), (0.3, 2.0, 0.6), "green")

    # delivery line to the hull, on the diagonal
    d = 1.0 / math.sqrt(2)
    m.hprism((c[0] - 1.4 * d, 0.9, c[1] - 1.4 * d), (HULL * d + 0.1, 0.9, HULL * d + 0.1), 0.32, "pipe")
    m.east(-45, (HULL + 0.12, 0.9, 0), (0.2, 1.0, 1.0), "band")
    return m


# ----------------------------------------------------------------------------- core

def core(lower, lit):
    """Half of the core vessel: plated walls with a barred slot on each axis face showing the heart."""
    name = ("corelower" if lower else "coreupper") + ("lit" if lit else "")
    m = Model(name, (0, 2 if lower else 10, 0))
    y0, y1 = (0.0, 8.0) if lower else (8.0, 16.3)
    wall_top = y1 if lower else 15.0
    wall_bot = 1.0 if lower else y0
    coil = "coillit" if lit else "coil"

    if lower:
        # The plinth stays inside the vessel's footprint: the deck round it is the way to the shaft hatch.
        m.vprism(0, 0, 0.0, 1.0, VESSEL + 0.12, "band", caps=(False, True), captex="checker")

    # the heart: a ribbed column that glows once lit
    m.vprism(0, 0, wall_bot, wall_top, 1.2, coil, glow=255 if lit else 0, chunk=2.0)

    slot = 0.5
    s = side(VESSEL) / 2
    for k in range(8):
        if k % 2:
            m.ring(wall_bot, wall_top, VESSEL, 0.3, "darkplate", sides=[k], rims=False, chunk=2.0)
            m.east(k * 45, (VESSEL + 0.06, (wall_bot + wall_top) / 2, 0), (0.14, wall_top - wall_bot, 0.3), "band", faces=["east", "north", "south"])
        else:
            m.wall(k, VESSEL, 0.3, wall_bot, wall_top, -s, -slot, "darkplate")
            m.wall(k, VESSEL, 0.3, wall_bot, wall_top, slot, s, "darkplate")
            y = wall_bot + 0.5
            while y < wall_top - 0.2:
                m.east(k * 45, (VESSEL - 0.1, y, 0), (0.16, 0.14, 2 * slot), "band", faces=["east", "west", "up", "down"])
                y += 1.0
    for y in ((4.0, 7.7) if lower else (11.5,)):
        m.flange(y, y + 0.3, VESSEL + 0.12, t=0.4)

    # standpipes up the diagonals
    # They leave the vessel overhead, so the walkway round the core stays clear.
    for k in range(4):
        x, z = yaw(k * 90 + 45, VESSEL + 0.5, 0)
        if lower:
            elbow = 2.75
            vx, vz = yaw(k * 90 + 45, VESSEL - 0.1, 0)
            m.hprism((vx, elbow, vz), (x, elbow, z), 0.28, "pipe")
            m.vprism(x, z, elbow - 0.3, elbow + 0.3, 0.36, "band", caps=(True, True), captex="band")
            m.vprism(x, z, elbow, y1, 0.28, "pipe")
        else:
            m.vprism(x, z, y0, y1, 0.28, "pipe")
        for y in ((4.6, 6.8) if lower else (10.5, 14.5)):
            m.east(k * 90 + 45, (VESSEL + 0.25, y, 0), (0.5, 0.12, 0.7), "steel")

    if not lower:
        m.flange(14.7, 15.0, VESSEL + 0.12, t=0.4)
        m.cone(15.0, 16.3, VESSEL, 1.2, 0.14, "darkplate", inside=False)
    return m


def manifold():
    """Distribution header over the core: a ring main fed by the standpipes, with spokes to the exhaust."""
    m = Model("manifold", (0, 18, 0))
    m.vprism(0, 0, 16.3, 22.0, 0.6, "pipe")
    m.vprism(0, 0, 16.3, 16.6, 1.25, "band", caps=(True, True), captex="band")
    yr, r = 18.0, VESSEL + 0.5
    for k in range(8):
        a0, a1 = k * 45 - 22.5, k * 45 + 22.5
        x0, z0 = yaw(a0, r / C22, 0)
        x1, z1 = yaw(a1, r / C22, 0)
        m.hprism((x0, yr, z0), (x1, yr, z1), 0.3, "pipe")
    for k in range(4):
        x, z = yaw(k * 90, 1, 0)
        m.hprism((x * 0.5, yr, z * 0.5), (x * r, yr, z * r), 0.24, "pipe")
        m.east(k * 90, (1.6, yr, 0), (0.14, 0.75, 0.75), "band")
        dx, dz = yaw(k * 90 + 45, r, 0)
        m.vprism(dx, dz, 16.3, yr, 0.28, "pipe")
        m.vprism(dx, dz, yr - 0.4, yr + 0.4, 0.42, "band", caps=(True, True), captex="band")
    m.vprism(0, 0, yr - 0.5, yr + 0.5, 0.85, "band", caps=(True, True), captex="band")
    m.vprism(0, 0, 21.6, 21.85, 0.8, "band", caps=(True, True), captex="band")
    return m


def exhaust():
    m = Model("exhaust", (0, 24, 0))
    m.vprism(0, 0, 22.0, 34.0, 0.6, "pipe")
    for y in (25.6, 29.6, 33.4):
        m.vprism(0, 0, y, y + 0.25, 0.8, "band", caps=(True, True), captex="band")
    return m


# ----------------------------------------------------------------------------- support frames

def frame(level, lapped=False):
    """One side (the east one) of a support frame, in three lifts. Each frame is two of these, face to face.

    The second frame's head beams are 'lapped': they sit on top of the first frame's where they cross."""
    name = ("framebase", "framemid", "frametop")[level] + ("b" if lapped and level == 2 else "")
    m = Model(name, (5, (2, 14, 26)[level], 0))
    y0, y1 = (0.0, 12.0, 24.0)[level], (12.0, 24.0, FRAME_TOP)[level]
    x = COLUMN_X

    for sz in (1, -1):
        z = sz * COLUMN_Z
        m.member((x, y0, z), (x, y1, z), 0.6, 0.6, "girder")
        if level == 0:
            m.box((x, 0.15, z), (1.1, 0.3, 1.1), "band")                      # column shoe
            # raking buttress with struts back to the column
            foot, head = (8.0, 0.0, z), (x + 0.2, 11.6, z)
            m.member(foot, head, 0.45, 0.45, "girder")
            m.box((8.0, 0.15, z), (1.3, 0.3, 1.0), "band")
            for y in (3.0, 6.0, 9.0):
                rx = foot[0] + (head[0] - foot[0]) * y / 11.6
                m.member((x, y, z), (rx, y, z), 0.25, 0.25, "girder")
                if y > 3.0:
                    prev = foot[0] + (head[0] - foot[0]) * (y - 3) / 11.6
                    m.member((x, y - 3, z), (rx, y, z), 0.18, 0.18, "girder")
            m.member((x, 0.3, z), (foot[0] + (head[0] - foot[0]) * 3 / 11.6, 3.0, z), 0.18, 0.18, "girder")

    # ties and cross-bracing between the two columns
    first = 4 if level == 0 else y0 + 4
    y = first
    while y <= min(y1, 32):
        m.member((x, y, -COLUMN_Z), (x, y, COLUMN_Z), 0.4, 0.4, "girder")
        for sz in (1, -1):
            m.box((x, y, sz * (COLUMN_Z - 0.15)), (0.7, 0.9, 0.9), "band", rot=(45, 0, 0))   # gusset, set on the diagonal
        top = y + 4
        if y == 12:
            # this bay stays open for the large steam port; knee braces stiffen its corners instead
            for sz in (1, -1):
                for yy, dy in ((y + 0.2, 1.2), (top - 0.2, -1.2)):
                    m.member((x, yy + dy, sz * (COLUMN_Z - 0.3)), (x, yy, sz * (COLUMN_Z - 1.5)), 0.2, 0.2, "girder")
        elif top <= 32:
            m.member((x, y + 0.2, -COLUMN_Z + 0.3), (x, top - 0.2, COLUMN_Z - 0.3), 0.2, 0.2, "girder")
            m.member((x, y + 0.2, COLUMN_Z - 0.3), (x, top - 0.2, -COLUMN_Z + 0.3), 0.2, 0.2, "girder")
        y += 4
    if level == 0:
        # portal: the first bay stays open for the doorway, stiffened by knee braces
        for sz in (1, -1):
            m.member((x, 2.6, sz * (COLUMN_Z - 0.3)), (x, 4.0, sz * (COLUMN_Z - 1.7)), 0.22, 0.22, "girder")
        # stay bolted to the hull at each tie
        for yy in (4.0, 8.0):
            m.member((x - 0.3, yy, 0), (HULL, yy, 0), 0.3, 0.3, "girder")
    else:
        for yy in range(int(y0) + 4, min(int(y1), 28) + 1, 4):
            m.member((x - 0.3, yy, 0), (HULL, yy, 0), 0.3, 0.3, "girder")

    if level == 2:
        beam_y = FRAME_TOP + (0.9 if lapped else 0.3)
        for sz in (1, -1):
            z = sz * COLUMN_Z
            if lapped:
                m.member((x, y1, z), (x, beam_y, z), 0.6, 0.6, "girder")
            m.member((x + 0.5, beam_y, z), (0.0, beam_y, z), 0.6, 0.5, "girder")                 # half the head beam
            m.member((x - 0.3, beam_y - 2.6, z), (x - 2.6, beam_y - 0.3, z), 0.3, 0.3, "girder")  # 45 degree knee brace
            m.box((x, beam_y, z), (1.0, 0.9, 0.7), "band")                                        # joint cap
        m.member((x, beam_y, -COLUMN_Z), (x, beam_y, COLUMN_Z), 0.5, 0.5, "girder")
    return m


# ----------------------------------------------------------------------------- hull

def louvre(m, k, a, y0, y1, width=1.7, lit=False):
    """A louvred opening on side k: slats tilted to shed weather, in a frame. Lit, the core shows through."""
    if lit:
        m.east(k * 45, (a - 0.55, (y0 + y1) / 2, 0), (0.06, y1 - y0, width), "coillit", faces=["east"], glow=255)
    y = y0 + 0.25
    while y < y1 - 0.1:
        m.east(k * 45, (a - 0.12, y, 0), (0.46, 0.06, width), "band", tilt=-38, faces=["up", "down", "east"])
        y += 0.42
    for sz in (1, -1):
        m.east(k * 45, (a - 0.05, (y0 + y1) / 2, sz * (width / 2 + 0.1)), (0.4, y1 - y0 + 0.4, 0.2), "band")
    for yy in (y0 - 0.1, y1 + 0.1):
        m.east(k * 45, (a - 0.05, yy, 0), (0.4, 0.2, width + 0.4), "band")


def hull_ribs(m, a, y0, y1):
    """Upright stiffeners on the diagonal faces."""
    for k in (1, 3, 5, 7):
        for z in (-0.95, 0.95):
            m.east(k * 45, (a + 0.07, (y0 + y1) / 2, z), (0.16, y1 - y0, 0.22), "band", faces=["east", "north", "south"])


def gallery(m, y, a0=5.5, a1=6.7):
    """An outside service gallery: an octagonal grated walkway with a guard rail, braced off the hull.

    A ladder way is left open in the south side, where the outside ladder comes up."""
    mid, width = (a0 + a1) / 2, a1 - a0
    half = side(a1) / 2
    for k in range(8):
        yy = y - 0.05 - (0.012 if k % 2 else 0)      # neighbours overlap at the corners, so they sit a hair apart
        spans = [(-half, -1.5), (-0.5, half)] if k == 6 else [(-half, half)]
        for z0, z1 in spans:
            m.east(k * 45, (mid, yy, (z0 + z1) / 2), (width, 0.1, z1 - z0), "grating", faces=["up", "down", "east", "west"])
        m.east(k * 45, (a0 + 0.06, y - 0.2, 0), (0.12, 0.3, side(a0)), "girder", faces=["east", "west", "down"])   # inner stringer
        m.east(k * 45, (a1 - 0.06, y - 0.2, 0), (0.12, 0.3, side(a1)), "girder", faces=["east", "west", "down"])   # outer stringer

    corners = []
    for k in range(8):
        x, z = yaw(k * 45 + 22.5, a1 / C22 - 0.08, 0)
        corners.append((x, z))
        m.member((x, y, z), (x, y + 1.1, z), 0.09, 0.09, "steel", caps=True, chunk=1.2)
        hx, hz = yaw(k * 45 + 22.5, HULL / C22 + 0.05, 0)
        bx, bz = yaw(k * 45 + 22.5, a1 / C22 - 0.5, 0)
        m.member((hx, y - 1.9, hz), (bx, y - 0.3, bz), 0.16, 0.16, "girder")                                        # brace
    for k in range(8):
        (x0, z0), (x1, z1) = corners[k - 1], corners[k]
        for h in (0.55, 1.05):
            m.member((x0, y + h, z0), (x1, y + h, z1), 0.06, 0.06, "steel", chunk=6)
        m.member(((x0 + x1) / 2, y, (z0 + z1) / 2), ((x0 + x1) / 2, y + 1.05, (z0 + z1) / 2), 0.07, 0.07, "steel", chunk=1.2)


RISER_OUT = 0.42      # heat-exchange risers stand this far off the diagonal faces
RISER_Z = 0.62


def hull(level, lit=False):
    """The hull drum in three lifts: 'a' has the doorways, 'b' and 'c' have louvres over the axis faces.

    The two lifts round the core have a lit model, with the heart's glow behind their louvres.
    Outside run the heat-exchange risers, two service galleries and the ladder that reaches them."""
    name = "hull" + "abc"[level] + ("lit" if lit else "")
    m = Model(name, (1, (2, 12, 22)[level], 0))
    y0, y1 = (0.0, 10.0, 19.0)[level], (10.0, 19.0, 28.0)[level]
    s = side(HULL) / 2
    t = 0.25

    if level == 0:
        m.flange(0.0, 0.5, HULL + 0.3, t=0.55)
    m.flange(y1 - 0.45, y1, HULL + 0.18, t=0.45)
    m.flange((y0 + y1) / 2 - 0.1, (y0 + y1) / 2 + 0.1, HULL + 0.08, t=0.2)        # stiffening ring

    for k in range(8):
        if k % 2:
            m.ring(y0, y1, HULL, t, "plating", sides=[k], rims=False)
            continue
        if level == 0:
            door, head, wy0, wy1 = 0.7, 2.4, 4.4, 8.8
            m.wall(k, HULL, t, y0, y1, -s, -0.85, "plating")
            m.wall(k, HULL, t, y0, y1, 0.85, s, "plating")
            m.wall(k, HULL, t, y0, wy0, -0.85, -door, "plating")
            m.wall(k, HULL, t, y0, wy0, door, 0.85, "plating")
            m.wall(k, HULL, t, head, wy0, -door, door, "plating")
            m.wall(k, HULL, t, wy1, y1, -0.85, 0.85, "plating")
            for sz in (1, -1):
                m.east(k * 45, (HULL - 0.05, head / 2, sz * (door + 0.1)), (0.45, head, 0.2), "band")
            m.east(k * 45, (HULL - 0.05, head + 0.12, 0), (0.45, 0.24, 2 * door + 0.4), "band")
            # a hood over the door, set at 45 degrees
            m.east(k * 45, (HULL + 0.28, head + 0.75, 0), (0.7, 0.08, 1.5), "band", tilt=-45)
            louvre(m, k, HULL, wy0, wy1, lit=lit)
        else:
            wy0, wy1 = y0 + 1.4, y1 - 1.6
            m.wall(k, HULL, t, y0, y1, -s, -0.85, "plating")
            m.wall(k, HULL, t, y0, y1, 0.85, s, "plating")
            m.wall(k, HULL, t, y0, wy0, -0.85, 0.85, "plating")
            m.wall(k, HULL, t, wy1, y1, -0.85, 0.85, "plating")
            louvre(m, k, HULL, wy0, wy1, lit=lit)
    hull_ribs(m, HULL, y0 + (0.5 if level == 0 else 0), y1 - 0.45)

    # heat-exchange risers up the diagonal faces
    ra, rb = max(y0, 2.6), min(y1, 27.4)
    for k in (1, 3, 5, 7):
        for tz in (-RISER_Z, RISER_Z):
            x, z = yaw(k * 45, HULL + RISER_OUT, tz)
            m.vprism(x, z, ra, rb, 0.2, "pipe")
            if level == 2:
                ix, iz = yaw(k * 45, HULL - 0.1, tz)
                m.hprism((x, rb, z), (ix, rb, iz), 0.2, "pipe")                    # back into the hull under the shoulder
                m.vprism(x, z, rb - 0.2, rb + 0.24, 0.27, "band", caps=(True, True), captex="band")
        y = math.ceil(ra) + 0.5
        while y < rb:
            m.east(k * 45, (HULL + 0.33, y, 0), (0.42, 0.1, 1.8), "steel")         # pipe bracket
            y += 3
        if level == 0:
            # bottom header joining the pair, with its stop valve
            (xa, za), (xb, zb) = yaw(k * 45, HULL + RISER_OUT, -RISER_Z), yaw(k * 45, HULL + RISER_OUT, RISER_Z)
            m.hprism((xa, ra, za), (xb, ra, zb), 0.2, "pipe", caps=(True, True), captex="band")
            cx, cz = yaw(k * 45, HULL + RISER_OUT, 0)
            ix, iz = yaw(k * 45, HULL - 0.1, 0)
            m.hprism((cx, ra, cz), (ix, ra, iz), 0.2, "pipe")
            m.vprism(cx, cz, ra - 0.3, ra + 0.3, 0.3, "red", caps=(True, True), captex="band")
            m.vprism(cx, cz, ra + 0.3, ra + 0.75, 0.05, "steel")
            m.handwheel(cx, ra + 0.75, cz, a=0.32)
        if level == 1:
            m.east(k * 45, (HULL + 0.05, 14.4, 0), (0.12, 1.2, 0.86), "band")      # inspection hatch
            for hy in (13.95, 14.85):
                m.east(k * 45, (HULL + 0.13, hy, 0), (0.08, 0.1, 1.0), "steel")

    # service ladder up the inside, standing off the north wall
    ladder_top = (10.0, 19.0, 21.0)[level]
    stays = []
    y = y0 + 2.6          # clear of anyone walking round the core or along the gallery
    while y < ladder_top:
        stays.append(y)
        y += 2.4
    m.ladder(LADDER_X, -3.44, y0, ladder_top, standoff=-0.75, ties=stays)
    # and one up the outside of the south frame, to the galleries
    m.ladder(1.0, 5.6, y0, min(y1, 25.1), standoff=-0.2)

    if level == 1:
        gallery(m, 12.0)
    if level == 2:
        gallery(m, 24.0)
        m.deck(21.0, HULL - t - 0.02, hole=1.5, gap=(0.5, 1.5))    # gallery over the manifold
        for sx, sz in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
            m.member((sx * 1.5, 21.0, sz * 1.5), (sx * 1.5, 22.05, sz * 1.5), 0.08, 0.08, "steel", caps=True)
        for sgn in (1, -1):
            m.member((-1.5, 22.05, sgn * 1.5), (1.5, 22.05, sgn * 1.5), 0.06, 0.06, "steel")
            m.member((sgn * 1.5, 22.05, -1.5), (sgn * 1.5, 22.05, 1.5), 0.06, 0.06, "steel")
    return m


def shoulder():
    """The 45 degree shoulder with its relief valves, and the crown drum above it."""
    m = Model("shoulder", (1, 30, 0))
    m.flange(28.0, 28.5, HULL + 0.18, t=0.45)
    m.cone(28.5, 30.5, HULL, CROWN, 0.22, "plating")
    m.flange(30.4, 30.8, CROWN + 0.15, t=0.4)
    m.ring(30.8, 33.5, CROWN, 0.22, "plating", rims=False)
    for k in range(8):
        m.east(k * 45, (CROWN + 0.02, 32.15, 0), (0.12, 1.5, 0.9), "black", faces=["east"])     # vent slots
        for z in (-0.3, 0.0, 0.3):
            m.east(k * 45, (CROWN + 0.08, 32.15, z), (0.1, 1.5, 0.08), "band", faces=["east", "north", "south"])
    m.flange(33.5, 34.0, CROWN + 0.2, t=0.45)

    # weighted-lever relief valves standing on the diagonal faces of the shoulder
    for k in (1, 3, 5, 7):
        x, z = yaw(k * 45, 3.55, 0)
        m.vprism(x, z, 29.3, 30.3, 0.16, "pipe")
        m.vprism(x, z, 30.3, 30.85, 0.3, "red", caps=(True, True), captex="band")
        m.vprism(x, z, 30.85, 31.5, 0.12, "pipe", caps=(False, True))
        ox, oz = yaw(k * 45, 4.75, 0)
        m.member((x, 30.95, z), (ox, 30.95, oz), 0.07, 0.07, "steel", caps=True)
        m.box((ox, 30.72, oz), (0.3, 0.4, 0.3), "band", rot=(0, k * 45, 0))
    return m


def stack():
    """The stack: a tapered base, the flue, a flared lip, and a weather cowl carried on struts above it."""
    m = Model("stack", (1, 36, 0))
    m.cone(34.0, 35.0, CROWN, STACK, 0.2, "plating", courses=2)
    m.ring(35.0, 38.2, STACK, 0.2, "plating", rims=False, intex="black")
    for y in (35.0, 36.6):
        m.flange(y, y + 0.3, STACK + 0.12, t=0.3)
    m.cone(38.2, 38.8, STACK, STACK + 0.5, 0.2, "band", courses=1, ribs=False)   # flared lip
    m.ring(38.8, 39.05, STACK + 0.5, 0.25, "band", intex="black", chunk=99)
    for k in range(8):
        x, z = yaw(k * 45 + 22.5, (STACK + 0.4) / C22, 0)
        m.member((x, 39.05, z), (x, 39.5, z), 0.1, 0.1, "steel", chunk=1.0)
    m.ring(39.4, 39.55, 2.35, 0.3, "band", chunk=99)
    m.cone(39.5, 40.0, 2.35, 0.5, 0.1, "plating", courses=2)
    m.vprism(0, 0, 39.9, 40.0, 0.55, "band", caps=(True, True), captex="band")
    return m


def ringmain():
    """Feed-water ring main round the foot of the tower. It threads the buttresses and the pump bases on
    the diagonals, and ducks under the pad either side of each doorway approach."""
    m = Model("ringmain", (6, 1, 0))
    r, y, a = 6.2, 0.6, 0.3
    corner = side(r) / 2
    for q in range(4):
        def pt(x, z):
            rx, rz = yaw(-q * 90, x, z)
            return (rx, y, rz)

        run = [pt(r, 1.9), pt(r, corner), pt(corner, r), pt(1.9, r)]
        for p0, p1 in zip(run, run[1:]):
            m.hprism(p0, p1, a, "pipe")
        for joint in run[1:3]:
            m.vprism(joint[0], joint[2], y - 0.42, y + 0.42, 0.42, "band", caps=(True, True), captex="band")   # corner fitting
        for end in (run[0], run[3]):
            m.vprism(end[0], end[2], 0.0, y + 0.3, a, "pipe", caps=(False, True), captex="band")              # drop into the pad
            m.vprism(end[0], end[2], 0.0, 0.14, 0.5, "band", caps=(False, True), captex="band")
        # a stop valve on each straight run, and saddles under the long diagonal
        for p0, p1 in ((run[0], run[1]), (run[3], run[2])):
            vx, vz = (p0[0] + p1[0]) / 2, (p0[2] + p1[2]) / 2
            m.vprism(vx, vz, y - 0.4, y + 0.4, 0.4, "red", caps=(True, True), captex="band")
            m.vprism(vx, vz, y + 0.4, y + 0.85, 0.05, "steel")
            m.handwheel(vx, y + 0.85, vz, a=0.34)
        for f in (0.22, 0.78):
            sx, sz = run[1][0] + (run[2][0] - run[1][0]) * f, run[1][2] + (run[2][2] - run[1][2]) * f
            m.box((sx, 0.16, sz), (0.8, 0.32, 0.3), "band", rot=(0, 45 - q * 90, 0))
    return m


# ----------------------------------------------------------------------------- insulating jacket

JACKET = 8.2          # outer apothem: a block and a half clear of the galleries
JACKET_Y0, JACKET_Y1 = 20.0, 33.0


def jacket(kind):
    """One panel of the insulating jacket round the upper tower, with the beams and struts that carry it.

    The jacket is an octagon of eight such panels standing clear of the galleries, open at the foot and
    round the stack. 'axis' is the east panel, hung off the frame columns, with a louvred opening;
    'diag' is the north-east panel, hung off the hull between the risers. The rest are these two turned."""
    axis = kind == "axis"
    k = 0 if axis else 1
    m = Model("jacket" + kind, (8, 22, 0) if axis else (6, 22, -6))
    t = 0.25
    half = side(JACKET) / 2
    win = 1.2           # half-width of the louvred opening on an axis panel
    wy0, wy1 = 22.6, 27.4

    if axis:
        m.wall(k, JACKET, t, JACKET_Y0, JACKET_Y1, -half, -win, "plating")
        m.wall(k, JACKET, t, JACKET_Y0, JACKET_Y1, win, half, "plating")
        m.wall(k, JACKET, t, JACKET_Y0, wy0, -win, win, "plating")
        m.wall(k, JACKET, t, wy1, JACKET_Y1, -win, win, "plating")
        louvre(m, k, JACKET, wy0, wy1, width=2 * win)
    else:
        m.wall(k, JACKET, t, JACKET_Y0, JACKET_Y1, -half, 0, "plating")
        m.wall(k, JACKET, t, JACKET_Y0, JACKET_Y1, 0, half, "plating")
    # upright ribs: one over the seam in the middle of a plain panel, one either side of it
    for z in (-half * 0.5, 0.0, half * 0.5):
        if axis and z == 0.0:
            continue
        m.east(k * 45, (JACKET + 0.07, (JACKET_Y0 + JACKET_Y1) / 2, z), (0.16, JACKET_Y1 - JACKET_Y0, 0.24), "band",
               faces=["east", "north", "south"])
    # a heavier rib closes the joint with the next panel round
    m.east(k * 45 + 22.5, (JACKET / C22 + 0.02, (JACKET_Y0 + JACKET_Y1) / 2, 0), (0.22, JACKET_Y1 - JACKET_Y0, 0.5), "band",
           faces=["east", "north", "south", "west"])

    for y in (JACKET_Y0, 24.2, 28.5, JACKET_Y1 - 0.45):
        m.ring(y, y + 0.45, JACKET + 0.16, 0.45, "band", sides=[k], chunk=99)
    m.cone(JACKET_Y0 - 0.8, JACKET_Y0, JACKET - 0.8, JACKET, 0.2, "plating", courses=1, sides=[k])       # drip skirt
    m.cone(JACKET_Y1, JACKET_Y1 + 2.0, JACKET, JACKET - 2.0, 0.22, "plating", courses=2, sides=[k])      # 45 degree hood
    m.ring(JACKET_Y1 + 1.9, JACKET_Y1 + 2.25, JACKET - 1.95, 0.4, "band", sides=[k], chunk=99)

    inner = JACKET - t
    if axis:
        # off the frame columns: level beams, and raking struts under the foot
        col = COLUMN_X + 0.3
        for sz in (1, -1):
            z = sz * COLUMN_Z
            for y in (21.0, 26.6, 31.8):
                m.member((col, y, z), (inner, y, z), 0.3, 0.3, "girder")
                m.box((inner - 0.1, y, z), (0.2, 0.9, 0.9), "band")                                  # bearing plate
            m.member((col, JACKET_Y0 - 2.4, z), (inner - 0.1, JACKET_Y0 + 0.1, z), 0.26, 0.26, "girder")
    else:
        # off the diagonal hull face, between the risers
        hx, hz = yaw(45, HULL, 0)
        for y in (21.0, 26.6):
            jx, jz = yaw(45, inner, 0)
            m.member((hx, y, hz), (jx, y, jz), 0.3, 0.3, "girder")
            m.east(45, (inner - 0.1, y, 0), (0.2, 0.9, 0.9), "band")
        jx, jz = yaw(45, inner - 0.1, 0)
        m.member((hx, JACKET_Y0 - 3.3, hz), (jx, JACKET_Y0 + 0.1, jz), 0.26, 0.26, "girder")
    return m


# ----------------------------------------------------------------------------- steam port

def steamport():
    """A steam offtake standing on the pad: a riser out of the foundation, a stop valve, and a flanged
    outlet on its east side that a steam pipe butts up against. A one-block model, turned to face out."""
    m = Model("steamport", (0, 1, 0))
    m.vprism(0, 0, 0.0, 0.12, 0.46, "band", caps=(False, True), captex="band")
    m.vprism(0, 0, 0.12, 0.5, 0.3, "pipe")
    m.vprism(0, 0, 0.16, 0.84, 0.36, "red", caps=(True, True), captex="band")      # valve body
    m.hprism((0.3, 0.5, 0), (0.44, 0.5, 0), 0.3, "pipe")
    m.box((0.44, 0.5, 0), (0.12, 0.86, 0.86), "band")                              # outlet flange
    m.vprism(0, 0, 0.84, 0.96, 0.05, "steel")
    m.handwheel(0, 0.95, 0, a=0.3)
    return m


# ----------------------------------------------------------------------------- steam mains

MAIN_BORE = 0.82
MAIN_FLANGE = 0.98
MAIN_PORT_Y = 15.0      # centre line of the large ports, 3 m above the first gallery's deck


def steammain(axis):
    """One block of a 2 x 2 steam main. Its cell is the minimum corner of the four it fills."""
    m = Model("steammain-" + axis, (0, 1, 0))
    if axis == "ud":
        m.vprism(0.5, 0.5, 0.0, 1.0, MAIN_BORE, "pipe")
        for y0, y1 in ((0.0, 0.12), (0.88, 1.0)):
            m.vprism(0.5, 0.5, y0, y1, MAIN_FLANGE, "band", caps=(True, True), captex="band")
    else:
        ends = ((-0.5, 1.0, 0.5), (0.5, 1.0, 0.5)) if axis == "we" else ((0.5, 1.0, -0.5), (0.5, 1.0, 0.5))
        m.hprism(ends[0], ends[1], MAIN_BORE, "pipe")
        for f0, f1 in ((0.0, 0.12), (0.88, 1.0)):
            p0 = tuple(a + (b - a) * f0 for a, b in zip(*ends))
            p1 = tuple(a + (b - a) * f1 for a, b in zip(*ends))
            m.hprism(p0, p1, MAIN_FLANGE, "band", caps=(True, True), captex="band")
    return m


def mainjunction():
    """A 2 x 2 x 2 fitting where steam mains meet from any side."""
    m = Model("mainjunction", (0, 1, 0))
    c = (0.5, 1.0, 0.5)
    m.vprism(c[0], c[2], 0.0, 2.0, MAIN_BORE, "pipe")
    m.hprism((-0.5, c[1], c[2]), (1.5, c[1], c[2]), MAIN_BORE, "pipe")
    m.hprism((c[0], c[1], -0.5), (c[0], c[1], 1.5), MAIN_BORE, "pipe")
    m.box(c, (1.5, 1.5, 1.5), "band")                                              # the body where the bores cross
    for y0, y1 in ((0.0, 0.12), (1.88, 2.0)):
        m.vprism(c[0], c[2], y0, y1, MAIN_FLANGE, "band", caps=(True, True), captex="band")
    for a, b in ((-0.5, -0.38), (1.38, 1.5)):
        m.hprism((a, c[1], c[2]), (b, c[1], c[2]), MAIN_FLANGE, "band", caps=(True, True), captex="band")
        m.hprism((c[0], c[1], a), (c[0], c[1], b), MAIN_FLANGE, "band", caps=(True, True), captex="band")
    return m


def mainpost():
    """One metre of the lattice post under a main stand: four legs, a tie round the top and a brace on
    each face. It fills the same 2 x 2 footprint as the fitting it carries, and stacks."""
    m = Model("mainpost", (0, 1, 0))
    c, r = 0.5, 0.72
    corners = [(c + r, c + r), (c - r, c + r), (c - r, c - r), (c + r, c - r)]
    for x, z in corners:
        m.member((x, 0.0, z), (x, 1.0, z), 0.18, 0.18, "girder", chunk=1.0)
    for i in range(4):
        (x0, z0), (x1, z1) = corners[i], corners[(i + 1) % 4]
        m.member((x0, 0.93, z0), (x1, 0.93, z1), 0.12, 0.12, "girder", chunk=2.0)      # tie
        m.member((x0, 0.05, z0), (x1, 0.9, z1), 0.09, 0.09, "steel", chunk=2.0)        # brace
    return m


def mainstand():
    """The stand as it is carried: a short post with a fitting on it, small enough to sit in one block."""
    m = Model("mainstand", (0, 1, 0))
    for sx in (0.26, -0.26):
        for sz in (0.26, -0.26):
            m.member((sx, 0.0, sz), (sx, 0.56, sz), 0.07, 0.07, "girder", chunk=1.0)
    m.box((0, 0.05, 0), (0.7, 0.1, 0.7), "band")
    m.box((0, 0.75, 0), (0.42, 0.42, 0.42), "band")
    m.vprism(0, 0, 0.5, 1.0, 0.2, "pipe")
    m.hprism((-0.45, 0.75, 0), (0.45, 0.75, 0), 0.2, "pipe")
    m.hprism((0, 0.75, -0.45), (0, 0.75, 0.45), 0.2, "pipe")
    return m


MAIN_PORT_ANCHORS = {"east": (7, 15, 0), "south": (-1, 15, 7), "west": (-7, 15, -1), "north": (0, 15, -7)}


def mainport(side):
    """A large steam port: a 2 m nozzle from the hull, out through the frame bay over the first gallery,
    with a wafer valve and a flanged mouth for a steam main. Drawn for the east side and turned."""
    turn = ("east", "south", "west", "north").index(side)
    m = Model("mainport-" + side, MAIN_PORT_ANCHORS[side], turn=turn)
    y, z = MAIN_PORT_Y, 0.5
    m.east(0, (HULL + 0.07, y, z), (0.16, 2.4, 2.4), "band")                       # saddle plate on the hull
    m.hprism((HULL, y, z), (7.5, y, z), MAIN_BORE - 0.02, "pipe")
    m.hprism((HULL + 0.14, y, z), (HULL + 0.4, y, z), MAIN_FLANGE - 0.03, "band", caps=(True, True), captex="band")
    m.hprism((7.3, y, z), (7.5, y, z), MAIN_FLANGE, "band", caps=(True, True), captex="band")     # the mouth
    # A wafer valve: a slim bolted ring round the bore, just outside the frame. Its spindle drops to a
    # handwheel at chest height on the gallery (deck at 12 m), where an operator can reach it.
    vx = 5.85
    m.hprism((vx - 0.12, y, z), (vx + 0.12, y, z), MAIN_FLANGE, "band", caps=(True, True), captex="band")
    m.hprism((6.6, y, z), (6.72, y, z), MAIN_BORE + 0.06, "band", caps=(True, True), captex="band")     # stiffening band
    low = y - MAIN_FLANGE
    m.box((vx, low - 0.14, z), (0.34, 0.28, 0.34), "band")                                              # gearbox
    m.vprism(vx, z, 12.0, low - 0.28, 0.04, "steel")                                                    # spindle
    m.vprism(vx, z, 12.0, 12.12, 0.16, "band", caps=(False, True), captex="band")                       # foot
    m.handwheel(vx, 13.15, z, a=0.24)
    m.box((vx, low - 0.14, z + 0.3), (0.08, 0.08, 0.3), "red")                                          # position indicator
    # hangers down to the gallery
    for dz in (-0.45, 1.45):
        m.member((6.9, y - 0.75, dz), (6.55, 12.05, dz), 0.1, 0.1, "steel")
    return m


# =========================================================================== build

def build_all():
    return [
        shaft("top"), shaft("plain"), shaft("platform"), shaft("landing"), collar(), headframe(),
        riser(False), riser(True), valvestation(),
        injectors(), injectors(top=True), pump(), ringmain(),
        core(True, False), core(True, True), core(False, False), core(False, True), manifold(), exhaust(),
        frame(0), frame(1), frame(2), frame(2, lapped=True),
        hull(0), hull(0, lit=True), hull(1), hull(1, lit=True), hull(2), shoulder(), stack(), jacket("axis"), jacket("diag"),
    ]


def write_blocktype(models):
    parts = [m.name for m in models]
    lit = ["*-corelowerlit-*", "*-coreupperlit-*"]
    block = {
        "code": "gensegment",
        "variantgroups": [
            {"code": "part", "states": parts},
            {"code": "turn", "states": ["r0", "r1", "r2", "r3"]},
        ],
        "attributes": {"handbook": {"exclude": True}},
        "drawtype": "json",
        "shapeByType": {
            "*-r0": {"base": "block/generator/segment/{part}", "rotateY": 0},
            "*-r1": {"base": "block/generator/segment/{part}", "rotateY": 270},
            "*-r2": {"base": "block/generator/segment/{part}", "rotateY": 180},
            "*-r3": {"base": "block/generator/segment/{part}", "rotateY": 90},
        },
        "textures": {code: {"base": path} for code, (path, _, _) in sorted(TEXTURES.items())},
        "blockmaterial": "Metal",
        "collisionbox": None,
        "selectionbox": None,
        "sidesolid": {"all": False},
        "sideopaque": {"all": False},
        "emitSideAo": {"all": False},
        "faceCullMode": "NeverCull",
        "renderPass": "OpaqueNoCull",
        "lightAbsorption": 0,
        "replaceable": 90,
        "resistance": 60,
        "requiredMiningTier": 5,
        "drops": [],
        "lightHsvByType": {p: [5, 6, 26] for p in lit},
        "behaviorsByType": {p: [{"name": "HeatSource", "properties": {"heatStrength": 14}}] for p in lit},
        "sounds": {"place": "game:block/chute", "break": "game:block/chute", "hit": "game:block/chute", "walk": "game:walk/stone"},
    }
    with open(BLOCKTYPE, "w") as f:
        json.dump(block, f, indent="\t")
        f.write("\n")


def main():
    os.makedirs(SHAPE_DIR, exist_ok=True)
    models = build_all()
    total = 0
    for m in models:
        n = m.save()
        total += n
        print(f"  {m.name:16s} {n:4d} elements")
    write_blocktype(models)
    parts = [steamport(), mainjunction(), mainpost(), mainstand()] + [steammain(a) for a in ("ud", "ns", "we")] + [mainport(s) for s in MAIN_PORT_ANCHORS]
    for part in parts:
        part.save(os.path.join(SHAPE_DIR, ".."))
    print(f"Wrote {len(parts)} steam fitting shapes")
    print(f"Wrote {len(models)} segment shapes ({total} elements) and gensegment.json")


if __name__ == "__main__":
    main()
