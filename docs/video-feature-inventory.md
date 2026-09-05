# Video Feature Inventory — Reference Analysis

**Source file:** `Fivem Ingame Clothing Creator Script  Clothing Designer.mp4`
**Duration:** 96.25 s · **Resolution:** 854×480 (30 fps source, downscaled)
**Vendor identified in-frame:** `© 2026 Oresmon Clothing Designer` (footer, all editor frames) / `ORESMON SOFTWARE` (outro, t=90–92)
**Product name in-frame:** `Clothing Designer` (top-left logo)

## 0. Method & honesty notes

ffmpeg and Python are not installed on this machine. Frames were extracted by serving the MP4
over a local HTTP server with byte-range support and seeking an HTML5 `<video>` element to exact
timestamps, capturing each to a canvas, and writing PNGs to disk. **97 frames at 1-second
intervals** were captured, plus 9 magnified crops (4–5×) of dense UI regions. A 10×10 contact
sheet was built to verify no scene was missed.

**Hard limitation:** the source is 480p. Body text in dialogs was recovered by magnifying crops,
but a few strings remain partly unreadable and are marked `[unreadable]` rather than guessed.

**Critical correction to the brief's framing:** the reference product is **not** a desktop
application. Every editor frame carries a FiveM watermark (`FiveM® (b3258) (Beta)`, visible
top-right at t=9, t=27), the UI is overlaid on the running game (t=17 shows the game world
bleeding through a modal backdrop), and at t=34/t=79–88 the same session is a player standing in
a Ponsonbys store. **The reference is an in-game FiveM NUI.** This does not weaken the brief — it
strengthens it, and it changes several architectural conclusions (see `architecture.md` §2).

Evidence frames: [`docs/research/frames/`](research/frames/)

---

## 1. Timeline map

| Time | Segment | Content |
|---|---|---|
| 0–12 s | Showreel montage | Four finished examples cut rapidly: purple/green paisley bandana (`berd_diff_004_a_uni`), Vice City logo hoodie (`jbib_diff_004_a_uni`), camo hat, Purge-style mask (`berd_diff_001_a_uni`) |
| 9–10 s | **Export Project dialog** | Full build-settings modal mid-export |
| 11–13 s | Windows Explorer | Exported resource folder + `stream/` contents (real `.ydd`/`.ytd`/`.ymt`) |
| 14–15 s | **Templates tab** | Female / JBIB browse grid, 14 items, "107 templates loaded" |
| 16–20 s | **AI texture generation** | Prompt dialog → pink Y2K dress result → orbit + back view |
| 21–23 s | Brush painting | White brush on the Purge mask, layers 11–12 |
| 24–29 s | Slots + AI | Ushanka hat template, Slot A/Slot B, second AI generation ("hunter-themed") |
| 30–32 s | Marketing overlay | `Blender ❌  CodeWalker ❌  Sollumz ❌` (spelled "Sallumz" in-frame) |
| 33–34 s | In-game | Player in an apartment, NUI closed |
| 35–42 s | **Quick AI Fit** | Random clothing generator; type dropdown; theme shuffle; navy monogram vest result |
| 43–49 s | Orbit / inspect | Vest rotated, AI layer selected with transform handles |
| 50–56 s | **Saved tab** | 4 snapshots, Continue → "Loading UV canvas…" → project restored with layers intact |
| 57–67 s | **External asset import** | GTA5-Mods download of "Floral Spring Dress for MP Female", `.rar` inspected, `jbib_012_u.ydd` copied into a `female/jbib` library folder |
| 68–78 s | Import verified + edit | Template count 14 → 15, Fill tool + colour picker → yellow, undo, then AI lace texture |
| 79–88 s | **In-game verification** | Clothing menu, Jackets drawable `545`, monogram vest worn, purchased at Ponsonbys ($100) |
| 89–96 s | Outro | ORESMON SOFTWARE logo, black |

---

## 2. Screen inventory

### 2.1 Main editor shell (three-pane)

```
┌───────────────────────────────────────────────────────────────────┐
│ 👕 Clothing Designer            [✨ Quick AI Fit] [💾 Save] [⤓ Export] [✕] │
├───────────────────────────────────────────────────────────────────┤
│ ▸ Select Tool │ Move layer │ Drag to reposition │ Alt + drag pan   │  ← context strip
├──┬──────────────────┬──────────────────────┬─────────────────────┤
│T │ ⟨•⟩ 3D PREVIEW   │ ⊞ EDITOR <texname>   │ LAYERS│TEMPLATES│SAVED│
│O │                  │                      │                     │
│O │   lit garment    │   UV canvas          │   LAYERS (n)        │
│L │   on black       │   + wireframe        │   …                 │
│S │                  │   overlay            │   SLOTS (n)         │
│  │        [62%][🔍][🔍]│         [100%][🔍][🔍]│                     │
├──┴──────────────────┴──────────────────────┴─────────────────────┤
│ ● 108 templates loaded                © 2026 Oresmon Clothing Designer │
└───────────────────────────────────────────────────────────────────┘
```

