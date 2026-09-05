# Feature Matrix

**Video** — was it demonstrated in the reference video? (`✅` yes · `—` no · `~` partial)
**Official** — in the vendor's official description? **Not retrievable** (see
`video-feature-inventory.md` §5); the column is left `?` throughout rather than invented.
**Priority** — P0 core · P1 important · P2 advanced · P3 future.
**Complexity** — S / M / L / XL (XL = multi-month or research-gated).
**Status** — this column was written during Phase 0 planning and is **not maintained**.

> **For current status, read [video-parity.md](video-parity.md).** It is checked against the
> running application feature by feature, and it separates parity with the reference video from
> our own additions. This matrix stays as the Phase 0 scoping record: it is useful for the
> priority and complexity estimates, not for what is built.

## A. Shell, projects, persistence

| Feature | Video | Official | Req | Pri | Cx | Implementation | Status |
|---|---|---|---|---|---|---|---|
| Windows desktop app (.exe, installer, portable) | — | ? | Yes | P0 | M | .NET 8 host + WebView2; WiX installer | Not started |
| Dark professional three-pane shell | ✅ | ? | Yes | P0 | M | React + TS | Not started |
| Resizable / dockable panels | — | ? | Yes | P1 | M | layout engine in shell | Not started |
| Start screen (New / Open / Recent / Library / Settings) | — | ? | Yes | P0 | S | — | Not started |
| New Project wizard (gender → component → base asset) | — | ? | Yes | P0 | S | — | Not started |
| Save / Save As / Open / Recent | ~ (Save only) | ? | Yes | P0 | M | folder-based project | Not started |
| Auto-save + crash recovery | — | ? | Yes | P1 | M | periodic snapshot + journal | Not started |
| Project backups & history timeline | — | ? | Yes | P2 | M | timestamped copies | Not started |
| Portable `.bitirimclothing` package | — | ? | Yes | P2 | S | zip + manifest, data only | Not started |
| Undo / redo (Ctrl+Z / Ctrl+Y) | ~ (undo only) | ? | Yes | P0 | M | command pattern | Not started |
| Settings (general, paths, editor, 3D, AI, export, FiveM) | — | ? | Yes | P1 | M | settings store + DPAPI | Not started |
| Logging (`application`/`errors`/`export`) + debug console | — | ? | Yes | P1 | S | — | Not started |
| Update system | — | ? | No | P3 | M | — | Not started |
| Licence / activation | — | ? | No | P3 | M | architecture leaves room; not built | Not started |

## B. Asset library & browsing

| Feature | Video | Official | Req | Pri | Cx | Implementation | Status |
|---|---|---|---|---|---|---|---|
| Template/asset browser, gender + component filter | ✅ | ? | Yes | P0 | M | library folder scan | Not started |
| Grid / list view, search, sort | ~ (grid + search) | ? | Yes | P1 | S | — | Not started |
| Favourites, recent, custom tags | — | ? | Yes | P2 | S | SQLite metadata | Not started |
| Thumbnail generation + cache (hash + mtime invalidation) | ~ (thumbnails shown) | ? | Yes | P1 | M | offscreen render → SQLite cache | Not started |
| Ingest loose `.ydd` from a folder | ✅ | ? | Yes | P0 | M | watcher + parse on ingest | Not started |
| Drag & drop import (ydd/ytd/png/jpg/dds/project/folder) | — | ? | Yes | P1 | M | WPF drop target | Not started |
| Vanilla vs. add-on distinction, collections | — | ? | Yes | P2 | S | metadata | Not started |
| Explorer integration (reveal, copy path, open folder) | — | ? | Yes | P2 | S | — | Not started |

## C. 3D viewport

| Feature | Video | Official | Req | Pri | Cx | Implementation | Status |
|---|---|---|---|---|---|---|---|
| Load a real `.ydd` and display its mesh | ✅ | ? | Yes | P0 | L | sidecar parse → mesh DTO → Three.js | Not started |
| Orbit / zoom | ✅ | ? | Yes | P0 | S | — | Not started |
| Pan, ortho/persp, axis views, camera presets | — | ? | Yes | P1 | S | — | Not started |
| Live texture update from the 2D editor | ✅ | ? | Yes | P0 | M | shared texture buffer | Not started |
| Display modes: wireframe / solid / material / texture | — | ? | Yes | P1 | M | — | Not started |
| Skeleton & bone display | — | ? | Yes | P2 | L | requires skeleton parse | Not started |
| Weight visualisation | — | ? | Yes | P2 | L | requires skinning data | Not started |
| Normals / vertex display | — | ? | Yes | P3 | M | — | Not started |
| Character body under the garment | — | ? | Yes | P1 | L | load freemode base drawables | Not started |
| Full outfit / multi-component preview | — | ? | Yes | P2 | L | multi-drawable scene | Not started |
| Clipping / compatibility checks | — | ? | Yes | P3 | XL | needs a real intersection approach; do not promise heuristics as detection | Not started |

