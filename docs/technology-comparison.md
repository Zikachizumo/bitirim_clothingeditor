# Technology Comparison & Architecture Decision

The brief asks for the stack to be justified before any code is written. This is that
justification. The decision hinges on one fact established in `asset-format.md`: the only
licence-clean, actively-maintained library that can *write* GTA V clothing formats is Python.

## 1. What actually constrains the choice

| Constraint | Consequence |
|---|---|
| Real YDD/YTD/YMT writing is mandatory (brief §28, §61, §74) | The asset engine must be built on something that can genuinely do it — that is `fivefury` (Python, Unlicense) or a from-scratch RSC7 packer |
| Windows 10/11 desktop, installer + portable (brief §2) | Rules out anything browser-only |
| Must feel like a professional tool (brief §4, §44) | Favours a mature UI stack with real layout/docking, not hand-rolled widgets |
| 3D viewport with skinned mesh + skeleton (brief §7, §21) | Needs a real renderer with skinning, not a toy |
| A 2D texture/UV editor with layers and brushes (brief §11–14) | Needs a fast 2D canvas; the web platform is genuinely excellent at this |
| Local-first, no cloud dependency (brief §49–50) | No server-side rendering or asset processing |
| Must run on low/mid hardware (brief §40) | Rules out anything that ships a full game engine runtime |

## 2. Candidate stacks

### A. C# / .NET 8+ host · WebView2 · React + TypeScript UI · Three.js viewport · Python sidecar
The brief's own preferred shape, plus a Python asset process.
- **+** WebView2 ships with Windows; no Chromium bundle. React/TS gives a fast, professional UI and the 2D canvas story is excellent (Canvas2D/WebGL layer compositing, exactly what the texture editor needs).
- **+** .NET host owns the filesystem, project format, settings, DPAPI key storage, installer, updater — all first-class on Windows.
- **+** Three.js renders a skinned GTA drawable perfectly well; this is not a demanding scene (one garment, ~10–50k tris).
- **+** The Python asset engine runs as a separate process, so a parser crash cannot take down the app, and heavy work is off the UI thread by construction.
- **−** Three processes (host, WebView, Python) to supervise, install, and debug.
- **−** Bundling a CPython runtime adds ~40–60 MB to the installer.
- **−** IPC design work: bitmaps and meshes must cross process boundaries efficiently.

### B. Pure C# — WPF/Avalonia + HelixToolkit.SharpDX, no web layer
- **+** One process, one language, native performance, straightforward debugging.
- **+** DirectX viewport is a good fit for skinned mesh + bone display.
- **−** The 2D layered texture editor is a large amount of custom work in WPF; the web platform gives it nearly free.
- **−** Still needs the Python sidecar for asset I/O, *or* a from-scratch RSC7 packer, *or* forking discontinued RageLib. All three are expensive.
- **−** Slowest path to a UI that looks like the brief's reference images.

### C. Electron + Node, everything in JS/TS
- **+** Fastest UI iteration, best 2D canvas ecosystem.
- **−** No credible JS path to writing RAGE binary formats; would need the Python sidecar anyway, so this saves nothing on the hard part.
- **−** ~150 MB installer, high idle RAM — directly contradicts brief §40.
- **−** Explicitly discouraged by brief §3.

### D. Python-first — PySide6/Qt + fivefury in-process
- **+** Zero IPC for the asset engine; the hardest part becomes the easiest part.
- **+** One runtime to ship.
- **−** Qt's 2D canvas and 3D viewport work is heavy; the UI would take far longer to reach the quality bar.
- **−** Qt licensing (LGPL dynamic linking obligations, or a commercial licence) needs care for a paid product.
- **−** Weakest fit for the "premium game-dev tool" look the brief insists on.

## 3. Decision

**Option A**, with one deliberate refinement: the Python sidecar is a **narrow, replaceable asset
I/O service**, not a general back end.