Both viewports have a collapse chevron and a panel-toggle icon in their header, and an
independent zoom readout + zoom-in/zoom-out buttons bottom-right. Undo/redo arrows sit at the
bottom of the left tool rail.

### 2.2 Left tool rail (9 tools, verified from a 5× crop at t=6)

| # | Icon | Tool | Context strip when active |
|---|---|---|---|
| 1 | cursor | **Select Tool** | `Move layer │ Drag to reposition │ Alt + drag pan` |
| 2 | brush | **Brush Tool** | `Color [swatch] │ Pixel [slider] 55` |
| 3 | eraser | **Eraser** | *(not demonstrated)* |
| 4 | image | **Image / Import** | *(not demonstrated — layers named after `.png` files prove it works)* |
| 5 | `T` | **Text** | *(not demonstrated anywhere in the video)* |
| 6 | paint bucket | **Fill Tool** | `Color │ Click canvas to fill │ Creates full-size layer` |
| 7 | pencil | *(unlabelled)* | *(not demonstrated)* |
| 8 | shapes | *(unlabelled — shape/primitive)* | *(not demonstrated; a layer named "Fill 1 · Rectangle" exists at t=4)* |
| 9 | sparkles | **AI** | opens Generate AI Texture |

### 2.3 Right panel — three tabs

**LAYERS tab.** Collapsible `LAYERS (n)` header. Each row: reorder ▲▼, thumbnail, name,
`<Type> · <opacity>%`, then four actions — 👁 visibility, ⧉ duplicate, ⚙ properties, 🗑 delete.
Selecting a row expands an inline property sheet: filename, type badge, an *info* and a *lock*
button, `GENERAL → Opacity` (numeric + slider), `DIMENSIONS → Width / Height` (numeric).
Observed layer types: **Image**, **Brush**, **Rectangle** (from Fill), **AI Generated Image**.

**TEMPLATES tab.** Search box · `Male / Female` segmented toggle · category chips
`ALL │ JBIB │ LOWER │ FEET │ ACCS │ TEETH` · `Templates — N items` · 3-column thumbnail grid
(grey silhouette renders) · a green dot marks newly-added/selected items · `+ Use Template` CTA
pinned to the bottom.

**SAVED tab.** `Saved snapshots — N items` + bulk-delete 🗑 · `▷ Continue` and `T Rename` action
buttons · card grid, each card: gender badge (`Male`), thumbnail, checkbox, name (`Jbib 004`),
and `<component> <DD/MM/YY, HH:MM>`.

**SLOTS section** (below layers, collapsible, always visible in the LAYERS tab):
`+ Add Slot`; each slot row shows a letter chip (A, B…), `Slot A`, the resolved texture name
(`berd_diff_000_a_uni`), a status badge (`TEMPLATE + EDIT` or `MANUAL`), layer count, and
`T` rename + 🗑 delete. Footer line: `Slot B is ready`. Slot A is annotated
*"Texture name syncs on export."*

### 2.4 Generate AI Texture (modal)

Title `✨ Generate AI Texture — ai image generation`. Prompt textarea. Amber warning:
*"For the best results, write a detailed prompt and clearly describe what should appear on the
front and back areas."* Then the key line:

> "The AI uses the UV skeleton PNG as the exact placement map, draws inside it, and adds the
> result as a movable image layer. Press Ctrl+Enter to submit."

Buttons `✕ Cancel` (red) / `✨ Generate` (green), which becomes `Generating…`.
Placeholder text when empty (t=26): `Describe your design: 'gta5 pink tshirt ...'` *[partly unreadable]*.
On success a toast appears: **"AI image added as a movable layer."**

### 2.5 Quick AI Fit (modal)

Title `✂ Quick AI Fit — random clothing generator`.
- `Character` dropdown → **Male**
- `Clothing Type` dropdown → **ACCS ✓, BEARD, DECL, FEET, JBIB, LOWER, TEETH** (7 entries)
- `RANDOM THEME PROMPT` card — a named theme + description, e.g.
  *Desert Tactical* — "tactical paneling, desert camo influence, utility straps implied through
  graphics, muted sand tones, elite operator mood"; *Luxury Monogram* — "luxury fashion monogram
  pattern, refined embroidery feel, metallic accent details, rich contrast, upscale designer
  energy". A **Shuffle** button re-rolls it.
