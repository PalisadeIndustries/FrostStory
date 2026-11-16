#!/usr/bin/env python3
"""Generates the shapes and textures of FrostStory's fur gear and iron ice tools.

    python3 tools/gen_gear.py

Every model here is built from boxes, and every box face gets its own patch of a texture atlas
painted procedurally for the material that face is made of (fur, hide, leather, iron, wood,
glass). Nothing is traced or copied from vanilla art.

The wearables attach to the player's skeleton with `stepParentName`, so their coordinates are in
the space of the body part they hang on. The seraph's parts (from the vanilla player shape) are:

    Head 5.5 x 5 x 5 (face on the west side, x = 0)   Neck 3 x 4 x 3
    UpperTorso 5 x 5 x 6.2      LowerTorso 5 x 5 x 6
    UpperArmL/R 3 x 7 x 2       LowerArmL/R 2.5 x 8 x 2 (hand end at y = 0)
    UpperFootL/R 4.2 x 9 x 3.2  LowerFootL/R 4 x 10 x 3 (sole at y = 0)

Outputs: FrostStory/assets/froststory/{shapes,textures}/item/gear/<name>.{json,png}
"""
import json
import math
import os
import random

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "FrostStory", "assets", "froststory")
SHAPE_DIR = os.path.join(ROOT, "shapes", "item", "gear")
TEX_DIR = os.path.join(ROOT, "textures", "item", "gear")
PX = 4          # texture pixels per shape unit
PAD = 2         # pixels of bleed round every face patch
ATLAS_W = 64    # atlas width in shape units

FACES = ("north", "east", "south", "west", "up", "down")


def clamp(v):
    return max(0, min(255, int(round(v))))


# --------------------------------------------------------------------------- materials
#
# A painter fills a w x h pixel patch (one box face) and returns rows of RGBA tuples.
# `rng` is seeded per face so regeneration is stable.

