"""The only module in this project that imports fivefury.

Everything above this line speaks the contract in contract.py. If fivefury is
ever replaced (see docs/risks.md R2), this file is what gets rewritten.
"""

from __future__ import annotations

import hashlib
import zlib
from pathlib import Path
from typing import Any

import fivefury as ff

from contract import (
    OpError,
    E_PARSE_FAILED,
    E_UNSUPPORTED_FORMAT,
    E_WRITE_FAILED,
)

# Component index -> filename prefix. Verified against the PedComponent enum
# shipped by fivefury and against the actual entry names inside
# streamedpeds_mp.rpf; see docs/asset-format.md section 2.
COMPONENT_PREFIX = {
    0: "head", 1: "berd", 2: "hair", 3: "uppr", 4: "lowr", 5: "hand",
    6: "feet", 7: "teef", 8: "accs", 9: "task", 10: "decl", 11: "jbib",
}
PREFIX_COMPONENT = {v: k for k, v in COMPONENT_PREFIX.items()}

_UNUSED = 255

# RSC7: 16-byte header (magic, version, virtual flags, physical flags) followed
# by a raw-deflate body. The flags tell the game how to page the resource in.
_RSC7_HEADER_BYTES = 16
_RAW_DEFLATE = -15


def _sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()[:16]


def _rsc7_splice(
    source: Path,
    destination: Path,
    replacements: list[tuple[bytes, bytes, str]],
) -> None:
    """Copy `source` to `destination`, swapping byte runs inside its body.

    Each replacement is `(old, new, what)`; `what` names the run for error
    messages. Every `new` must be the same length as its `old`, and every `old`
    must occur exactly once.

    fivefury can parse a YTD and write one back, but its writer lays the RSC7
    container out differently from the game's own files. Measured on an
    untouched round-trip of a Rockstar texture, the page flags came back as
    0x08000000 / 0xDD000870 where the original held 0x00020000 / 0xD0000012.
    The game loads that file and the D3D driver dies inside nvwgf2umx.dll --
    confirmed in-game against a control that streamed the untouched bytes and
    did not crash.

    So a texture swap never rebuilds the container. The header is copied
    verbatim, page flags and all, and only bytes inside the decompressed body
    change. Equal lengths are what keep every offset the header describes
    still true.
    """
    raw = source.read_bytes()
    header, payload = raw[:_RSC7_HEADER_BYTES], raw[_RSC7_HEADER_BYTES:]
    try:
        body = zlib.decompress(payload, _RAW_DEFLATE)
    except zlib.error as exc:
        raise OpError(E_WRITE_FAILED, f"Could not unpack texture container: {exc}") from exc

    for old, new, what in replacements:
        if len(old) != len(new):
            raise OpError(
                E_WRITE_FAILED,
                f"Replacement {what} is {len(new)} bytes against {len(old)}; "
                "the container is preserved, so lengths must match.",
            )
        occurrences = body.count(old)
        if occurrences != 1:
            raise OpError(
                E_WRITE_FAILED,
                f"Replacement {what} occurs {occurrences} times in the container; "
                "patching it in place would be ambiguous.",
            )
        body = body.replace(old, new, 1)

    compressor = zlib.compressobj(9, zlib.DEFLATED, _RAW_DEFLATE)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(header + compressor.compress(body) + compressor.flush())


def detect_kind(path: Path) -> str:
    suffix = path.suffix.lower().lstrip(".")
    if suffix not in ("ydd", "ytd", "ymt", "yft"):
        raise OpError(E_UNSUPPORTED_FORMAT, f"Unsupported GTA asset format: .{suffix}")
    return suffix


# --------------------------------------------------------------------------
# YTD
# --------------------------------------------------------------------------

def ytd_read(path: Path) -> dict[str, Any]:
    try:
        ytd = ff.read_ytd(path)
    except Exception as exc:  # noqa: BLE001 - normalised into a coded error
        raise OpError(E_PARSE_FAILED, f"Could not read texture dictionary: {exc}") from exc
    return {
        "kind": "ytd",
        "game": str(ytd.game),
        "textureCount": len(ytd.textures),
        "textures": [_texture_info(t) for t in ytd.textures],
    }