- `Extra Prompt` textarea, placeholder "Optional extra direction… e.g. gold dragon sleeves,
  cleaner black base, more premium details"
- Explanation: *"We pick a random template from the selected gender and clothing type, activate
  it, then generate a UV-aware AI design with the random theme prompt plus anything you add here."*
- `✕ Cancel` / `✨ Generate Design`
- Result toast: **"Random jbib template activated and AI design added."**

### 2.6 Export Project (modal)

Title `⤓ Export Project — build settings`. Live progress bar `Exporting project files…`.
A full-width green button labelled **`Addon`** (mode selector; no other mode was shown).

| Field | Observed value |
|---|---|
| Project Folder Name | `jbib_004_u` |
| Resource Name | `fcd_jbib_jbib_004` |
| Ped Name | `mp_m_freemode_01` |
| DLC Name | `fcd_m_jbib_jbib_004` |
| Full DLC Name | `mp_m_freemode_01_fcd_m_jbib_jb…` (truncated in field) |
| Component Prefix | `jbib` |
| Drawable Index | `4` |

`Export Slots — Active slot: A` with badge `1 READY`; the slot row shows
`SLOT A` / `jbib_diff_004_a_uni` / `mp_m_freemode_01_fcd_m_jbib_jbib_004^jbib_diff_004_a_uni.ytd`
and status **Ready**. Buttons `✕ Cancel` / `⟳ Exporting`.

### 2.7 Exported output (Windows Explorer, t=11–13)

