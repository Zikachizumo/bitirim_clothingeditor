# Development Roadmap

Every phase ends with a build that runs. Phases 1–3 are deliberately ordered so that the riskiest
unknown is settled before any UI investment is made.

## Phase 0 — Research & decision *(this document set)*

**Done:** video feature inventory from 97 extracted frames; format research; licence audit of every
candidate library; empirical inspection of the `fivefury` wheel; stack comparison; architecture;
feature matrix; risk register.

**Not done, and deliberately so:** no code, no stack commitment. The Phase 1 spike decides the stack
(`technology-comparison.md` §6).

**Exit:** user approval to proceed.

---

## Phase 1 — The export spike ⚠️ **highest priority, blocks everything**

This is a throwaway command-line spike, not product code. Its only job is to answer: *can we write
GTA clothing assets the game actually loads?*

1. Set up an isolated Python 3.11 environment, `pip install fivefury==0.4.20`.
2. Acquire a real clothing fixture — a freely-redistributable add-on `.ydd`/`.ytd` pair.
3. `read_ydd` it; dump geometry, LOD count, bones, skinning, embedded textures. Confirm the parse is
   structurally sane.
4. `read_ytd` → decode to RGBA → re-encode → `save_ytd`. Compare against the original.
5. Write a *modified* YTD from a plain PNG and build a complete addon pack:
   `<ped>_<dlc>.ymt` + `<ped>_<dlc>^<comp>_000_u.ydd` + `<ped>_<dlc>^<comp>_diff_000_a_uni.ytd`
   + `fxmanifest.lua`.
6. **Load it in a real FiveM server. Wear it. Screenshot it.**
7. Measure: parse time, BC7 encode time for 2048², YTD write time.

**Exit gate.** If step 6 succeeds, the stack in `technology-comparison.md` §3 is confirmed and
Phase 2 starts. If it fails, we reassess: fork MIT-licensed RageLib, or write our own RSC7 packer —
both of which make pure .NET (Option B) the better stack. **Do not start Phase 2 before this gate.**

**Deliverable:** `docs/spike-report.md` with commands, outputs, timings, the in-game screenshot, and
a go/no-go recommendation.

---

## Phase 2 — Asset service

Turn the spike into the versioned sidecar (`architecture.md` §3). Narrow contract, ~12 operations,
JSON-RPC over stdio, memory-mapped bulk transfer, startup capability report. Fixture-based pytest
suite with golden outputs. **No UI yet.**

**Exit:** `assetservice` opens a drawable, returns a mesh DTO, round-trips a texture dictionary, and
builds a pack — all under test, all from the command line.

## Phase 3 — Desktop shell

.NET 8 host, WebView2, the three-pane layout, dark theme, panel resize, start screen, single
instance, logging, error surface, sidecar supervision.

**Exit:** the app opens, looks like the product, supervises a live sidecar, and reports its
capabilities.

## Phase 4 — Project system

Folder-based projects, `project.json` schema v1, save/open/recent, auto-save, recovery, command bus
and undo stack, backups.

**Exit:** create → save → close → reopen → identical state.

## Phase 5 — 3D viewport

Three.js scene, mesh DTO → geometry, orbit/pan/zoom, camera presets, display modes, live texture
binding.

**Exit:** open a real `.ydd` from the library and see it correctly lit and shaded.

## Phase 6 — Asset & template browser

Library scanning, gender/component filters, search, grid/list, thumbnail generation and SQLite
cache with hash + mtime invalidation, loose-file ingest, drag & drop.

**Exit:** the video's t=57–68 workflow — drop a downloaded `.ydd` in, see it appear, click it, see
it in 3D.

## Phase 7 — UV canvas + texture editor

UV wireframe overlay, layer stack, brush engine, fill, colour picker, image layers with transform,
text tool, per-layer properties, deterministic compositor.

**Exit:** paint a garment and watch the 3D preview update live.

## Phase 8 — Slots, materials, properties

Texture slots mapped to variant letters with independent stacks; context-sensitive Properties panel;
material channel display (diffuse from slot, normal/spec from base drawable).

