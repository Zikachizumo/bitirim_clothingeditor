# Known Limitations

Everything on this page is a real gap. None of it is hidden behind a control that looks like it
works; the interface either omits the feature or states the reason in place.

---

## FiveM export is unvalidated

The application writes real `.ydd`, `.ytd` and `.ymt` files and verifies them by parsing them back.
**No exported resource has been loaded by a running FiveM client.**

The export dialog says so, the generated `README.md` says so, and the home screen says so. Nothing
in the product claims "FiveM verified", "game tested" or "production ready".

Two things are unresolved until that test happens:

1. **Which `fxmanifest.lua` form is correct.** Community guidance disagrees on whether a
   ped-variation `.ymt` in `stream/` needs an explicit `data_file` entry. The export dialog offers
   both forms; whichever loads becomes the default and the other is deleted.
2. **Whether internal names need the DLC prefix.** Only the *file* name carries the `^` prefix in
   our output, because the drawable's `DiffuseSampler` looks up the plain texture name. That is
   reasoned from the material data, not confirmed by a loading asset.

---

## The YDD writer is disabled

`capabilities.writeYdd` is reported **false**, and the export path copies the source drawable
byte-for-byte instead of rewriting it.

This is deliberate. A read → write round-trip through the asset backend preserves geometry, LODs,
UVs, blend indices and weights, materials and embedded textures *exactly* — and then recomputes the
bounding volume wrongly for skinned ped drawables:

```
original      z 0.90493 .. 1.57700   radius 0.50459
round-tripped z -0.09521 .. 0.57715  radius 0.38911
```

A wrong bounding volume means wrong culling: the garment pops or vanishes at certain camera
distances. Copying the file sidesteps the entire problem, and a texture-only workflow never needs to
touch the mesh.

**Everything gated on this:**

- Mesh editing (vertex/edge/face selection, transforms, mirror, smooth)
- UV editing that writes geometry back
- Weight painting
- Material parameter editing

The interface shows these as unavailable with the reason, rather than offering tools whose output
could not be saved.

---

## Not implemented in 0.2.0

| Area | State |
|---|---|
| **AI texture generation** | Implemented against `gemini-3.1-flash-image`. Not yet exercised against a live key by this build — every layer around the call is tested, the call itself is not, because a mocked one would only prove the mock works. What is known-missing: no variations or regenerate history, no "quick fit", and the garment reference image (IMAGE 4) is defined but not yet sent. |
| **Weight / skeleton display** | The backend parses bone weights; the interface neither shows nor edits them. Inspector → Mesh shows `—` rather than a plausible zero. |
| **UV editing** | The UV view is read-only. Zoom, pan, grid, wireframe and texture overlay work; moving a UV vertex does not, because it could not be saved. |
| **Character / outfit preview** | Outfits are **stored**, not rendered. The garment renders alone. See [character-system.md](character-system.md). |
| **Clipping detection** | Not attempted. A heuristic would not be detection. |
| **Safe server export** | Export writes to a folder. The preview → confirm → backup flow for overwriting a live server is designed but not built. |
| **Multi-monitor panel tear-off** | Panels resize, collapse and persist; they cannot be detached into their own window. |
| **Auto-update** | Not built. |
| **Licence / activation** | Not built, by design. |

### Fixed since 0.1.0

| Was | Now |
|---|---|
| Undo / redo disabled | A real command stack covering every edit — [undo-redo.md](undo-redo.md) |
| Brush pixels lost on close | Strokes recorded as data and replayed |
| No thumbnails | Real rendered previews of the actual geometry, cached |
| One garment per project | Multi-garment projects, one exported resource |
| Texture size fixed at 512² | Per-project setting (256 / 512 / 1024 / 2048) |
| Only the addon resource export | Four presets, a plan preview, progress, cancel and history |
| Flat validation list | Categories, severities, and findings that navigate to the cause |
| No crash recovery | Snapshot per tick, offered only when it differs from disk |
| No search | `Ctrl+P` across drawables, projects, garments and variations |
| No raw inspection | Asset inspector and a read-only hex view in developer mode |

---

## Behaviours worth knowing

**One drawable per addon DLC.** The exporter emits drawable `000` per DLC, matching the convention
the reference product uses. A multi-garment project produces several DLCs inside one resource, each
named `<pack>_<component>_<index>` — packing several garments into a *single* DLC would need the
drawable indices inside the metadata to agree with the file names, which is a different and unproven
layout.

**Legacy only.** All format work was verified against GTA V Legacy. The backend exposes an Enhanced
target but nothing has been tested against it.

**Texture size is per project, and applies to every garment in it.** Layer coordinates are in
texture space, so one project cannot mix resolutions.

**Blend modes and groups are baked at composite time.** They cost the exporter nothing, but they are
not something the game understands — what ships is the flattened result.

**Cloth physics (`.yld`) is not authored.** The `ownsCloth` flag is carried through unchanged.

**No ped props.** Hats, glasses and earrings use a separate `_p` pack structure that is not
implemented. The component list covers the 12 real component slots only — the wizard's category list
is the engine's, not a superset.

**Race variants unhandled.** Everything assumes the `uni` universal suffix.

**The base texture must exist to paint on the garment's own diffuse.** With no `.ytd` assigned, the
canvas starts as neutral grey and the toolbar says "No base texture" — it does not invent one.

---

## Environment constraints

**MAX_PATH.** `pip` and MSBuild both fail when paths get long. The bundled runtime must live in a
short directory; the build redirects MSBuild output off-tree for the same reason. The application
manifest sets `longPathAware`, but the Python runtime's own tooling is the binding constraint at
build time, not run time.

**WebView2 is required.** It ships with Windows 11 and current Windows 10. When it is absent the
application shows a dedicated screen with a link rather than failing silently — this is the one
dependency not bundled, because keeping a redistributable current is a worse trade than pointing at
Microsoft's evergreen installer.

**Asset engine is optional at startup.** If the Python runtime or asset service is missing, the
application still opens, reports "Asset engine unavailable" on the home screen and in the status
bar, and mock projects still work. Real assets cannot be read or written.
