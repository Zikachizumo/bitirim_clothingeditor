# Phase 1 — Export Spike Results

**Date:** 2026-08-30 · **Backend:** fivefury 0.4.20 · **Runtime:** CPython 3.12.10 (embeddable)
**Fixtures:** extracted from the developer's own legal GTA V (Legacy) installation, never committed

---

## 1. Executive summary

The export pipeline works, up to the one step this environment cannot perform.

Every binary format the product depends on can be read, and the two that matter for a
texture-authoring workflow — `.ytd` and `.ymt` — can be **written**, verified by re-parsing our own
output. A complete, correctly-named FiveM addon clothing resource is generated end to end by a
working CLI, from a real drawable plus a PNG, in about 100 ms. The block encoder produces a surface
that is **byte-for-byte the same size and shape as the game's own** for the same input dimensions.

Two findings changed the plan:

1. **The YDD writer is not safe for clothing.** A read → write round-trip preserves geometry,
   skinning, materials, UVs and embedded textures *exactly*, but silently recomputes the bounding
   volume wrong for skinned ped drawables. The export path therefore **copies the source YDD
   byte-for-byte** rather than rewriting it. This costs nothing for the texture workflow and removes
   a whole class of "garment vanishes at distance" bug. Phase 0 guessed this was how the reference
   product works; it turns out to be the correct engineering choice regardless.

2. **Shipping without a user-installed Python is confirmed possible.** A trimmed, relocated
   embeddable runtime (77 MB) with no pip runs the backend and parses real assets.

**What is not proven:** the assets have not been loaded by a real FiveM client. There is no FXServer
on this machine and the loop requires launching GTA V and joining a server. Per the Phase 1 success
criteria, **this spike is not yet "successful"** — it is *ready to be tested*. Two ready-to-install
resources are staged for that test (section 8).

---

## 2. Tested pipeline

```
GTA V install (user's own, legal)
   └─ x64v.rpf → models/cdimages/streamedpeds_mp.rpf
        ├─ mp_m_freemode_01/jbib_000_u.ydd            186,568 B
        ├─ mp_m_freemode_01/jbib_diff_000_a_uni.ytd   163,350 B
        └─ mp_m_freemode_01.ymt                         3,489 B
                    │
                    ▼  read (fivefury, via asset service)
        structural model: 3 LODs, 1005/400/64 verts, skinned,
        2 ped materials, 3 embedded textures, 1 external diffuse
                    │
   synthetic PNG ───┤  encode (BCnEncoder.NET, host-side)
   512x512          │  → BC3, 8 mips, 349,520 B
                    ▼
        ytd_replace_texture  → new .ytd  (internal name preserved)
        ymt_build_addon      → new .ymt  (single component, N textures)
        byte copy            → new .ydd  (renamed into DLC namespace)
                    │
                    ▼  read-back verification (every file re-parsed)
        bcc_test_jbib_stream/
        ├── fxmanifest.lua
        ├── README.md
        └── stream/
            ├── mp_m_freemode_01_bcc_m_jbib_test.ymt                       947 B
            ├── mp_m_freemode_01_bcc_m_jbib_test^jbib_000_u.ydd        186,568 B
            ├── mp_m_freemode_01_bcc_m_jbib_test^jbib_diff_000_a_uni.ytd 9,407 B
            └── mp_m_freemode_01_bcc_m_jbib_test^jbib_diff_000_b_uni.ytd 5,076 B
                    │
                    ▼
        [ NOT YET DONE ] FXServer → FiveM client → freemode ped
```

Compare against the reference product's own export, read off the video at t=13: same triple, same
`^` naming, same ~1 KB YMT. Our YMT is 947 B; theirs was 1 KB.

---

## 3. FiveFury findings

Verified by inspecting the installed package and running it, not by reading the README.

**What is genuinely there:** full read/write for `YDR`/`YDD`/`YTD`/`YMT`/`YBN`/`YFT`/`YCD`, RPF7
archives including **encrypted retail archives** (key derivation from the game executable works —
`load_game_keys` returned a 32-byte AES key and 101 NG keys in 1.9 s), DLC packaging, and a ped
variation model that names the real schema (`CPVComponentData`, `CPVDrawblData`, `CPVTextureData`,
`availComp`, `numAvailTex`, `texId`, `clothData`).

