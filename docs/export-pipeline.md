# Export Pipeline

**Status after Phase 1:** implemented and verified up to, but not including, loading in a real game
client. Every claim below is either backed by a passing test or explicitly marked unverified.
See [phase-1-results.md](phase-1-results.md) for the evidence.

## 1. Pipeline

```
Project (or CLI arguments)
  │
  ├─ 1. Read source drawable ──────► structural model + validation      ✅ verified
  │        the material's DiffuseSampler tells us the internal
  │        texture name the exported YTD must keep
  │
  ├─ 2. Composite per slot ────────► LayerStack → RGBA                  ⬜ Phase 7
  │        (Phase 1 takes a PNG directly)
  │
  ├─ 3. Encode ────────────────────► BCnEncoder.NET, host-side          ✅ verified
  │        BC1/BC3/BC7 + mip chain capped at 4x4
  │
  ├─ 4. Write YTD, one per slot ───► asset service                      ✅ verified
  │
  ├─ 5. Copy YDD ──────────────────► byte-for-byte, renamed             ✅ verified
  │        NOT rewritten -- see section 5
  │
  ├─ 6. Build YMT ─────────────────► single-component CPedVariationInfo ✅ verified
  │
  ├─ 7. Lay out resource ──────────► fxmanifest.lua + stream/ + README  ✅ verified
  │
  ├─ 8. Read back ─────────────────► re-parse every written file        ✅ verified
  │
  ├─ 9. Validate ──────────────────► GREEN / YELLOW / RED               ✅ verified
  │
  ├─10. Preview changes + backup ──► before touching a server folder    ⬜ Phase 10
  │
  └─11. Load in FiveM ─────────────► freemode ped wears the garment     ❌ NOT YET TESTED
```

## 2. Naming — verified against real game files

Every pattern below was read out of `streamedpeds_mp.rpf` in a retail GTA V installation, not taken
from a tutorial.

```
jbib_000_u.ydd                drawable; component prefix, 3-digit index, sex suffix (u|m|f)
jbib_diff_000_a_uni.ytd       external diffuse; 'a' = variation letter, 'uni' = universal race
jbib_normal_000               embedded normal   (lives inside the .ydd)
jbib_spec_000                 embedded specular (lives inside the .ydd)
jbib_pall_000_x               embedded palette  (lives inside the .ydd)
```

Addon DLC file names, matching what the reference product emits:

```
<ped>_<dlcName>.ymt
<ped>_<dlcName>^<component>_000_u.ydd
<ped>_<dlcName>^<component>_diff_000_<variant>_uni.ytd
```

```
mp_m_freemode_01_bcc_m_jbib_test.ymt
mp_m_freemode_01_bcc_m_jbib_test^jbib_000_u.ydd
mp_m_freemode_01_bcc_m_jbib_test^jbib_diff_000_a_uni.ytd
```

**The caret applies to the file name only.** The texture name *inside* the dictionary stays plain
(`jbib_diff_000_a_uni`), because that is the string the drawable's `DiffuseSampler` parameter looks
up. `AddonResourceBuilder` does not assume this — it reads the actual name out of the source
drawable's material and preserves it.

One addon pack carries **one drawable at index 000**, regardless of what the pack is named. The game
appends it after the vanilla range.

## 3. Component slots — verified

| Idx | Prefix | Slot | Idx | Prefix | Slot |
|---|---|---|---|---|---|
| 0 | `head` | Head | 6 | `feet` | Shoes |
| 1 | `berd` | Mask | 7 | `teef` | Accessories |
| 2 | `hair` | Hair | 8 | `accs` | Undershirt |
| 3 | `uppr` | Torso / arms | 9 | `task` | Body armour |
| 4 | `lowr` | Legs | 10 | `decl` | Decals |
| 5 | `hand` | Bags & parachute | 11 | `jbib` | Tops / jackets |

## 4. Resource layout

