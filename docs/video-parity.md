# Video Parity Checklist — v0.2.0

Every row below is checked against
[`video-feature-inventory.md`](video-feature-inventory.md), which was written from
97 frames of the reference video. **Nothing is listed as "in the video" unless a
frame shows it.** Features we added that the video does not demonstrate are
marked `ENHANCEMENT` and are ours to justify on their own merits.

Status key:

| | |
|---|---|
| ✅ | Done, and verified by running the application |
| ◐ | Partly done; the gap is stated |
| ⬜ | Not implemented |
| 🧪 | Implemented but **never validated in a running FiveM client** |

---

## 1. Parity with what the video demonstrates

| Feature | Video ref | Status | Notes |
|---|---|---|---|
| Project creation | F-13 | ✅ | 5-step wizard. The video only shows saving, never creating. |
| Asset / template browser | F-01 | ✅ | Library scan, gender + component filters, grid and list. |
| Template ingest from loose `.ydd` | F-02 | ✅ | Point the library at a folder; rescan picks up new files. |
| Clothing (component) selection | F-01 | ✅ | Real 12-slot component grid from the verified enum. |
| Drawable selection | §2.8 | ✅ | Stepper plus ← / → keys. |
| 3D preview | F-03 | ✅ | Real geometry, orbit / pan / zoom, six camera presets. |
| UV canvas / wireframe overlay | F-04 | ✅ | Real UV islands; zoom, pan, grid, texture overlay. |
| Texture editing (paint on the UV canvas) | F-04 | ✅ | Live 3D preview while painting. |
| Brush tool | F-05 | ✅ | Size, softness, colour. Strokes stored as data. |
| Eraser | §2.2 #3 | ✅ | Not demonstrated in the video; implemented anyway. |
| Fill tool | F-06 | ✅ | Full-canvas layer, or bounded by an active selection. |
| Colour picker | F-07 | ✅ | HEX / RGB / HSV, preset palette, recents, eyedropper. |
| Image / logo import | F-08 | ✅ | PNG / JPG / BMP / WEBP, transparency preserved. |
| Text tool | §2.2 #5 | ✅ | Font, size, bold, italic, colour, outline, shadow, spacing, align. |
| Transform (move / scale / rotate) | F-08 | ✅ | On-canvas handles plus numeric fields; flip H / V. |
| Layer system | F-09 | ✅ | Reorder, hide, lock, duplicate, rename, delete, opacity. |
| Layer blend modes | — | ✅ `ENHANCEMENT` | 16 modes. The video shows none. |
| Layer groups | — | ✅ `ENHANCEMENT` | The video shows a flat list only. |
| Texture variations ("slots") | F-10 | ✅ | Mapped onto GTA variant letters a–z. |
| Save project | F-13 | ✅ | Folder project plus portable `.bitirimclothing`. |
| Load / continue project | F-13 | ✅ | Recent list, file dialog, drag & drop, file association. |
| Undo | F-14 | ✅ | Command stack; labelled ("Undo Brush stroke"). |
| Redo | §2.1 | ✅ | The video shows the button but never uses it. |
| Export to a FiveM addon resource | F-15 | 🧪 | Real `.ydd`/`.ytd`/`.ymt` + `fxmanifest.lua`, re-parsed after writing. |
| Export settings form | §2.6 | ✅ | 5-step wizard; every field the video shows, plus manifest mode. |
| In-game verification | F-16 | ⬜ | **The gate that has not moved.** No export has been loaded by a client. |
| AI texture generation | F-11 | 🟨 | Gemini 3.1 Flash Image, with the UV layout and mask as placement maps. Not yet run against a live key. |
| Quick AI Fit | F-12 | ⬜ | Depends on the above. |

### Deliberately not copied

| Video behaviour | Decision |
|---|---|
| Runs as an in-game FiveM NUI | We are a desktop application. Stated in `architecture.md` §2. |
| Vendor's exact UI layout and colours | Own design; the workflow is the parity target, not the pixels. |
| "Blender ❌ CodeWalker ❌ Sollumz ❌" marketing claim | It is a claim, not a demonstrated capability. We do not repeat it. |

---

## 2. Enhancements — ours, not the video's

None of these appear anywhere in the reference video (§4 of the inventory lists
them as absent). They are here because a development tool needs them.

| Feature | Status | Notes |
|---|---|---|
| Multi-garment projects | ✅ | One project, several garments, one exported resource. |
| Validation panel with categories and severities | ✅ | Clickable findings that navigate to the cause. |
| Export presets | ✅ | Resource · texture pack · project archive · development package. |
| Export plan preview | ✅ | Exact file names, produced by the same code that writes them. |
| Export history | ✅ | Per project, with status and file count. |
| Crash recovery | ✅ | Snapshot per tick; offered only when it differs from disk. |
| Project schema migration | ✅ | v0.1 opens in v0.2 with the original kept under `backups/`. |
| Real rendered thumbnails | ✅ | Software rasteriser over actual drawable geometry. |
| Thumbnail cache | ✅ | Keyed on size + mtime; cleared from Settings. |
| Favourites, sorting | ✅ | Name / date / component / drawable / size. |
| Global search (Ctrl+P) | ✅ | Drawables, projects, garments, variations. |
| Keyboard shortcuts, rebindable | ✅ | Table in Settings; clashes flagged. |
| Material inspector | ✅ read-only | Real shader and texture bindings; `—` for anything absent. |
| Raw asset inspector | ✅ | Parsed structure plus file hash and container magic. |
| Hex viewer | ✅ read-only | Developer mode. Paged, searchable, cannot write. |
| Log console with filters | ✅ | Level filter, text filter, copy, open folder. |
| Preview modes | ✅ | Material · solid · wireframe · UV checker · normals. |
| Orthographic camera, FOV | ✅ | Shared orbit state between projections. |
| Bounding box, vertex normals overlay | ✅ | Normals overlay is developer mode. |
| Character / outfit model | 🧪 | Outfits are stored. **Nothing renders a dressed character.** |
| Selection tools (marquee, lasso) | ✅ | Recorded on the stroke, so replay is exact. |
| Shape tools (line, rectangle, ellipse) | ✅ | Filled or outlined. |
| Gradient layers | ✅ | Linear and radial. |
| Panel collapse / resize / fullscreen viewport | ✅ | Persisted between sessions. |
| Theme, interface scale | ✅ | Dark / light / system; 75 %–200 % scale for 4K. |

---

## 3. Still not implemented

Named, not hidden.

| Feature | Why |
|---|---|
| In-game FiveM validation | Blocker **B1**. Needs a running server; the user deferred it. |
| AI generation backend | Implemented for Gemini only. Untested against a live key by this build; variations, regenerate history and "quick fit" are not built. |
| Mesh editing | Gated on a working `.ydd` geometry writer. See below. |
| UV editing | Same gate: a moved UV vertex could not be saved. |
| Weight painting | Same gate. |
| Cloth (`.yld`) authoring | No library models the format. |
| Clipping detection between components | Heuristics are not detection; we will not sell them as such. |
| Rendered character preview | Needs body meshes, skeleton and per-component draw order. |
| Update system, licensing | Out of scope for this build. |

**The `.ydd` writer stays disabled.** Round-tripping a skinned ped drawable
through the backend preserves geometry, skinning, materials and embedded
textures, but recomputes the bounding volume incorrectly — the garment then
culls at the wrong distance. Export copies the drawable byte-for-byte instead,
and `capabilities.writeYdd` is reported `false`, which is what makes the mesh,
UV and weight tools structurally unofferable rather than merely disabled.
See [`known-limitations.md`](known-limitations.md).
