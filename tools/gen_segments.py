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
    def __init__(self, name, anchor):
        self.name = name
        self.anchor = anchor
        self.elements = []
        self.used = set()

    # ------------------------------------------------------------------ primitives

    def box(self, center, size, tex, rot=(0, 0, 0), faces=ALL, glow=0, uvoff=(0, 0), facetex=None):
        """A box turned about its own centre by rot = (rx, ry, rz) degrees."""
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

    def ring(self, y0, y1, a, t, tex, sides=range(8), inside=True, outside=True, rims=True, origin=(0, 0), phase=0, intex=None, chunk=None):
        """An octagonal wall: one panel per side, outer faces meeting at the corners."""
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
                self.east(k * 45 + phase, (a - t / 2, y0 + (i + 0.5) * step, 0), (t, step, s), tex, origin=origin, faces=faces,
                          facetex={"west": intex or tex}, uvoff=(k * 24, 0))

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

    def cone(self, y0, y1, a0, a1, t, tex, ribtex="band", courses=3, origin=(0, 0), ribs=True, inside=True):
        """A faceted taper between two octagons: plated courses with a rib over each hip."""
        tilt = math.degrees(math.atan2(a0 - a1, y1 - y0))
        for i in range(courses):
            f0, f1 = i / courses, (i + 1) / courses
            yb, yt = y0 + (y1 - y0) * f0, y0 + (y1 - y0) * f1
            ab, at = a0 + (a1 - a0) * f0, a0 + (a1 - a0) * f1
            length = math.hypot(ab - at, yt - yb)
            for k in range(8):
                self.east(k * 45, ((ab + at) / 2 - t / 2, (yb + yt) / 2, 0), (t, length, side((ab + at) / 2)), tex, tilt=tilt,
                          origin=origin, faces=["east"] + (["west"] if inside else []), uvoff=(k * 24, i * 20))
        if ribs:
            rb, rt = a0 / C22, a1 / C22
            rib_tilt = math.degrees(math.atan2(rb - rt, y1 - y0))
            length = math.hypot(rb - rt, y1 - y0)
            for k in range(8):
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

    def ladder(self, x, z, y0, y1, standoff=0.0):
        """A steel ladder on the north side of a cell column; its rungs run east-west."""
        zr = z
        for dx in (-0.36, 0.36):
            self.member((x + dx, y0, zr), (x + dx, y1, zr), 0.07, 0.07, "steel", chunk=1.0)
        y = y0 + 0.2
        while y < y1:
            self.box((x, y, zr), (0.66, 0.05, 0.05), "steel", faces=["north", "south", "up", "down"])
            y += 0.4
        if standoff:
            y = y0 + 1
            while y < y1:
                for dx in (-0.36, 0.36):
                    self.box((x + dx, y, z + standoff / 2), (0.06, 0.06, abs(standoff)), "steel", faces=["east", "west", "up", "down"])
                y += 2

    # ------------------------------------------------------------------ output

    def save(self):
        data = {
            "editor": {"allAngles": True},
            "textureWidth": 16,
            "textureHeight": 16,
            "textures": {code: "froststory:" + TEXTURES[code][0] for code in sorted(self.used)},
            "textureSizes": {code: [TEXTURES[code][1], TEXTURES[code][2]] for code in sorted(self.used)},
            "elements": self.elements,
        }
        with open(os.path.join(SHAPE_DIR, self.name + ".json"), "w") as f:
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
LINER = 3.5       # shaft liner, inside the block lining
COLUMN_X = 5.1    # frame columns stand just outside the hull
COLUMN_Z = 2.0
FRAME_TOP = 35.0
MODULE = 8        # shaft module height


# ----------------------------------------------------------------------------- shaft