def _texture_info(t) -> dict[str, Any]:
    return {
        "name": t.name,
        "width": t.width,
        "height": t.height,
        "format": t.format_name,
        "mipCount": t.mip_count,
        "usage": int(t.usage),
        "usageFlags": int(t.usage_flags),
        "dataBytes": len(t.data),
        "dataSha": _sha(t.data),
    }


def ytd_replace_texture(
    source: Path,
    destination: Path,
    *,
    blob: Path,
    width: int,
    height: int,
    fmt: str,
    mip_count: int,
    name: str | None = None,
) -> dict[str, Any]:
    """Replace the diffuse texture in `source` with pre-encoded block data.

    The block data is produced host-side by BCnEncoder.NET -- fivefury ships no
    encoder, it only stores raw compressed surfaces. See
    docs/phase-1-results.md section 5.
    """
    try:
        ytd = ff.read_ytd(source)
    except Exception as exc:  # noqa: BLE001
        raise OpError(E_PARSE_FAILED, f"Could not read texture dictionary: {exc}") from exc

    if not ytd.textures:
        raise OpError(E_PARSE_FAILED, "Texture dictionary contains no textures.")

    existing = ytd.textures[0]
    target_name = name or existing.name
    original_data = existing.data
    original_shape = (
        existing.width, existing.height,
        existing.format, existing.mip_count, len(original_data),
    )
    try:
        fmt_enum = getattr(ff.TextureFormat, fmt.upper())
    except AttributeError as exc:
        raise OpError(E_UNSUPPORTED_FORMAT, f"Unsupported texture format: {fmt}") from exc

    data = blob.read_bytes()
    replacement = ff.Texture.from_raw(
        data, width, height, fmt_enum, mip_count,
        name=target_name, usage=existing.usage, usage_flags=existing.usage_flags,
    )

    # The container is preserved rather than rebuilt (see _rsc7_splice), which
    # only holds when the replacement occupies exactly the same space. Anything
    # else would need a real RSC7 writer; refuse instead of shipping a file that
    # crashes the graphics driver on load.
    replacement_shape = (
        replacement.width, replacement.height,
        replacement.format, replacement.mip_count, len(replacement.data),
    )
    if len(ytd.textures) != 1 or replacement_shape != original_shape:
        raise OpError(
            E_UNSUPPORTED_FORMAT,
            "A texture can only be replaced by one of identical shape "
            f"(size/format/mips/bytes). Source is {original_shape}, "
            f"replacement is {replacement_shape}, "
            f"textures in dictionary: {len(ytd.textures)}.",
        )

    # Renaming is allowed only at equal length, for the same reason: the name
    # lives inside the body the header describes. A same-length rename lets a
    # surface be published under the lookup name a drawable's sampler expects
    # (jbib_diff_000_b_uni -> jbib_diff_000_a_uni), which is how one slot's
    # pixels reach another slot without rebuilding anything.
    if target_name != existing.name and len(target_name) != len(existing.name):
        raise OpError(
            E_UNSUPPORTED_FORMAT,
            f"Cannot rename '{existing.name}' to '{target_name}': names must be "
            "the same length while the container is preserved.",
        )

    ytd.texture(replacement, replace=True)

    report = ytd.validate()
    issues = [str(i) for i in getattr(report, "issues", [])]

    replacements = [(original_data, replacement.data, "pixel data")]
    if target_name != existing.name:
        replacements.append(
            (existing.name.encode("ascii"), target_name.encode("ascii"), "texture name"))
    _rsc7_splice(source, destination, replacements)

    return {
        "path": str(destination),
        "sizeBytes": destination.stat().st_size,
        "replaced": target_name,
        "issues": issues,
        "verify": ytd_read(destination),
    }


# --------------------------------------------------------------------------
# YDD
# --------------------------------------------------------------------------

