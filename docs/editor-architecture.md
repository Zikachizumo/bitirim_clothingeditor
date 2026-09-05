# Editor Architecture (v0.2)

v0.2 is built **on top of** the v0.1 backend, not instead of it. The parser,
asset service, texture decode, `.ytd` and `.ymt` writers and the export path
are unchanged in behaviour; what is new sits above them.

```
┌─────────────────────────────────────────────────────────────────┐
│ React UI (WebView2)                                             │
│   store.ts ──► call('op', args) ──► bitirim-host-v1             │
├─────────────────────────────────────────────────────────────────┤
│ .NET host                                                       │
│   HostOperations  ──► AppSession.Apply ──► CommandBus  (new)    │
│                   ──► ProjectService ──► ProjectMigrator (new)  │
│                   ──► RecoveryService                    (new)  │
│                   ──► ThumbnailService                   (new)  │
│                   ──► ProjectValidator                   (new)  │
│                   ──► AddonResourceBuilder.BuildManyAsync(new)  │
├─────────────────────────────────────────────────────────────────┤
│ Python asset service (unchanged)  ──► fivefury ──► RAGE files   │
└─────────────────────────────────────────────────────────────────┘
```

## What changed, and why it did not need a rewrite

### Schema 2: `assets[]`

Schema 1 held exactly one garment, its fields at the top of the document.
Schema 2 moves them into `ClothingAsset` so a project can carry a whole pack.

Every existing caller — export, validation, the host operations — was written
against `document.Component`, `document.Variations` and friends. Rather than
edit all of them, those names survive as **projections onto the active
garment**:

```csharp
[JsonIgnore]
public ClothingAsset Active =>
    Assets.FirstOrDefault(a => a.Id == ActiveAssetId) ?? Assets[0];

[JsonIgnore]
public PedComponent Component
{
    get => Active.Component;
    set => Active.Component = value;
}
```

`Active` creates a garment if the list is empty, so it never throws on a
hand-edited or partially written document.

This is the adapter the brief asks for: new capability, existing API intact.

### One door for edits

`AppSession.Apply(document, label, mergeKey)` is the only method that replaces
the open document, and it records the change on the command stack. That is what
makes undo cover everything rather than a chosen few operations — see
[`undo-redo.md`](undo-redo.md).

### Multi-garment export

`AddonResourceBuilder.BuildAsync` (one garment) now delegates to
`BuildManyAsync` with a single-element list, so the v0.1 output is
byte-identical: same file names, same DLC name, no suffix. Only packs of two or
more get the per-garment `_<component>_<index>` suffix, because two DLCs of the
same name would overwrite each other's metadata.

## Layout

```
┌──────────────────────────────────────────────────────────────────────┐
│ File  Edit  View  Tools  Export  Help                                │
├────────────┬────────────────────────────────────┬────────────────────┤
│ Library    │                                    │ Inspector          │
│  · assets  │                                    │  Layer · Asset ·   │
│  · garment │           3D VIEWPORT              │  Mesh · Texture ·  │
│            │                                    │  Material· Project │
│            │                                    ├────────────────────┤
│            │                                    │ Layers             │
│            │                                    │ Variations         │
├────────────┴────────────────────────────────────┴────────────────────┤
│ Model │ UV │ Texture │ Material │ Character │ Validation │ Developer │
├──────────────────────────────────────────────────────────────────────┤
│ Status · asset · component · drawable · texture · vertices · undo    │
└──────────────────────────────────────────────────────────────────────┘
```

Both side panels resize by dragging, collapse to a rail (never to nothing — a
collapsed panel you cannot find is worse than a narrow one), and their state is
persisted. `F11` gives the viewport the whole window.

The Developer tab appears only in developer mode.

## State

Zustand, one store. The document itself is **not** owned by the UI: every
mutation is a round-trip through `project.update`, and the host's reply is the
new truth. That is why undo, autosave, recovery and validation all agree with
what is on screen — there is one copy of the document, and it lives in .NET.

Transient editor state (current tool, zoom, pan, selection, which panel is
open) is UI-local, because none of it belongs in a saved project.

## Capability gating

The UI binds affordances to `app.capabilities`. `writeYdd` is `false`, so:

- there is no mesh editing surface at all;
- the UV view has no edit mode and says "UV editing: unavailable";
- the material inspector is read-only and explains why;
- validation reports the limitation as **INFO** — a fact about this build, not
  a defect in the user's work.

The point of gating on the capability flag rather than on discipline is that an
unproven feature is structurally unofferable.

## Background work

Thumbnails render off the UI thread, two at a time, on demand as cards scroll
into view. Asset parsing, texture decode and export all run async with a
cancellation token; the export wizard's Cancel button pulls the real token.

## New services

| Service | Job |
|---|---|
| `ProjectMigrator` | Schema upgrades. Reads the raw JSON, never trusts the model. |
| `RecoveryService` | Crash snapshots. Offers only what differs from disk. |
| `ThumbnailService` | Cache + concurrency around the rasteriser. |
| `MeshThumbnailRenderer` | Z-buffered software rasteriser over real geometry. |
| `ProjectValidator` | Whole-project rules, categorised and targeted. |
| `CommandBus` | The undo stack. |
| `BinaryInspector` | Read-only hex windows and byte search. |
| `ExportPresets` | Named export configurations. |

All of them are in `Bitirim.Clothing.Editor`, which has no window and no
WebView — which is why 143 tests can drive them directly.