def shaft(platform):
    """One 8 m lift of the shaft: liner, ring beam, ladder, and (every other lift) a platform at its top."""
    m = Model("shaftplatform" if platform else "shaftplain", (-1, -4, 0))
    y0, y1 = -MODULE, 0.0
    m.ring(y0, y1, LINER - 0.02, 0.12, "tubbing", outside=False, rims=False)
    m.flange(y0, y0 + 0.35, LINER - 0.02, t=0.4)
    m.flange(y0 + 4, y0 + 4.2, LINER - 0.02, t=0.25)
    m.ladder(1.0, -LINER + 0.14, y0, y1)
    if platform:
        m.deck(y1, LINER - 0.15, hole=1.5, gap=(0.5, 1.5))
        # guard rail round the well
        for sx, sz in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
            m.member((sx * 1.5, y1, sz * 1.5), (sx * 1.5, y1 + 1.05, sz * 1.5), 0.08, 0.08, "steel", caps=True)
        for h in (0.55, 1.05):
            for sgn in (1, -1):
                m.member((-1.5, y1 + h, sgn * 1.5), (1.5, y1 + h, sgn * 1.5), 0.06, 0.06, "steel")
                m.member((sgn * 1.5, y1 + h, -1.5), (sgn * 1.5, y1 + h, 1.5), 0.06, 0.06, "steel")
    return m


def collar():
    """The shaft mouth: a steel curb and the grated deck the core will stand on."""
    m = Model("collar", (-1, -1, 0))
    m.flange(-0.5, 0.0, LINER + 0.25, t=0.5)
    m.deck(0.0, LINER - 0.02, gap=(0.5, 1.5))
    return m


# ----------------------------------------------------------------------------- riser and valves

def riser(base):
    m = Model("riserbase" if base else "riser", (0, -4, 0))
    y0, y1 = -MODULE, 0.0
    if base:
        # wellhead: a heavy footing with a 45 degree bell where the bore comes up
        m.vprism(0, 0, y0, y0 + 0.4, 1.5, "band", caps=(False, True), captex="checker")
        m.cone(y0 + 0.4, y0 + 1.3, 1.4, 0.55, 0.12, "darkplate", courses=2, inside=False)
        for k in range(4):
            m.east(k * 90 + 45, (1.0, y0 + 0.85, 0), (0.9, 0.9, 0.12), "band", faces=["north", "south", "east", "up"])
        m.vprism(0, 0, y0 + 1.3, y1, 0.55, "pipe")
    else:
        m.vprism(0, 0, y0, y1, 0.55, "pipe")
    for y in (y0 + 1.9, y0 + 3.9, y0 + 5.9, y0 + 7.8):
        m.vprism(0, 0, y, y + 0.2, 0.75, "band", caps=(True, True), captex="band")
    return m


def valvestation():
    """Four gate valves on arms from the riser to relief ports in the liner. Anchor cell is the arms' level."""
    m = Model("valvestation", (0, 0, 0))
    yc = -0.5
    m.vprism(0, 0, yc - 0.55, yc + 0.55, 0.9, "band", caps=(True, True), captex="band")
    for k in range(4):
        ox, oz = yaw(k * 90, 1, 0)

        def at(r, y=yc):
            return (ox * r, y, oz * r)

        m.hprism(at(0.8), at(LINER - 0.1), 0.3, "pipe")
        m.vprism(*at(2.0)[::2], yc - 0.6, yc + 0.6, 0.55, "red", caps=(True, True), captex="band")   # valve body
        m.vprism(*at(2.0)[::2], yc + 0.6, yc + 1.05, 0.3, "band", caps=(False, True), captex="band")  # bonnet
        m.vprism(*at(2.0)[::2], yc + 1.05, yc + 1.6, 0.06, "steel")                                   # stem
        m.handwheel(at(2.0)[0], yc + 1.55, at(2.0)[2])
        for r in (1.3, 2.7):
            m.east(k * 90, (r, yc, 0), (0.14, 0.9, 0.9), "band")                                      # pipe flanges
        m.east(k * 90, (LINER - 0.2, yc, 0), (0.2, 1.3, 1.3), "band")                                 # relief port
    return m


# ----------------------------------------------------------------------------- pumps

INJECTOR = (3.15, -1.2)