**What is missing and had to be built host-side:** there is **no block encoder**. `Texture.from_raw`
accepts already-compressed surfaces; nothing in the package produces them. This is why
`BCnEncoder.NET` sits in the .NET layer. In hindsight this split is desirable anyway — compression is
the expensive step and belongs where it can be profiled and parallelised.

**Sharp edges hit during the spike:**

- `Drawable.has_skeleton` / `has_joints` are properties, not methods. Calling them raises
  `TypeError: 'bool' object is not callable`.
- Clothing drawables report `has_skeleton == False`. They are skinned (blend indices and weights are
  present for every vertex) but carry no embedded skeleton — they bind to the ped's skeleton from
  the `.yft`. Do not treat `has_skeleton == False` as "not rigged".
- `Ydd.name` follows the *output filename*, not the source. Saving `jbib_000_u.ydd` as
  `rt_jbib_000_u.ydd` silently renames the dictionary.
- Several `CPedVariationInfo` fields come back as unresolved hashes. The component index is
  `0xD12F579D`; recovered by correlating its value distribution against per-component drawable
  counts (0:46, 1:8, 2:16, 3:16, 4:16, 5:9, 6:16, 7:16, 8:16, 9:10, 10:7, 11:16 — an exact match).

---

## 4. YDD support

| Capability | Status | Evidence |
|---|---|---|
| Read | **SUPPORTED** | `jbib_000_u.ydd` parsed in ~10 ms (29 ms cold) |
| Preserve materials | **SUPPORTED** | `ped_palette.sps` + `ped_default_palette.sps`, all parameters and texture refs identical after round-trip |
| Preserve UVs | **SUPPORTED** | 2 UV sets per mesh, unchanged |
| Preserve weights | **SUPPORTED** | 1005 blend indices + 1005 blend weights on the high LOD, unchanged |
| Preserve skeleton | **N/A for clothing** | Clothing drawables reference the ped skeleton rather than embedding one |
| Preserve shader data | **SUPPORTED** | Shader name/file hashes, render bucket, all 13 material parameters identical |
| Preserve embedded textures | **SUPPORTED** | `jbib_normal_000` (BC1), `jbib_pall_000_x` (BC3), `jbib_spec_000` (BC1) — SHAs identical |
| Preserve LODs | **SUPPORTED** | high 1005v/4242i, med 400v/1146i, low 64v/213i — unchanged |
| **Write (round-trip)** | **PARTIALLY SUPPORTED** | Everything above survives, **but the bounding volume is recomputed incorrectly** |
| Write (edited geometry) | **NOT TESTED** | Out of Phase 1 scope; gated on the bounding-box defect |

### The bounding-box defect

```
original     min (-0.33874, -0.14932,  0.90493)  max (0.34051, 0.17531, 1.57700)  r 0.50459
round-tripped min (-0.33887, -0.14941, -0.09521)  max (0.34033, 0.17529, 0.57715)  r 0.38911
```

X and Y survive. Z is shifted down by ~1.0 m and the radius shrinks by 23 %. The writer appears to
recompute bounds from raw vertex positions without accounting for the skinning bind pose. Assigning
`bounding_box_min/max/center/radius` before saving does **not** help — the values are writable but
the writer overwrites them unconditionally.

A wrong bounding volume on clothing means wrong culling: the garment disappears or pops at certain
camera distances and angles. That is a subtle, hard-to-diagnose bug to ship.

**Decision:** `AddonResourceBuilder` copies the source `.ydd` byte-for-byte and only renames it.
`capabilities.writeYdd` is reported as **false**, so nothing in the future UI can offer YDD writing
until this is fixed. Worth reporting upstream.

---

## 5. YTD support

| Capability | Status | Evidence |
|---|---|---|
| Read | **SUPPORTED** | 2.0 ms median; name, dimensions, format, mips, usage flags, surface bytes |
| Write | **SUPPORTED** | Round-trip is structurally identical and the surface SHA is unchanged (`733a019e...`) |
| Replace texture | **SUPPORTED** | Donor surface swapped in, target's lookup name preserved, validation clean |
| Add texture | **SUPPORTED (untested)** | `Ytd.texture(t, replace=False)` exists; clothing needs one texture per dictionary, so unused |
| Remove texture | **SUPPORTED (untested)** | `Ytd.remove_texture(name)` exists |
| Preserve mipmaps | **SUPPORTED** | 8 levels in, 8 levels out |
| Preserve compression | **SUPPORTED** | BC3 in, BC3 out; BC1 donor stays BC1 |
| Encode from image | **SUPPORTED (host-side)** | BCnEncoder.NET; BC1/BC3/BC4/BC5/BC7 |

