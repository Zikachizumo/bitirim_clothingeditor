# Desktop Application

How Bitirim Clothing Creator is assembled, and why.

## 1. Layers

```
┌──────────────────────────────────────────────────────────────────┐
│  BitirimClothingCreator.exe        .NET 8, WPF, self-contained   │
│                                                                  │
│  ┌────────────────────────────┐  ┌────────────────────────────┐  │
│  │  WebView2                  │  │  AssetService              │  │
│  │  React + TypeScript        │  │  CPython 3.12 (bundled)    │  │
│  │  Three.js viewport         │  │  fivefury 0.4.20           │  │
│  │  Canvas texture editor     │  │                            │  │
│  └────────────────────────────┘  └────────────────────────────┘  │
│        ▲  JSON over WebView msg        ▲  JSON-RPC over stdio     │
│        │                               │                          │
│  ┌─────┴───────────────────────────────┴────────────────────────┐ │
│  │  HostBridge  ·  HostOperations  ·  AppSession                │ │
│  ├──────────────────────────────────────────────────────────────┤ │
│  │  Editor      projects, settings, logging, asset library      │ │
│  │  Export      addon resource builder                          │ │
│  │  Validation  rule engine                                     │ │
│  │  Textures    BCnEncoder.NET encode + DDS decode              │ │
│  │  Core        IRageAssetBackend, naming, joaat, DTOs          │ │
│  └──────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

The interface holds no business logic. It cannot touch the filesystem, cannot reach the asset
backend, and cannot run a process. Everything goes through the host bridge
([ipc-protocol.md](ipc-protocol.md)).

## 2. Why WebView2 rather than native WPF

The two heaviest pieces of this application are a 2D layered texture editor and a 3D viewport that
share a selection model. The web platform gives both cheaply — Canvas2D for compositing, WebGL for
the viewport, and one process so 3D↔UV↔texture synchronisation is a function call rather than an IPC
round trip. Rebuilding that in WPF would be months of work for no user-visible gain.

WebView2 also ships with Windows, so the installer carries no browser engine. The cost is one
external dependency, handled explicitly (see [known-limitations.md](known-limitations.md)).

## 3. Why a Python sidecar

Writing RAGE binaries is not serialisation — it is a paged memory-layout problem with pointer
fix-ups and silent failure modes. The only licence-clean, actively-maintained implementation is
[`fivefury`](https://github.com/Hancapo/fivefury), which is Python and public domain.

The sidecar is deliberately narrow: it speaks only in files and buffers, never in projects or
layers. Every `fivefury` import in the codebase lives in one file, `src/assetservice/rage.py`.
Above `IRageAssetBackend`, nothing knows Python exists. That is what makes the dependency
replaceable — `fivefury` is pre-1.0, and designing for its replacement is the responsible reading of
that risk.

The sidecar is stateless between calls, so the host can restart it at any time without losing work.

## 4. Startup

1. WPF window opens with a **native** splash — instant, because it does not wait on WebView2.
2. The asset backend starts in the background. Failure is not fatal: the app opens and reports it.
3. WebView2 initialises and loads the UI from a virtual host mapping (`https://app.bitirim.local/`),
   not `file://`, so a real origin and a strict CSP apply.
4. The UI calls `ui.ready`; the splash fades out.

Navigation away from that origin is blocked, and external links open in the user's real browser.

## 5. Capability gating

The asset backend reports what it can actually do at startup:

```json
{ "readYdd": true, "readYtd": true, "readYmt": true,
  "writeYtd": true, "writeYmt": true,
  "writeYdd": false, "writeYddGeometry": false }
```

The interface binds affordances to these flags. When `writeYdd` is false, the mesh and weight
surfaces are unavailable *with the reason shown*. This is the mechanism that makes "never fake a
feature" structural rather than a matter of discipline — a capability we have not proven cannot be
offered as working, because the UI has nothing to bind to.

## 6. Real vs mock

Two things could be mistaken for each other, so they never are:

- **Real asset** — parsed from a `.ydd` on disk. Badged `REAL ASSET` in the viewport.
- **Mock asset** — synthetic torso geometry so the editor is usable without a GTA installation.
  Badged `MOCK` in the viewport, the status bar, the project header and the properties panel.
  **Mock projects cannot be exported**; the export dialog refuses with an explanation.

## 7. Values the application does not have

The status bar, properties panel and asset cards render an em dash for anything unknown — never a
zero, never a plausible guess. `PropertyRow` enforces this: a null value becomes `—` styled as
absent. Vertex counts, component indices and texture counts are blank until something real supplies
them.

## 8. Error handling

No user ever sees a stack trace.

- Host operations catch everything. `EditorException` and `RageAssetException` carry a message
  already written for a person; anything else becomes "Something went wrong while handling that
  request" plus a reference.
- The reference points at a line in `logs/errors.log` with the full exception.
- WPF's dispatcher exception handler does the same for anything that escapes the UI thread.

Logs live in `%APPDATA%\Bitirim\ClothingCreator\logs` (or `userdata\logs` in a portable build):
`application.log`, `errors.log`, `export.log`.

## 9. Portable vs installed

A `portable.marker` file beside the executable switches all user data to a `userdata` folder in the
application directory. Nothing is written to `%APPDATA%` and nothing is registered. The same binaries
serve both distributions.

## 10. Project layout

```
src/
├── Bitirim.Clothing.Core/         IRageAssetBackend, DTOs, naming, joaat, ITextureEncoder
├── Bitirim.Clothing.FiveFury/     the backend implementation: process supervision + JSON-RPC
├── Bitirim.Clothing.Textures/     BCnEncoder.NET encode, DDS decode, thumbnail rasteriser
├── Bitirim.Clothing.Validation/   rule engine, finding categories
├── Bitirim.Clothing.Export/       addon resource builder (single and multi-garment)
├── Bitirim.Clothing.Editor/       projects, migration, recovery, commands, library,
│                                  thumbnails, validation, export presets, settings, AI
├── Bitirim.Clothing.Desktop/      WPF host, WebView2, HostBridge, HostOperations
├── assetservice/                  Python sidecar (rage.py is the only fivefury importer)
└── ui/                            React + TypeScript
tools/    Bitirim.Clothing.Tester (Phase 1 CLI), bootstrap-runtime.ps1
build/    build.ps1, installer.wxs
tests/    xunit
```

Everything with logic lives below `Bitirim.Clothing.Desktop`, in projects with no
window and no WebView — which is why 143 tests can drive them directly.

## 11. What v0.2 added

The v0.1 backend is unchanged in behaviour. What sits above it grew:

| Area | See |
|---|---|
| Editor architecture, schema 2, layout | [editor-architecture.md](editor-architecture.md) |
| Undo / redo | [undo-redo.md](undo-redo.md) |
| Texture editor and compositing | [texture-editor.md](texture-editor.md) |
| Asset browser and thumbnails | [asset-browser.md](asset-browser.md) |
| Outfits | [character-system.md](character-system.md) |
| Project format and migration | [project-format.md](project-format.md) |
| Parity with the reference video | [video-parity.md](video-parity.md) |

## 12. Keyboard and interface

Shortcuts live in one table (`src/ui/src/state/shortcuts.ts`), are rebindable in
Settings → Editor, and flag clashes. Shortcuts marked *global* — Save, Save As,
Undo, Redo — work while a text field has focus; the rest hold back so they do
not eat a keystroke mid-rename.

Interface scale (Settings → Appearance, 75 %–200 %) sets the root font size, so
every rem-derived size follows. Windows DPI scaling applies on top, which is why
100 % here already follows the system setting.
