# Risks & Blockers

Ordered by how much damage they do if ignored.

## R1 — Writing RAGE binaries may not work · **Critical · Blocks the product**

The entire value proposition is "export a FiveM-ready clothing resource". If we cannot write a
game-loadable `.ytd`/`.ymt`/`.ydd`, we have a texture editor that produces PNGs, which is what
brief §61 explicitly forbids shipping.

RSC7 is a paged virtual/physical memory layout with pointer fix-ups, not a serialisation format
(`asset-format.md` §1). Failures are silent — the game crashes or drops the asset rather than
reporting an error.

**Current evidence:** `fivefury` (Unlicense) exports `save_ydd`, `save_ytd`, `save_ymt` and models
the ped-variation schema (`CPVComponentData`, `availComp`, `numAvailTex`…). Verified by inspecting
the shipped wheel. **This is symbol-level evidence, not proof of working output.**

**Mitigation:** Phase 1 spike, before anything else. Fallbacks in order: fork MIT-licensed RageLib;
shell out to an existing CLI tool; write our own RSC7 packer (months). All three fallbacks push the
stack toward pure .NET.

**Trigger:** spike step 6 fails to load in-game.

## R2 — Depending on a pre-1.0 library · **High**

`fivefury` is at `0.4.20` with 113 releases. Its API will change; its own README admits coverage
gaps. It is also, as far as I can tell, largely one maintainer.

**Mitigation:** pin the exact version; confine all `fivefury` imports to `assetservice/rage/`; define
our contract in C# so the implementation is swappable; keep a fixture-based test suite that fails
loudly on any upgrade. Vendoring the wheel in-repo is acceptable given the Unlicense.

## R3 — Scope is far larger than the reference product · **High**

The brief asks for mesh editing, UV editing, weight painting, skeleton tooling, validation, AI,
multi-monitor, updates and licensing. The reference product does approximately **one** of these
things (texture authoring) and does it well. Durty Cloth Tool, a funded commercial product, does not
offer mesh or weight editing either.

Building all of it at once produces a demo, not a product.

**Mitigation:** the phase gates in `roadmap.md`; MVP explicitly excludes AI, mesh, UV and weight
work; capability gating (`architecture.md` §7) makes unbuilt features *absent* rather than broken.

**This is the risk most likely to actually bite.** It is worth saying plainly: the brief's full
feature list is a multi-year product. Phases 1–10 are a credible first release.

## R4 — Mesh editing may be infeasible at acceptable quality · **High**

Even with a working YDD writer, authoring *valid* freemode clothing geometry requires correct rigging
to the freemode skeleton, three LODs, PED shader setup, and specific vertex-colour conventions
(`asset-format.md` §4). Any one wrong produces garments that stretch, flap, or vanish at distance.
Blender + Sollumz exists because this is genuinely hard.

**Mitigation:** treat mesh editing as a separate research project after MVP. Ship read-only mesh and
weight *inspection* first — that is real value at a fraction of the risk. Never expose an editing
tool that produces assets we cannot validate.

## R5 — Licence contamination · **Medium · Legal**

The most complete reference implementation, CodeWalker, is **not licensed for reuse**: no `LICENSE`
file, and `Readme_Src.txt` states it is released for educational purposes only. Sollumz is GPL-3.0.
Copying from either into a proprietary product creates real legal exposure. The reference product
itself is escrowed commercial code and must not be reverse-engineered.

**Mitigation:** the allow-list in `legal-and-oss.md`. Reference technical *approaches* from
unlicensed sources; copy code only from Unlicense/MIT sources with attribution preserved. A
dependency audit is a release gate.

## R6 — No official product description obtained · **Medium**

The brief asks for video-vs-official comparison. The Tebex store returns 403 to automated fetches and
YouTube is blocked in this environment. The feature inventory is therefore video-only.

**Impact:** we may have missed advertised features that were not demonstrated, and we cannot flag
marketing claims that the video does not support (beyond the one at t=30–32).

**Mitigation:** the user can paste the store description; I will fold it in and produce the
comparison. Low cost to close.

## R7 — Legacy vs. Enhanced fragmentation · **Medium**

GTA V Enhanced changed resource versions, vertex layouts and shader metadata. `fivefury` exposes
`GameTarget.GTA5_LEGACY` / `GTA5_ENHANCED`, which implies the difference is real and load-bearing.
Producing assets for the wrong target yields assets that do not load.

**Mitigation:** make target an explicit, surfaced project setting; test both in the spike if possible;
never guess a default silently.

## R8 — Three-process architecture complexity · **Medium**

Host + WebView2 + Python sidecar means three failure modes, three debugging stories, and IPC
overhead on every mesh and texture.

**Mitigation:** memory-mapped bulk transfer; supervision with restart and user-readable failure
reporting; a hard rule that the sidecar is stateless between calls, so restarting it is always safe.

## R9 — Template library seeding / asset redistribution · **Medium · Legal**

The reference ships 107+ templates. We cannot redistribute extracted Rockstar assets, and most
GTA5-Mods content has its own licence terms.

**Mitigation:** ship **no** clothing assets. Provide a first-run flow that points the library at the
user's own asset folders, plus clear documentation of the expected layout. Optionally curate a list
of freely-licensed community templates the user can fetch themselves.

## R10 — AI generation quality on UV layouts · **Medium**

Text-to-image models do not naturally produce artwork that lands correctly on a garment's UV
islands. The reference's stated approach — pass the UV skeleton PNG as a placement map — is
plausible but its actual quality is unknown, and generic image models will often ignore such
conditioning.

**Mitigation:** treat it as P1, not P0. Design the pipeline (prompt → generate → preview → fit →
UV-aware apply) so a mediocre generation is still usable as a movable layer the user can position by
hand — which is exactly what the reference does. Declare provider capabilities honestly.

## R11 — 480p reference video · **Low**

Some UI strings were unrecoverable even at 5× magnification and are marked `[unreadable]`. A few
dialogs were shown only briefly.

**Mitigation:** noted inline. Does not affect any architectural decision.

## R12 — Performance of Python-side texture work · **Low**

BC7 encoding a 2048² texture is slow enough to be noticeable; doing it in Python per preview would
make the app feel bad.

**Mitigation:** measure in the spike. Preview compositing stays in the browser layer with no
compression; BC encoding happens only on export, as a background job with progress. If host-side
encoding is faster, BCnEncoder.NET is available and the sidecar only writes the container.

---

## Blocker summary

| # | Blocker | Blocks | Resolved by |
|---|---|---|---|
| B1 | Can we write loadable RAGE assets? | Everything | Phase 1 spike |
| B2 | Legacy or Enhanced target? | Export correctness | User answer + spike |
| B3 | Commercial or internal? | Licence strategy, GPL options | User answer |
| B4 | Where do base templates come from? | Asset browser usefulness | User answer |
| B5 | Official product description | Completeness of the parity analysis | User pastes it |
