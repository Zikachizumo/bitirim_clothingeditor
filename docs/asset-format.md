# GTA V / RAGE Asset Pipeline Research & Feasibility

Every claim here is either sourced or marked as unverified. Where I inspected something directly,
I say so. Where the community documentation disagrees or is thin, I say that too.

## 1. The formats that matter for clothing

| Ext | Name | Role in clothing | We must |
|---|---|---|---|
| `.ydd` | Drawable Dictionary | The garment mesh(es). Holds geometry, 3 LODs, vertex data, skinning, shader group, **embedded normal/spec textures** | read + write |
| `.ytd` | Texture Dictionary | The **diffuse** texture(s), as block-compressed DDS surfaces | read + write |
| `.ymt` | Meta (PSO/RBF) | Ped variation metadata: which components/drawables/textures exist, flags, cloth data | read + write |
| `.yft` | Fragment | The freemode **skeleton** source (`mp_m_freemode_01.yft`) — needed as the rig reference | read only |
| `.yld` | Cloth | Optional cloth-physics data for a drawable | out of MVP scope |
| `.rpf` | Archive | Only needed if we ever read the base game or build a real DLC pack | read (later) |

All resource files (`ydd`/`ydr`/`ytd`/`yft`/`ybn`) share the RAGE **RSC7** container: a magic
`RSC7`, a resource type, and system/graphics page-flag words that describe a paged virtual/physical
memory layout, followed by a zlib-deflated payload of pointer-relative structures.
([format overview](https://zoov.dev/kb/guides/gta-v-fivem-file-formats),
[RSC7 notes](http://zenhax.com/viewtopic.php@t=1113.html))

**This is the crux of the whole project.** Writing an RSC7 resource is not serialisation — it is a
memory-layout problem. You must lay structures out across virtual and physical pages, fix up every
pointer as a page-relative offset, and encode the resulting page counts into the header flags. Get
it wrong and the game does not error, it crashes or silently drops the asset. Writing a *correct*
RSC7 packer from scratch is a multi-month effort with no forgiving failure mode.

## 2. The freemode component system

12 ped components, addressed by index and by a 4-letter prefix used in filenames
([cfx.re how-to](https://forum.cfx.re/t/how-to-streaming-addon-clothes-and-ped-props-for-mp-freemode-models/458854),
[5mod tutorials](https://github.com/lucienlmy/5mod-tutorials/blob/master/Basic-Ped-YMT-Editing-%E2%80%90-Components%2C-Clothes%2C-Textures.md)):

| Idx | Prefix | Common name | In reference video's dropdown |
|---|---|---|---|
| 0 | `head` | Head | — |
| 1 | `berd` | Mask / beard | ✅ as `BEARD` |
| 2 | `hair` | Hair | — |
| 3 | `uppr` | Torso / arms | — |
| 4 | `lowr` | Legs / pants | ✅ as `LOWER` |
| 5 | `hand` | Bags & parachute | — |
| 6 | `feet` | Shoes | ✅ as `FEET` |
| 7 | `teef` | Accessories (ties/scarves) | ✅ as `TEETH` |
| 8 | `accs` | Undershirt / t-shirt | ✅ as `ACCS` |
| 9 | `task` | Body armour | — |
| 10 | `decl` | Decals | ✅ as `DECL` |
| 11 | `jbib` | Tops / jackets / vests | ✅ as `JBIB` |

Note the reference product exposes **7 of 12** — no head, hair, uppr, hand, or task. Its label
`TEETH` for `teef` is a common community mislabel (the slot is accessories, not teeth); its
`BEARD` for `berd` likewise (the slot carries masks far more often than beards). We should use
correct labels with the prefix shown alongside.

**Filename grammar**

```
<component>_<drawable:3>_<sexSuffix>.ydd          e.g. jbib_004_u.ydd
<component>_diff_<drawable:3>_<variant>_<race>.ytd e.g. jbib_diff_004_a_uni.ytd
```
- `drawable` is zero-padded to 3 digits.
- `sexSuffix` is `u` (universal), `m`, or `f`.
- `variant` is `a`–`z` — this is the **texture variation letter**, which the reference product maps
  onto its Slot A / Slot B / … UI.
- `race` is `uni` (universal) or a race code (`whi`, `bla`, `kor`…). Clothing is almost always `uni`.

Embedded (inside the YDD, not the YTD) textures use `<component>_normal_<drawable:3>` and
`<component>_spec_<drawable:3>`.
([Sollumz clothes docs](https://docs.sollumz.org/tutorials/basic-clothes-editing))

## 3. Addon DLC packaging for FiveM

The convention observed in the video and documented by the community
([cfx.re](https://forum.cfx.re/t/how-to-streaming-addon-clothes-and-ped-props-for-mp-freemode-models/458854),
[altV docs](https://docs.altv.mp/gta/articles/tutorials/stream_clothes_overwrite.html)):

```
<ped>_<dlcName>.ymt                     ← ped variation metadata for this addon pack
<ped>_<dlcName>^<component>_<idx>_u.ydd ← the drawable
<ped>_<dlcName>^<component>_diff_<idx>_<v>_uni.ytd
```

The `^` character separates the *DLC identity* from the *asset name inside that DLC*. Matching the
video exactly:

```
mp_m_freemode_01_fcd_m_jbib_jbib_004.ymt
mp_m_freemode_01_fcd_m_jbib_jbib_004^jbib_000_u.ydd
mp_m_freemode_01_fcd_m_jbib_jbib_004^jbib_diff_000_a_uni.ytd
```

Decomposed against the reference's own export dialog:
`Ped Name` = `mp_m_freemode_01`, `DLC Name` = `fcd_m_jbib_jbib_004`, and the drawable *inside* the
pack is index `000` regardless of the pack being labelled `004`. **One addon pack = one garment at
drawable 0.** The game appends addon drawables after the vanilla range, which is why the video's
vest landed at Jackets index 545 out of 545.

`mpClothes`-style slot expansion is the other common approach; we should support the plain-addon
form first because it is what the reference does and it needs no companion resource.

## 4. What a valid clothing drawable actually requires

From the Sollumz clothing documentation
([basic clothes editing](https://docs.sollumz.org/tutorials/basic-clothes-editing)):

1. **Rigged to the freemode skeleton.** The bone set comes from the ped's `.yft`; the mesh must be
   skinned to those exact bones with correct indices. A wrong bone mapping produces the classic
   "clothes flapping in the wind" bug.
2. **PED shader family** on the material for MP freemode drawables.
3. **Three LODs** — high, medium, low. All three must exist.
4. **Embedded** normal + spec named `<component>_normal_<idx>` / `<component>_spec_<idx>`;
   **external** diffuse in the YTD.
5. **Vertex colours** — Colour 1 `#FF8000`, Colour 2 `#000000` with zero alpha.
6. Skin-swap garments need square, power-of-two textures.

Point 4 is the single most important fact for our roadmap: **the diffuse texture — the only thing a
texture editor changes — lives in a separate file from the mesh.** Re-skinning a garment therefore
requires writing a YTD and *nothing else*. Editing the mesh requires writing a YDD, which is the
hard problem. These two capabilities can and should ship on different schedules.

## 5. How the reference product almost certainly works

Reading the exported `stream/` listing against the above:

- The exported YDD sizes (126 KB / 182 KB / 125 KB) differ per template and the drawable inside is
  always `_000_`. The YTD sizes vary wildly (5 KB for one jbib pack, 2,026 KB and 2,420 KB for
  others) — consistent with a hand-authored small texture vs. a full 2048² BC-compressed AI image.
- The product never exposes any mesh operation.

**Hypothesis (not proven):** the reference copies the template's YDD through largely unmodified —
renaming it into the DLC namespace — and generates only the YTD (and a small YMT) per export. That
would explain why it needs neither Blender nor Sollumz, why every garment must start from an
existing `.ydd`, and why the entire feature set is texture-shaped. I flag this as a hypothesis
because I have not opened any of those files; it is the simplest explanation consistent with every
observed artefact.

If correct, it defines the honest MVP boundary for us too: **ship real YTD authoring first, treat
YDD authoring as a separate, later, explicitly-scoped capability.**

## 6. Feasibility per format

> **Updated after Phase 1.** The table below is no longer a prediction — every row marked
> *verified* was executed against real game assets and confirmed by re-parsing our own output.
> Full evidence in [phase-1-results.md](phase-1-results.md).

| Capability | Status | Basis |
|---|---|---|
| Read YDD (geometry, LODs, skinning, materials, embedded textures) | **Verified** | `jbib_000_u.ydd` parsed in ~10 ms; 3 LODs, 1005 skinned verts, 2 ped shaders, 3 embedded textures |
| Render that geometry in a viewport | **Feasible** | ordinary mesh data once parsed; not yet exercised |
| Read YTD → decode BCn | **Verified** | 2 ms; name, dimensions, format, mip count, surface bytes |
| Composite layers → RGBA → BC1/BC3/BC7 | **Verified** | [BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET) (Unlicense); our 512² BC3 output matches the game's own surface size and mip count exactly |
| **Write YTD** | **Verified (not yet in-game)** | Round-trip structurally identical, surface SHA unchanged; texture replacement preserves the material lookup name |
| **Write YMT (ped variation)** | **Verified (not yet in-game)** | Round-trip structurally identical; single-component addon built at 947 B with correct `availComp` |
| **Write YDD (copy/rename a template)** | **Verified — as a byte copy** | Renaming the file is sufficient; the internal texture name must stay plain |
| **Write YDD (rewrite through the writer)** | **Rejected** | Geometry, skinning, materials and embedded textures survive, but the bounding volume is recomputed wrongly for skinned ped drawables. `capabilities.writeYdd` is reported `false` |
| Write YDD (edited geometry) | **Blocked** | Gated on the bounding-box defect above |
| Write YDD from scratch with correct freemode rigging | **Not for v1** | requires correct bone mapping, 3 LODs, PED shader setup, vertex colour conventions — each an independent failure mode |
| Author cloth `.yld` | **No** | not modelled by any library we found |
| Read encrypted retail RPF archives | **Verified** | `load_game_keys` derived a 32-byte AES key + 101 NG keys from the game install in 1.9 s |

### 6.1 The decisive finding

`fivefury` ([GitHub](https://github.com/Hancapo/fivefury), [PyPI](https://pypi.org/project/fivefury/))
is a Python toolkit for GTA V binary formats released under **The Unlicense** (public domain —
verified via the GitHub licence API). I downloaded the actual Windows wheel
(`fivefury-0.4.20-cp311-abi3-win_amd64.whl`, 3.6 MB, published 2026-08-24) and inspected its
contents without executing it. Confirmed present in the shipped package:

```
fivefury/ydd/{model,reader,writer,rigging,runtime_headers}.py
  read_ydd · create_ydd · build_ydd_bytes · save_ydd
  rig_ydd_to_bones_radially · find_body_skeleton_ydd
  YDD_VERSION_LEGACY / YDD_VERSION_GEN9
  LEGACY_YDD_FULL_PED_RUNTIME_PROFILE
fivefury/ytd/…
  read_ytd · save_ytd · YtdCatalog · read_embedded_ytd_catalog
fivefury/ymt/ped_metadata.py, ped_variation.py
  read_ymt · save_ymt · YmtPedMetadata · YmtPedInitData
  PedComponent · PedDrawableVariation · PedPropVariation
  references to CPVComponentData / CPVDrawblData / CPVTextureData / availComp / numAvailTex / texId / clothData
fivefury/dlc/…
  DlcContentXml · DlcPack · create_dlc_folder_metadata · write_dlc_folder_metadata
```

That is precisely the clothing pipeline, from a public-domain source, actively released (113
versions, latest six days before this analysis), with a prebuilt Windows x64 binary wheel. It
removes the single largest risk in the project — the RSC7 packer — from our critical path.

**Caveats I am not glossing over:**
- Version `0.4.20` is pre-1.0. The API will move and coverage has gaps the README admits to
  ("not every runtime subtype is modeled semantically").
- Its own README does not mention clothing specifically. The clothing-shaped API surface is my
  inference from module and symbol names, not from documented, tested clothing workflows.
- It is Python with a native extension. Embedding it in a .NET desktop app is an architectural
  cost (see `technology-comparison.md` §4).
- **Nothing here is proven until an asset we wrote loads in an actual FiveM server.** Symbol names
  are not a working export. This is exactly what Phase 0's spike exists to settle.

### 6.2 Rejected and fallback options

- **CodeWalker** ([dexyfex/CodeWalker](https://github.com/dexyfex/CodeWalker)) — the most complete
  implementation in existence, and **we cannot use its code.** The repo has no `LICENSE` file and
  `Readme_Src.txt` reads, in full: *"CodeWalker by dexyfex - Source Code. This source code is
  released for educational purposes only."* `Notice.txt` carries only third-party notices. No
  licence grant means all rights reserved. The `CodeWalker.Core` NuGet package is published by a
  third party ("Ancapo"), not by dexyfex, so it does not repair the licensing position. **Reference
  the technical approach; copy nothing.**
- **Sollumz** ([Sollumz/Sollumz](https://github.com/Sollumz/Sollumz)) — GPL-3.0 (verified). Excellent
  and correct, but GPL is incompatible with a proprietary desktop product. Usable only as
  documentation, or if we ship the whole product under GPL-3.0.
- **RageLib** ([Neodymium146/gta-toolkit](https://github.com/Neodymium146/gta-toolkit)) — no root
  `LICENSE`, but every source file carries a full **MIT** header (`Copyright(c) 2016 Neodymium`),
  which is a valid grant. It is .NET, which would suit us. However `README.txt` says exactly:
  **"THIS PROJECT IS DISCONTINUED."** Viable as a licence-clean starting point to fork and modernise
  if we decide against the Python dependency; expect it to predate a decade of format changes.
- **ytdtool** ([kngrektor/ytdtool](https://github.com/kngrektor/ytdtool)) — C#, packs and unpacks
  YTDs. No licence file → unusable as code, useful as a correctness cross-check.

## 7. Prior art we should be honest about

**[Durty Cloth Tool](https://gta.clothing/)** is a Windows desktop GTA V clothing/tattoo tool with
3D preview, error detection, texture optimisation, project management, and export to FiveM, alt:V,
RageMP and singleplayer. Free tier (5 drawables per type, 7 textures per drawable), €10/mo and
€15/mo paid tiers.

This is much closer to the brief's target product than the reference video is. It should be
evaluated hands-on during Phase 0 — not to copy, but so that we know which of our planned features
are genuinely differentiating and which are table stakes. Ignoring it would mean designing in the
dark.

## 8. Open technical questions — answered by Phase 1

1. **Does `save_ytd` produce a loadable YTD?** — It produces a structurally correct one; the surface
   round-trips with an identical SHA and our encoder matches the game's own output size and mip
   count. **Whether the game loads it is still untested.**
2. **Does a YDD round-trip survive?** — Geometry, LODs, UVs, skinning, materials and embedded
   textures survive exactly; the bounding volume does not. **Answer: copy the file instead.**
3. **Can `save_ymt` produce an addon ped-variation YMT?** — Yes. 947 bytes, `availComp` correct,
   re-parses cleanly. Built by pruning a known-good ped YMT rather than synthesising it.
4. **What does the `^` naming require of internal names?** — **Only the file name carries the DLC
   prefix.** The texture name inside the dictionary must stay plain, because that is what the
   drawable's `DiffuseSampler` looks up. The exporter reads the real name from the source material
   rather than assuming it. *(Reasoned from the material reference; confirmed by a loading asset only
   once the in-game test runs.)*
5. **Legacy vs Enhanced?** — Still open. All fixtures were Legacy. The user has both editions
   installed, so this is testable when they choose a target.
6. **Is Python-side work fast enough?** — Parsing is: YTD 2 ms, YDD 10 ms, YMT 6 ms. Writing is:
   ~12 ms YTD, ~8 ms YMT. The expensive step is host-side encoding, not the sidecar — BC7 at 2048²
   takes **6.3 s** and must be a background job. A full two-variation export runs in ~196 ms.

### Still open

- Which `fxmanifest.lua` form a ped-variation YMT needs (two variants staged for testing).
- Whether the same output works on GTA V Enhanced.
- Multi-garment packs (the exporter currently emits one drawable per pack).

---

### Sources

- [dexyfex/CodeWalker](https://github.com/dexyfex/CodeWalker) · [Readme_Src.txt](https://raw.githubusercontent.com/dexyfex/CodeWalker/master/Readme_Src.txt) · [Notice.txt](https://raw.githubusercontent.com/dexyfex/CodeWalker/master/Notice.txt)
- [Sollumz/Sollumz](https://github.com/Sollumz/Sollumz) · [Basic Clothes Editing](https://docs.sollumz.org/tutorials/basic-clothes-editing)
- [Hancapo/fivefury](https://github.com/Hancapo/fivefury) · [fivefury on PyPI](https://pypi.org/project/fivefury/)
- [Neodymium146/gta-toolkit (RageLib)](https://github.com/Neodymium146/gta-toolkit)
- [Nominom/BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET)
- [kngrektor/ytdtool](https://github.com/kngrektor/ytdtool)
- [cfx.re — streaming addon clothes for MP freemode models](https://forum.cfx.re/t/how-to-streaming-addon-clothes-and-ped-props-for-mp-freemode-models/458854)
- [lucienlmy/5mod-tutorials — Basic Ped YMT Editing](https://github.com/lucienlmy/5mod-tutorials/blob/master/Basic-Ped-YMT-Editing-%E2%80%90-Components%2C-Clothes%2C-Textures.md)
- [alt:V docs — stream clothes](https://docs.altv.mp/gta/articles/tutorials/stream_clothes_overwrite.html)
- [GTA V / FiveM file formats overview](https://zoov.dev/kb/guides/gta-v-fivem-file-formats)
- [GTA 5 RPF7 file format notes](http://zenhax.com/viewtopic.php@t=1113.html)
- [Durty Cloth Tool](https://gta.clothing/)
- [CodeWalker.Core on NuGet](https://www.nuget.org/packages/CodeWalker.Core/1.0.3)

## 9. Glow-in-the-dark clothing (`ped_emissive`)

Measured in a running FiveM client on 2026-09-04, across thirteen in-game
tests. Every claim here is an observation, not a reading of someone's guide.

**Emission is a product of three things:**

```
emission = vertex colour BLUE  x  diffuse ALPHA  x  emissiveMultiplier
```

Any factor at zero makes the garment dark, which is what makes this easy to
get wrong. Swapping a material to `ped_emissive` and changing nothing else
produces no glow at all, on base-ped and DLC garments alike: base-ped meshes
carry blue = 0 on every vertex.

**What is identical between a garment that glows and one that does not**, and
therefore what is useless to compare: shader name, `.sps` file name, shader
file hash (`0x1a0aeada`), render bucket, the full parameter list and its
values, bound textures, embedded textures, mesh channels, vertex stride,
declaration flags and types, and the material descriptor. Only the vertex
colour blue channel differed.

**How Rockstar authors it.** `mp_m_2024_01/jbib_014_u` (shop jacket 538) sets
blue to a two-valued mask — 63 of 1911 high-lod vertices, 3.3%, the chest print
— and repeats it on every lod (5.1% of med, 9.6% of low). Its diffuse alpha is
a hard black/white outline of the same print. The vertex mask is the coarse
permission; the alpha is the pixel-accurate shape.

**Scope.** 200 of the 12,468 clothing drawables in the game use `ped_emissive`,
across 44 DLC folders. None are in the base ped folder, which is why "convert a
base-ped garment" looked impossible for a while — it is not, it just needs the
vertex channel painted too.

**What the app does.** `ydd_make_emissive` performs both halves in one call
(`IRageAssetBackend.MakeEmissiveAsync`), because splitting them would let a
caller ship a garment that is emissive on paper and dark in game.
`GlowMask.Apply` folds the mask into the diffuse alpha. A neon export therefore
ships a `.ydd` as well as its textures, which a plain re-skin never does.

### Things that were measured and turned out not to matter

- **Bounding boxes.** fivefury recomputes them from geometry and discards the
  stored value; setting them back has no effect. Base-ped garments store a box
  offset about +1.0 in Z from their vertices, DLC ones do not (jbib_014 stores
  -0.24), so the recomputed value sits inside the range Rockstar itself ships.
  Tested in game: no crash, no culling, no disappearing at distance.
- **fivefury's writer.** An earlier crash (`bcc_rt`, `bcc_ov0`) made a
  fivefury-written asset look dangerous. That was `.ytd`. A round-trip `.ydd`
  loads and renders correctly, and a round-tripped emissive garment still
  glows.
- **The ped `.ymt`.** The drawable entry for a glowing garment and a plain one
  differ only in `propMask`, which is about component exclusion.
