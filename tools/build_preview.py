#!/usr/bin/env python3
"""Builds a self-contained 3D preview page of the generator from the mod's real assets.

    dotnet run --project tools/BlueprintExport -c Release -- tools/preview/blueprint.json
    python3 tools/build_preview.py

It reads the exported blueprint plus the mod's blocktypes, shapes, textures, lang and stage config,
and writes dist/generator-preview.html. Open that file in a browser; nothing else is needed.
"""
import base64
import fnmatch
import glob
import io
import itertools
import json
import math
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.join(HERE, "..")
ASSETS = os.path.join(REPO, "FrostStory", "assets", "froststory")
OUT = os.path.join(REPO, "dist", "generator-preview.html")

FACES = ["north", "east", "south", "west", "up", "down"]

REQUIREMENT_NAMES = {
    "game:stonebricks-*": "ashlar blocks",
    "game:metalplate-iron": "iron plates",
    "game:metalplate-steel": "steel plates",
    "game:ingot-iron": "iron ingots",
    "game:ingot-steel": "steel ingots",
    "game:ingot-brass": "brass ingots",
    "game:ingot-copper": "copper ingots",
    "game:gear-rusty": "rusty gears",
    "game:gear-temporal": "temporal gears",
}


def load(path):
    with open(path) as f:
        return json.load(f)


# --------------------------------------------------------------------------- texture atlas

class Atlas:
    """Packs every generator texture into one sheet, each with a gutter of repeated edge pixels."""
    WIDTH = 512
    PAD = 2

    def __init__(self):
        base = os.path.join(ASSETS, "textures")
        paths = sorted(glob.glob(os.path.join(base, "block", "generator", "**", "*.png"), recursive=True))
        tiles = [(os.path.relpath(p, base)[:-4].replace(os.sep, "/"), Image.open(p).convert("RGBA")) for p in paths]
        tiles.sort(key=lambda t: (-t[1].height, t[0]))

        self.rects = {}
        x = y = shelf = 0
        for key, img in tiles:
            w, h = img.width + 2 * self.PAD, img.height + 2 * self.PAD
            if x + w > self.WIDTH:
                x, y, shelf = 0, y + shelf, 0
            self.rects[key] = (x + self.PAD, y + self.PAD, img.width, img.height)
            x, shelf = x + w, max(shelf, h)
        self.height = 1
        while self.height < y + shelf:
            self.height *= 2

        self.image = Image.new("RGBA", (self.WIDTH, self.height))
        for key, img in tiles:
            rx, ry, w, h = self.rects[key]
            grown = img.resize((w + 2 * self.PAD, h + 2 * self.PAD), Image.NEAREST)   # stand-in for edge repeat
            self.image.paste(grown, (rx - self.PAD, ry - self.PAD))
            self.image.paste(img, (rx, ry))

    def uv(self, texture_path, u, v):
        """Atlas coordinates (0..1, v down) of a point given as fractions of one texture."""
        rx, ry, w, h = self.rects[texture_path.split(":")[-1]]
        return (rx + u * w) / self.WIDTH, (ry + v * h) / self.height

    def data_uri(self):
        buf = io.BytesIO()
        self.image.save(buf, "PNG")
        return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()


# --------------------------------------------------------------------------- block resolution

def expand_variants(bt):
    groups = bt.get("variantgroups", [])
    if not groups:
        return [(bt["code"], {})]
    out = []
    for combo in itertools.product(*[g["states"] for g in groups]):
        out.append((bt["code"] + "-" + "-".join(combo), {g["code"]: s for g, s in zip(groups, combo)}))
    return out


def by_type(bt, key, code):
    for name in (key + "ByType", key + "bytype"):
        for pattern, value in bt.get(name, {}).items():
            if fnmatch.fnmatchcase(code, pattern):
                return value
    return bt.get(key)


def fill(text, variant):
    for k, v in variant.items():
        text = text.replace("{" + k + "}", v)
    return text


def rot(p, axis, deg, origin):
    if not deg:
        return p
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    x, y, z = (p[i] - origin[i] for i in range(3))
    if axis == "x":
        y, z = y * c - z * s, y * s + z * c
    elif axis == "y":
        x, z = x * c + z * s, -x * s + z * c
    else:
        x, y = x * c - y * s, x * s + y * c
    return (x + origin[0], y + origin[1], z + origin[2])


