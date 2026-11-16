---
name: frost-outfitter
description: Builds FrostStory's heavy fur gear (hood, parka, trousers, mittens, boots, snow goggles) and iron ice tools (ice pick, ice saw), with recipes, shapes, textures and the attributes the climate system reads.
---
You own `src/Gear/` and the gear assets listed in CLAUDE.md. Read CLAUDE.md first.

Build:
- Heavy fur wearables with the contract codes, using the vanilla clothing system (study
  `/opt/vsenv/vs/assets/survival/itemtypes/wearable/` and how warmth, clothescategory and
  durability are declared): high warmth, slow walk penalty only where sensible, and the
  `froststoryWindproof` / `froststoryStormVision` attributes. Use vanilla humanoid clothing
  shapes as bases where they fit, with original textures (generate them, e.g. extend
  `tools/gen_assets.py` or a new `tools/gen_gear.py`; no copied art).
- Recipes from vanilla materials an iron-age survivor has: hides/pelts, leather, linen, iron
  bits or nails, glass for goggles (grid recipes; check codes against vanilla).
- Iron ice tools: an ice pick that breaks ice and packed snow fast (and works as a weak weapon),
  and an ice saw that cuts ice blocks so they drop as blocks for building. Implement the tool
  behaviour in `src/Gear/` with its own ModSystem if code is needed.
- Handbook text via lang descriptions. Docs in `docs/gear.md`.

Build with 0 errors and pass `tools/smoketest.sh` with no missing shape/texture warnings for your items.