def ydd_read(path: Path) -> dict[str, Any]:
    try:
        ydd = ff.read_ydd(path)
    except Exception as exc:  # noqa: BLE001
        raise OpError(E_PARSE_FAILED, f"Could not read drawable dictionary: {exc}") from exc

    drawables = []
    for wrapper in ydd.drawables:
        d = wrapper.drawable
        lods = {}
        for lod in ff.DRAWABLE_LOD_ORDER:
            meshes = d.get_lod_meshes(lod)
            if not meshes:
                continue
            lods[str(lod).rsplit(".", 1)[-1]] = {
                "meshes": len(meshes),
                "vertices": sum(len(m.positions) for m in meshes),
                "indices": sum(len(m.indices) for m in meshes),
                "uvSets": [len(m.texcoords) for m in meshes],
                "skinned": [bool(m.is_skinned) for m in meshes],
                "blendIndices": sum(len(m.blend_indices or []) for m in meshes),
                "blendWeights": sum(len(m.blend_weights or []) for m in meshes),
                "normals": sum(len(m.normals or []) for m in meshes),
                "tangents": sum(len(m.tangents or []) for m in meshes),
            }
        embedded = d.embedded_textures
        drawables.append({
            "wrapperName": wrapper.name,
            "nameHash": wrapper.name_hash,
            "boundingBox": {
                "min": [d.bounding_box_min.x, d.bounding_box_min.y, d.bounding_box_min.z],
                "max": [d.bounding_box_max.x, d.bounding_box_max.y, d.bounding_box_max.z],
                "radius": d.bounding_sphere_radius,
            },
            "hasSkeleton": bool(d.has_skeleton),
            "hasJoints": bool(d.has_joints),
            "materials": [
                {
                    "shader": m.shader_name,
                    "shaderFile": m.shader_file_name,
                    "renderBucket": m.render_bucket,
                    "textures": [
                        {"parameter": t.parameter_name, "name": t.name} for t in m.textures
                    ],
                }
                for m in d.materials
            ],
            "textureNames": list(d.texture_names),
            "embeddedTextures": [_texture_info(t) for t in embedded.textures],
            "lods": lods,
        })

    return {
        "kind": "ydd",
        "name": ydd.name,
        "version": ydd.version,
        "game": str(ydd.game),
        "drawableCount": ydd.drawable_count,
        "drawables": drawables,
    }


# --------------------------------------------------------------------------
# YMT
# --------------------------------------------------------------------------

# Field name hashes that fivefury does not resolve to readable names. Recovered
# empirically by correlating value distributions against aComponentData3
# drawable counts in mp_m_freemode_01.ymt -- see docs/phase-1-results.md.
H_COMPONENT_INDEX = "0xD12F579D"


def ymt_read(path: Path) -> dict[str, Any]:
    try:
        ymt = ff.read_ymt(path)
    except Exception as exc:  # noqa: BLE001
        raise OpError(E_PARSE_FAILED, f"Could not read metadata file: {exc}") from exc

    info: dict[str, Any] = {
        "kind": "ymt",
        "contentType": str(ymt.content_type),
        "format": str(ymt.format),
        "resourceVersion": ymt.resource_version,
    }
    pv = ymt.ped_variation
    if not pv:
        return info

    info["root"] = pv.get("_meta_name")
    info["dlcName"] = pv.get("dlcName")
    info["availComp"] = list(pv.get("availComp", ()))
    info["flags"] = {
        k: pv.get(k) for k in
        ("bHasTexVariations", "bHasDrawblVariations", "bHasLowLODs", "bIsSuperLOD")
    }
    info["components"] = [
        {
            "slot": i,
            "numAvailTex": c.get("numAvailTex"),
            "drawableCount": len(c.get("aDrawblData3", [])),
            "drawables": [
                {"textures": len(d.get("aTexData", [])),
                 "ownsCloth": bool((d.get("clothData") or {}).get("ownsCloth", False))}
                for d in c.get("aDrawblData3", [])
            ],
        }
        for i, c in enumerate(pv.get("aComponentData3", []))
    ]
    info["compInfoCount"] = len(pv.get("compInfos", []))
    return info


