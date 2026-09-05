# Project Format

A project is a **folder**. The `.bitirimclothing` file is that folder zipped.

That split is deliberate: a folder is easy to inspect, diff and back up while you are working, and a
single file is what you actually want when moving a project to another machine or handing it to
someone else.

**Current schema: 2.** Schema 1 projects open automatically; see [Migration](#migration).

## Layout

```
MyJacket/
├── project.json      the manifest (below)
├── assets/           base .ydd, .ytd and .ymt, copied in on creation
├── layers/           images imported as layers
├── textures/         composited PNG per variation, named <variationId>.png
├── previews/         decoded base texture, thumbnails
├── exports/          built resources (excluded from the package)
└── backups/          timestamped project.json snapshots
    ├── schema1/      the file as it was before a migration
    └── before-recovery-<stamp>/
```

Base assets are **copied into the project**, not referenced in place. That is what lets a project
move between machines and still open.

## `project.json`

```json
{
  "schemaVersion": 2,
  "appVersion": "0.2.0",
  "name": "Real Test Jacket",
  "author": "Bitirim",
  "createdAt": "2026-08-31T12:00:00+00:00",
  "updatedAt": "2026-09-01T02:08:00+00:00",

  "male": true,
  "ymtTemplatePath": "assets/mp_m_freemode_01.ymt",

  "assets": [
    {
      "id": "a1b2c3d4",
      "name": "Jacket",
      "component": "Jbib",
      "drawableIndex": 0,
      "baseAssetOrigin": "Imported",
      "baseYddPath": "assets/jbib_000_u.ydd",
      "baseYtdPath": "assets/jbib_diff_000_a_uni.ytd",
      "includeInExport": true,
      "variations": [
        {
          "id": "var0",
          "name": "Original",
          "index": 0,
          "texturePath": "textures/var0.png",
          "layers": []
        }
      ]
    }
  ],
  "activeAssetId": "a1b2c3d4",

  "outfits": [],
  "settings": { "textureSize": 512 },

  "export": {
    "resourceName": "bcc_ui_test_jbib",
    "dlcName": "bcc_m_jbib_uitest",
    "textureFormat": "BC3",
    "manifestMode": "Stream",
    "preset": "fivem-resource",
    "history": []
  }
}
```

### Project fields

| Field | Notes |
|---|---|
| `schemaVersion` | Refused if higher than the application understands, with a clear message |
| `male` | Selects `mp_m_freemode_01` or `mp_f_freemode_01` |
| `ymtTemplatePath` | Shared by every garment: addon metadata is derived from one real ped `.ymt` |
| `assets` | The garments. Never empty once opened |
| `activeAssetId` | Which garment the editor is working on. Navigation, not an edit |
| `outfits` | Saved component combinations — see [character-system.md](character-system.md) |
| `settings.textureSize` | Authoring resolution: 256, 512, 1024 or 2048 |

### Garment fields

| Field | Notes |
|---|---|
| `component` | One of the 12 engine slots: `Head` `Berd` `Hair` `Uppr` `Lowr` `Hand` `Feet` `Teef` `Accs` `Task` `Decl` `Jbib` |
| `drawableIndex` | Which drawable slot this garment represents |
| `baseAssetOrigin` | `None` · `Imported` · `Library` · `Mock` — `Mock` blocks export |
| `baseYddPath` etc. | **Project-relative.** Resolved through a check that refuses to leave the project root |
| `includeInExport` | Whether an export writes this garment |
| `variations[].index` | 0 becomes GTA variant letter `a`; a dense 0..n-1 run, capped at 26 |
| `variations[].texturePath` | Set when the variation's texture is saved; absent means unsaved |

### Layers

```json
{
  "id": "a1b2c3",
  "name": "logo.png",
  "kind": "Image",
  "visible": true,
  "locked": false,
  "opacity": 1.0,
  "blendMode": "multiply",
  "parentId": null,
  "x": 128, "y": 96, "width": 171, "height": 168, "rotation": 0,
  "source": "layers/logo.png"
}
```

`kind` is one of `Base` `Image` `Text` `Fill` `Brush` `Generated` `Shape`
`Gradient` `Group`.

| Kind | Extra fields |
|---|---|
| `Text` | `text` `fontFamily` `fontSize` `bold` `italic` `color` `outlineColor` `outlineWidth` `letterSpacing` `align` `shadowBlur` `shadowColor` |
| `Fill` | `color` |
| `Shape` | `shape` (`rectangle`/`ellipse`/`line`) `color` `filled` `strokeColor` `strokeWidth` |
| `Gradient` | `color` `color2` `gradientType` (`linear`/`radial`) `angle` |
| `Brush` | `strokes[]` |
| `Group` | `collapsed`; children carry `parentId` |

`blendMode` is the canvas composite operation, stored as the wire value
(`multiply`, `screen`, …) so the compositor never has to translate. `null`
means normal.

Compositing is deterministic: the same stack always produces the same pixels, which is what lets the
viewport preview, the saved PNG and the exported YTD agree with each other. Layers paint from the
bottom of the array upward, matching how the layers panel reads.

### Brush strokes

```json
{
  "color": "#c81e1e",
  "size": 28,
  "erase": false,
  "softness": 0.35,
  "points": [104, 88, 111, 93, 120, 101],
  "clip": { "kind": "ellipse", "x": 40, "y": 60, "width": 220, "height": 180 }
}
```

`points` is flattened x,y pairs in texture space. `clip` records the selection
the stroke was painted inside, so replay reproduces exactly the same pixels.

**This is new in schema 2 and it closed the format's one known asymmetry.**
Schema 1 painted strokes onto an offscreen canvas: the pixels only existed in
memory, so a brush layer's content did not survive closing the project.

## Migration

Opening a schema-1 project upgrades it in memory, then:

1. copies the original to `backups/schema1/project.json` **before** anything can
   overwrite it;
2. reports what it did — the notes appear as a toast and in the log.

The migration reads the schema-1 JSON directly rather than through the current
model, so it keeps working as the model moves on.

| Schema 1 | Schema 2 |
|---|---|
| top-level `component`, `drawableIndex`, `baseAssetOrigin`, `baseYddPath`, `baseYtdPath`, `variations` | `assets[0]` |
| — | `assets[]`, `activeAssetId`, `outfits`, `settings` |
| brush layer with no stroke data | kept, and the notes say the strokes could not come across |

Normalisation runs on every open, whatever the version: variation indices are
re-indexed into a dense run, unknown blend modes are dropped, opacities are
clamped, and a `parentId` pointing at a non-group is cleared.

A project from a **newer** schema is refused with a readable message rather than
opened partially.

## Safety

Project files are **data only**. Nothing in them is executed on load.

- Every path read from a project is canonicalised and checked to stay inside the project root
  (`ProjectService.ResolveInProject`).
- Package extraction rejects any entry whose destination would escape the workspace (zip slip).
- Unknown fields are ignored rather than trusted.
- There is no field in the document that names a command, a script or an
  executable — and a test asserts that, so adding one would fail the build.

## Saving

`project.json` is written to a temp file and moved into place, so an interrupted save never leaves a
half-written manifest. When a project was opened from a `.bitirimclothing` package, saving rewrites
the package too.

## Autosave and recovery

Two jobs on two clocks, both driven by a 60-second tick:

- **The recovery snapshot** runs every tick while the project is dirty. It costs
  a few kilobytes and bounds how much a crash can take.
- **The real save** runs on the user's autosave interval (Settings → Editor,
  default 5 minutes). Writing over someone's file more often than they asked is
  not ours to decide.

A snapshot is only *offered* at start-up when it differs from what is on disk.
If they match, nothing was lost and there is nothing to ask about. Restoring
keeps the current file under `backups/before-recovery-<stamp>/`, so choosing
"Restore" is never the destructive option.