Path: `txData\QBox_396A00.base\resources\[clothing_designer]\0r-clothing_exports\`
containing `metas/`, `stream/`, `export-index.json`, `fxmanifest.lua`, `README.md`.

`stream/` contents, verbatim, with sizes:

| File | Type | Size |
|---|---|---|
| `mp_m_freemode_01_fcd_m_berd_berd_000.ymt` | YMT | 1 KB |
| `mp_m_freemode_01_fcd_m_berd_berd_000^berd_000_u.ydd` | YDD | 126 KB |
| `mp_m_freemode_01_fcd_m_berd_berd_000^berd_diff_000_a_uni.ytd` | YTD | 2,420 KB |
| `mp_m_freemode_01_fcd_m_jbib_jbib_000.ymt` | YMT | 1 KB |
| `mp_m_freemode_01_fcd_m_jbib_jbib_000^jbib_000_u.ydd` | YDD | 182 KB |
| `mp_m_freemode_01_fcd_m_jbib_jbib_000^jbib_diff_000_a_uni.ytd` | YTD | 5 KB |
| `mp_m_freemode_01_fcd_m_jbib_jbib_004.ymt` | YMT | 1 KB |
| `mp_m_freemode_01_fcd_m_jbib_jbib_004^jbib_000_u.ydd` | YDD | 125 KB |
| `mp_m_freemode_01_fcd_m_jbib_jbib_004^jbib_diff_000_a_uni.ytd` | YTD | 2,026 KB |

This is **real GTA V asset output**, not a stub. Two structural facts follow:

1. Each exported pack contains exactly **one drawable at index 000** (`^jbib_000_u.ydd`) even
   when the pack is *named* `..._004` — i.e. one addon DLC per garment, drawable 0 inside it.
2. The YDD sizes differ per source template (126/182/125 KB) and the YTD sizes vary enormously
   (5 KB vs 2,420 KB). See `asset-format.md` §5 for what this implies about how the tool builds
   the YDD.

### 2.8 In-game verification (t=79–88)

A standard component-based clothing menu (`Clothes` → Mask / Scarf and chains / Jackets / Shirt /
Body armor / Bags and parachute / Hands / Legs / Shoes / Decals, each with Drawable + Texture
steppers). `Jackets` is stepped to drawable **545** of `27/545`; the monogram vest appears on
`mp_m_freemode_01`. The player then buys it at Ponsonbys for $100 (`REMOVED $100 MONEY`,
`Success` notification). This is the loop closing: exported asset → live in game → purchasable.

---

## 3. Feature table

Format per the brief: FEATURE · PURPOSE · INPUT · OUTPUT · UI LOCATION · TECHNICAL IMPLEMENTATION · STATUS.

`Observed` = seen working on screen. `Observed — implementation unknown` = visible result, no
evidence of how. `Inferred` = strongly implied by artefacts but not directly shown.
`Not shown` = the UI affordance exists but was never exercised.

### F-01 Template browser
- **Purpose:** pick the base garment to design on
- **Input:** gender toggle, component chip, search text
- **Output:** activated template → mesh in 3D preview, UV skeleton in editor
- **UI:** right panel → TEMPLATES tab
- **Implementation:** scans a per-gender/per-component library folder of `.ydd` files; counter shown as "N templates loaded" (107 → 108 over the video). Thumbnails are pre-rendered grey silhouettes — **implementation unknown** whether generated on ingest or shipped.
- **Status:** Observed

### F-02 Template ingest from loose YDD files
- **Purpose:** add third-party/purchased clothing to the library
- **Input:** a `.ydd` copied into `…/female/jbib/`
- **Output:** new entry in the Templates grid (14 → 15 items), marked with a green dot
- **UI:** Windows Explorer + Templates tab
- **Implementation:** filesystem drop, no in-app import dialog was used. Whether it hot-reloads or requires a restart was **not shown**.
- **Status:** Observed — implementation unknown

### F-03 3D preview
- **Purpose:** see the garment as the game will show it
- **Input:** active template + composited texture
- **Output:** lit, orbitable 3D render on a dark backdrop
- **UI:** centre-left pane
- **Implementation:** orbit + zoom (62 %/75 %/91 % readouts). **No** pan, no camera presets, no view-mode toggles, no skeleton/wireframe/normals overlay were shown. The mesh appears alone — **no character body, head, hands or other components are rendered**. Renderer unknown.
- **Status:** Observed

### F-04 UV canvas / 2D editor
- **Purpose:** paint on the garment's texture in UV space
- **Input:** pointer input, zoom, `Alt + drag` pan
- **Output:** composited texture applied to the 3D preview live
- **UI:** centre-right pane, titled with the texture name (`jbib_diff_004_a_uni`)
- **Implementation:** the UV *wireframe* is drawn over the canvas as a "UV skeleton" (visible as triangle edges at t=24, t=54). It is a **display + placement guide only** — no UV island selection, no UV vertex editing, no UV transform, no 3D↔UV selection sync was shown.
- **Status:** Observed (view + paint) / **Not present** (UV editing)

### F-05 Brush tool
- **Purpose:** freehand paint
- **Input:** colour swatch, pixel-size slider (observed value 55)
- **Output:** one new layer per stroke, named `Brush 9`, `Brush 10`, `Brush 11`…
- **UI:** tool rail #2 + context strip
- **Implementation:** each stroke becomes a discrete layer with its own opacity + stroke-width + colour properties retained after the fact (t=21 shows `Brush 11` still editable). Notable design choice — strokes are **not** flattened.
- **Status:** Observed

### F-06 Fill tool
- **Purpose:** flood the whole canvas with a colour
- **Input:** colour
- **Output:** a full-size `Rectangle` layer
- **UI:** tool rail #6; strip reads `Click canvas to fill │ Creates full-size layer`
- **Implementation:** explicitly *not* a flood-fill by region — it creates a full-canvas rectangle layer.
- **Status:** Observed

### F-07 Colour picker
- **Purpose:** choose brush/fill colour
- **Input:** SV square, hue slider, eyedropper, R/G/B/A numeric fields
- **Output:** hex colour (`#B00000`, `#fdfdfd` observed)
- **UI:** popover from the context strip
- **Status:** Observed

### F-08 Image layer
- **Purpose:** place a logo/graphic on the garment
- **Input:** an image file (layer named `Grand_Theft_Auto_Vice_City_logo.svg.png`)
- **Output:** movable, resizable image layer; W/H editable numerically (171 × 168 observed)
- **UI:** tool rail #4, layer property sheet
- **Implementation:** the *import gesture* was never shown — no file dialog, no drag-and-drop. Transform handles with a rotate grip are visible at t=45.
- **Status:** Observed — implementation unknown

### F-09 Layer system
- **Purpose:** non-destructive composition
- **Input:** reorder, hide, duplicate, delete, opacity
- **Output:** composited texture
- **UI:** LAYERS tab
- **Implementation:** flat list (no groups/folders shown). **No blend modes, no masks, no adjustment layers** were visible. Max observed: 12 layers.
- **Status:** Observed