**Exit:** two variations of one garment, both previewable.

## Phase 9 — Validation

Rule engine, the P0 rule set (structure, naming, texture constraints, index validity), green/yellow/
red UI, errors blocking export with actionable messages.

**Exit:** a deliberately broken project produces precise, useful findings.

## Phase 10 — Export

Export dialog, addon resource generation, `fxmanifest.lua`, correct `^` naming, export to a server
resources folder with preview → confirm → backup → write, progress with cancel, "Open Export Folder".

**Exit:** **the PRD §7 success criterion, end to end.** This is the milestone that makes the product
real.

## Phase 11 — AI

`ITextureGenerator`, OpenAI + local + custom providers, DPAPI key storage, UV-skeleton conditioning,
preview → fit → apply pipeline, regenerate and variations, graceful absence when unconfigured.

## Phase 12 — Character & outfit preview

Freemode base body under the garment; multi-component outfit preview for visual compatibility
checking.

## Phase 13 — Read-only mesh, skeleton & weight inspection

Mesh stats, LODs, materials, bone hierarchy and search, weight visualisation. **Read-only.** Editing
stays gated on a proven YDD geometry writer.

## Phase 14 — Performance & polish

Background parsing, texture/asset/thumbnail caching, large-texture handling, memory profiling,
resolution testing, keyboard shortcuts with rebinding, global search.

## Phase 15 — Packaging

WiX installer, portable build, file associations, code signing, first-run experience, docs.

---

## MVP definition

MVP is **Phases 1–10**. Concretely, the MVP user can:

- open the app and create a project;
- pick gender and component, and load a base garment from the library or an imported `.ydd`;
- see it in 3D;
- author its diffuse texture with layers, brush, fill, colour, and imported images;
- create texture variations;
- save, close, reopen, and continue;
- validate;
- export a FiveM addon clothing resource that loads in a real server and can be worn.

**Not in MVP:** AI, character/outfit preview, mesh or weight tooling, UV editing, installer polish.
AI is deliberately excluded — it is the reference product's headline feature, but it is worthless on
top of an export that does not work, and it is the easiest thing to add once the pipeline is solid.

---

## What can be implemented immediately

No research needed; these could start today in parallel with the spike:

- Desktop shell, theming, panel layout, start screen (Phase 3)
- Project format, schema, save/open/recovery (Phase 4)
- Command bus and undo stack (Phase 4)
- Layer model and the deterministic compositor (Phase 7) — pure logic, testable without any asset
- Brush engine, colour picker, image and text layers (Phase 7) — all standard 2D work
- Settings, DPAPI secret store, logging, error surface (Phase 3)
- Validation rule engine skeleton and the naming/index rules (Phase 9) — these need no binary parsing
- `fxmanifest.lua` generation and resource folder layout (Phase 10) — text output, fully testable
- SQLite metadata and thumbnail cache schema (Phase 6)

## What requires deeper research or reverse engineering

- **Anything that writes a RAGE binary.** Settled by the Phase 1 spike, not by planning.
- The exact internal-name rewriting required by the `^` DLC convention (`asset-format.md` §8, q4).
- Legacy vs. Enhanced target differences for our specific outputs.
- Correct freemode rigging for *new* geometry — bone mapping, 3 LODs, PED shader setup, vertex
  colour conventions. This is the gate on the entire mesh-editing surface and should be treated as
  a separate research project, not a feature.
- Cloth `.yld` authoring — no library models it; currently out of scope.
- Real clipping detection between components. Heuristics are not detection and must not be sold as
  such.

## Recommended first implementation task

**Run the Phase 1 export spike.** Nothing else. It is a few days of work, it has a binary outcome,
and it determines both the stack and whether the product's core promise is achievable at all.
Building UI before that answer risks weeks of work sitting on top of an engine that cannot ship.

If you want parallel progress while the spike runs, the safest second track is the **layer model +
compositor** (Phase 7 logic) — pure, testable, stack-agnostic, and needed no matter which way the
gate falls.

---

## Status update — 2026-08-31

