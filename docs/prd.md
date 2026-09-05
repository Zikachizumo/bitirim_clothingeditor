# Product Requirements Document — BITIRIM CLOTHING CREATOR

## 1. What this is

A Windows desktop **clothing development studio** for GTA V / FiveM developers. It opens existing
GTA clothing assets, renders them in 3D, lets the developer author the garment's texture on a
UV-aware 2D canvas, validates the result, and exports a FiveM-ready addon clothing resource.

It is **not** a player clothing menu, a character creator, a clothing shop, a FiveM NUI, or an
in-game outfit picker.

## 2. Who it is for

A FiveM server developer or clothing creator who today needs OpenIV or CodeWalker to extract, Blender
plus Sollumz to open and re-export, an image editor to paint, and manual file surgery plus a server
restart to test. Each of those is a separate tool with its own learning curve, and the loop from
"idea" to "wearing it in game" is measured in hours.

Our user wants that loop measured in minutes, without giving up correctness.

## 3. Positioning — what we are honestly better at

The reference product (0RESMON Clothing Designer) is an in-game NUI. Its strength is immediacy: you
design and wear the result in the same session. Its limits, as observed, are real — 7 of 12
components, no validation, no mesh or UV or weight tooling, no character context, no configurable
AI, and work that appears bound to the server rather than to a portable file.

[Durty Cloth Tool](https://gta.clothing/) is the closer competitor: a Windows desktop tool with 3D
preview, validation, and multi-platform export, on a subscription.

We are not trying to beat either on their strongest axis. Our thesis is:

1. **Correctness first.** Validation before export, and a UI that never claims a capability the
   engine has not demonstrated.
2. **Projects that are real files.** Portable, resumable, shareable, version-controllable.
3. **Depth where the reference has none.** Full component coverage, character context, material
   channels, and — only when proven — mesh and weight tooling.
4. **AI as a tool you control**, with your own provider and key, not a black box.

## 4. Primary workflow

```
New Project → gender → component → base asset (library or imported .ydd)
   → 3D viewport + UV canvas
   → texture authoring: layers, brush, fill, image, text, colour
   → optional AI generation (UV-conditioned)
   → texture slots (variations a, b, c…)
   → live 3D preview
   → asset properties (component / drawable / texture ids)
   → VALIDATE
   → Save
   → Export → FiveM addon clothing resource
```

Closing a project persists everything. Reopening resumes exactly where the user stopped
(brief §1) — including layer stacks per slot, camera, zoom, and selection.

## 5. Functional requirements

Detail and priorities live in `feature-matrix.md`. Summary of what must be true for v1.0:

**Must (P0)** — desktop shell with the three-pane editor; project create/open/save with folder
format; template library with gender + component filtering; ingest a loose `.ydd`; parse and render
a real drawable in 3D; UV canvas with UV wireframe; layer stack; brush, fill, colour picker, image
layer; texture slots mapped to GTA variant letters; component/drawable/texture ids surfaced;
validation with blocking errors; **real YTD + YMT + YDD emission**; FiveM addon resource generation
with correct `^` naming; undo/redo.

**Should (P1)** — auto-save and recovery; drag & drop import; thumbnail cache; camera presets and
display modes; character body under the garment; text tool; eraser; DDS/TGA/WEBP import; texture
export; AI generation with provider abstraction and DPAPI-stored keys; export straight to a server
resources folder with overwrite guard and backup; progress with cancel; settings; logging.

**Could (P2)** — UV islands and 3D↔UV selection sync; skeleton and weight *viewing*; full-outfit
preview; export presets; portable `.bitirimclothing`; favourites and tags; project history.

**Won't, in v1** — mesh editing, UV editing that writes geometry, weight painting, cloth `.yld`
authoring, clipping detection, multi-monitor panel tear-off, licence system, auto-update.
Each is either research-gated or not load-bearing for the core promise. They are absent, not faked.

## 6. Non-functional requirements

| Area | Requirement |
|---|---|
| Platform | Windows 10 (1809+) and Windows 11, x64 |
| Distribution | Signed installer + portable zip |
| Resolutions | 1920×1080 primary; usable at 1366×768, 2560×1440, 3840×2160 |
| Startup | Cold start to usable start screen under 3 s on an SSD |
| Responsiveness | UI thread never blocked; anything over 150 ms shows progress and can be cancelled |
| Memory | Under 1.5 GB with one 2048² project open |
| Offline | Every core feature works with no network. Network is needed only for AI, updates, optional sharing |
| Errors | No raw stack traces reach the user; every failure has a readable message and a log reference |
| Security | Path traversal blocked on all imports; project files are data only and never executed; API keys in DPAPI, never in source or plaintext; any external process launch is disclosed |
| Licensing | Every shipped dependency permissively licensed; nothing derived from CodeWalker, Sollumz, or the reference product |

## 7. Success criteria

The product is done when a developer can, on a clean Windows machine:

1. Install and open the app.
2. Create a project, pick gender and component.
3. Import a `.ydd` clothing asset they downloaded.
4. See it correctly in the 3D viewport.
5. Author its texture: layers, image, text, colour.
6. Optionally generate a texture with AI using their own key.
7. Add a second texture variation.
8. Save, close, reopen, and continue.
9. Run validation and understand every finding.
10. Export a FiveM addon clothing resource.
11. Drop it into a FiveM server, restart, and **wear it in game** — the same loop the reference video
    closes at t=79–88.

Step 11 is the only criterion that matters for the engine. Steps 1–10 are the product around it.

## 8. Explicit anti-requirements

- No "Export completed" message unless real, loadable RAGE assets were written. If a format cannot
  be produced, the feature is disabled with a stated reason and a TODO in the docs (brief §28, §61).
- No feature copied from the reference product's implementation, branding, or assets. We build our
  own solution to the same problems (brief §56, §72).
- No mandatory cloud, account, or licence check in v1 (brief §49, §50, §52).
- No silent overwriting of a user's existing server files (brief §35, §70).

## 9. Open product questions

These need the user's decision before or during Phase 1; none blocks the Phase 1 spike.

1. **Which FiveM build** does the target server run — Legacy or Enhanced? This changes asset targets
   (`asset-format.md` §8, q5).
2. **Commercial intent** — internal Bitirim tooling, or a product to sell? This decides whether GPL
   options (Sollumz-derived approaches) are even on the table, and whether the licence architecture
   in brief §52 needs to be real sooner.
3. **AI provider preference** — is there an existing OpenAI account/key, or should local generation
   (SD/ComfyUI) be the first-class path?
4. **Template library seeding** — do we ship base templates, and if so, where do they come from?
   Redistributing extracted Rockstar assets is not something we can do.