def ymt_build_addon(
    template: Path,
    destination: Path,
    *,
    component: int,
    texture_count: int,
    dlc_name_hash: int = 0,
) -> dict[str, Any]:
    """Derive a single-component, single-drawable addon CPedVariationInfo.

    Rather than synthesising the structure from scratch -- which would mean
    guessing at a dozen hashed fields whose meaning is not documented -- this
    prunes a known-good ped YMT down to one component with one drawable. Every
    field value that survives is one the game already accepts.
    """
    if component not in COMPONENT_PREFIX:
        raise OpError(E_PARSE_FAILED, f"Component index out of range: {component}")

    try:
        ymt = ff.read_ymt(template)
    except Exception as exc:  # noqa: BLE001
        raise OpError(E_PARSE_FAILED, f"Could not read template metadata: {exc}") from exc

    pv = ymt.ped_variation
    if not pv:
        raise OpError(E_PARSE_FAILED, "Template is not a ped variation YMT.")

    comps = pv["aComponentData3"]
    # aComponentData3 is packed, not indexed by component slot -- availComp maps
    # slot -> index into it. The base ped YMT happens to carry availComp ==
    # (0..11) alongside 12 populated slots, which is why indexing by slot looked
    # right; a DLC YMT stores only the slots it ships (measured: valentines2
    # male carries four, availComp[11] == 3), so the same code read the wrong
    # component or ran off the end.
    avail = pv.get("availComp") or ()
    source_index = avail[component] if component < len(avail) else _UNUSED
    if source_index == _UNUSED or source_index >= len(comps):
        raise OpError(E_PARSE_FAILED, f"Template has no component slot {component}.")

    source_comp = comps[source_index]
    source_drawables = source_comp.get("aDrawblData3") or []
    if not source_drawables:
        raise OpError(E_PARSE_FAILED, f"Template component {component} has no drawables.")

    drawable = _copy(source_drawables[0])
    tex_data = drawable.get("aTexData") or []
    if not tex_data:
        raise OpError(E_PARSE_FAILED, "Template drawable carries no texture data.")
    # One CPVTextureData per exported texture variation (slot a, b, c...).
    template_tex = tex_data[0]
    drawable["aTexData"] = []
    for tex_id in range(texture_count):
        entry = _copy(template_tex)
        entry["texId"] = tex_id
        drawable["aTexData"].append(entry)

    new_comp = _copy(source_comp)
    new_comp["numAvailTex"] = texture_count
    new_comp["aDrawblData3"] = [drawable]

    # availComp[i] indexes into aComponentData3; 255 marks a component this
    # pack does not contribute to. Verified against the base ped YMT, where
    # availComp == (0..11) alongside 12 populated component slots.
    avail = [_UNUSED] * 12
    avail[component] = 0

    source_infos = [
        e for e in pv.get("compInfos", []) if e.get(H_COMPONENT_INDEX) == component
    ]
    comp_infos = [_copy(source_infos[0])] if source_infos else []

    pv["aComponentData3"] = [new_comp]
    pv["availComp"] = tuple(avail)
    pv["compInfos"] = comp_infos
    pv["aSelectionSets"] = []
    pv["dlcName"] = dlc_name_hash
    pv["bHasTexVariations"] = texture_count > 1
    pv["bHasDrawblVariations"] = True
    pv["bHasLowLODs"] = False
    pv["bIsSuperLOD"] = False
    # propInfo describes ped props (hats/glasses), which a component-only
    # clothing pack does not contribute.
    prop_info = pv.get("propInfo")
    if isinstance(prop_info, dict):
        prop_info["numAvailProps"] = 0
        prop_info["aPropMetaData"] = []

    destination.parent.mkdir(parents=True, exist_ok=True)
    try:
        ff.save_ymt(ymt, destination)
    except Exception as exc:  # noqa: BLE001
        raise OpError(E_WRITE_FAILED, f"Could not write metadata file: {exc}") from exc

    return {
        "path": str(destination),
        "sizeBytes": destination.stat().st_size,
        "component": component,
        "componentPrefix": COMPONENT_PREFIX[component],
        "textureCount": texture_count,
        "verify": ymt_read(destination),
    }


def _copy(value):
    if isinstance(value, dict):
        return {k: _copy(v) for k, v in value.items()}
    if isinstance(value, list):
        return [_copy(v) for v in value]
    if isinstance(value, tuple):
        return tuple(_copy(v) for v in value)
    return value