**Phase 1 (export spike) — complete.** See [phase-1-results.md](phase-1-results.md). Real `.ytd` and
`.ymt` writing verified by read-back; `.ydd` handled by byte-copy after the writer was found to
corrupt bounding volumes on skinned ped drawables.

**Phases 3, 4, 5, 6, 7, 9, 10 and 15 — delivered in v0.1.0**, ahead of the original ordering.
The user chose to defer the in-game FiveM test and prioritise a usable desktop build, so the shell,
project system, viewport, asset browser, texture editor, validation, export UI and installer were
built on top of the proven Phase 1 backend.

| Phase | State |
|---|---|
| 1 Export spike | ✅ complete (in-game test deferred, not cancelled) |
| 2 Asset service | ✅ complete — versioned contract, 10 operations |
| 3 Desktop shell | ✅ complete — WPF + WebView2, splash, menus, status bar |
| 4 Project system | ✅ complete — folder projects, `.bitirimclothing` packages, autosave |
| 5 3D viewport | ✅ complete — real drawable geometry, camera presets, shading modes |
| 6 Asset browser | ✅ complete — library scan, filters, search; thumbnails still pending |
| 7 UV + texture editor | ✅ texture editor complete; UV view is read-only |
| 8 Slots / materials | ✅ variations complete; material panel read-only |
| 9 Validation | ✅ complete |
| 10 Export | ✅ complete in the UI — **still unvalidated in game** |
| 11 AI | 🟨 Gemini implemented; not yet run against a live key |
| 12 Character preview | ⬜ not started |
| 13 Mesh / weight inspection | ⬜ blocked on the YDD writer |
| 14 Performance | ◐ background parsing and caching partial |
| 15 Installer | ✅ complete — MSI + portable zip |

**The gate that has not moved:** no exported resource has been loaded by a real FiveM client. That
remains blocker B1 and it is the first thing to close when the user is ready to test.

---

## Status update — 2026-09-01 (v0.2.0)

The user accepted v0.1.0, deferred the in-game test again, and asked for a full
clothing development editor. v0.2.0 was built **on top of** the v0.1.0 backend:
the parser, asset service, texture decode, `.ytd`/`.ymt` writers and the export
path are unchanged in behaviour.

| Phase | State |
|---|---|
| 1 Export spike | ✅ complete (in-game test deferred, not cancelled) |
| 2 Asset service | ✅ complete — unchanged in v0.2.0 |
| 3 Desktop shell | ✅ layout v2: collapsible panels, rails, fullscreen viewport |
| 4 Project system | ✅ schema 2, multi-garment, migration, crash recovery |
| 5 3D viewport | ✅ five preview modes, ortho/persp, FOV, bounds, normals |
| 6 Asset browser | ✅ **real rendered thumbnails**, cache, sort, favourites, search |
| 7 UV + texture editor | ✅ 13 tools, layers with groups and blend modes, selection; UV still read-only |
| 8 Slots / materials | ✅ variation manager; material panel read-only with the reason |
| 9 Validation | ✅ categories, severities, clickable findings |
| 10 Export | ✅ 5-step wizard, presets, plan, progress, cancel, history — **still unvalidated in game** |
| 11 AI | 🟨 gemini-3.1-flash-image via Google.GenAI; UV layout and mask sent as placement maps |
| 12 Character preview | ◐ outfit model is real; **rendering is not implemented** |
| 13 Mesh / weight inspection | ⬜ blocked on the YDD writer |
| 14 Performance | ✅ background thumbnails, caching, disposal, cancellation |
| 15 Installer | ✅ MSI (upgrades 0.1.0 → 0.2.0) + portable zip |
| **Undo / redo** | ✅ **new** — command stack over every edit |
| **Developer tools** | ✅ **new** — asset inspector, read-only hex view, filtered console |

**143 tests, 0 skipped.** The 7 that silently skipped in v0.1.0 were a path bug,
not an absence of fixtures; they run now.

### Next

**B1 is still the gate.** Everything downstream of it — which `fxmanifest` form
is right, whether internal names need the DLC prefix, Legacy vs. Enhanced — is
answered by one test on a real server.