def _noise_grid(rng, w, h, cell):
    """Smooth value noise, 0..1, of roughly `cell` pixel features."""
    cx, cy = max(2, w // cell + 2), max(2, h // cell + 2)
    g = [[rng.random() for _ in range(cx)] for _ in range(cy)]
    out = []
    for y in range(h):
        row = []
        for x in range(w):
            fx, fy = x / cell, y / cell
            x0, y0 = int(fx), int(fy)
            tx, ty = fx - x0, fy - y0
            tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
            a = g[y0][x0] * (1 - tx) + g[y0][x0 + 1] * tx
            b = g[y0 + 1][x0] * (1 - tx) + g[y0 + 1][x0 + 1] * tx
            row.append(a * (1 - ty) + b * ty)
        out.append(row)
    return out


def fur(base, tip, root, strand=22):
    """Long pile: vertical locks of uneven length, pale tips, dark roots between the locks."""
    def paint(rng, w, h):
        blot = _noise_grid(rng, w, h, 5)
        # each column belongs to a lock; locks have their own length and shade
        locks, x = [], 0
        while x < w:
            lw = rng.randint(2, 4)
            locks.append((x, lw, rng.uniform(-strand, strand), rng.randint(3, 7), rng.randint(0, 6)))
            x += lw
        px = [[None] * w for _ in range(h)]
        for (lx, lw, shade, period, phase) in locks:
            for xx in range(lx, min(w, lx + lw)):
                edge = xx == lx
                for y in range(h):
                    t = ((y + phase) % period) / period          # 0 at the top of a lock, 1 at its end
                    c = [base[i] + (tip[i] - base[i]) * max(0.0, 0.6 - t) for i in range(3)]
                    if t > 0.82 or edge:
                        c = [c[i] * 0.55 + root[i] * 0.45 for i in range(3)]
                    d = shade + (blot[y][xx] - 0.5) * 26 + rng.uniform(-7, 7)
                    px[y][xx] = (clamp(c[0] + d), clamp(c[1] + d), clamp(c[2] + d), 255)
        return px
    return paint


def hide(base, stitch=None, grain=9):
    """Smoked hide or leather: soft mottling, fine grain, a running stitch round the edge."""
    def paint(rng, w, h):
        blot = _noise_grid(rng, w, h, 6)
        px = []
        for y in range(h):
            row = []
            for x in range(w):
                d = (blot[y][x] - 0.5) * 30 + rng.uniform(-grain, grain)
                row.append((clamp(base[0] + d), clamp(base[1] + d), clamp(base[2] + d), 255))
            px.append(row)
        if stitch and w >= 10 and h >= 10:
            # seams run down the two long edges of a panel
            inset = PAD + 2
            for y in range(inset, h - inset, 3):
                for xx in (inset, w - inset - 1):
                    px[y][xx] = stitch + (255,)
        return px
    return paint


def metal(base, rust=(118, 70, 40), rusty=0.05):
    def paint(rng, w, h):
        blot = _noise_grid(rng, w, h, 4)
        px = []
        for y in range(h):
            row = []
            for x in range(w):
                d = (blot[y][x] - 0.5) * 34 + rng.uniform(-6, 6)
                if x == PAD or y == PAD:
                    d += 22
                elif x == w - PAD - 1 or y == h - PAD - 1:
                    d -= 22
                c = base
                if rng.random() < rusty * (0.4 + blot[y][x]):
                    c = rust
                row.append((clamp(c[0] + d), clamp(c[1] + d), clamp(c[2] + d), 255))
            px.append(row)
        return px
    return paint


def wood(base):
    """Grain runs along the longer side of the patch."""
    def paint(rng, w, h):
        along_x = w >= h
        n = h if along_x else w
        lines = [rng.uniform(-18, 12) for _ in range(n)]
        px = []
        for y in range(h):
            row = []
            for x in range(w):
                i, j = (y, x) if along_x else (x, y)
                d = lines[i] + math.sin(j * 0.45 + i * 1.7) * 5 + rng.uniform(-5, 5)
                row.append((clamp(base[0] + d), clamp(base[1] + d), clamp(base[2] + d), 255))
            px.append(row)
        return px
    return paint


def glass(base, glint=(214, 226, 222)):
    """Smoked lens: dark, with one diagonal glint."""
    def paint(rng, w, h):
        px = []
        for y in range(h):
            row = []
            for x in range(w):
                d = rng.uniform(-4, 4) - (y / max(1, h)) * 16
                c = base
                if abs((x - PAD) - (y - PAD) - w * 0.25) < max(1.0, w * 0.08):
                    c = glint
                row.append((clamp(c[0] + d), clamp(c[1] + d), clamp(c[2] + d), 255))
            px.append(row)
        return px
    return paint


MATERIALS = {
    # pale winter fur for ruffs, cuffs and hems
    "fur-pale": fur((206, 200, 188), (238, 236, 230), (128, 118, 104)),
    # wolf-grey body fur for the trousers and mittens
    "fur-grey": fur((118, 112, 104), (170, 164, 154), (60, 56, 52)),
    # warm brown fur for the boots' upper and the mittens' backs
    "fur-brown": fur((116, 86, 60), (160, 128, 94), (62, 44, 30)),
    # smoked caribou-style hide shell of the parka and hood
    "hide": hide((150, 112, 74), stitch=(214, 196, 160)),
    "hide-dark": hide((104, 76, 52), stitch=(188, 166, 128)),
    "leather": hide((82, 56, 38), stitch=(170, 140, 100), grain=6),
    "sole": hide((48, 40, 34), grain=5),
    "lining": hide((170, 150, 120), grain=5),
    "iron": metal((116, 120, 126)),
    "iron-dark": metal((78, 80, 86), rusty=0.08),
    "edge": metal((176, 182, 188), rusty=0.0),
    "brass": metal((150, 120, 64), rust=(80, 96, 70), rusty=0.03),
    "wood": wood((128, 92, 58)),
    "glass": glass((48, 58, 60)),
    "toggle": wood((210, 196, 160)),   # bone toggles
}


# --------------------------------------------------------------------------- shape building

class Box:
    def __init__(self, name, frm, to, mat, parent=None, skip=(), faces=None, rot=None):
        self.name, self.frm, self.to, self.mat = name, list(frm), list(to), mat
        self.parent, self.skip, self.facemats, self.rot = parent, set(skip), faces or {}, rot

    def face_size(self, f):
        dx, dy, dz = (abs(self.to[i] - self.frm[i]) for i in range(3))
        return {"north": (dx, dy), "south": (dx, dy), "east": (dz, dy), "west": (dz, dy),
                "up": (dx, dz), "down": (dx, dz)}[f]


def build(name, boxes, code):
    """Packs every face into one atlas, paints it, writes shape JSON and PNG."""
    patches = []
    for bi, b in enumerate(boxes):
        for f in FACES:
            if f in b.skip:
                continue
            w, h = b.face_size(f)
            if w <= 0 or h <= 0:
                continue
            pw, ph = max(1, math.ceil(w * PX)), max(1, math.ceil(h * PX))
            patches.append((bi, f, w, h, pw, ph))

    # shelf packing, tallest first
    order = sorted(range(len(patches)), key=lambda i: -patches[i][5])
    width_px = ATLAS_W * PX
    pos, x, y, shelf = {}, 0, 0, 0
    for i in order:
        pw, ph = patches[i][4] + 2 * PAD, patches[i][5] + 2 * PAD
        if x + pw > width_px:
            x, y, shelf = 0, y + shelf, 0
        pos[i] = (x, y)
        x += pw
        shelf = max(shelf, ph)
    used_h = (y + shelf + PX - 1) // PX
    atlas_h = 16
    while atlas_h < used_h:
        atlas_h *= 2

    img = Image.new("RGBA", (width_px, atlas_h * PX), (0, 0, 0, 0))
    pix = img.load()
    faces_json = [dict() for _ in boxes]
    for i, (bi, f, w, h, pw, ph) in enumerate(patches):
        px0, py0 = pos[i]
        b = boxes[bi]
        mat = b.facemats.get(f, b.mat)
        rng = random.Random(f"{name}/{b.name}/{f}")
        patch = MATERIALS[mat](rng, pw + 2 * PAD, ph + 2 * PAD)
        for yy in range(ph + 2 * PAD):
            for xx in range(pw + 2 * PAD):
                pix[px0 + xx, py0 + yy] = patch[yy][xx]
        u0, v0 = (px0 + PAD) / PX, (py0 + PAD) / PX
        faces_json[bi][f] = {"texture": "#" + code, "uv": [round(u0, 4), round(v0, 4), round(u0 + w, 4), round(v0 + h, 4)]}

    elements = []
    for bi, b in enumerate(boxes):
        e = {"name": b.name}
        if b.parent:
            e["stepParentName"] = b.parent
        e["from"], e["to"] = [round(v, 4) for v in b.frm], [round(v, 4) for v in b.to]
        if b.rot:
            origin, axis, angle = b.rot
            e["rotationOrigin"] = origin
            e["rotation" + axis.upper()] = angle
        e["faces"] = faces_json[bi]
        elements.append(e)

    shape = {
        "textureWidth": ATLAS_W,
        "textureHeight": atlas_h,
        "textureSizes": {code: [ATLAS_W, atlas_h]},
        "textures": {code: "item/gear/" + name},
        "elements": elements,
    }
    os.makedirs(SHAPE_DIR, exist_ok=True)
    os.makedirs(TEX_DIR, exist_ok=True)
    with open(os.path.join(SHAPE_DIR, name + ".json"), "w") as fh:
        json.dump(shape, fh, indent="\t")
        fh.write("\n")
    img.save(os.path.join(TEX_DIR, name + ".png"))
    print(f"{name}: {len(boxes)} boxes, atlas {width_px}x{atlas_h * PX}")


def around(parent, name, size, grow, mat, y0=None, y1=None, **kw):
    """A box round a whole body part of `size`, grown by `grow` on every side."""
    sx, sy, sz = size
    y0 = -grow if y0 is None else y0
    y1 = sy + grow if y1 is None else y1
    return Box(name, (-grow, y0, -grow), (sx + grow, y1, sz + grow), mat, parent, **kw)


# --------------------------------------------------------------------------- the gear

def furhood():
    p = "Head"
    return [
        # hide shell round the back and sides of the head; open at the face (west)
        Box("hoodshell", (0.5, -0.7, -0.5), (6.0, 5.5, 5.5), "hide", p, skip=("west",), faces={"down": "lining"}),
        # peak over the brow
        Box("hoodpeak", (-0.2, 4.7, -0.5), (0.6, 5.5, 5.5), "hide", p),
        # pale fur ruff framing the face
        Box("ruff-top", (-0.6, 4.5, -1.0), (0.6, 6.0, 6.0), "fur-pale", p),
        Box("ruff-l", (-0.6, -1.1, -1.0), (0.6, 4.5, 0.0), "fur-pale", p),
        Box("ruff-r", (-0.6, -1.1, 5.0), (0.6, 4.5, 6.0), "fur-pale", p),
        Box("ruff-chin", (-0.6, -1.1, 0.0), (0.6, -0.2, 5.0), "fur-pale", p),
        # drape over the nape and shoulders
        Box("hooddrape", (1.2, -2.4, -0.8), (6.3, -0.6, 5.8), "hide", p, faces={"down": "lining"}),
        Box("drapehem", (1.0, -2.9, -1.0), (6.5, -2.2, 6.0), "fur-pale", p),
    ]


def furparka():
    torso, lower = (5, 5, 6.2), (5, 5, 6)
    upper_arm, lower_arm = (3, 7, 2), (2.5, 8, 2)
    boxes = [
        around("UpperTorso", "chest", torso, 0.35, "hide"),
        around("LowerTorso", "belly", lower, 0.4, "hide"),
        # skirts front and back, left free so the legs can swing
        Box("skirt-front", (-0.5, -3.6, -0.4), (0.1, 0.2, 6.4), "hide", "LowerTorso"),
        Box("skirt-back", (4.9, -3.6, -0.4), (5.5, 0.2, 6.4), "hide", "LowerTorso"),
        Box("hem-front", (-0.7, -4.3, -0.6), (0.3, -3.4, 6.6), "fur-pale", "LowerTorso"),
        Box("hem-back", (4.7, -4.3, -0.6), (5.7, -3.4, 6.6), "fur-pale", "LowerTorso"),
        # belt of dark hide
        Box("belt", (-0.55, 3.2, -0.55), (5.55, 4.0, 6.55), "leather", "LowerTorso"),
        # fur collar round the neck
        Box("collar", (-0.7, 0.2, -0.7), (3.7, 2.8, 3.7), "fur-pale", "Neck", skip=("up",)),
        # bone toggles down the front
        Box("toggle1", (-0.6, 1.0, 2.8), (-0.3, 1.5, 3.4), "toggle", "UpperTorso"),
        Box("toggle2", (-0.6, 2.6, 2.8), (-0.3, 3.1, 3.4), "toggle", "UpperTorso"),
        Box("toggle3", (-0.6, 4.2, 2.8), (-0.3, 4.7, 3.4), "toggle", "UpperTorso"),
    ]
    for side in ("L", "R"):
        boxes += [
            around("UpperArm" + side, "sleeve-up" + side, upper_arm, 0.3, "hide"),
            around("LowerArm" + side, "sleeve-low" + side, lower_arm, 0.25, "hide", y0=2.4),
            around("LowerArm" + side, "cuff" + side, lower_arm, 0.5, "fur-pale", y0=2.0, y1=3.2),
        ]
    return boxes


def furtrousers():
    boxes = [
        around("LowerTorso", "seat", (5, 5, 6), 0.25, "fur-grey", y1=3.8, faces={"up": "leather"}),
        Box("waistband", (-0.32, 3.2, -0.32), (5.32, 3.9, 6.32), "leather", "LowerTorso"),
    ]
    for side in ("L", "R"):
        boxes += [
            around("UpperFoot" + side, "thigh" + side, (4.2, 9, 3.2), 0.25, "fur-grey"),
            around("LowerFoot" + side, "shin" + side, (4, 10, 3), 0.22, "fur-grey", y0=1.2),
            Box("kneepad" + side, (-0.42, 7.6, 0.4), (-0.18, 10.4, 2.6), "hide-dark", "LowerFoot" + side),
        ]
    return boxes


def furmittens():
    boxes = []
    for side in ("L", "R"):
        p = "LowerArm" + side
        boxes += [
            Box("mitt" + side, (-0.3, -0.4, -0.3), (2.8, 3.4, 2.3), "fur-brown", p, faces={"down": "leather", "west": "leather"}),
            Box("thumb" + side, (-0.8, 0.8, 0.6), (-0.3, 2.4, 1.6), "fur-brown", p),
            Box("mittcuff" + side, (-0.6, 2.9, -0.6), (3.1, 4.4, 2.6), "fur-pale", p),
        ]
    return boxes


def furboots():
    boxes = []
    for side in ("L", "R"):
        p = "LowerFoot" + side
        boxes += [
            Box("boot" + side, (-0.35, 0.2, -0.35), (4.35, 6.0, 3.35), "hide-dark", p, faces={"up": "fur-brown"}),
            Box("toe" + side, (-0.6, 0.2, -0.25), (0.0, 2.0, 3.25), "hide-dark", p),
            Box("sole" + side, (-0.7, -0.35, -0.45), (4.45, 0.3, 3.45), "sole", p),
            Box("bootcuff" + side, (-0.6, 5.6, -0.6), (4.6, 7.2, 3.6), "fur-pale", p),
            Box("lacing" + side, (-0.45, 2.4, 1.1), (-0.3, 5.6, 1.9), "leather", p),
        ]
    return boxes


def snowgoggles():
    p = "Head"
    return [
        # leather strap round the head at eye height
        Box("strap-front-l", (-0.35, 2.0, -0.35), (0.0, 2.9, 0.6), "leather", p),
        Box("strap-front-r", (-0.35, 2.0, 4.4), (0.0, 2.9, 5.35), "leather", p),
        Box("strap-l", (-0.35, 2.0, -0.35), (5.85, 2.9, 0.0), "leather", p),
        Box("strap-r", (-0.35, 2.0, 5.0), (5.85, 2.9, 5.35), "leather", p),
        Box("strap-back", (5.5, 2.0, -0.35), (5.85, 2.9, 5.35), "leather", p),
        # two iron-rimmed cups with smoked lenses
        Box("cup-l", (-0.85, 1.55, 0.6), (-0.05, 3.25, 2.35), "iron-dark", p, faces={"west": "glass"}),
        Box("cup-r", (-0.85, 1.55, 2.65), (-0.05, 3.25, 4.4), "iron-dark", p, faces={"west": "glass"}),
        Box("rim-l", (-0.95, 3.1, 0.5), (-0.05, 3.4, 2.45), "brass", p),
        Box("rim-r", (-0.95, 3.1, 2.55), (-0.05, 3.4, 4.5), "brass", p),
        Box("bridge", (-0.7, 2.3, 2.35), (-0.15, 2.7, 2.65), "brass", p),
    ]


def icepick():
    # laid out like a vanilla pickaxe: shaft along x, head at the far end, points along z
    y0, y1, z0, z1 = 0.0, 1.4, 7.3, 8.7
    return [
        Box("shaft", (1.5, y0, z0), (14.0, y1, z1), "wood"),
        Box("grip", (1.2, y0 - 0.2, z0 - 0.2), (6.5, y1 + 0.2, z1 + 0.2), "leather"),
        Box("ferrule", (0.4, y0 + 0.1, z0 + 0.1), (1.4, y1 - 0.1, z1 - 0.1), "iron-dark"),
        Box("socket", (12.0, y0 - 0.4, z0 - 0.6), (14.8, y1 + 0.4, z1 + 0.6), "iron"),
        # the pick: a long square spike drooping a little
        Box("spike", (12.6, y0 + 0.05, 1.6), (14.2, y1 - 0.05, z0 - 0.6), "iron",
            rot=([13.4, 0.7, z0 - 0.6], "x", -6)),
        Box("spiketip", (13.0, y0 + 0.3, 0.2), (13.8, y1 - 0.3, 1.6), "edge",
            rot=([13.4, 0.7, z0 - 0.6], "x", -6)),
        # the adze: a flat chopping blade on the other side
        Box("adze", (12.4, y0 - 0.1, z1 + 0.6), (14.4, y1 + 0.1, 12.6), "iron",
            rot=([13.4, 0.7, z1 + 0.6], "x", 5)),
        Box("adzeedge", (12.1, y0 - 0.2, 12.6), (14.7, y1 + 0.2, 13.2), "edge",
            rot=([13.4, 0.7, z1 + 0.6], "x", 5)),
    ]


def icesaw():
    # laid out like the vanilla saw: a flat blade along x, lying in the x-z plane
    y0, y1 = 7.45, 7.75
    boxes = [
        Box("blade", (3.0, y0, 9.0), (15.0, y1, 11.4), "iron"),
        Box("bladetip", (15.0, y0, 9.0), (16.0, y1, 10.6), "iron"),
        Box("back", (3.0, y0 - 0.1, 11.2), (15.0, y1 + 0.1, 11.6), "iron-dark"),
        Box("tang", (1.6, y0 - 0.25, 9.6), (3.2, y1 + 0.25, 10.9), "iron-dark"),
        # T handle: a stout wooden crossbar, leather wrapped in the middle
        Box("crossbar", (0.4, y0 - 0.5, 7.0), (1.6, y1 + 0.5, 13.6), "wood"),
        Box("wrap", (0.3, y0 - 0.6, 9.4), (1.7, y1 + 0.6, 11.2), "leather"),
    ]
    # coarse raker teeth along the lower edge
    x = 3.2
    while x < 15.4:
        boxes.append(Box(f"tooth{len(boxes)}", (x, y0, 8.4), (x + 0.6, y1, 9.0), "edge"))
        x += 1.0
    return boxes


def main():
    build("furhood", furhood(), "fshood")
    build("furparka", furparka(), "fsparka")
    build("furtrousers", furtrousers(), "fstrousers")
    build("furmittens", furmittens(), "fsmittens")
    build("furboots", furboots(), "fsboots")
    build("snowgoggles", snowgoggles(), "fsgoggles")
    build("icepick-iron", icepick(), "fsicepick")
    build("icesaw-iron", icesaw(), "fsicesaw")


if __name__ == "__main__":
    main()