# --------------------------------------------------------------------------
# Mesh extraction for the 3D viewport
# --------------------------------------------------------------------------

def ydd_mesh(path: Path, blob: Path, *, drawable: int = 0, lod: str = "high") -> dict[str, Any]:
    """Extract render geometry as flat binary buffers for the viewport.

    Written to a side file rather than inlined in the RPC envelope: a garment is
    a few thousand vertices, but base64 in JSON would still be several times the
    size of the raw floats for no benefit.

    Layout, little-endian, back to back:
        positions  float32 * 3 * vertexCount
        normals    float32 * 3 * vertexCount
        uvs        float32 * 2 * vertexCount
        indices    uint32  * indexCount
    """
    import struct

    try:
        ydd = ff.read_ydd(path)
    except Exception as exc:  # noqa: BLE001
        raise OpError(E_PARSE_FAILED, f"Could not read drawable dictionary: {exc}") from exc

    if drawable >= ydd.drawable_count:
        raise OpError(E_PARSE_FAILED, f"Drawable {drawable} not present in dictionary.")

    d = ydd.drawables[drawable].drawable
    wanted = {"high": ff.DrawableLod.HIGH, "med": ff.DrawableLod.MEDIUM,
              "low": ff.DrawableLod.LOW}.get(lod, ff.DrawableLod.HIGH)
    meshes = d.get_lod_meshes(wanted) or d.primary_meshes
    if not meshes:
        raise OpError(E_PARSE_FAILED, "Drawable carries no render geometry.")

    positions: list[float] = []
    normals: list[float] = []
    uvs: list[float] = []
    indices: list[int] = []
    parts = []
    base = 0

    for mesh in meshes:
        count = len(mesh.positions)
        for v in mesh.positions:
            positions.extend((v.x, v.y, v.z))

        mesh_normals = mesh.normals
        if mesh_normals and len(mesh_normals) == count:
            for n in mesh_normals:
                normals.extend((n.x, n.y, n.z))
        else:
            # Signal "absent" rather than inventing normals; the viewport
            # computes them itself so the shading is honestly derived.
            normals.extend([0.0] * (count * 3))

        uv_sets = mesh.texcoords
        if uv_sets and len(uv_sets[0]) == count:
            for t in uv_sets[0]:
                uvs.extend((t.x, t.y))
        else:
            uvs.extend([0.0] * (count * 2))

        start = len(indices)
        indices.extend(base + i for i in mesh.indices)
        parts.append({
            "materialIndex": mesh.material_index,
            "indexStart": start,
            "indexCount": len(mesh.indices),
            "vertexCount": count,
            "skinned": bool(mesh.is_skinned),
            "uvSets": len(uv_sets) if uv_sets else 0,
            "hasNormals": bool(mesh_normals),
        })
        base += count

    vertex_count = len(positions) // 3
    payload = b"".join((
        struct.pack(f"<{len(positions)}f", *positions),
        struct.pack(f"<{len(normals)}f", *normals),
        struct.pack(f"<{len(uvs)}f", *uvs),
        struct.pack(f"<{len(indices)}I", *indices),
    ))
    blob.parent.mkdir(parents=True, exist_ok=True)
    blob.write_bytes(payload)

    return {
        "blob": str(blob),
        "vertexCount": vertex_count,
        "indexCount": len(indices),
        "triangleCount": len(indices) // 3,
        "lod": lod,
        "parts": parts,
        "materials": [
            {"shader": m.shader_name,
             "textures": [{"parameter": t.parameter_name, "name": t.name} for t in m.textures]}
            for m in d.materials
        ],
        "bounds": {
            "min": [d.bounding_box_min.x, d.bounding_box_min.y, d.bounding_box_min.z],
            "max": [d.bounding_box_max.x, d.bounding_box_max.y, d.bounding_box_max.z],
            "radius": d.bounding_sphere_radius,
        },
        "byteLayout": {
            "positions": [0, len(positions) * 4],
            "normals": [len(positions) * 4, len(normals) * 4],
            "uvs": [(len(positions) + len(normals)) * 4, len(uvs) * 4],
            "indices": [(len(positions) + len(normals) + len(uvs)) * 4, len(indices) * 4],
        },
    }


