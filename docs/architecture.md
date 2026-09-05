# Technical Architecture — BITIRIM CLOTHING CREATOR

Read `technology-comparison.md` first for why this stack was chosen.

## 1. Principles

1. **The asset engine is the product.** UI quality matters enormously, but a beautiful shell over a
   fake export is worthless (brief §61, §74). Layering reflects that: nothing in the UI may claim a
   capability the engine has not demonstrated on a real fixture.
2. **One direction of dependency.** UI → Application → Domain ← Infrastructure. The domain knows
   nothing about WebView2, Python, or the filesystem.
3. **Every mutation is a command.** Undo/redo (brief §37) is not bolted on; it is how edits are
   expressed.
4. **Capability gates, not stubs.** A feature the engine cannot deliver is *absent or disabled with
   a stated reason*, never a button that lies.

## 2. Two deployment shapes (a consequence of the video analysis)

The reference product is an in-game FiveM NUI; our product is a desktop application. That is a
deliberate, defensible divergence — a desktop tool gets real file access, real memory, real GPU,
and no game-restart cycle. But two of its features only make sense *because* it lives in the game:

- **Live preview on your own character** — the desktop app cannot do this natively.
- **Export straight into the running server, then wear it** — the desktop app can write the files
  (brief §35) but cannot restart the resource or reload the ped.

The architecture therefore reserves an optional, later **`FiveMBridge`**: a small companion FiveM
resource that the desktop app talks to over the local network to trigger `refresh`/`ensure` and
apply the drawable to the developer's ped. It is out of MVP scope but must not be designed out —
it is the one place the reference product is genuinely better, and closing that gap is a real
differentiator. Nothing in the core may assume a game is running.

## 3. Process and layer map

```
┌──── Desktop host (C#/.NET 8) ─────────────────────────────────────────┐
│ Presentation.Host      window, tray, single-instance, file assoc,     │
│                        installer/updater, crash reporting             │
│ Application            use cases: OpenProject, ApplyTexture, Export…  │
│                        command bus + undo stack + event bus           │
│ Domain                 Project, Garment, Component, Drawable, Slot,   │
│                        LayerStack, Material, ValidationRule           │
│ Infrastructure         ProjectRepository (fs), MetadataCache (SQLite),│
│                        SettingsStore, SecretStore (DPAPI),            │
│                        AssetServiceClient (RPC), TemplateLibrary      │
└───────────────────────────────────────────────────────────────────────┘
        │ WebView2 host bridge                    │ JSON-RPC / stdio
        ▼                                          ▼
┌──── UI (React + TypeScript) ────┐   ┌──── AssetService (Python) ─────┐
│ shell: docking, panels, theme   │   │ rage/    fivefury adapters      │
│ viewport/  Three.js scene       │   │ mesh/    drawable → mesh DTO    │
│ uveditor/  UV overlay + sync    │   │ texture/ DDS ⇄ RGBA, BCn        │
│ texture/   layers, brushes      │   │ pack/    ymt + dlc + fxmanifest │
│ panels/    properties, assets   │   │ validate/ structural probes     │
│ state: zustand + command mirror │   │ contract.py  (versioned)        │
└─────────────────────────────────┘   └─────────────────────────────────┘
```

### Why the RPC boundary sits where it does
The sidecar's contract is **binary in, binary out**: it never knows what a "project" or a "layer"
is. It opens a `.ydd` and returns a mesh DTO; it takes an RGBA buffer and writes a `.ytd`. All
product semantics stay in .NET/TS. This is what makes the Python dependency replaceable
(`technology-comparison.md` §3).

## 4. Repository layout

```
BitirimClothingCreator/
├── src/
│   ├── Desktop/                    C# WPF host, WebView2 bootstrap, single-instance
│   ├── Application/                use cases, command bus, undo stack, events
│   ├── Domain/                     entities, value objects, rules — no I/O
│   ├── Infrastructure/
│   │   ├── Projects/               project.json read/write, backups, recovery
│   │   ├── Assets/                 template library scanning, thumbnail cache
│   │   ├── AssetService/           RPC client, process supervision, contract types
│   │   ├── Persistence/            SQLite metadata + thumbnail cache
│   │   ├── Settings/               settings store, DPAPI secret store
│   │   └── FiveM/                  server path handling, safe-write + backup
│   ├── Export/                     export orchestration, presets, resource layout
│   ├── Validation/                 rule engine + rule set
│   ├── AI/                         ITextureGenerator + providers
│   ├── ui/                         React + TypeScript
│   │   ├── shell/                  layout, docking, theme, command palette
│   │   ├── viewport/               Three.js scene, camera, overlays
│   │   ├── uveditor/               UV overlay, island display, selection sync
│   │   ├── texture/                layer stack, brush engine, compositor
│   │   ├── panels/                 assets, properties, layers, slots, validation
│   │   └── state/                  stores, command mirror, IPC client
│   └── assetservice/               Python sidecar
│       ├── contract.py             versioned request/response schema
│       ├── rage/                   fivefury adapters (the only fivefury imports)
│       ├── mesh/  texture/  pack/  validate/
│       └── main.py                 stdio JSON-RPC loop
├── tests/
│   ├── Domain.Tests/  Application.Tests/  Export.Tests/  Validation.Tests/
│   ├── ui/                         vitest + component tests
│   ├── assetservice/               pytest, fixture round-trips
│   └── fixtures/                   real .ydd/.ytd/.ymt samples + golden outputs
├── docs/                           this folder
├── tools/                          build, packaging, fixture management
├── examples/                       sample projects
└── build/                          installer (WiX), portable zip, CI
```

