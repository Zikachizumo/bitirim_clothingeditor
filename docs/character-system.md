# Character & Outfit System

**Read this first: outfits are stored, not rendered.**

The data model is real — slots, drawable and texture indices are saved in the
project and travel with it. What does not exist is a rendered character: the
viewport shows one garment at a time, and this build cannot put several
garments on a dressed freemode body.

The UI says exactly that, on the panel, with an `EXPERIMENTAL` badge. The
alternative — drawing the garments floating in roughly the right places and
calling it a character — would look right in a screenshot and lie to anyone who
relied on it.

## The model

```
Outfit
├── id, name, male
└── slots[]
      ├── component   0–11, the engine's index
      ├── drawable    index within that component
      ├── texture     variation index
      ├── yddPath     the file backing this slot, when the library has one
      └── enabled
```

Stored under `outfits[]` in `project.json`. Saving, updating and deleting an
outfit all go through the command stack, so they undo like anything else.

## Component slots

Taken from the host's component list, which reads them from the verified
`PedComponent` enum — not a hard-coded table in the UI.

| Index | Prefix | Label |
|---|---|---|
| 0 | `head` | Head |
| 1 | `berd` | Mask |
| 2 | `hair` | Hair |
| 3 | `uppr` | Torso / Arms |
| 4 | `lowr` | Legs |
| 5 | `hand` | Bags & Parachute |
| 6 | `feet` | Shoes |
| 7 | `teef` | Accessories |
| 8 | `accs` | Undershirt |
| 9 | `task` | Body Armour |
| 10 | `decl` | Decals |
| 11 | `jbib` | Tops / Jackets |

Note `berd` is masks and `teef` is accessories, not what their names suggest.
These were confirmed three ways in Phase 1 — see `asset-format.md` §2.

The outfit editor orders them the way a person dresses (tops, undershirt, legs,
shoes, …), which is display order only.

## Backing files

When the asset library holds a drawable at a slot's component and index for the
outfit's ped, the row shows a dot and clicking the slot loads that drawable into
the viewport. When it does not, the row shows `—`: the slot is still saved, it
just cannot be previewed.

That is the honest state to show. A slot pointing at nothing is a real thing to
record — you are describing a look you intend to build.

## Base body

An outfit records which ped it was built for (`mp_m_freemode_01` or
`mp_f_freemode_01`), because the drawable indices only mean something against
one of them. The project's ped is set in the Garment panel; the outfit inherits
it and can be changed independently.

## What rendering a character would need

Not implemented, and each of these is a real piece of work:

1. **The freemode body meshes.** `mp_m_freemode_01` / `mp_f_freemode_01` ship
   their own head, torso, arms, legs and feet drawables. They would have to be
   loaded from the user's game files — we ship no Rockstar assets.
2. **The skeleton.** Every clothing drawable is skinned to the freemode
   skeleton. Posing them together means reading the `.yft` and applying the bind
   pose, then skinning each drawable to it.
3. **Draw order and hiding.** Components hide parts of the body and of each
   other, driven by the ped's own metadata. Getting this wrong produces a
   character with arms through its sleeves.
4. **Per-component texture binding.** Each drawable's diffuse comes from its own
   dictionary at its own variation index.

Until all four exist, an outfit is a saved intention rather than a preview.

## Status

| | |
|---|---|
| Outfit model, save / load / duplicate / delete | **REAL** |
| Slot backing by a library drawable | **REAL** |
| Loading one slot's drawable into the viewport | **REAL** |
| Dressed character preview | **NOT IMPLEMENTED** |
| Clipping detection between components | **NOT IMPLEMENTED** — heuristics are not detection |
| Anything here seen in a running game | **NO** |
