# Texture Editor

The texture editor paints the diffuse of one texture variation. It starts from
the garment's **real** decoded diffuse, not a blank square, and the 3D viewport
shows the composite live.

## The composite

```
base diffuse (decoded from the .ytd)
  └── layer stack, bottom of the list first
        ├── group        → rendered to its own canvas, then blended as one
        ├── fill         → whole canvas
        ├── gradient     → linear or radial
        ├── brush        → recorded strokes, replayed
        ├── shape        → rectangle / ellipse / line
        ├── image        → transformed draw
        └── text         → outline, shadow, letter spacing
```

Compositing is **deterministic**: the same stack always produces the same
pixels. That is what lets the viewport preview, the saved PNG and the exported
`.ytd` agree with each other.

Layers paint from the bottom of the array upward, matching how the layers panel
reads.

## Brush strokes are data

Schema 1 painted strokes onto an offscreen canvas. The pixels only ever existed
in memory: a brush layer did not survive closing the project, and a stroke
could not be undone individually.

Schema 2 records the stroke:

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

`points` is flattened x,y pairs in texture space. Points closer than 1.2 px to
the previous one are dropped: fewer points, identical line.

This is what makes a reopened project look identical, and what lets undo step
through a paint session.

### Erasing

An eraser stroke uses `destination-out` **against a scratch surface holding
that layer's own strokes**, not against the whole composite. It removes what
the layer painted; it does not punch a hole through the garment underneath.

### Soft brushes

A soft brush is drawn as a shadowed stroke — one pass instead of per-dab radial
gradients, and it reads the same at these sizes.

## Selection

A selection constrains painting. It is recorded **on the stroke** (`clip`
above), not held as editor state, so replaying a project reproduces exactly the
same pixels. A selection that only existed while you were painting would make
the saved file disagree with what was on screen when it was saved.

| Tool | Shape |
|---|---|
| Marquee (`M`) | Rectangle |
| Lasso (`Q`) | Freehand polygon |

`Esc` clears the selection. With a selection active, the fill tool produces a
shape layer bounded by it rather than a full-canvas fill.

## Tools

| Tool | Key | Produces |
|---|---|---|
| Select | `V` | Moves, scales and rotates the selected layer |
| Brush | `B` | A stroke on a brush layer |
| Eraser | `E` | An erasing stroke on the selected brush layer |
| Fill | `G` | A fill layer, or a shape bounded by the selection |
| Picker | `I` | Samples a colour from the composite |
| Line | `L` | A line shape layer |
| Rectangle | `R` | A rectangle shape layer |
| Ellipse | `O` | An ellipse shape layer |
| Gradient | `D` | A gradient layer |
| Text | `T` | A text layer |
| Image | — | An image layer from a file |
| Marquee | `M` | A rectangular selection |
| Lasso | `Q` | A freehand selection |

`[` and `]` change the brush size.

## Canvas

| | |
|---|---|
| Zoom | `Ctrl` + wheel, or the −/+ buttons |
| Pan | `Alt` + drag, or middle-drag |
| Fit | Fits the canvas to the pane |
| 100 % | Actual size |
| Checkerboard | Toggles the transparency backdrop |

## Transform

The selected layer gets corner handles and a rotation grip. `Shift` while
rotating snaps to 15°. Position, size and rotation are also numeric fields in
the Inspector. Flip horizontal / vertical negate the layer's extents, which
mirrors the drawn content about the layer's own centre.

One drag is **one** undo entry, not one per pointer event — see
[`undo-redo.md`](undo-redo.md).

## Blend modes

All 16 canvas composite operations, named for people (Normal, Multiply,
Screen, Overlay, …) but stored as the wire value the canvas takes. Blend modes
are baked at composite time, so they cost the exporter nothing.

Groups render into their own canvas first, so a group's opacity and blend mode
apply to the composed result rather than to each child in turn.

## Colour

HEX, RGB and HSV, a preset palette of neutrals plus common garment colours, the
last 16 colours used, and an eyedropper that samples the live composite.
Recents persist per machine.

## Saving

**Save texture** composites at the project's authoring resolution and writes
`textures/<variationId>.png`. That PNG is what the exporter encodes to BC1/BC3/
BC7 — the editor and the export never disagree about what the texture is.

The button shows an asterisk while there are unsaved changes.

## Resolution

Authoring resolution is a per-project setting (default 512²). Every texture in
a project shares it, because the layer coordinates are in texture space.

## What the editor does not do

- **Paint on the 3D model.** Painting happens in UV space only.
- **Edit UVs.** The drawable writer is disabled, so a moved UV could not be
  saved. The UV view is read-only and says so.
- **Adjustment layers or masks.** Not implemented; not shown as if they were.
