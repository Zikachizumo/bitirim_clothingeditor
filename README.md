# BITIRIM CLOTHING CREATOR

A Windows desktop clothing development studio for GTA V and FiveM.

Open a real `.ydd` drawable, see it in 3D, inspect its UV layout, paint its diffuse texture, and
export a FiveM addon clothing resource — without Blender, CodeWalker, OpenIV or Sollumz.

**Version 0.2.0 · Windows 10 / 11 · x64**

---

## What it does today

| | |
|---|---|
| **Reads real GTA assets** | `.ydd` drawables, `.ytd` texture dictionaries, `.ymt` ped metadata — parsed, not faked |
| **3D viewport** | The actual garment geometry, correctly oriented. Material, solid, wireframe, UV-checker and normals modes; orthographic or perspective; camera presets; bounding box |
| **Asset browser** | **Real rendered thumbnails** of the actual geometry, cached; search, filters, sort, favourites |
| **UV view** | The drawable's real UV islands, drawn from its vertex data. Zoom, pan, grid, texture overlay |
| **Texture editor** | 13 tools — brush, eraser, fill, picker, line, rectangle, ellipse, gradient, text, image, marquee, lasso, select — over a layer stack with groups and 16 blend modes, starting from the garment's own diffuse |
| **Undo / redo** | A real command stack over every edit, with labels: *Undo Brush stroke* |
| **Texture variations** | Up to 26 per garment, mapped onto the GTA `a`–`z` variant letters |
| **Multi-garment projects** | A jacket, trousers and shoes in one project, exported as one resource |
| **Validation** | Categorised findings with severities, each one clickable through to its cause |
| **Export** | 5-step wizard, four presets, a plan preview, progress, cancel and history. Real `.ydd` + `.ytd` + `.ymt` + `fxmanifest.lua`, correctly named, every file re-parsed after writing |
| **Projects** | Folder-based, portable as a single `.bitirimclothing` package, autosaved, with crash recovery |
| **Developer tools** | Raw asset inspector, read-only hex view, filtered log console |

### What it does not do yet

Mesh editing, UV editing, weight painting, rendered character preview and AI generation are
**not implemented**. Where the interface would show them, it says so plainly rather than offering a
control that does nothing. See [docs/known-limitations.md](docs/known-limitations.md).

### Upgrading from 0.1.0

Projects open automatically. The original `project.json` is kept under
`backups/schema1/` before anything touches it, and the application says what it
changed. See [docs/project-format.md](docs/project-format.md#migration).

> **FiveM export is experimental.** The application writes real RAGE binaries and verifies them by
> reading them back, but **no exported resource has yet been loaded by a running FiveM client**.
> Nothing in this product claims otherwise.

---

## Installation

### Installer

Run `BitirimClothingCreator-0.2.0-Setup.msi`. Installs to
`C:\Program Files\Bitirim\Clothing Creator`, adds a Start Menu entry, and associates
`.bitirimclothing` files.

### Portable

Extract `BitirimClothingCreator-0.2.0-Portable.zip` and run `BitirimClothingCreator.exe`. It keeps
everything in a `userdata` folder beside the executable and writes nothing else to the machine.

### Requirements

- Windows 10 (1809 or newer) or Windows 11, 64-bit
- **Microsoft Edge WebView2 Runtime** — ships with Windows 11 and current Windows 10. If it is
  missing the application says so and links to Microsoft's free installer.

You do **not** need .NET, Python, Node.js, pip or Visual Studio. The .NET runtime is published into
the application folder and a trimmed CPython runtime is bundled beside it.

---

## Getting started

1. **Settings → Paths** — point the asset library at a folder of `.ydd` drawables.
2. **New Project** — pick gender, component, and a base drawable. You also need the matching `.ytd`
   and a ped `.ymt` (for example `mp_m_freemode_01.ymt` from your own game files) to export.
3. **Texture tab** — paint. The 3D preview updates live.
4. **Save texture** on each variation.
5. **Validation tab** — run the checks.
6. **Export** — produces a resource folder ready to drop into a server.

No project? Choose **Mock asset** in the wizard to try the editor on synthetic geometry. Anything
built on it is badged `MOCK` and cannot be exported — there is no real drawable to ship.

---

## Project format

A project is a folder; the `.bitirimclothing` file is that folder zipped.

```
MyJacket/
├── project.json      schema-versioned manifest
├── assets/           the base .ydd, .ytd and .ymt, copied in so the project travels
├── layers/           imported images
├── textures/         composited output per variation
├── previews/         decoded base texture and thumbnails
├── exports/          built resources
└── backups/          timestamped snapshots
```

Project files contain data only. Nothing in them is executed on load, and paths are checked to stay
inside the project root. See [docs/project-format.md](docs/project-format.md).

---

## Export output

```
bcc_ui_test_jbib/
├── fxmanifest.lua
├── README.md
└── stream/
    ├── mp_m_freemode_01_bcc_m_jbib_uitest.ymt
    ├── mp_m_freemode_01_bcc_m_jbib_uitest^jbib_000_u.ydd
    └── mp_m_freemode_01_bcc_m_jbib_uitest^jbib_diff_000_a_uni.ytd
```

The source `.ydd` is copied byte-for-byte rather than rewritten — see
[docs/known-limitations.md](docs/known-limitations.md#the-ydd-writer-is-disabled) for why that is the
correct behaviour and not a shortcut.

---

## Building from source

```bash
./tools/bootstrap-runtime.ps1 -Destination C:\bcc\python
```

```bash
./build/build.ps1 -PythonRuntime C:\bcc\python -Output C:\bcc\release
```

Produces the application folder, the portable zip and the MSI. Requires the .NET 8 SDK, Node.js 18+,
and the WiX tool (`dotnet tool install --global wix`) for the installer.

Run the tests with:

```bash
dotnet test tests/Bitirim.Clothing.Tests -c Release
```

---

## Documentation

| Document | |
|---|---|
| [desktop-app.md](docs/desktop-app.md) | How the application is put together |
| [editor-architecture.md](docs/editor-architecture.md) | What v0.2 added, and why nothing needed rewriting |
| [undo-redo.md](docs/undo-redo.md) | The command stack, merging, and what resets it |
| [texture-editor.md](docs/texture-editor.md) | Tools, layers, compositing, stroke recording |
| [asset-browser.md](docs/asset-browser.md) | Library scanning, thumbnails, the cache |
| [character-system.md](docs/character-system.md) | Outfits — and why nothing renders a character |
| [video-parity.md](docs/video-parity.md) | Feature-by-feature against the reference video |
| [ipc-protocol.md](docs/ipc-protocol.md) | The two protocols: host ↔ UI, and host ↔ asset service |
| [project-format.md](docs/project-format.md) | `project.json`, the package format, migration |
| [export-pipeline.md](docs/export-pipeline.md) | How an export is built and what "succeeded" means |
| [asset-format.md](docs/asset-format.md) | GTA/RAGE format research, verified |
| [phase-1-results.md](docs/phase-1-results.md) | Format support matrix and benchmarks |
| [known-limitations.md](docs/known-limitations.md) | What does not work, and why |
| [legal-and-oss.md](docs/legal-and-oss.md) | Licence audit of every dependency |
| [roadmap.md](docs/roadmap.md) | Phases and MVP definition |

---

## Legal

Ships **no Rockstar assets**. Contains **no code from CodeWalker or Sollumz**. Every bundled
dependency is permissively licensed — see [docs/legal-and-oss.md](docs/legal-and-oss.md).

You need your own legal copy of GTA V to obtain clothing assets to work on.

Not affiliated with Rockstar Games or Cfx.re.