### F-10 Texture slots (variations)
- **Purpose:** ship several colourways of one garment
- **Input:** `+ Add Slot`
- **Output:** additional texture names `..._a_uni`, `..._b_uni`, each with its own layer stack
- **UI:** SLOTS section
- **Implementation:** slot letter maps directly onto the GTA texture-variant letter. Badges distinguish `TEMPLATE + EDIT` (started from the template's own texture) from `MANUAL` (blank start). Rename (`T`) and delete supported.
- **Status:** Observed

### F-11 AI texture generation
- **Purpose:** generate artwork onto the garment from a text prompt
- **Input:** free-text prompt
- **Output:** one "AI Generated Image" layer, movable/scalable
- **UI:** tool rail #9 → modal
- **Implementation:** the dialog states the UV skeleton PNG is sent as a placement map and the model draws inside it. **Which provider/model, whether it is image-to-image, inpainting, or ControlNet-style conditioning, is unknown.** No API-key UI, no settings screen, no cost/quota indicator, no regenerate/variation/history controls were shown.
- **Status:** Observed — implementation unknown

### F-12 Quick AI Fit
- **Purpose:** one-click generated garment for people who don't want to design
- **Input:** gender, component, shuffled theme, optional extra prompt
- **Output:** random template activated + AI layer applied
- **UI:** top bar → modal
- **Implementation:** curated named theme presets (a fixed pool — "Desert Tactical", "Luxury Monogram" observed) with a shuffle. Composition of theme + extra prompt is server-side.
- **Status:** Observed — implementation unknown

### F-13 Save / snapshots
- **Purpose:** persist and resume work
- **Input:** `Save` button
- **Output:** a named snapshot card with gender, component, thumbnail, timestamp
- **UI:** top bar + SAVED tab
- **Implementation:** `Continue` restores the full layer stack (`AI Generated Image · Image · 100%` intact at t=56) after a `Loading UV canvas…` step. Storage location unknown — the FiveM context suggests server-side or KVP, not a portable project file. **No Save-As, no versioning, no auto-save indicator, no recovery UI shown.**
- **Status:** Observed — implementation unknown

### F-14 Undo
- **Purpose:** revert an edit
- **Input:** undo arrow at the bottom of the tool rail
- **Output:** yellow fill removed, layer count 1 → 0 (t=72 → t=74)
- **Implementation:** depth unknown; redo arrow present but never used.
- **Status:** Observed

### F-15 Export to FiveM addon resource
- **Purpose:** produce a loadable clothing resource
- **Input:** the build-settings form (§2.6)
- **Output:** a resource folder with `fxmanifest.lua`, `stream/` (`.ymt` + `.ydd` + `.ytd`), `metas/`, `export-index.json`, `README.md`
- **UI:** top bar → modal
- **Implementation:** writes correctly-named RAGE assets to the live server's resources directory. `Addon` is the only mode shown. See `asset-format.md` §5 for the YDD-copy hypothesis.
- **Status:** Observed — implementation partly unknown

### F-16 In-game result verification
- **Purpose:** confirm the export loaded
- **Output:** garment selectable at Jackets drawable 545 and purchasable
- **Implementation:** requires a resource restart/refresh — **not shown**.
- **Status:** Observed

---

## 4. Explicitly NOT in the video

The brief asks for many features; the reference product demonstrates none of the following. They
are **our** additions, not parity items, and must be scoped on their own merits:

Mesh editing of any kind · vertex/edge/face selection · UV island editing, UV transform, UV
unwrapping · 3D↔UV selection sync · skeleton viewer, bone list, weight painting, weight tools ·
character/body preview under the garment · full-outfit or multi-component preview · clipping
detection · a validation panel with green/yellow/red results · material channels
(normal/spec/roughness/metallic/AO) · a settings screen of any kind · AI provider or API-key
configuration · text tool usage · eraser, pencil, shape tools in use · blend modes, layer masks,
groups · drag-and-drop import · project export/import as a portable file · project sharing ·
backup-before-overwrite · thumbnail/preview render generation · multi-monitor, docking, resizable
panels · keyboard shortcuts other than `Ctrl+Enter` · an installer, updater, or licence system ·
progress cancellation · error handling of any kind.

## 5. Video vs. official product description

The brief asks that discrepancies between the video and the official description be reported
separately. **I could not retrieve an official product description.** The `0resmon.tebex.io`
store returns HTTP 403 to automated fetches, YouTube navigation is blocked in this environment,
and searches surfaced only 0RESMON's *other* clothing products (Advanced Clothing v4,
0R-Appearance) — not a "Clothing Designer" listing. Everything in this document is therefore
sourced from the video frames alone.

One in-video **marketing claim** is worth separating from observed behaviour, because it is a
claim and not a demonstration:

> t=30–32, full-screen overlay: `blender ❌   CodeWalker ❌   Sollumz ❌`

The video does demonstrate texture authoring and export without those tools. It does **not**
demonstrate mesh authoring without them — every garment shown originates from a pre-existing
`.ydd`, either shipped as a template or downloaded from GTA5-Mods. The honest reading is
*"no external tools needed to re-skin an existing garment"*, not *"no external tools needed to
create clothing"*.
