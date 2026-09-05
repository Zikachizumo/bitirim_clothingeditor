# IPC Protocols

Two boundaries, two protocols, both versioned.

```
React UI  ──[ bitirim-host-v1 ]──►  .NET host  ──[ asset-service 1.0.0 ]──►  Python
```

---

## 1. Host bridge — `bitirim-host-v1`

Between the WebView2 UI and the .NET host, over the WebView message channel.

### Request

```json
{ "id": 1, "op": "project.save", "args": {} }
```

### Response

```json
{ "id": 1, "ok": true, "result": { ... } }
{ "id": 1, "ok": false, "error": { "code": "no_project", "message": "No project is open.", "hint": null } }
```

### Unsolicited events

```json
{ "event": "export.progress", "data": { "stage": "Encoding textures", "percent": 10 } }
```

Events in use: `export.progress`, `project.autosaved`, `shell.filesDropped`.

`export.progress` carries `{ stage, percent }`, and `{ cancelled: true }` when
an export was stopped.

### Error contract

Every failure carries a `message` already written for a person, and optionally a `hint` — the "what
to do about it" line. Raw exception text never crosses this boundary; unexpected failures become a
generic message plus a reference into `errors.log`.

Codes the UI branches on: `no_project`, `backend_missing`, `unsupported_format`, `not_found`,
`path_escape`, `mock_export`, `variation_empty`, `bad_args`, `unknown_op`, `internal`,
`project_newer`, `project_corrupt`, `export_cancelled`, `developer_only`,
`last_asset`, `undo_failed`.

### Operations

| Group | Operations |
|---|---|
| **app** | `app.info`, `app.capabilities`, `app.components` |
| **window** | `ui.ready`, `window.minimize`, `window.toggleMaximize`, `window.close`, `window.setTitle`, `devtools.open` |
| **settings** | `settings.get`, `settings.set`, `settings.setAiKey` |
| **project** | `project.current`, `project.recent`, `project.new`, `project.open`, `project.save`, `project.saveAs`, `project.update`, `project.close`, `project.autosave` |
| **edit** | `edit.undo`, `edit.redo`, `edit.history` |
| **recovery** | `recovery.list`, `recovery.restore`, `recovery.discard` |
| **garments** | `assets.setActive`, `assets.add`, `assets.remove`, `assets.setBase` |
| **outfits** | `outfit.save`, `outfit.delete` |
| **library** | `library.scan`, `library.pickRoot`, `library.thumbnail`, `library.thumbnailPeek` |
| **cache** | `cache.usage`, `cache.clear` |
| **search** | `search.query` |
| **asset** | `asset.inspect`, `asset.mesh`, `asset.pickFile`, `asset.importImage`, `asset.readImage`, `asset.stat`, `asset.hex`, `asset.findBytes` |
| **texture** | `texture.baseImage`, `texture.saveComposite` |
| **ai** | `ai.status`, `ai.testConnection`, `ai.generateTexture`, `ai.cancel` |
| **validate / export** | `validate.run`, `export.presets`, `export.plan`, `export.history`, `export.pickFolder`, `export.run`, `export.cancel` |
| **shell** | `shell.openFolder`, `shell.reveal`, `shell.copyPath` |
| **log** | `log.recent`, `log.write` |

`asset.hex` and `asset.findBytes` require developer mode and are **read-only**:
there is no write path for raw bytes at all. Hand-editing a RAGE container
produces a file the game rejects without saying why, so offering it would be
worse than useless.

### Editing and undo

`project.update` carries the edit's intent as well as its content:

```json
{
  "op": "project.update",
  "args": {
    "project": { … },
    "save": false,
    "label": "Change opacity",
    "mergeKey": "opacity:a1b2c3"
  }
}
```

`label` is what the Edit menu shows. `mergeKey` folds a continuous gesture — a
dragged slider, a typed field — into one undo entry. See
[undo-redo.md](undo-redo.md).

Every reply that describes a project carries its history:

```json
{
  "history": {
    "canUndo": true, "canRedo": false,
    "undoLabel": "Brush stroke", "redoLabel": null,
    "depth": 7, "recent": ["Brush stroke", "Add brush layer"]
  }
}
```

The host owns the document. The UI sends a whole edited copy and takes the
reply as the new truth; it never holds a second copy that could drift.

### Bulk data

Mesh vertex data crosses as one base64 blob with a byte-offset map, not as four JSON number arrays:

```json
{
  "buffer": "<base64>",
  "layout": { "positions": [0, 12060], "normals": [12060, 12060],
              "uvs": [24120, 8040], "indices": [32160, 16968] }
}
```

A 15,000-vertex garment is roughly 700 KB of raw floats; as JSON numbers it would be several
megabytes of text to parse.

Images cross as `data:` URLs. Only paths inside the open project resolve — `asset.readImage`
validates against the project root, so a crafted project file cannot read arbitrary disk.

### Security

- The UI is served from a virtual host mapping with a strict CSP; navigation off that origin is
  cancelled.
- Every project-relative path is canonicalised and checked to stay inside the project root.
- Package extraction rejects entries that would escape the destination (zip slip).
- The AI API key is never returned to the UI. `settings.get` reports only `hasAiApiKey: true|false`,
  and `SettingsService.PublicView()` strips the DPAPI ciphertext out of the settings payload as
  well -- the interface never receives key material in any form.
- `ai.status` answers from local configuration only, so opening the dialog costs no API call. It
  reports where the key came from (`environment`, `.env`, `settings`), never what it is.
- `ai.generateTexture` takes the prompt and `data:` URL images and returns a path inside the
  project. One request at a time: a second call while one is in flight is refused with `ai_busy`,
  so a double-click cannot become two billed calls.

---

## 2. Asset service — `asset-service 1.0.0`

Between the .NET host and the bundled Python process, newline-delimited JSON over stdio.

### Request / response

```json
{ "id": 1, "op": "ytd_read", "args": { "path": "C:/…/jbib_diff_000_a_uni.ytd" } }
{ "id": 1, "ok": true, "result": { "kind": "ytd", "textureCount": 1, "textures": [ … ], "elapsedMs": 2.0 } }
{ "id": 1, "ok": false, "error": { "code": "parse_failed", "message": "Could not read texture dictionary: …" } }
```

### Operations

| Operation | Purpose |
|---|---|
| `capabilities` | What this build can actually do — the flags the UI gates on |
| `inspect` | Dispatches on file extension |
| `ydd_read` | Drawable structure: LODs, materials, embedded textures, bounds |
| `ydd_mesh` | Render geometry to a binary side file, for the viewport |
| `ytd_read` | Texture dictionary contents |
| `ytd_decode` | A texture out as DDS, for host-side decoding |
| `ytd_replace_texture` | Write a dictionary with new pre-encoded block data |
| `ymt_read` | Ped variation metadata |
| `ymt_build_addon` | Derive a single-component addon `CPedVariationInfo` |
| `validate_pack` | Re-parse written files to confirm they are readable |

### Design rules

**The service speaks only in files and buffers.** It does not know what a project or a layer is. All
product semantics live in .NET.

**Bulk payloads go by path, never inlined.** Mesh data, DDS surfaces and encoded block data are
written to a temp file whose path travels in the RPC.

**Compression is the host's job.** `fivefury` stores compressed surfaces but cannot produce them, so
`capabilities.encodeTexture` is false and BCnEncoder.NET does the work in .NET. This also keeps the
expensive step where it can be profiled — BC7 at 2048² takes about 6 seconds.

**Stateless between calls.** Restarting the process loses nothing.

**One importer.** `src/assetservice/rage.py` is the only module that imports `fivefury`. Replacing
the backend means rewriting that file and nothing else.

### Error codes

`unknown_op`, `bad_args`, `not_found`, `unsupported_format`, `parse_failed`, `write_failed`,
`internal`. Detail for `internal` goes to the service's stderr, which the host captures into its
diagnostics buffer; the user sees the coded message only.