Round-trip is not byte-identical (163,350 → 162,304 B) because the writer's zlib settings differ.
That is expected and harmless; the decoded surface is identical, which is the bar that matters.

**Encoder correctness signal.** For a 512×512 input the encoder produces BC3 with 8 mip levels and
**349,520 bytes of surface data — exactly matching the game's own `jbib_diff_000_a_uni`.** This only
came out right after capping the mip chain at 4×4: BCnEncoder defaults to a full chain down to 1×1,
which produced 10 levels and 349,552 bytes. BC formats compress in 4×4 blocks, so 2×2 and 1×1 levels
occupy a whole block each and carry nothing. The game stops at 4×4 and now so do we.

---

## 6. YMT support

| Capability | Status | Evidence |
|---|---|---|
| Read | **SUPPORTED** | 6.2 ms; `CPedVariationInfo` fully decoded |
| Write | **SUPPORTED** | Round-trip structurally identical (3,489 → 3,102 B) |
| Modify drawable metadata | **SUPPORTED** | `aDrawblData3` rebuilt with a single drawable |
| Modify texture count | **SUPPORTED** | `numAvailTex` and per-drawable `aTexData` regenerated, `texId` 0..N-1 |
| Create component metadata | **SUPPORTED** | `availComp` rewritten to 255 everywhere except the shipped slot |
| Create custom clothing metadata | **SUPPORTED** | Complete single-component addon written and re-parsed |
| Cloth data (`.yld`) | **NOT SUPPORTED** | `clothData.ownsCloth` is carried through, but authoring cloth physics is out of scope |

### Decoded structure

```
CPedVariationInfo
├── bHasTexVariations / bHasDrawblVariations / bHasLowLODs / bIsSuperLOD
├── availComp        [12]  index into aComponentData3, or 255 = this pack does not use the slot
├── aComponentData3  [n]   CPVComponentData { numAvailTex, aDrawblData3[] }
│     └── CPVDrawblData { aTexData[ CPVTextureData{ texId, distribution } ], clothData{ownsCloth} }
├── aSelectionSets   [ ]
├── compInfos        [n]   per-drawable; component index lives in field 0xD12F579D
├── propInfo               CPedPropInfo { numAvailProps, aPropMetaData, aAnchors }
└── dlcName                joaat hash
```

The base male ped confirms the model: `availComp == (0..11)`, 12 component entries, and JBIB
carrying `numAvailTex: 237` — matching the 237 `jbib_diff_*.ytd` entries actually present in
`streamedpeds_mp.rpf`.

### How the addon YMT is built

Rather than synthesise `CPedVariationInfo` from scratch — which would mean guessing at a dozen
hashed fields — `ymt_build_addon` **prunes a known-good ped YMT** down to one component with one
drawable, then rewrites `availComp`, `numAvailTex` and `aTexData`. Every surviving field value is one
the game already accepts. Output: 947 bytes, `availComp = [255 × 11, 0]` for a JBIB pack.

---

## 7. Clothing metadata support

Component indices and file naming are now confirmed against ground truth rather than documentation:

| Idx | Prefix | Slot | Idx | Prefix | Slot |
|---|---|---|---|---|---|
| 0 | `head` | Head | 6 | `feet` | Shoes |
| 1 | `berd` | Mask | 7 | `teef` | Accessories |
| 2 | `hair` | Hair | 8 | `accs` | Undershirt |
| 3 | `uppr` | Torso / arms | 9 | `task` | Body armour |
| 4 | `lowr` | Legs | 10 | `decl` | Decals |
| 5 | `hand` | Bags & parachute | 11 | `jbib` | Tops / jackets |

Confirmed three independent ways: fivefury's `PedComponent` enum, the `0xD12F579D` distribution in
the real ped YMT, and the actual entry names in `streamedpeds_mp.rpf`.

**Naming, verified from real game entries:**

```
jbib_000_u.ydd                 drawable, zero-padded to 3, 'u' = universal
jbib_diff_000_a_uni.ytd        external diffuse, 'a' = variation letter, 'uni' = universal race
jbib_normal_000                embedded normal   (inside the .ydd)
jbib_spec_000                  embedded specular (inside the .ydd)
jbib_pall_000_x                embedded palette  (inside the .ydd)
```