```
<resourceName>/
├── fxmanifest.lua
├── README.md          what this is, how to install, how to verify
└── stream/
    ├── <ped>_<dlc>.ymt
    ├── <ped>_<dlc>^<comp>_000_u.ydd
    └── <ped>_<dlc>^<comp>_diff_000_<v>_uni.ytd   x N variations
```

### The manifest — the one genuinely open question

Community guidance conflicts on whether a ped-variation `.ymt` under `stream/` is picked up by the
streamer on its own or needs an explicit declaration. Rather than assert one, the exporter emits
either on request:

`--manifest-mode stream` (default):

```lua
fx_version 'cerulean'
game 'gta5'

files {
    'stream/mp_m_freemode_01_<dlc>.ymt',
}
```

`--manifest-mode datafile` adds:

```lua
data_file 'DLC_ITYP_REQUEST' 'stream/mp_m_freemode_01_<dlc>.ymt'
```

Both variants are staged and ready to test. **Whichever loads in game becomes the default and the
other path is deleted.** Until then neither is presented as correct.

## 5. Why the YDD is copied, not rewritten

A read → write round-trip through the current backend preserves geometry, LODs, UVs, blend indices
and weights, materials, shader parameters and embedded textures **exactly** — and then recomputes
the bounding volume wrongly for skinned ped drawables:

```
original      z 0.90493 .. 1.57700   radius 0.50459
round-tripped z -0.09521 .. 0.57715  radius 0.38911
```

A wrong bounding volume means wrong culling — the garment pops or vanishes at certain distances.
Since a re-skin never needs to touch the mesh, the exporter copies the source `.ydd` byte-for-byte
and renames it. `capabilities.writeYdd` is reported `false`, which structurally prevents any UI from
offering YDD writing until this is fixed.

## 6. Texture encoding

| Input | Output |
|---|---|
| RGBA, power-of-two | BC1 / BC3 / BC7 with a mip chain down to 4×4 |

The mip chain stops at 4×4 because BC formats compress in 4×4 blocks — 2×2 and 1×1 levels occupy a
whole block each and carry nothing. The game's own textures stop there too. Getting this right is
what made our 512² BC3 output **exactly 349,520 bytes, matching `jbib_diff_000_a_uni` byte for
byte in size and mip count**.

Encoding is host-side (BCnEncoder.NET) because the asset backend stores compressed surfaces but
cannot produce them.

**Cost, measured:** BC3 is ~85 ms at 512² and ~414 ms at 2048². BC7 is ~485 ms at 512² and
**~6.3 s at 2048²**. BC7 at full size must be a cancellable background job with progress; it can
never sit on the preview path.

## 7. Safe export to a live server

Not yet implemented (Phase 10). The design stands:

1. Enumerate what already exists at the destination.
2. Show a change preview: *N added, M overwritten, K unchanged*, by name.
3. Require explicit confirmation — `You are about to overwrite 4 existing files.`
   → `[Cancel]` `[Backup & Export]`
4. Copy the destination to `backups/<yyyy-MM-dd_HH-mm-ss>/` first.
5. Write to a temp directory, then move into place, so a failure never leaves a half-written resource.
6. Offer "Open Export Folder".

The app does **not** restart the resource or touch the server process. A server restart is required
for streamed assets — a `refresh` alone will not reload them — and telling the user that is more
honest than a bridge that silently reloads things.

## 8. What "export succeeded" is allowed to mean

Only this: real RAGE resource files were written, **every one of them was re-parsed successfully
after writing**, and the resource layout is complete. Read-back is not optional — it is a step in
`AddonResourceBuilder.BuildAsync` and its failure produces an `export.readback` error finding.

If any step falls back to a placeholder, the UI says so and the operation is reported as partial.
There is no code path in which a PNG-only or JSON-only output is described as a successful FiveM
export.

And until an exported pack has been loaded by a real FiveM client, this document, the CLI and the
future UI all say **unverified** — not "supported".