## 5. Domain model (initial)

```
Project
 ├ metadata: name, author, createdAt, updatedAt, schemaVersion, appVersion
 ├ target:   gender (Male|Female), ped (mp_m_freemode_01|mp_f_freemode_01)
 ├ garment:  Component(index, prefix), drawableIndex, baseAssetRef
 ├ slots:    Slot[]            ← texture variations, letter a..z
 │    └ Slot: letter, textureName, origin(TemplateEdit|Manual), LayerStack
 ├ material: channel refs (diffuse from slot; normal/spec from base drawable)
 ├ export:   resourceName, dlcName, preset, lastExportPath
 └ history:  command log (bounded), autosave marker
```

`LayerStack` is an ordered list of layers (`Image`, `Brush`, `Fill`, `Text`, `Generated`), each with
opacity, visibility, transform and type-specific payload. Compositing is deterministic: the same
stack always produces the same RGBA buffer, which is what makes thumbnails, previews, and exports
consistent and cacheable.

## 6. Project on disk

Directory-based (brief §5), portable (brief §31, §50):

```
MyHoodie/
├── project.json          schema-versioned manifest
├── assets/               copied base drawable(s) — makes the project self-contained
├── layers/               layer payloads (imported images, brush stroke data)
├── textures/             composited outputs per slot
├── previews/             thumbnails
├── exports/              last built resources
└── backups/              timestamped snapshots
```

Copying the base `.ydd` into `assets/` is what makes a project survive being moved to another
machine. `.bitirimclothing` (brief §31) is this folder, zipped, with a manifest — **data only, never
executable content** (brief §55).

## 7. Cross-cutting decisions

- **Commands.** Every mutation is `ICommand { Do(); Undo(); }` with a stable description string, so
  the history timeline (brief §5) is free.
- **Long work.** Every operation over ~150 ms runs off the UI thread behind a progress contract with
  cancellation (brief §40, §41). The sidecar streams progress over the same RPC channel.
- **Errors.** Exceptions never reach the user. A `UserFacingError { title, message, logRef }` is what
  the UI receives; the stack trace goes to `logs/errors.log` with a reference id the user can quote
  (brief §42, §43).
- **Capabilities.** The sidecar reports a capability set at startup (`canWriteYtd`, `canWriteYdd`,
  `canWriteYmt`, …). The UI binds affordances to those flags. If YDD writing is not proven, the
  mesh-editing surface is not shown. This is the mechanism that makes brief §61 structurally
  enforced rather than a matter of discipline.
- **Secrets.** AI API keys go to Windows DPAPI via the .NET host, never to disk in plaintext, never
  to source (brief §16, §48, §55).
- **Paths.** Every user-supplied path is canonicalised and checked against its intended root before
  use; archive extraction rejects entries that escape the destination (brief §55).

## 8. AI texture generation

```csharp
interface IAiTextureProvider {
    string Id { get; }
    AiProviderStatus Status { get; }                                    // local config only
    Task<AiTextureResult> GenerateAsync(AiTextureRequest r, CancellationToken ct);
    Task<AiConnectionCheck> TestConnectionAsync(CancellationToken ct);
}
```

One implementation: `GeminiTextureProvider`, on `gemini-3.1-flash-image` through Google's own
`Google.GenAI` SDK for .NET. `AiProviderRegistry` exists so a second vendor is a registration
rather than a rewrite, but there is no stub for a vendor that is not implemented — a provider in a
dropdown that cannot generate is worse than one that is not offered.

`AiTextureRequest` carries the prompt **and up to four labelled images**: the current diffuse, the
UV wireframe, the UV mask and a garment render. The UV layout as a placement map is the reference
product's key insight (`video-feature-inventory.md` §2.4) — the model draws inside the garment's
islands rather than across the whole square. The images are named `IMAGE 1`, `IMAGE 2`… in the
prompt text and attached in that same fixed order, so the model is never left guessing which one is
the mask.

The system instruction (`GeminiPrompt.GEMINI_TEXTURE_SYSTEM_PROMPT`) is separate from the user's
sentence and is sent as `GenerateContentConfig.SystemInstruction`. Folding the two together is how
"make this black" becomes a photograph of a new jacket: the rules drown the request.

**Where the key lives.** `GEMINI_API_KEY` from the environment, then a `.env`/`.env.local` file,
then the DPAPI-protected value from Settings. Resolved on the host at the moment of an outbound
request. It never crosses the bridge, never reaches the JavaScript bundle, never appears in a log
line or an error message, and `SettingsService.PublicView()` strips even the ciphertext from what
the interface is sent. `.env` is git-ignored; `.env.example` is the committed template.

**What stays ours.** Gemini draws. UV placement, cropping, compositing, block compression, the YDD,
YTD, YMT and manifest writers and every validation rule are untouched — the returned image lands in
the project's `layers/` folder and joins the layer stack as an ordinary `generated` image layer.
With no key configured, every non-AI feature works unchanged (brief §16, §49), and the application
starts normally.

## 9. Testing posture

The rule from brief §54 — do not say "it works" without tests — is enforced by where the fixtures
live. `tests/fixtures/` holds real `.ydd`/`.ytd`/`.ymt` files and golden outputs. The export test
suite asserts on **bytes and structure**, not on "did the function return true". Until an exported
pack has been loaded by an actual FiveM server and visually confirmed, export status in the docs and
in the UI stays *unproven*.
