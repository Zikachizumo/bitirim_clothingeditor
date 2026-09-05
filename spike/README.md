# Phase 1 spike scripts

Throwaway exploration scripts, kept for reproducibility rather than reuse. The production code
lives in `src/`; these are what produced the findings in `docs/phase-1-results.md`.

Run them with the bootstrapped runtime:

```bash
C:/bcc/python/python.exe spike/<script>.py
```

| Script | What it established |
|---|---|
| `explore_api.py` | fivefury's real API surface, read from the installed package |
| `probe_rpf.py`, `probe_rpf2.py` | RPF structure and archive methods |
| `find_fixture.py` | Game key derivation works against a retail install |
| `extract_fixture.py`, `extract2.py`, `extract3.py` | Locating and extracting freemode clothing |
| `probe_ytd.py`, `inspect_ytd.py` | YTD object model and mutation API |
| `test_ytd_roundtrip.py` | YTD round-trip is structurally lossless; texture replace works |
| `probe_ydd.py`, `inspect_ydd.py`, `ydd_full.py` | Drawable internals: LODs, skinning, materials, embedded textures |
| `ydd_roundtrip.py` | **Found the bounding-box defect** |
| `ydd_bbox_fix.py` | Confirmed the bounding box cannot be preserved through the public API |
| `probe_ymt.py`, `ymt_variation.py`, `ymt_dump.py`, `ymt_top.py` | `CPedVariationInfo` structure |
| `ymt_compinfo.py` | Identified `0xD12F579D` as the component index field |
| `ymt_roundtrip.py` | YMT round-trip is structurally lossless |

Fixture paths are hardcoded to `C:\bcc\fixtures`. Those files are extracted from a legal GTA V
installation and are **never committed** — see `docs/legal-and-oss.md`.