def injectors():
    """Four injection lines down one lift of the shaft."""
    m = Model("injectors", (1, -4, 0))
    for k in range(4):
        x, z = yaw(k * 90, *INJECTOR)
        m.vprism(x, z, -MODULE, 0.0, 0.25, "pipe")
        for y in (-MODULE + 0.1, -MODULE + 4.1):
            m.vprism(x, z, y, y + 0.16, 0.36, "band", caps=(True, True), captex="band")
            m.east(k * 90, (LINER - 0.14, y + 0.08, INJECTOR[1]), (0.3, 0.1, 0.5), "steel")   # wall bracket
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
        m.vprism(0, 0, 0.0, 0.6, 3.0, "band", caps=(False, True), captex="checker")
        m.cone(0.6, 1.0, 3.0, VESSEL + 0.1, 0.12, "darkplate", courses=1, ribs=False, inside=False)

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
    for k in range(4):
        x, z = yaw(k * 90 + 45, VESSEL + 0.5, 0)
        m.vprism(x, z, 0.6 if lower else y0, y1, 0.28, "pipe")
        for y in ((2.5, 6.5) if lower else (10.5, 14.5)):
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
        if top <= 32:
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


def hull(level, lit=False):
    """The hull drum in three lifts: 'a' has the doorways, 'b' and 'c' have louvres over the axis faces.

    The two lifts round the core have a lit model, with the heart's glow behind their louvres."""
    name = "hull" + "abc"[level] + ("lit" if lit else "")
    m = Model(name, (1, (2, 12, 22)[level], 0))
    y0, y1 = (0.0, 10.0, 19.0)[level], (10.0, 19.0, 28.0)[level]
    s = side(HULL) / 2
    t = 0.25

    if level == 0:
        m.flange(0.0, 0.5, HULL + 0.3, t=0.55)
    m.flange(y1 - 0.45, y1, HULL + 0.18, t=0.45)

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

    # service ladder up the inside, standing off the north wall
    ladder_top = (10.0, 19.0, 21.0)[level]
    m.ladder(1.0, -3.44, y0, ladder_top, standoff=-0.75)
    if level == 2:
        m.deck(21.0, HULL - t - 0.02, hole=1.5, gap=(0.5, 1.5))    # gallery over the manifold
        for sx, sz in ((1, 1), (1, -1), (-1, 1), (-1, -1)):
            m.member((sx * 1.5, 21.0, sz * 1.5), (sx * 1.5, 22.05, sz * 1.5), 0.08, 0.08, "steel", caps=True)
        for sgn in (1, -1):
            m.member((-1.5, 22.05, sgn * 1.5), (1.5, 22.05, sgn * 1.5), 0.06, 0.06, "steel")
            m.member((sgn * 1.5, 22.05, -1.5), (sgn * 1.5, 22.05, 1.5), 0.06, 0.06, "steel")
    return m


def shoulder():
    """The 45 degree shoulder and the crown drum above it."""
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
    return m


def stack():
    m = Model("stack", (1, 36, 0))
    m.cone(34.0, 35.0, CROWN, STACK, 0.2, "plating", courses=2)
    m.ring(35.0, 39.0, STACK, 0.2, "plating", rims=False, intex="black")
    for y in (35.0, 37.0):
        m.flange(y, y + 0.3, STACK + 0.12, t=0.3)
    m.cone(39.0, 39.6, STACK, STACK + 0.5, 0.2, "band", courses=1, ribs=False)   # flared lip
    m.ring(39.6, 40.0, STACK + 0.5, 0.25, "band", intex="black", chunk=99)
    return m


# =========================================================================== build

def build_all():
    return [
        shaft(False), shaft(True), collar(),
        riser(False), riser(True), valvestation(),
        injectors(), pump(),
        core(True, False), core(True, True), core(False, False), core(False, True), manifold(), exhaust(),
        frame(0), frame(1), frame(2), frame(2, lapped=True),
        hull(0), hull(0, lit=True), hull(1), hull(1, lit=True), hull(2), shoulder(), stack(),
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
    print(f"Wrote {len(models)} segment shapes ({total} elements) and gensegment.json")


if __name__ == "__main__":
    main()