```
┌─────────────────────────────────────────────────────────┐
│  BitirimClothingCreator.Desktop   (C# / .NET 8, WPF host)│
│  window chrome · installer · updater · settings ·        │
│  DPAPI secret storage · file system · process supervision│
│                          │                               │
│              ┌───────────┴───────────┐                   │
│              ▼                       ▼                   │
│  ┌───────────────────────┐  ┌──────────────────────────┐│
│  │ WebView2              │  │ AssetService (Python)     ││
│  │ React + TypeScript    │  │ fivefury + Pillow         ││
│  │ · shell & panels      │  │ · read/write ydd ytd ymt  ││
│  │ · UV + texture editor │  │ · DDS encode/decode       ││
│  │ · Three.js viewport   │  │ · mesh extraction         ││
│  │ · layers, brushes     │  │ · validation probes       ││
│  └───────────────────────┘  └──────────────────────────┘│
│         JSON-RPC over stdio  ·  bulk data over shared    │
│         memory-mapped files (meshes, textures)           │
└─────────────────────────────────────────────────────────┘
```

**Why the sidecar is drawn narrowly.** It exposes ~12 operations (open drawable, extract mesh,
read texture dictionary, write texture dictionary, write ped meta, build pack, validate…) behind a
versioned contract defined in C#. Everything else — projects, undo, layers, settings, export
orchestration — lives in .NET/TS. That means if `fivefury` stalls, breaks its API, or turns out to
have gaps, we swap the *implementation* behind the same contract (a forked RageLib, our own packer,
or a CLI shell-out) without touching the app. Given that `fivefury` is at `0.4.x`, designing for
its replaceability is not paranoia, it is the responsible reading of the risk.

**Why not put the viewport in .NET.** The garment scene is small and the win from having the
viewport share a process — and a selection model — with the UV/texture editor is large. 3D↔UV
selection sync (brief §11) is nearly free when both live in the same JS context and very awkward
across a process boundary.

**Bulk data does not go through JSON-RPC.** Meshes and 2048² textures move via memory-mapped files
with the RPC carrying only a handle. A 2048×2048 RGBA buffer is 16 MB; base64 in JSON would be
both slow and wasteful.

## 4. Accepted costs

| Cost | Mitigation |
|---|---|
| Bundled CPython (~40–60 MB) | Embeddable distribution, trimmed; it is a one-time installer cost, not a runtime cost |
| Three processes | The .NET host supervises the sidecar: health checks, restart on crash, surfacing failures as user-readable errors (brief §42) |
| Python↔C# marshalling | Narrow contract + memory-mapped bulk transfer; measured in the Phase 1 spike |
| `fivefury` is pre-1.0 | Pinned version, contract-level isolation, our own fixture-based test suite that fails loudly on upgrade |
| WebView2 runtime dependency | Present on all supported Windows 11 and current Windows 10; the Evergreen Bootstrapper covers the rest |

## 5. Supporting library choices

| Need | Choice | Licence |
|---|---|---|
| BCn/DDS encode in .NET (if we do it host-side) | [BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET) | Unlicense |
| RAGE asset I/O | [fivefury](https://github.com/Hancapo/fivefury) | Unlicense |
| Local metadata / thumbnail cache | SQLite (`Microsoft.Data.Sqlite`) | MIT |
| 3D viewport | Three.js | MIT |
| Installer | WiX v4 or Inno Setup | MS-RL / free |

Every third-party dependency in the shipping product is permissively licensed. No GPL code, no
unlicensed code, nothing derived from CodeWalker or from the reference product.

## 6. What would change this decision

If the Phase 1 spike shows `fivefury` cannot write a game-loadable YTD, the calculus shifts: the
Python dependency stops paying for itself and Option B (pure .NET, forked MIT RageLib, our own RSC7
packer) becomes the better trade despite being slower — because at that point we are writing the
packer either way, and we may as well write it in the host language. **Do not commit to the stack
until the spike answers that question.**