def ytd_decode(path: Path, blob: Path, *, index: int = 0) -> dict[str, Any]:
    """Decode a texture to straight RGBA so the editor can start from it."""
    try:
        ytd = ff.read_ytd(path)
    except Exception as exc:  # noqa: BLE001
        raise OpError(E_PARSE_FAILED, f"Could not read texture dictionary: {exc}") from exc

    if index >= len(ytd.textures):
        raise OpError(E_PARSE_FAILED, f"Texture {index} not present in dictionary.")

    tex = ytd.textures[index]
    try:
        dds = tex.to_dds_bytes()
    except Exception as exc:  # noqa: BLE001
        raise OpError(E_PARSE_FAILED, f"Could not decode texture: {exc}") from exc

    blob.parent.mkdir(parents=True, exist_ok=True)
    blob.write_bytes(dds)
    return {"blob": str(blob), "format": "dds", **_texture_info(tex)}


def ydd_make_emissive(
    source: Path,
    destination: Path,
    *,
    multiplier: float = 1.0,
    material_index: int = 0,
) -> dict[str, Any]:
    """Turn a garment's drawable into one that glows.

    Measured in a running FiveM client, not guessed:

        emission = vertex colour BLUE  x  diffuse ALPHA  x  emissiveMultiplier

    Both factors are needed. Swapping the shader alone changes nothing, because
    base-ped meshes and most DLC ones carry blue = 0 on every vertex and the
    product collapses to zero. Finding that took eight in-game tests: shader
    name, .sps file, shader hash, render bucket, parameters, textures, mesh
    channels, vertex declaration and material descriptor are all identical
    between a garment that glows and one that does not. The blue channel was
    the only difference.

    Blue is painted on EVERY lod. Rockstar paints all three as well (measured
    on mp_m_2024_01/jbib_014: 3.3% of high, 5.1% of med, 9.6% of low), and a
    garment that stopped glowing at distance would be worse than one that never
    glowed at all.

    The remaining materials drop to ped_default rather than staying
    palette-based, mirroring that same vanilla garment. A bound palette would
    keep tinting a garment we are about to light up.
    """
    try:
        ydd = ff.read_ydd(source)
    except Exception as exc:  # noqa: BLE001
        raise OpError(E_PARSE_FAILED, f"Could not read drawable dictionary: {exc}") from exc

    if not ydd.drawables:
        raise OpError(E_PARSE_FAILED, "The drawable dictionary is empty.")

    shaders: list[str] = []
    painted = 0

    for wrapper in ydd.drawables:
        drawable = wrapper.drawable

        for index in range(len(drawable.materials)):
            emissive = index == material_index
            drawable.update_material(
                index,
                shader="ped_emissive" if emissive else "ped_default",
                textures={"TextureSamplerDiffPal": None},
                parameters={"emissiveMultiplier": float(multiplier)} if emissive else None,
                preserve_values=True,
            )
            shaders.append(drawable.materials[index].shader_name)

        for lod in ff.DRAWABLE_LOD_ORDER:
            for mesh in drawable.get_lod_meshes(lod) or ():
                if not mesh.colours0:
                    continue
                mesh.colours0 = [(r, g, 1.0, a) for (r, g, _b, a) in mesh.colours0]
                painted += len(mesh.colours0)

    if painted == 0:
        raise OpError(
            E_PARSE_FAILED,
            "This drawable carries no vertex colour channel, so it cannot be lit.")

    destination.parent.mkdir(parents=True, exist_ok=True)
    try:
        ff.save_ydd(ydd, destination)
    except Exception as exc:  # noqa: BLE001
        raise OpError(E_WRITE_FAILED, f"Could not write drawable dictionary: {exc}") from exc

    return {
        "path": str(destination),
        "issues": [],
        "shaders": shaders,
        "verticesPainted": painted,
        "emissiveMultiplier": float(multiplier),
        "sizeBytes": destination.stat().st_size,
    }
