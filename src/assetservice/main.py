"""Asset service entry point: newline-delimited JSON-RPC over stdio.

Stateless between calls, so the host can restart it at any time without losing
anything (docs/risks.md R8).
"""

from __future__ import annotations

import json
import sys
import time
import traceback
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import contract  # noqa: E402
import rage  # noqa: E402
from contract import OpError  # noqa: E402


def op_capabilities(_args: dict) -> dict:
    """Report what this build can actually do.

    The host binds UI affordances to these flags. Anything false must not be
    offered to the user as a working feature.
    """
    import fivefury  # noqa: PLC0415

    return {
        "contractVersion": contract.CONTRACT_VERSION,
        "operations": list(contract.OPERATIONS),
        "backend": "fivefury",
        "backendVersion": _fivefury_version(),
        "python": sys.version.split()[0],
        "capabilities": {
            "readYdd": True,
            "readYtd": True,
            "readYmt": True,
            "writeYtd": True,
            "writeYmt": True,
            "extractMesh": True,
            "decodeTexture": True,
            # Geometry is preserved on a YDD rewrite but the bounding volume is
            # recomputed incorrectly for skinned ped drawables, so the export
            # path copies the source YDD byte-for-byte instead of rewriting it.
            # See docs/phase-1-results.md section 4.
            "writeYdd": False,
            "writeYddGeometry": False,
            "encodeTexture": False,  # host-side, BCnEncoder.NET
        },
        "fivefuryPresent": bool(fivefury),
    }


def _fivefury_version() -> str:
    try:
        from importlib.metadata import version  # noqa: PLC0415

        return version("fivefury")
    except Exception:  # noqa: BLE001
        return "unknown"


def op_inspect(args: dict) -> dict:
    path = _path(args, "path")
    kind = rage.detect_kind(path)
    return {"ydd": rage.ydd_read, "ytd": rage.ytd_read, "ymt": rage.ymt_read}[kind](path) \
        if kind != "yft" else {"kind": "yft", "note": "read not implemented in Phase 1"}


def op_ytd_read(args: dict) -> dict:
    return rage.ytd_read(_path(args, "path"))


def op_ydd_read(args: dict) -> dict:
    return rage.ydd_read(_path(args, "path"))


def op_ymt_read(args: dict) -> dict:
    return rage.ymt_read(_path(args, "path"))


def op_ytd_replace_texture(args: dict) -> dict:
    return rage.ytd_replace_texture(
        _path(args, "source"),
        Path(args["destination"]),
        blob=_path(args, "blob"),
        width=int(args["width"]),
        height=int(args["height"]),
        fmt=str(args["format"]),
        mip_count=int(args["mipCount"]),
        name=args.get("name"),
    )


def op_ydd_make_emissive(args: dict) -> dict:
    return rage.ydd_make_emissive(
        _path(args, "source"),
        Path(args["destination"]),
        multiplier=float(args.get("multiplier", 1.0)),
        material_index=int(args.get("materialIndex", 0)),
    )


def op_ydd_mesh(args: dict) -> dict:
    return rage.ydd_mesh(
        _path(args, "path"),
        Path(args["blob"]),
        drawable=int(args.get("drawable", 0)),
        lod=str(args.get("lod", "high")),
    )


def op_ytd_decode(args: dict) -> dict:
    return rage.ytd_decode(
        _path(args, "path"),
        Path(args["blob"]),
        index=int(args.get("index", 0)),
    )


def op_ymt_build_addon(args: dict) -> dict:
    return rage.ymt_build_addon(
        _path(args, "template"),
        Path(args["destination"]),
        component=int(args["component"]),
        texture_count=int(args["textureCount"]),
        dlc_name_hash=int(args.get("dlcNameHash", 0)),
    )


def op_validate_pack(args: dict) -> dict:
    """Read back every file we just wrote and confirm it still parses.

    Cheap, and it catches an entire category of silent corruption
    (docs/export-pipeline.md section 7).
    """
    results = []
    for entry in args.get("files", []):
        p = Path(entry)
        if not p.exists():
            results.append({"file": entry, "ok": False, "error": "missing"})
            continue
        try:
            kind = rage.detect_kind(p)
            reader = {"ydd": rage.ydd_read, "ytd": rage.ytd_read, "ymt": rage.ymt_read}.get(kind)
            info = reader(p) if reader else {"kind": kind}
            results.append({"file": entry, "ok": True, "kind": info.get("kind"),
                            "sizeBytes": p.stat().st_size})
        except OpError as exc:
            results.append({"file": entry, "ok": False, "error": exc.message})
    return {"files": results, "allOk": all(r["ok"] for r in results) if results else False}


def _path(args: dict, key: str) -> Path:
    raw = args.get(key)
    if not raw:
        raise OpError(contract.E_BAD_ARGS, f"Missing required argument: {key}")
    p = Path(raw)
    if not p.exists():
        raise OpError(contract.E_NOT_FOUND, f"File not found: {p}")
    return p


HANDLERS = {
    "capabilities": op_capabilities,
    "inspect": op_inspect,
    "ytd_read": op_ytd_read,
    "ydd_read": op_ydd_read,
    "ymt_read": op_ymt_read,
    "ytd_replace_texture": op_ytd_replace_texture,
    "ydd_mesh": op_ydd_mesh,
    "ydd_make_emissive": op_ydd_make_emissive,
    "ytd_decode": op_ytd_decode,
    "ymt_build_addon": op_ymt_build_addon,
    "validate_pack": op_validate_pack,
}


def handle(request: dict) -> dict:
    rid = request.get("id")
    op = request.get("op")
    args = request.get("args") or {}
    handler = HANDLERS.get(op)
    if handler is None:
        return {"id": rid, "ok": False,
                "error": {"code": contract.E_UNKNOWN_OP, "message": f"Unknown operation: {op}"}}
    started = time.perf_counter()
    try:
        result = handler(args)
        result["elapsedMs"] = round((time.perf_counter() - started) * 1000, 2)
        return {"id": rid, "ok": True, "result": result}
    except OpError as exc:
        return {"id": rid, "ok": False, "error": {"code": exc.code, "message": exc.message}}
    except Exception as exc:  # noqa: BLE001
        # Detail goes to stderr for the host's error log; the user sees the
        # coded message only.
        print(traceback.format_exc(), file=sys.stderr, flush=True)
        return {"id": rid, "ok": False,
                "error": {"code": contract.E_INTERNAL, "message": str(exc)}}


def main() -> int:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            request = json.loads(line)
        except json.JSONDecodeError as exc:
            response = {"id": None, "ok": False,
                        "error": {"code": contract.E_BAD_ARGS, "message": str(exc)}}
        else:
            response = handle(request)
        sys.stdout.write(json.dumps(response) + "\n")
        sys.stdout.flush()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