def box_quads(frm, to):
    """Corners of each face in the order the engine's cube mesh maps (u1,v1), (u2,v1), (u2,v2), (u1,v2) to."""
    x0, y0, z0 = frm
    x1, y1, z1 = to
    return {
        "north": [(x1, y1, z0), (x0, y1, z0), (x0, y0, z0), (x1, y0, z0)],
        "east": [(x1, y1, z1), (x1, y1, z0), (x1, y0, z0), (x1, y0, z1)],
        "south": [(x0, y1, z1), (x1, y1, z1), (x1, y0, z1), (x0, y0, z1)],
        "west": [(x0, y1, z0), (x0, y1, z1), (x0, y0, z1), (x0, y0, z0)],
        "up": [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)],
        "down": [(x1, y0, z0), (x0, y0, z0), (x0, y0, z1), (x1, y0, z1)],
    }


def shade_of(corners, flip):
    a, b, d = corners[0], corners[1], corners[3]
    u = [b[i] - a[i] for i in range(3)]
    v = [d[i] - a[i] for i in range(3)]
    n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
    length = math.sqrt(sum(c * c for c in n)) or 1
    sign = 1 if flip else -1
    nx, ny, nz = (sign * c / length for c in n)   # outward
    return max(0.35, min(1.0, 0.70 + 0.30 * ny + 0.09 * nx + 0.06 * nz))


def quad(corners, uvs, face_index, glow=0, flip=False):
    flat = [round(shade_of(corners, flip), 3), face_index, 1 if glow else 0]
    for (x, y, z), (u, v) in zip(corners, uvs):
        flat += [round(x / 16, 3), round(y / 16, 3), round(z / 16, 3), round(u, 5), round(v, 5)]
    return flat


def resolve_block(bt, code, variant, atlas):
    textures = by_type(bt, "textures", code) or {}

    def path(key):
        return fill(textures[key]["base"], variant)

    if bt.get("drawtype") == "cube":
        quads = []
        full = box_quads((0, 0, 0), (16, 16, 16))
        for i, face in enumerate(FACES):
            key = face if face in textures else ("horizontals" if i < 4 else "verticals")
            key = key if key in textures else "all"
            uvs = [atlas.uv(path(key), u, v) for u, v in ((0, 0), (1, 0), (1, 1), (0, 1))]
            quads.append(quad(full[face], uvs, i, flip=face == "down"))
        return {"o": 1, "q": quads}

    ref = by_type(bt, "shape", code)
    base = fill(ref["base"], variant)
    if base.startswith("game:"):
        return {"o": 0, "q": []}          # invisible filler

    shape = load(os.path.join(ASSETS, "shapes", base + ".json"))
    sizes = shape.get("textureSizes", {})
    default_size = (shape.get("textureWidth", 16), shape.get("textureHeight", 16))
    centre = (8, 8, 8)
    quads = []
    for el in shape["elements"]:
        origin = el.get("rotationOrigin", (0, 0, 0))
        for face, corners in box_quads(el["from"], el["to"]).items():
            spec = el["faces"].get(face)
            if not spec or spec.get("enabled") is False:
                continue
            pts = []
            for p in corners:
                for axis in "zyx":        # the engine composes Rx * Ry * Rz, so Z turns first
                    p = rot(p, axis, el.get("rotation" + axis.upper(), 0), origin)
                for axis in "xyz":
                    p = rot(p, axis, ref.get("rotate" + axis.upper(), 0), centre)
                pts.append(p)
            tex = spec["texture"].lstrip("#")
            tw, th = sizes.get(tex, default_size)
            u1, v1, u2, v2 = spec["uv"]
            uvs = [atlas.uv(path(tex), u / tw, v / th) for u, v in ((u1, v1), (u2, v1), (u2, v2), (u1, v2))]
            quads.append(quad(pts, uvs, -1, spec.get("glow", 0), flip=face == "down"))
    return {"o": 0, "q": quads}


def resolve_all(codes, atlas):
    resolved = {}
    for path in glob.glob(os.path.join(ASSETS, "blocktypes", "generator", "*.json")):
        bt = load(path)
        for code, variant in expand_variants(bt):
            resolved["froststory:" + code] = (bt, code, variant)
    return [resolve_block(*resolved[c], atlas) for c in codes]


