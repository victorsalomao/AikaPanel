# Aika Item Icon Mapping — Research Notes

> ## SESSION 2 UPDATE (2026-06-26) — SOLUTION SHIPPED (browser + growable map)
>
> **What was built (works now, zero client risk — all read-only on client files):**
> - `IconService.cs` in AikaPanel decodes `UI\ItemIconsNN.jit` (JT31/33/35) to **transparent PNG sprite-sheets**
>   in memory and serves them; holds a persistent `IconID → [atlas, cell]` map.
> - Endpoints: `GET /api/iconmeta`, `GET /api/iconmap`, `GET /api/iconsheet/{atlas}` (PNG),
>   `GET /api/iconcell/{atlas}/{cell}` (reverse lookup), `POST/DELETE /api/iconmap/{iconId}` (link/unlink, auth).
>   Icon **reads** are exempt from the auth middleware so `<img>` tags work.
> - Frontend: new **"Ícones" tab** = full atlas browser (click a cell → see/edit its IconID link);
>   **per-item sprites** in the Itens list; item-edit modal shows the icon + **"Escolher do jogo…"** picker
>   (click an icon to set the item's IconID, or teach the map for the current item).
> - Map persisted to `UI\iconmap.json`. Seeded with the **proven A–Z run** (IconIDs 4113–4138 → atlas8 cells 145–170).
> - Decoder tool `tools/jit2png` gained `RAW=1` (raw RGB dump) and `ALPHA=1` (transparent RGBA PNG) modes.
> - Published: `publish/AikaPanel.exe` (password `aika123`, http://127.0.0.1:8099).
>
> **Mapping structure — now PROVEN precisely (was "best guess" before):** it is a **segment table**. Each segment is
> **IconID-contiguous AND cell-contiguous** (`cell = IconID − offset_k` inside segment k), offset resets at batch
> boundaries. Verified on atlas08: "Cristal da Lua/Estrela" + "Amistad" (IconIDs 4110/4111/4112) sit exactly at cells
> 142/143/144, immediately before A=cell145. **Gaps (unused IconIDs) appear to gap-compress**, so you CANNOT bulk-add a
> range that contains an unused IconID without per-cell verification (cells 171–186 only loosely matched 4139–4154
> because 4141/4145 are unused). The gap-free A–Z run is the only segment safely auto-mappable.
>
> **Newly ruled out this session (Node-only, no disassembler needed):**
> - `ItemIcons` string absent from EXE (ASCII+UTF16) AND from every UI data file <20MB → filename built dynamically.
> - No per-IconID **cell array** (zero monotonic +1 u16 runs in EXE) and no per-IconID **atlas array**.
> - No segment-record table findable as data (4113 never co-locates with 145).
> - `ItemList` record has **no** atlas/page field (checked all 464 bytes around two known items).
> - `ItemIndex.csv` / `Itemlist*.csv` are NOT shipped (dev/build-time). `TextureSet.bin` = UI-skin coords (no item keys).
>   `ReliqueIcon.bin` = relic→item map. No icon-coordinate file exists in the client.
> - **Atlas10 is JT20** (2048×2048, 64×64 cells) — still undecoded; deferred. Atlases 06/07 carry mips (same 1024 cells).
>
> **To GROW the map (3 options, in order of leverage):**
> 1. **Reverse-engineer AIKA.exe** (Ghidra/IDA — user authorized). Target = the segment table / UV-compute routine.
>    It is NOT a simple data table; likely computed in compiled code. This is the only route to a COMPLETE map.
> 2. **Runtime capture** (RenderDoc/Frida) of IconID→texture+UV while opening in-game inventories.
> 3. **Guided empirical**: per-atlas, render strips and verify against ItemList consecutive-IconID runs, bulk-add
>    each verified gap-free segment to `iconmap.json`. Safe but laborious; ~10,000 cells, batch boundaries invisible.
> The UI itself is a map-building tool: click cell → enter IconID → vincular.

---


**Goal:** map an item's `IconID` (UInt16 @ offset 320 in `ItemList.bin`, 464‑byte records) to the
exact in‑game icon cell inside the `UI/ItemIcons01..11.jit` atlases, so a web panel can render the
correct icon per item.

**Status: NOT SOLVED (no closed‑form formula).** But the search space is now *heavily* narrowed and
several arithmetic models are conclusively ruled out with visual proof. The real structure looks
**piecewise‑linear with arbitrary per‑segment offsets** (see "Best current model" below) — i.e. it
needs a lookup/segment table, not a single equation.

---

## TL;DR for the next person

- `atlas = IconID / 1024`, `cell = IconID % 1024` (row‑major) — **WRONG, proven.**
- Any *global* linear index (row‑major, column‑major, 0/1‑based, placeholder offset) — **WRONG.**
- A single *constant per‑atlas base* (`cell = IconID − baseAtlas`) — **WRONG** (fails within one atlas).
- Within an atlas, icons that were **added in the same batch are placed contiguously, row‑major**
  (verified: the 26 "Letra do Enigma A..Z" tiles, IconIDs 4113‑4138, sit in atlas08 as a clean
  contiguous raster run). **But different batches in the same atlas use different, unrelated offsets**,
  and batches are **not** ordered by IconID. So the mapping is a set of segments
  `(IconID_start, atlas_file, cell_start, count)` — that segment table is the missing artifact.
- The atlas filenames `ItemIcons01..11.jit` **do not appear anywhere in `AIKA.exe`** (ASCII or UTF‑16)
  and in no data file — the loader builds them at runtime / from a packed resource. So the segment
  table is almost certainly inside `AIKA.exe` (needs a real disassembler — none installed) or a packed
  resource.

---

## Atlas inventory (decoded headers)

All under `C:\Users\user\Downloads\AnotherAikaDelphi\AIKAClient\UI\`.
Format: magic @0, int32 width @4, int32 height @8, DXT blocks @12. `JT31`=DXT1, `JT33`=DXT3, `JT35`=DXT5.

| File | Magic | WxH | Notes |
|---|---|---|---|
| ItemIcons01 | JT33 (DXT3) | 1024×1024 | 32×32 cells. Cell 0 = "Now Printing" placeholder. |
| ItemIcons02 | JT33 | 1024×1024 | |
| ItemIcons03 | JT33 | 1024×1024 | |
| ItemIcons04 | JT33 | 1024×1024 | |
| ItemIcons05 | JT35 (DXT5) | 1024×1024 | |
| ItemIcons06 | JT33 | 1024×1024 | file is larger → contains mip levels after main image (harmless: decoder reads main first). |
| ItemIcons07 | JT35 | 1024×1024 | mip levels too. |
| ItemIcons08 | JT35 | 1024×1024 | |
| ItemIcons09 | JT35 | 1024×1024 | |
| **ItemIcons10** | **JT20** | header reads w=0x00020000 (bogus under the JT3x layout) | **Different format** — file is 4,194,334 B ≈ 4 MB (likely uncompressed 1024×1024 RGBA or a 2048² sheet). **The jit2png tool can NOT decode JT20** (it falls through to the DXT3 path and produces garbage). Needs separate handling. |
| ItemIcons11 | JT35 | 1024×1024 | |

Each 1024×1024 atlas = 32 columns × 32 rows = **1024 cells of 32×32 px**, row‑major within the image
(top‑left = cell 0). Confirmed visually.

## IconID stats (from `ItemList.bin`, 31000 records)

- Records: 31000. Items with `IconID > 0`: 15119. **Max IconID = 6391.**
- `IconID/1024` buckets used: 0..6 only. → a naive `/1024` model can never reference atlases 08‑11,
  yet those atlases are full of real item art and are recent (2023‑2024). Another nail in the
  `/1024` coffin.
- Offset **322** (the UInt16 right after IconID) = **max stack size** (values 1 / 1000 / 9999), **not**
  an atlas/page field. No atlas/cell/uv field was found adjacent to IconID in the record.

---

## What was tested and ruled out (with evidence)

Anchor items (name → IconID, read from `ItemList.bin`):

| Item | IconID |
|---|---|
| Pedra da Pran (summon stone, run 1126‑1129) | 1126 |
| Pena Pérola Branca (white feather) | 36 |
| Pena Pérola Negra (black feather) | 75 |
| Maharaja de Ferro | 25 |
| Poção HP de Batalha (red potion) | 2418 |
| Grande Poção de HP | 1519 |
| Diamante | 1896 |
| Bala de Rifle – Categoria F..SSS (run) | 338‑347 |
| Letra do Enigma [A..Z] (run) | 4113‑4138 |
| Conquistadora costume set (run) | 3968‑3993 |

1. **`atlas=IconID/1024`, `cell=IconID%1024` row‑major.**
   Pran stones 1126‑1129 → atlas02 (index 1), cells 102‑105 (row3 cols6‑9). Cropped that region:
   it is **weapons/armor**, not summon‑stones. WRONG (matches the prior failed attempt).

2. **Raster index into atlas01 for low IconIDs.**
   IconID 36 = "Pena Pérola Branca" (a white feather; *every* item with IconID 36 is a Pena feather).
   atlas01 cell 36 (row1 col4) = **a sword**. So IconID is not a raster cell index. The apparent
   "match" of basic blades (IconIDs 1‑8) to atlas01 row0 was coincidence — atlas01 row0 is entirely
   weapons, so anything landing there "looks right".

3. **`/1024` selects atlas at all.** The "Letra do Enigma A..Z" run (IconIDs 4113‑4138) is physically
   located in **ItemIcons08**, but `4113/1024 = 4` would point at ItemIcons05. So atlas ≠ IconID/1024.

4. **Constant per‑atlas base (`cell = IconID − baseAtlas`, row‑major).**
   The A..Z alphabet in atlas08 lays out as a *perfect contiguous raster run* (A at row4 col17 = cell
   145; O at row4 col31 = cell 159; P wraps to row5 col0 = cell 160 …). That forces `base = 3968` for
   that segment. But under `base=3968`, atlas08 row0 (cells 0‑31) should be the Conquistadora costume
   set (IconIDs 3968‑3993). The crop of atlas08 row0 cols14‑17 (where a sword, "Lâmina da Bravura"
   IconID 3985, is predicted) shows **uniform purple winged "core" icons — no sword**. So a single base
   does not hold across the atlas: **different IconID batches in the same atlas use different offsets.**

5. **Column‑major, 0‑/1‑based, placeholder off‑by‑one variants** — all fail the same anchors above.

---

## Best current model (piecewise‑linear segments)

Evidence points to: the client loads icons in **batches** (one batch ≈ a content patch / item group).
Each batch is blitted into a free rectangular run of cells in whichever atlas had room at the time,
**row‑major and contiguous within the batch**. So:

```
cell_in_atlas(IconID) = cell_start_of_segment + (IconID − IconID_start_of_segment)
atlas(IconID)         = atlas_file_of_segment
```

where segments `(IconID_start, count, atlas_file, cell_start)` are defined by an external table that is
**not** ordered by IconID and is **not** a clean `/1024` partition.

Confirmed segment (one fully‑pinned example):
- **Segment:** IconIDs 4113‑4138 ("Letra do Enigma A..Z") → **ItemIcons08**, cells **145‑170**
  (A = row4 col17 … Z = row5 col10). Verified visually letter‑by‑letter.

This is the structural key: if the segment table can be recovered, the panel is solved.

---

## Per‑atlas content (rough categories, from full‑res decode)

Useful to sanity‑check guesses; atlases are mixed, roughly chronological batches, NOT category‑pure:

- **01** — weapons (rows 0‑6: swords/daggers/spears/axes/guns/bows), shields & armor (mid), then
  gems/orbs, potions, materials, gift boxes lower down. Cell 0 = "Now Printing".
- **02** — mostly weapons & armor (gauntlets/boots/shields), darker set.
- **03** — graded gem/orb rows (red orbs, blue orbs, white **diamonds** graded F‑E‑D‑C‑B‑A‑S‑SSS),
  coins, red potions, CJK/"CLEAR"/"LUCK"/"WAR" text tiles, ores.
- **04** — armor/weapons, costumes, jewelry (P+ rings), purple gear, gems, potions.
- **05** — **fishing** content up top (fish/eel/pufferfish/squid), then food, armor sets, gems.
- **06** — heavy mix: feathers/wings, gems, potions, cards, accessories, costumes, grade rows
  (AA/S/SSS overlays).
- **07** — mix (not individually catalogued).
- **08** — costumes ("Conquistadora", "Seifuku", "Copa"), Elter set, **A‑Z + AMOR letter tiles**,
  cores, recipes, event/Valentine/Christmas items, jewelry.
- **09** — coins, medals, costumes, jewelry, gems, potions, pumpkins/Halloween, grade rows.
- **10** — **JT20, not decodable by current tool.**
- **11** — costumes, gems, gift boxes, scrolls, hearts/roses, grade rows.

---

## Consecutive‑IconID runs (calibration goldmine)

`ItemList.bin` contains many runs where consecutive records have `IconID+1` and visually‑related names.
These are the best probes because a batch shows up as a contiguous strip in an atlas. High‑value runs:

- 4113‑4138 "Letra do Enigma A..Z" (26 letter tiles) ← already pinned to atlas08 cells145‑170.
- 4577‑4580 "Letra: A M O R" (red letters).
- 169‑186 "…Sagrado Selado" (18 holy items: chalice/spear/torch/tunic/sword/staff/statue/holy‑water/axe/shield…).
- 321‑333 ingots/metals (silver ingot, mithril, agiotita…).
- 338‑347 "Bala de Rifle – Cat. F..SSS" & 487‑496 "Bala de Pistola" (graded cartridges).
- 1355‑1364 "Hira – Cat. F..SSS", 1379‑1388 "Kaize", 1200‑1233 "Reparador…" (graded crystals/hammers).
- 1126‑1129 "Pedra da Pran" summon stones; 541‑547 personality Pran stones.
- 4271‑4279 baking set (dragon eggs/flour/milk/chocolate/cake) — Halloween/Christmas batch.
- 4112‑4138, 4239‑4262, 4292‑4316, 4321‑4330, 4361‑4372, 4517‑4761, 4949‑5021 … (atlas08/09 batches).

To turn these into the segment table: locate each run visually in the atlases (they are distinctive
strips), record `(atlas, cell_start)`, and store the segment. Doing this for all runs builds a complete
empirical `IconID → (atlas, cell)` table without needing the EXE.

---

## Other files examined

- **`UI/ReliqueIcon.bin`** (2048 B) = 128 records × 4 int32. col0 ranges 0‑25199 (looks like an item
  *record index*, we have 31000 items), cols1‑3 up to ~5475‑5483. This is a **relic→item** mapping,
  NOT an item‑icon coordinate table. It does prove the engine uses explicit binary lookup tables for
  icon‑adjacent data, so an item‑icon segment/coord table plausibly exists somewhere too.
- **`UI/ItemList4.bin`** (14,384,016 B, header `BR00022I…`) = the *client‑side* item list (server copy
  is `Bin/Data/ItemList.bin`, 14,384,004 B, blank record 0). Same 464‑byte record layout → same IconID
  field → does not by itself add icon‑position info.
- **`AIKA.exe`**: NO occurrence of `ItemIcons`, `Icons`, `Icon%`, or any `*Icons*.jit` literal (ASCII or
  UTF‑16). The only `.jit` format strings present are for environment/terrain tiles
  (`Env/.../TTile%02d.jit`, `Texture\%s%02d%02d%02d.jit`, `GuildMarkWorld%d.jit`). So the item‑icon
  atlas list is built another way (packed resource / computed). `strings`/`ghidra`/`ida` are NOT
  installed; only `dotnet`, `node`, `perl`, and `grep -a` are available — heavy static RE was not
  feasible.

---

## Most promising lead + concrete next steps

1. **Recover the segment table from the EXE.** Install Ghidra/IDA (or use `radare2`/`dotnet`‑based PE
   tooling). Find the icon loader: it does not reference the filename literally, so search for the
   integer constants 1024 / 0x400, the DXT magic, or the place that takes `IconID` and produces a
   texture + UV. The table of `(IconID_start, atlas, cell_start, count)` (or `(IconID → atlas, x, y)`)
   should be near it. This is the deterministic solve.
2. **Or build the table empirically (no RE needed).** Programmatically locate each consecutive‑IconID
   run (list above) in the atlases — batches are distinctive strips (graded rows, letter tiles, costume
   sets) — and record `(atlas, cell_start)` per segment. With ~a few hundred runs this covers the bulk
   of the 6392 used IconIDs. Gaps can be filled by interpolation within a segment.
3. **Handle ItemIcons10 (JT20) separately** — extend the decoder for the JT20 format (probably
   uncompressed RGBA or a 2048² sheet) before it can be searched.

---

## How to crop a single cell (reproducible recipe)

Decoder: `C:\Users\user\Downloads\AikaPanel\tools\jit2png` (C#, no deps).
It composites alpha over **white**, so transparent icon backgrounds read as white.

```bash
TOOL="C:/Users/user/Downloads/AikaPanel/tools/jit2png"
UI="C:/Users/user/Downloads/AnotherAikaDelphi/AIKAClient/UI"

# Whole atlas → PNG (full res):
dotnet run -c Release --project "$TOOL" -- "$UI/ItemIcons08.jit" out.png

# Whole atlas → downscaled overview (e.g. 512 px):
dotnet run -c Release --project "$TOOL" -- "$UI/ItemIcons08.jit" ov.png 512

# Crop a region via CROP=x,y,w,h (FULL-RES pixel coords). Cells are 32 px.
#   cell (row, col):  x = col*32 , y = row*32 , w = 32 , h = 32
# Example: atlas08 cell 145 = row4 col17  →  x=544, y=128
CROP=544,128,32,32 dotnet run -c Release --project "$TOOL" -- "$UI/ItemIcons08.jit" cell145.png

# Tip: the image Read tool downscales wide images. To read icons clearly, crop a
# WHOLE ROW (0, row*32, 1024, 32) and count letters/icons against both edges, or crop
# small 4-8 cell windows (e.g. CROP=448,0,192,32). Single 32x32 cells render very small.
```

Read IconID of any item (Node; Python is only the Windows Store stub):

```js
const fs=require('fs');
const f=fs.readFileSync('C:/Users/user/Downloads/AikaMerged/Bin/Data/ItemList.bin');
const REC=464; // record i: name@0 (cp1252,64B), nameEN@64, desc@128, IconID=u16LE@320, maxStack=u16LE@322
const i=100, o=i*REC;
console.log(f.readUInt16LE(o+320)); // IconID
```

Scratch PNGs from this session: `C:\Users\user\AppData\Local\Temp\claude\icons\`.
