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
    COLS = 8

    def __init__(self):
        self.names = sorted(os.path.splitext(os.path.basename(p))[0]
                            for p in glob.glob(os.path.join(ASSETS, "textures", "block", "generator", "*.png")))
        self.rows = 4
        assert len(self.names) <= self.COLS * self.rows
        self.image = Image.new("RGBA", (self.COLS * 32, self.rows * 32))
        for i, name in enumerate(self.names):
            tile = Image.open(os.path.join(ASSETS, "textures", "block", "generator", name + ".png")).convert("RGBA")
            self.image.paste(tile, ((i % self.COLS) * 32, (i // self.COLS) * 32))

    def index(self, texture_path):
        return self.names.index(texture_path.rsplit("/", 1)[-1])

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
    """Corners of each face as top-left, top-right, bottom-right, bottom-left seen from outside."""
    x0, y0, z0 = frm
    x1, y1, z1 = to
    return {
        "north": [(x1, y1, z0), (x0, y1, z0), (x0, y0, z0), (x1, y0, z0)],
        "east": [(x1, y1, z1), (x1, y1, z0), (x1, y0, z0), (x1, y0, z1)],
        "south": [(x0, y1, z1), (x1, y1, z1), (x1, y0, z1), (x0, y0, z1)],
        "west": [(x0, y1, z0), (x0, y1, z1), (x0, y0, z1), (x0, y0, z0)],
        "up": [(x0, y1, z0), (x1, y1, z0), (x1, y1, z1), (x0, y1, z1)],
        "down": [(x0, y0, z1), (x1, y0, z1), (x1, y0, z0), (x0, y0, z0)],
    }


def shade_of(corners):
    a, b, d = corners[0], corners[1], corners[3]
    u = [b[i] - a[i] for i in range(3)]
    v = [d[i] - a[i] for i in range(3)]
    n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
    length = math.sqrt(sum(c * c for c in n)) or 1
    nx, ny, nz = (-c / length for c in n)   # outward
    return max(0.35, min(1.0, 0.70 + 0.30 * ny + 0.09 * nx + 0.06 * nz))


def quad(tile, corners, uvs, face_index):
    flat = [tile, round(shade_of(corners), 3), face_index]
    for (x, y, z), (u, v) in zip(corners, uvs):
        flat += [round(x / 16, 4), round(y / 16, 4), round(z / 16, 4), round(u / 16, 4), round(v / 16, 4)]
    return flat


def resolve_block(bt, code, variant, atlas):
    textures = by_type(bt, "textures", code) or {}

    def tile(key):
        return atlas.index(fill(textures[key]["base"], variant))

    if bt.get("drawtype") == "cube":
        quads = []
        full = box_quads((0, 0, 0), (16, 16, 16))
        for i, face in enumerate(FACES):
            key = face if face in textures else ("horizontals" if i < 4 else "verticals")
            key = key if key in textures else "all"
            quads.append(quad(tile(key), full[face], [(0, 0), (16, 0), (16, 16), (0, 16)], i))
        return {"o": 1, "q": quads}

    ref = by_type(bt, "shape", code)
    shape = load(os.path.join(ASSETS, "shapes", fill(ref["base"], variant) + ".json"))
    centre = (8, 8, 8)
    quads = []
    for el in shape["elements"]:
        origin = el.get("rotationOrigin", centre)
        for face, corners in box_quads(el["from"], el["to"]).items():
            spec = el["faces"].get(face)
            if not spec or spec.get("enabled") is False:
                continue
            pts = []
            for p in corners:
                for axis in "xyz":
                    p = rot(p, axis, el.get("rotation" + axis.upper(), 0), origin)
                for axis in "xyz":
                    p = rot(p, axis, ref.get("rotate" + axis.upper(), 0), centre)
                pts.append(p)
            u1, v1, u2, v2 = spec["uv"]
            quads.append(quad(tile(spec["texture"].lstrip("#")), pts, [(u1, v1), (u2, v1), (u2, v2), (u1, v2)], -1))
    return {"o": 0, "q": quads}


def resolve_all(codes, atlas):
    resolved = {}
    for path in glob.glob(os.path.join(ASSETS, "blocktypes", "generator", "*.json")):
        bt = load(path)
        for code, variant in expand_variants(bt):
            resolved["froststory:" + code] = (bt, code, variant)
    return [resolve_block(*resolved[c], atlas) for c in codes]


# --------------------------------------------------------------------------- page

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
        cells = s["cells"]
        stages.append({
            "id": s["id"],
            "name": lang["stage-" + s["id"]],
            "desc": lang["stagedesc-" + s["id"]],
            "blocks": sum(1 for i in range(3, len(cells), 4) if cells[i] >= 0),
            "needs": [[r["quantity"], REQUIREMENT_NAMES.get(r["code"], r["code"])] for r in config[s["id"]]["requirements"]],
            "cells": cells,
        })

    data = {
        "depth": blueprint["depth"],
        "height": blueprint["height"],
        "consoleDistance": blueprint["consoleDistance"],
        "heart": blueprint["heart"],
        "litCode": blueprint["codes"].index("froststory:coreheart-lit"),
        "consoleCode": blueprint["codes"].index("froststory:generatorconsole-north"),
        "figureTile": atlas.index("paint-red"),
        "atlas": {"cols": Atlas.COLS, "rows": atlas.rows, "uri": atlas.data_uri()},
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