# --------------------------------------------------------------------------- page

# --------------------------------------------------------------------------- pipe runs
# These mirror FrostStory/src/Steam/SteamRuns.cs and BlockEntitySteamNode.AddTube, so a run in the
# preview is laid out the way the game lays it out.

NORMALS = {"north": (0, 0, -1), "east": (1, 0, 0), "south": (0, 0, 1), "west": (-1, 0, 0), "up": (0, 1, 0), "down": (0, -1, 0)}
PIECE_LENGTH = 0.5


def attach_point(anchor, size, face):
    n = NORMALS[face]
    return tuple(anchor[i] + size[i] / 2 + n[i] * size[i] / 2 for i in range(3))


def run_point(start, face, end, other_face, big, t):
    distance = math.dist(start, end)
    h = max(1.5 if big else 0.75, min(distance * 0.45, 8.0))
    n0, n1 = NORMALS[face], NORMALS[other_face]
    u = 1 - t
    b = (u ** 3, 3 * u * u * t, 3 * u * t * t, t ** 3)
    return tuple(b[0] * start[i] + b[1] * (start[i] + n0[i] * h) + b[2] * (end[i] + n1[i] * h) + b[3] * end[i] for i in range(3))


def run_sample(start, face, end, other_face, big):
    length, previous = 0.0, start
    for i in range(1, 25):
        p = run_point(start, face, end, other_face, big, i / 24)
        length += math.dist(previous, p)
        previous = p
    pieces = max(2, min(160, math.ceil(length / (PIECE_LENGTH * 0.9))))
    return [run_point(start, face, end, other_face, big, i / pieces) for i in range(pieces + 1)], length


def place_piece(piece, scale, direction, centre):
    """One copy of the run piece: scaled about the block centre, pitched, turned, then moved to centre."""
    dx, dy, dz = direction
    pitch = -math.asin(max(-1.0, min(1.0, dy)))
    heading = math.atan2(dx, dz)
    cp, sp, ch, sh = math.cos(pitch), math.sin(pitch), math.cos(heading), math.sin(heading)
    quads = []
    for q in piece:
        corners, uvs = [], []
        for j in range(4):
            o = 3 + j * 5
            x, y, z = (q[o] - 0.5) * scale[0], (q[o + 1] - 0.5) * scale[1], (q[o + 2] - 0.5) * scale[2]
            y, z = y * cp - z * sp, y * sp + z * cp                 # pitch about X
            x, z = x * ch + z * sh, -x * sh + z * ch                # heading about Y
            corners.append(((x + centre[0]) * 16, (y + centre[1]) * 16, (z + centre[2]) * 16))
            uvs.append((q[o + 3], q[o + 4]))
        quads.append(quad(corners, uvs, -1))
    return quads


def run_quads(piece, start, face, end, other_face, big):
    points, length = run_sample(start, face, end, other_face, big)
    quads = []
    for a, b in zip(points, points[1:]):
        d = tuple(b[i] - a[i] for i in range(3))
        chord = math.sqrt(sum(c * c for c in d))
        if chord < 1e-4:
            continue
        quads += place_piece(piece, (1, 1, chord / PIECE_LENGTH * 1.08), tuple(c / chord for c in d),
                             tuple((a[i] + b[i]) / 2 for i in range(3)))
    for at, f in ((start, face), (end, other_face)):
        n = NORMALS[f]
        quads += place_piece(piece, (1.3, 1.3, 0.3), n, tuple(at[i] + n[i] * 0.08 for i in range(3)))
    return quads, length