## D. Texture editor

| Feature | Video | Official | Req | Pri | Cx | Implementation | Status |
|---|---|---|---|---|---|---|---|
| UV canvas with UV wireframe overlay | ✅ | ? | Yes | P0 | M | UV from parsed mesh | Not started |
| Layer stack (reorder, hide, duplicate, delete, opacity) | ✅ | ? | Yes | P0 | M | — | Not started |
| Layer property sheet (opacity, W/H, lock) | ✅ | ? | Yes | P0 | S | — | Not started |
| Brush tool (colour, size) | ✅ | ? | Yes | P0 | M | — | Not started |
| Eraser | ~ (tool present, unused) | ? | Yes | P1 | S | — | Not started |
| Fill tool + colour picker (HEX/RGB/HSV, eyedropper) | ✅ | ? | Yes | P0 | S | — | Not started |
| Image import (drag & drop, PNG alpha) + transform | ~ (result only) | ? | Yes | P0 | M | — | Not started |
| Text tool (font, size, style, align, outline, shadow) | ~ (tool present, unused) | ? | Yes | P1 | M | — | Not started |
| Shapes | ~ (a `Rectangle` layer exists) | ? | Yes | P2 | S | — | Not started |
| Blur / smudge / clone | — | ? | No | P3 | L | — | Not started |
| Blend modes, layer masks, groups | — | ? | No | P2 | M | — | Not started |
| Import PNG/JPG/WEBP/TGA/DDS | — | ? | Yes | P1 | M | DDS decode via sidecar | Not started |
| Export texture (PNG/DDS) | — | ? | Yes | P1 | S | — | Not started |

## E. UV editor

| Feature | Video | Official | Req | Pri | Cx | Implementation | Status |
|---|---|---|---|---|---|---|---|
| UV layout display, zoom, pan | ✅ | ? | Yes | P0 | M | — | Not started |
| UV island detection + colouring | — | ? | Yes | P2 | M | connected-component over UV topology | Not started |
| 3D ↔ UV selection sync | — | ? | Yes | P2 | L | shared selection model | Not started |
| UV transform (move/scale/rotate/mirror/snap) | — | ? | Yes | P3 | XL | **requires YDD geometry writing** — gated | Not started |
| Checker texture, grid | — | ? | Yes | P2 | S | — | Not started |

## F. Mesh / skeleton / weights

Everything in this section is gated on proven YDD **write** support. Until then these features must
not appear in the UI at all (`architecture.md` §7, capability gating).

| Feature | Video | Official | Req | Pri | Cx | Implementation | Status |
|---|---|---|---|---|---|---|---|
| Mesh inspection (counts, LODs, materials, shader) | — | ? | Yes | P1 | M | read-only; safe to ship early | Not started |
| Vertex/edge/face selection | — | ? | Yes | P3 | XL | gated | Not started |
| Transform tools (G/R/S), mirror, symmetry, proportional | — | ? | Yes | P3 | XL | gated | Not started |
| Skeleton viewer, bone list & search | — | ? | Yes | P2 | L | read-only | Not started |
| Weight display per vertex | — | ? | Yes | P2 | L | read-only | Not started |
| Weight painting + tools (normalize/smooth/mirror/copy) | — | ? | Yes | P3 | XL | gated | Not started |

## G. AI

| Feature | Video | Official | Req | Pri | Cx | Implementation | Status |
|---|---|---|---|---|---|---|---|
| Prompt → texture, added as a movable layer | ✅ | ? | Yes | P1 | M | `IAiTextureProvider` → `generated` layer | Done |
| UV skeleton PNG as placement map | ✅ (stated in-app) | ? | Yes | P1 | M | UV wireframe + UV mask, labelled IMAGE 2 / IMAGE 3 | Done |
| Edit the current texture rather than replace it | — | ? | Yes | P1 | M | auto mode; current diffuse attached as IMAGE 1 | Done |
| Provider abstraction | — | ? | Yes | P1 | M | `AiProviderRegistry`; Gemini only, no stubs | Done |
| API key in Windows credential storage | — | ? | Yes | P1 | S | DPAPI, plus `GEMINI_API_KEY` / `.env` | Done |
| Preview → crop/fit → UV-aware apply pipeline | ~ (result only) | ? | Yes | P1 | L | existing image-layer pipeline, unchanged | Done |
| Garment render as a reference image | — | ? | No | P2 | M | role defined, not yet sent | Not started |
| Regenerate / variations / history | — | ? | Yes | P2 | M | — | Not started |
| "Quick Fit" random themed generation | ✅ | ? | No | P2 | S | curated theme pool + shuffle | Not started |