**The finding that shapes the whole product:** the material's `DiffuseSampler` references the plain
name `jbib_diff_000_a_uni`. In an addon pack the *file* becomes
`mp_m_freemode_01_<dlc>^jbib_diff_000_a_uni.ytd`, but the texture name **inside** the dictionary must
stay plain, or the material will not resolve it. This answers the question Phase 0 left open
(`asset-format.md` §8 q4) — and the exporter preserves the internal name by reading it back out of
the source drawable's material rather than assuming it.

Because the diffuse is external and normal/spec are embedded, a re-skin only ever needs to write a
`.ytd`. That is the structural reason the MVP can ship without a YDD writer.

---

## 8. FiveM test result

**NOT PERFORMED — and this is the gate on Phase 2.**

There is no FXServer on this machine (searched `C:` and `D:`; only the FiveM *client* is installed).
Completing the loop needs a running server, GTA V launched, a client joined, and a clothing menu
driven to the new drawable. That is a human-in-the-loop step.

Two ready-to-install resources are staged at `C:\bcc\export\`:

| Resource | Difference |
|---|---|
| `bcc_test_jbib_stream` | Relies on the streamer picking up everything under `stream/` |
| `bcc_test_jbib_datafile` | Adds an explicit `data_file` declaration for the `.ymt` |

Both are otherwise identical, and both pass validation and read-back.

**Why two.** Community guidance genuinely conflicts on whether a ped-variation `.ymt` in `stream/`
needs an explicit declaration. Rather than pick one and present it as settled, the exporter takes
`--manifest-mode stream|datafile` so the in-game test decides. This is the single largest remaining
unknown in the pipeline.

**To run the test:**

```bash
cp -r /c/bcc/export/bcc_test_jbib_stream <server>/resources/
```

Then `ensure bcc_test_jbib_stream` in `server.cfg`, restart the server (a `refresh` alone does not
reload streamed assets), and step the **Jackets** drawable to the end of the list. The test texture is
a deliberately garish magenta/cyan checker with a yellow border and a white diagonal — if it appears,
there is no ambiguity that it worked, and the diagonal makes mirrored or rotated UVs obvious.

Check the server console for streaming errors and the client console (F8) for asset load failures.

---

## 9. Performance

Median of 7 runs, warm process. Machine: Windows 10, the developer's workstation.

| Operation | Median | Min | Max | Input |
|---|---|---|---|---|
| YDD parse | 10.4 ms | 9.8 | 725.3¹ | 186,568 B |
| YTD parse | 2.0 ms | 2.0 | 3.7 | 163,350 B |
| YMT parse | 6.2 ms | 6.1 | 11.6 | 3,489 B |
| YTD write (replace) | ~12 ms | — | — | 512² BC3 |
| YMT write (addon) | ~8 ms | — | — | 947 B out |
| YDD copy | 0.7 ms | — | — | 186,568 B |

¹ First call includes process start and `import fivefury`. Cold start is ~700 ms and happens once
per session — the service is long-lived.

**Block encoding (the expensive step):**

| Format | 512×512 | 2048×2048 |
|---|---|---|
| BC1 | 84 ms | 406 ms |
| BC3 | 85 ms | 414 ms |
| BC7 | 485 ms | **6,342 ms** |

**Complete addon export, 2 texture variations at 512²: ~196 ms** (136 ms encode + 29 ms YDD read +
23 ms YTD writes + 8 ms YMT + copy).

**The number that matters for UX:** BC7 at 2048² takes **6.3 seconds**. That cannot happen on the
preview path. Preview compositing stays uncompressed in the UI layer; BC encoding runs only on
export, as a cancellable background job with progress. BC3 at 2048² (414 ms) is tolerable
interactively if needed.

---

## 10. Packaging strategy

**Verified:** the embeddable CPython distribution is a self-contained folder — no installer, no
registry keys, no PATH changes, nothing machine-wide. Copied to a different path with `pip` deleted,
it still imports the backend and parses real assets.

| Configuration | Size |
|---|---|
| Full runtime after `pip install fivefury` | 114 MB |
| Trimmed (no pip, no `__pycache__`, no numpy tests) | **77 MB** |

Breakdown: numpy 55 MB (34 + 21 MB of libs), fivefury 16 MB, trimesh 4.5 MB, CPython ~13 MB.

`tools/bootstrap-runtime.ps1` reproduces the whole setup in about a minute.

**Not trimmable:** `trimesh/resources` is imported at load time even though we never touch mesh
import — deleting it breaks `import fivefury` outright. Further shrinking would mean lazy-importing
inside fivefury, which is an upstream change.

**MAX_PATH.** `pip install` fails with `WinError 206` when site-packages lands under a deep path;
this repo's worktree path alone was enough to trigger it. The installed product must target a short
directory, and `Directory.Build.props` already redirects MSBuild output off-tree for the same reason.

---

## 11. Python dependency

> **Can we ship the real `.ydd`/`.ytd`/`.ymt` export pipeline inside a Windows application without
> making the user install Python?**
>
> ## YES

**How it packages.**

```
BitirimClothingCreator/
├── BitirimClothingCreator.exe        .NET 8, WPF host
├── runtime/
│   └── python/                       77 MB embeddable CPython + fivefury (trimmed)
│       ├── python.exe
│       └── Lib/site-packages/{fivefury,numpy,trimesh,cffi,...}
└── assetservice/                     our sidecar (main.py, contract.py, rage.py)
```

The installer lays this down under a short path (`C:\Program Files\Bitirim\ClothingCreator\`), the
host launches `runtime\python\python.exe assetservice\main.py` as a child process, and the user never
learns Python is involved. Nothing is registered system-wide; the portable build is the same folder
zipped.

**Cost:** ~77 MB of installer size and one extra process. **Benefit:** a working RAGE writer we did
not have to write, under a public-domain licence.

**The dependency stays replaceable.** Every fivefury import in the codebase lives in exactly one
file (`src/assetservice/rage.py`). Above it, the application sees only `IRageAssetBackend`. Swapping
in a .NET implementation later means writing one class, not touching the app.

---

## 12. Legal / licence

Re-verified this phase.

| Component | Licence | Verified |
|---|---|---|
| **fivefury 0.4.20** | **The Unlicense** (public domain) | PyPI metadata `license: Unlicense`, GitHub licence API `spdx_id: Unlicense` |
| BCnEncoder.Net 2.2.1 | The Unlicense | GitHub licence API |
| System.Drawing.Common 8.0.10 | MIT | Microsoft |
| CPython 3.12.10 embeddable | PSF License | bundled `LICENSE.txt` |
| numpy / trimesh / cffi / pycparser | BSD-3 / MIT | transitive deps of fivefury |
| xunit | Apache-2.0 | test-only, not shipped |

Nothing GPL. Nothing unlicensed. **No CodeWalker or Sollumz code was read or copied** — the joaat
implementation is written from the published algorithm description, and the format understanding
came from parsing real files plus public documentation.

**Outstanding for commercialisation** (not blocking Phase 2):
- Generate `THIRD-PARTY-NOTICES.md` including the PSF licence for the bundled runtime.
- Confirm the numpy/trimesh BSD/MIT attribution requirements are satisfied by that file.
- The product ships **no Rockstar assets**. Fixtures live outside the repo and `.gitignore` blocks
  `*.ydd`, `*.ytd`, `*.ymt`, `*.yft`, `*.rpf` outright.

---

## 13. Known limitations

1. **In-game verification not done.** The pipeline is unproven at its last step.
2. **YDD writing is disabled**, so mesh and UV editing remain gated (see section 4).
3. **The manifest form is unresolved** — hence two staged variants (section 8).
4. **One drawable per pack.** The exporter emits drawable `000` per addon DLC, matching the reference
   product. Multi-garment packs need a different YMT shape and are untested.
5. **Legacy only.** All fixtures came from GTA V Legacy. fivefury exposes `GameTarget.GTA5_ENHANCED`
   but nothing was tested against it. The user has both editions installed.
6. **Cloth physics (`.yld`) is not authored.** `ownsCloth` is carried through unchanged.
7. **No prop support.** `propInfo` is emptied for component packs; hats and glasses need `_p` packs.
8. **Texture variations capped at 26** (a–z), an engine constraint, enforced with a clear error.
9. **The addon YMT needs a donor.** It is derived from a real ped YMT, so the product must ship a
   path to one — the user's own game install — rather than synthesising it.
10. **Race variants unhandled.** Everything assumes `uni`.

---

## 14. Blockers

| # | Blocker | Severity | Resolved by |
|---|---|---|---|
| **B1** | Assets never loaded by a real FiveM client | **Critical** | User runs the staged test (section 8) |
| B2 | Which manifest form is correct | High | Same test — try `stream`, then `datafile` |
| B3 | Legacy vs Enhanced target | Medium | User states which their server runs; then re-test |
| B4 | YDD bounding-box defect blocks all mesh/UV editing | Medium | Upstream fix, or our own YDD writer — post-MVP |
| B5 | Where template drawables come from for end users | Medium | Product decision; the game-install path now demonstrably works |

B1 is the only one that blocks Phase 2.

---

## 15. Recommended architecture

Phase 0's proposal survives contact with reality, with one correction.

```
Bitirim.Clothing.Core          IRageAssetBackend, DTOs, naming, joaat, ITextureEncoder
Bitirim.Clothing.FiveFury      FiveFuryRageAssetBackend — process supervision + JSON-RPC
Bitirim.Clothing.Textures      BcnTextureEncoder — BC1..BC7, GTA-matching mip chain
Bitirim.Clothing.Validation    ValidationEngine — GREEN / YELLOW / RED
Bitirim.Clothing.Export        AddonResourceBuilder — naming, layout, manifest, read-back
src/assetservice/              Python sidecar; rage.py is the ONLY fivefury importer
tools/Bitirim.Clothing.Tester  CLI harness
```

**The correction:** Phase 0 assumed texture encoding would sit behind the backend. It cannot —
fivefury has no encoder. Encoding is host-side, and the backend contract deals only in
already-compressed surfaces. This is better: the expensive step is in the profilable language, and
`capabilities.encodeTexture` is reported `false` so the split is explicit rather than implied.

**Confirmed sound:** the capability-gating mechanism did its job the first time it was tested. The
YDD bounding-box defect turned into `writeYdd: false`, which structurally prevents any future UI from
offering a feature we cannot deliver correctly. That is exactly what brief §61 asks for, enforced by
architecture rather than by discipline.

**Bulk transfer:** encoded surfaces go host → service via a temp file with the path in the RPC, not
base64 in JSON. A 2048² BC7 surface is 5.6 MB.

---

## 16. Phase 2 plan

**Do not start until B1 is resolved.**

If the in-game test **passes**:

1. Record which manifest mode worked; make it the default and delete the other path.
2. Harden the asset service: cancellation, progress streaming, crash-restart, structured logging.
3. Add the read operations the UI will need — mesh geometry export as a DTO for the viewport,
   UV extraction, embedded texture decode to RGBA.
4. Add a raw-surface read op so the texture editor can start from the existing diffuse.
5. Repeat the whole pipeline against **GTA V Enhanced** and record the differences.
6. Test a second component (`lowr` or `feet`) and a female ped to confirm nothing is jbib-specific.
7. Then, and only then, Phase 3 (desktop shell) from the Phase 0 roadmap.

If the in-game test **fails**:

1. Try `--manifest-mode datafile`.
2. If still failing, compare our output against a known-working third-party addon pack field by
   field — the exporter's `inspect` command exists for exactly this.
3. Suspect the internal-name question first: whether the drawable's own dictionary name or embedded
   texture names also need the DLC prefix. Our current answer (filename only) is reasoned from the
   material reference but is not yet confirmed by a loading asset.
4. Only after those, question the YMT structure.

---

## Appendix: reproducing this

```bash
# one-time
./tools/bootstrap-runtime.ps1 -Destination C:\bcc\python

# environment
export BCC_PYTHON=C:/bcc/python/python.exe
export BCC_ASSETSERVICE=<repo>/src/assetservice/main.py

dotnet build tools/Bitirim.Clothing.Tester -c Release
dotnet test tests/Bitirim.Clothing.Tests -c Release      # 27 passing
```

```bash
BitirimClothing.Tester.exe capabilities
```

```bash
BitirimClothing.Tester.exe inspect C:/bcc/fixtures/jbib_000_u.ydd
```

```bash
BitirimClothing.Tester.exe export-addon --resource bcc_test_jbib_stream --dlc bcc_m_jbib_test --component jbib --ydd C:/bcc/fixtures/jbib_000_u.ydd --ytd C:/bcc/fixtures/jbib_diff_000_a_uni.ytd --ymt-template C:/bcc/fixtures/mp_m_freemode_01.ymt --textures C:/bcc/testtex/test-a.png,C:/bcc/testtex/test-b.png --out C:/bcc/export --manifest-mode stream
```

Fixtures are extracted from the developer's own GTA V install by
`spike/extract3.py`-equivalent logic; they are never committed.