def pipework_example(codes, atlas):
    """A sample of what a player can build off the generator, so every steam part can be seen in place.

    Fittings stand where a player has put them; pipe runs are strung between them and the ports. Each
    group hangs off one port and is only drawn once that port has been built. Positions are blueprint
    cells: the east large port's mouth is at (7, 15, 0), the pad stands at y = 0."""
    def idx(name):
        code = "froststory:" + name
        if code not in codes:
            codes.append(code)
        return codes.index(code)

    def piece(shape):
        bt = {"drawtype": "json", "shape": {"base": "block/generator/" + shape},
              "textures": {"pipe": {"base": "block/generator/segment/pipe"}}}
        return resolve_block(bt, shape, {}, atlas)["q"]

    pieces = {False: piece("pipeseg"), True: piece("mainseg")}
    SMALL, FITTING, MOUTH = (1, 1, 1), (2, 2, 2), (1, 2, 2)

    def run(a, a_size, a_face, b, b_size, b_face, big):
        quads, _ = run_quads(pieces[big], attach_point(a, a_size, a_face), a_face, attach_point(b, b_size, b_face), b_face, big)
        return quads

    def cells(*parts):
        return [v for x, y, z, name in parts for v in (x, y, z, idx(name))]

    def stand(x, z, top):
        return [(x, y, z, "mainpost") for y in range(1, top)] + [(x, top, z, "mainjunction")]

    port, a, b, c = (7, 15, 0), (12, 15, 0), (20, 11, 4), (12, 15, -9)
    tap = (10, 8, 5)
    large_cells = cells(*stand(a[0], a[2], a[1]), *stand(b[0], b[2], b[1]), *stand(c[0], c[2], c[1]), (*tap, "pipejunction"))
    large_runs = (
        run(port, MOUTH, "east", a, FITTING, "west", True)           # out from the large port, level
        + run(a, FITTING, "east", b, FITTING, "west", True)          # on to a lower stand, off to one side
        + run(a, FITTING, "north", c, FITTING, "south", True)        # a branch
        + run(a, FITTING, "down", tap, SMALL, "up", False)           # an ordinary pipe tapping the fitting
    )

    base, e, f = (7, 1, 3), (12, 1, 6), (14, 5, 10)
    small_cells = cells((*e, "pipejunction"), (*f, "pipejunction"))
    small_runs = (
        run(base, SMALL, "east", e, SMALL, "west", False)            # from a base port along the ground
        + run(e, SMALL, "south", f, SMALL, "down", False)            # and up to a fitting overhead
    )

    return [
        {"at": list(port), "port": "mainport", "cells": large_cells, "quads": large_runs},
        {"at": list(base), "port": "steamport", "cells": small_cells, "quads": small_runs},
    ]


def main():
    blueprint_path = os.path.join(HERE, "preview", "blueprint.json")
    if not os.path.exists(blueprint_path):
        sys.exit("Run the BlueprintExport tool first (see the docstring).")

    blueprint = load(blueprint_path)
    atlas = Atlas()
    lang = load(os.path.join(ASSETS, "lang", "en.json"))
    config = {s["id"]: s for s in load(os.path.join(ASSETS, "config", "generatorstages.json"))["stages"]}

    stages = []
    for s in blueprint["stages"]:
        cells = [c for step in s["steps"] for c in step["cells"]]
        stages.append({
            "id": s["id"],
            "name": lang["stage-" + s["id"]],
            "desc": lang["stagedesc-" + s["id"]],
            "segments": sum(1 for i in range(3, len(cells), 4) if cells[i] >= 0 and "gensegment" in blueprint["codes"][cells[i]]),
            "needs": [[r["quantity"], REQUIREMENT_NAMES.get(r["code"], r["code"])] for r in config[s["id"]]["requirements"]],
            "steps": [{"name": lang["step-" + step["key"]].format(step["number"], step["of"]), "cells": step["cells"]}
                      for step in s["steps"]],
        })

    data = {
        "depth": blueprint["depth"],
        "height": blueprint["height"],
        "consoleDistance": blueprint["consoleDistance"],
        "swaps": blueprint["swaps"],
        "pipework": pipework_example(blueprint["codes"], atlas),
        "codes": [c.split(":")[-1] for c in blueprint["codes"]],
        "consoleCode": blueprint["codes"].index("froststory:generatorconsole-north"),
        "figure": list(atlas.uv("block/generator/paint-red", 0.2, 0.2) + atlas.uv("block/generator/paint-red", 0.8, 0.8)),
        "atlas": atlas.data_uri(),
        "defs": resolve_all(blueprint["codes"], atlas),
        "stages": stages,
    }

    with open(os.path.join(HERE, "preview", "template.html")) as f:
        page = f.read()
    page = page.replace("/*__DATA__*/null", json.dumps(data, separators=(",", ":")))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w") as f:
        f.write(page)
    print(f"Wrote {os.path.relpath(OUT, REPO)} ({os.path.getsize(OUT) // 1024} KB)")


if __name__ == "__main__":
    main()