## H. Variations, components, properties

| Feature | Video | Official | Req | Pri | Cx | Implementation | Status |
|---|---|---|---|---|---|---|---|
| Texture slots/variations (a, b, c…), add/rename/delete | ✅ | ? | Yes | P0 | M | maps to GTA variant letter | Not started |
| Per-slot independent layer stack | ✅ | ? | Yes | P0 | M | — | Not started |
| Slot duplicate | — | ? | Yes | P1 | S | — | Not started |
| Component/drawable/texture IDs surfaced in Properties | ~ (in export dialog) | ? | Yes | P0 | S | — | Not started |
| Context-sensitive Properties panel | ~ (layer only) | ? | Yes | P1 | M | — | Not started |
| Preview render (front/back/left/right) → thumbnail | — | ? | Yes | P1 | M | offscreen render | Not started |

## I. Validation

| Feature | Video | Official | Req | Pri | Cx | Implementation | Status |
|---|---|---|---|---|---|---|---|
| Validation engine with green/yellow/red results | — | ? | Yes | P0 | M | rule engine | Not started |
| Structural: missing mesh/texture/material, bad indices | — | ? | Yes | P0 | M | — | Not started |
| Naming: filename & convention conformance | — | ? | Yes | P0 | S | — | Not started |
| Texture: size, power-of-two, format, budget | — | ? | Yes | P0 | S | — | Not started |
| Skeleton/weight validity | — | ? | Yes | P2 | L | needs skinning parse | Not started |
| Shader support check | — | ? | Yes | P2 | M | — | Not started |
| Block export on error, warn on yellow | — | ? | Yes | P0 | S | — | Not started |

## J. Export

| Feature | Video | Official | Req | Pri | Cx | Implementation | Status |
|---|---|---|---|---|---|---|---|
| **Write a real `.ytd` the game loads** | ✅ | ? | Yes | **P0** | **L** | `fivefury.save_ytd` — **unproven, spike first** | Not started |
| **Write a real ped `.ymt`** | ✅ | ? | Yes | **P0** | **L** | `fivefury.save_ymt` — unproven | Not started |
| **Emit `.ydd` (copy/rename of base drawable)** | ✅ | ? | Yes | **P0** | **L** | read → rename → `save_ydd` — unproven | Not started |
| Write `.ydd` with edited geometry | — | ? | Yes | P3 | XL | gated on the above succeeding | Not started |
| FiveM addon resource generator + `fxmanifest.lua` | ✅ | ? | Yes | P0 | M | — | Not started |
| Correct `<ped>_<dlc>^<asset>` naming | ✅ | ? | Yes | P0 | M | — | Not started |
| Export presets (addon / pack / texture / dev / prod) | ~ (`Addon` only) | ? | Yes | P2 | S | — | Not started |
| Export directly to a FiveM server resources folder | ✅ | ? | Yes | P1 | M | — | Not started |
| Overwrite guard: preview → confirm → backup → export | — | ? | Yes | P1 | M | brief §70 | Not started |
| "Open Export Folder" | — | ? | Yes | P1 | S | — | Not started |
| Progress + cancel on export | ~ (progress only) | ? | Yes | P1 | M | — | Not started |

## K. Beyond the reference — our differentiators

| Feature | Why it matters | Pri | Cx |
|---|---|---|---|
| Runs on the desktop, not in-game | No game restart to iterate; real files, real memory | P0 | — |
| Real validation before export | The reference ships whatever you built; we catch broken assets first | P0 | M |
| Portable, resumable, shareable projects | The reference's snapshots appear server-bound | P0 | M |
| Correct 12-component coverage with real names | The reference exposes 7 and mislabels two | P1 | S |
| Character body / full-outfit preview | Judge a garment in context, not floating in the void | P1 | L |
| Pluggable AI providers with your own key | The reference's AI is a black box with no configuration | P1 | M |
| Optional live FiveM bridge (`architecture.md` §2) | Recovers the one thing the in-game tool does better | P2 | L |
