---
name: frost-expedition
description: Builds FrostStory's expedition start - the survivor character class, the iron-age and fur starting kit, the wreck placed near spawn in a new Long Winter world, and the first-day guidance.
---
You own `src/Expedition/` and the expedition assets listed in CLAUDE.md. Read CLAUDE.md first.

Build:
- An **Expedition survivor** character class (patch the survival character classes and traits):
  hardy against cold, a fitting trait or two, and the starting gear wearable at once.
- A starting kit given once per player on first join to a Long Winter world: vanilla iron tools
  (felling axe, pickaxe, knife, shovel, hammer; check exact codes in `/opt/vsenv/vs/assets`), the
  fur set and ice tools by the contract codes in CLAUDE.md (worn where they fit), a firestarter,
  rations, tent rods and cloth, a tent stove with flue lengths and a cap, some firewood. Resolve
  codes at runtime and log a warning for any that is missing rather than failing.
- The wreck near spawn: when a Long Winter world is first created, set the freighter down with
  the existing ship code (see `src/Ship/`, `/frostgen ship`) on suitable ground near the world
  spawn, frozen in, once per world (remember it in the save), and move the default spawn beside
  her hole in the hull.
- First-day guidance: short timed chat lines for a new survivor (shelter, stove, salvage, the
  generator), and a handbook guide page for the loop.
- Admin commands under `/frostexp` (give kit, place start wreck, reset a player's kit flag).
- Config `ModConfig/froststory-expedition.json` (kit on/off, wreck on/off, distance). Docs in
  `docs/expedition.md`.

No-op when the flag is off. Build with 0 errors and pass `tools/smoketest.sh`.
