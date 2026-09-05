# Asset Browser & Thumbnails

The browser lists the `.ydd` drawables under the configured library folder.
Gender and component are **inferred** from the file name and path, and reported
as `—` when they cannot be determined rather than defaulted to something
plausible.

## Scanning

`AssetLibraryService.Scan` walks the folder, at most 8 levels deep, up to 5,000
drawables. For each `jbib_004_u.ydd` it looks for `jbib_diff_004_*.ytd` beside
it and reports them as that drawable's texture variations.

Inaccessible folders are skipped, and the result carries a note saying so — the
browser never silently shows a partial library as if it were complete.

## Thumbnails

Cards show a **real render of the real mesh**, not an icon and not a picture of
the texture.

```
library.thumbnail(path)
  └── backend.ExtractMeshAsync(path, blob, lod: "low")
        └── MeshThumbnailRenderer.ReadBlob(blob, layout)
              └── z-buffered rasteriser ──► 256×256 PNG
```

### Why a software rasteriser

Thumbnails are generated in the background, off the UI thread, in a WPF process
that has no spare swap chain to render into. At 256 px with the low LOD of a
clothing drawable this costs a few milliseconds.

The camera is orthographic and front-on, so two drawables of the same component
are directly comparable in the grid. Lighting is a single key plus a weak fill —
enough to read a garment's folds without inventing a material it does not have.

RAGE is Z-up; the thumbnail is drawn with Z as the vertical axis and Y as depth,
matching the conversion the viewport bakes into its geometry.

### When it cannot render

`MeshThumbnailRenderer.Render` returns `false` rather than writing an empty
square, and the card falls back to the component prefix with the reason in its
tooltip. A blank thumbnail presented as a render would be a lie about the asset.

### Cache

Cached PNGs live under `cache/thumbnails/`. The key is
`SHA256(path | size | mtime | version | pixels)[..24]`, so:

- an asset replaced on disk re-renders;
- an unchanged asset is never decoded twice, however many times the library is
  opened.

Failures are remembered too — a drawable that cannot be rendered is asked about
once, not once per card per re-render.

Settings → Performance shows the cache size and clears it.

### Concurrency

Two renders at a time (`ThumbnailService.MaxConcurrent`), because each one
round-trips through the Python backend. Requests for the same asset share one
in-flight task.

Cards request their thumbnail through an `IntersectionObserver` with a 160 px
margin, so a library of a few thousand drawables does not fire a few thousand
renders on open.

## Filtering and sorting

| Filter | Source |
|---|---|
| Search | Name and component prefix |
| Gender | All / male / female, inferred from the path |
| Component | The verified 12-slot list from the host |
| Favourites | Per machine, stored in browser storage |

Sort by name, date, component, drawable index or size.

## Views

**Grid** — cards with thumbnail, name, component, drawable index, variation
count, and hover actions (preview, use as base).

**List** — a denser table with the same data plus file size, and a favourite
toggle per row.

## Actions

| Action | Effect |
|---|---|
| Double-click / 👁 | Loads the drawable into the viewport |
| Use | Sets it as the active garment's base asset, copying it into the project |
| ★ | Favourite |
| … | Change the library folder |

"Use" copies the file **into the project**, which is what lets a project move
between machines and still open.

## Global search

`Ctrl+P` searches drawables, recent projects, garments in the open project and
their variations. Debounced at 180 ms, because the library scan behind it is not
free. Arrow keys and Enter to choose.

## Where a Browse dialog opens

`asset.pickFile` starts the `.ydd`, `.ytd` and `.ymt` pickers in the configured
asset library, so the first Browse of a run lands where the game files are
rather than wherever Explorer happened to be. After a pick, that kind follows
the user — being pulled back to the library on every Browse is worse than never
being taken there. Image pickers are never redirected; reference art comes from
anywhere.

`FilePickerStart.Resolve` decides this, and it is a pure function so the rules
are testable without a window.

### The path has to be canonical

The shell resolves `InitialDirectory` through `SHCreateItemFromParsingName`,
which rejects a forward-slash path. The failure is not a dialog opening in the
wrong place: `ShowDialog` throws, and the Browse button does nothing at all.

This is not hypothetical. The folder picker and hand-typed settings both produce
paths like `C:/bcc/fixtures`, which is what the settings file actually held the
first time this shipped. Every path is now run through `Path.GetFullPath` before
it reaches the dialog, and the whole lookup is wrapped so that a bad library
folder can never take the picker down with it.
