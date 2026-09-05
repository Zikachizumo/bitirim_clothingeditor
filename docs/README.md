# BITIRIM CLOTHING CREATOR

A Windows desktop clothing development studio for GTA V / FiveM.

**v0.2.0 is built.** Phase 0 (research), Phase 1 (export spike) and the desktop
application are complete; the in-game FiveM test remains the one open gate.

## Documents

| Document | What it answers |
|---|---|
| [phase-1-results.md](phase-1-results.md) | **What actually works.** Support matrix, benchmarks, packaging decision, blockers |
| [video-parity.md](video-parity.md) | **Feature-by-feature parity** with the reference video, plus our own additions |
| [editor-architecture.md](editor-architecture.md) | What v0.2 added on top of the v0.1 backend |
| [undo-redo.md](undo-redo.md) | The command stack |
| [texture-editor.md](texture-editor.md) | Tools, layers, deterministic compositing |
| [asset-browser.md](asset-browser.md) | Library, thumbnails, cache |
| [character-system.md](character-system.md) | Outfits, and why nothing renders a character |
| [desktop-app.md](desktop-app.md) | How the application is assembled |
| [ipc-protocol.md](ipc-protocol.md) | The two protocols |
| [project-format.md](project-format.md) | `project.json`, packages, migration |
| [known-limitations.md](known-limitations.md) | What does not work, and why |
| [video-feature-inventory.md](video-feature-inventory.md) | What the reference video actually shows, timestamped, with evidence frames |
| [prd.md](prd.md) | What we are building, for whom, and what "done" means |
| [feature-matrix.md](feature-matrix.md) | Every feature × video × priority × complexity × status |
| [technology-comparison.md](technology-comparison.md) | Four candidate stacks, the decision, and what would reverse it |
| [architecture.md](architecture.md) | Layers, processes, domain model, repository layout, cross-cutting rules |
| [asset-format.md](asset-format.md) | GTA/RAGE format research and per-format feasibility, with sources |
| [export-pipeline.md](export-pipeline.md) | How an export is built, and what "export succeeded" is allowed to mean |
| [legal-and-oss.md](legal-and-oss.md) | Licence audit of every dependency; rules for this project |
| [roadmap.md](roadmap.md) | 16 phases, MVP definition, what to build first |
| [risks.md](risks.md) | Ranked risk register and the blocker list |

## Where things stand

**Reading and writing real GTA clothing assets works.** `.ytd` and `.ymt` are written and verified by
re-parsing our own output; `.ydd` is read fully and copied rather than rewritten (the writer
recomputes bounding volumes wrongly for skinned ped drawables). A complete, correctly-named FiveM
addon resource is generated end to end in ~196 ms. **143 tests pass, none skipped.**

**Shipping without user-installed Python is confirmed.** A trimmed 73 MB embeddable runtime,
relocated and with pip removed, still parses real assets.

**The pipeline has not been loaded by a real game client.** That is the one remaining gate, and it
needs a human: there is no FXServer on the dev machine. Two ready-to-install test resources are
staged — see [phase-1-results.md §8](phase-1-results.md#8-fivem-test-result).

## Getting started

```bash
./tools/bootstrap-runtime.ps1 -Destination C:\bcc\python
```

```bash
dotnet test tests/Bitirim.Clothing.Tests -c Release
```

Then `tools/Bitirim.Clothing.Tester` for the CLI; run it with no arguments for usage.

## Method note

ffmpeg and Python were unavailable, so video frames were extracted by serving the MP4 over a local
range-capable HTTP server, seeking an HTML5 video element to exact timestamps, and capturing each
frame to disk — 97 frames at 1 s intervals plus 9 magnified crops, verified complete against a
contact sheet. Library claims were checked against the GitHub licence API, raw `LICENSE`/`NOTICE`
files, source headers, and the contents of the actual published wheel, rather than against READMEs
alone.

Everything unverified is labelled unverified. Everything unreadable in the 480p source is labelled
unreadable rather than guessed.
