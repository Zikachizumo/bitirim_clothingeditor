import fivefury as ff, hashlib, time, json
from pathlib import Path
F = Path(r"C:\bcc\fixtures"); O = Path(r"C:\bcc\out"); O.mkdir(exist_ok=True)
src = F/"jbib_000_u.ydd"

def struct(p):
    y = ff.read_ydd(p)
    out = {"name": y.name, "version": y.version, "game": str(y.game),
           "drawable_count": y.drawable_count, "drawables": []}
    for w in y.drawables:
        d = w.drawable
        emb = d.embedded_textures
        lods = {}
        for lod in ff.DRAWABLE_LOD_ORDER:
            ms = d.get_lod_meshes(lod)
            if not ms: continue
            lods[str(lod)] = {"meshes": len(ms),
                "verts": sum(len(m.positions) for m in ms),
                "indices": sum(len(m.indices) for m in ms),
                "uv_sets": [len(m.texcoords) for m in ms],
                "skinned": [bool(m.is_skinned) for m in ms],
                "blend_idx": sum(len(m.blend_indices or []) for m in ms),
                "blend_wgt": sum(len(m.blend_weights or []) for m in ms),
                "normals": sum(len(m.normals or []) for m in ms),
                "tangents": sum(len(m.tangents or []) for m in ms),
                "bone_ids": [list(m.bone_ids or [])[:8] for m in ms]}
        out["drawables"].append({
            "wrapper_name": w.name, "name_hash": w.name_hash,
            "drawable_name": d.name,
            "bbox": [round(v,5) for v in (d.bounding_box_min.x,d.bounding_box_min.y,d.bounding_box_min.z,
                                          d.bounding_box_max.x,d.bounding_box_max.y,d.bounding_box_max.z)],
            "radius": round(d.bounding_sphere_radius,5),
            "materials": [{"shader": m.shader_name, "sps": m.shader_file_name,
                           "bucket": m.render_bucket,
                           "textures": [t.name for t in m.textures]} for m in d.materials],
            "texture_names": list(d.texture_names),
            "embedded": [{"name":t.name,"w":t.width,"h":t.height,"fmt":t.format_name,
                          "mips":t.mip_count,"sha":hashlib.sha256(t.data).hexdigest()[:16]}
                         for t in emb.textures],
            "lods": lods,
            "lod_distances": {str(k):v for k,v in d.lod_distances.items()},
            "render_mask": {str(k):v for k,v in d.render_mask_flags.items()},
            "has_skeleton": d.has_skeleton, "has_joints": d.has_joints,
        })
    return out

print("--- YDD ROUND-TRIP ---")
t=time.time(); y = ff.read_ydd(src); t_read=(time.time()-t)*1000
dst = O/"rt_jbib_000_u.ydd"
t=time.time(); ff.save_ydd(y, dst); t_write=(time.time()-t)*1000
a = struct(src); b = struct(dst)
same = a==b
print(f"read={t_read:.1f}ms write={t_write:.1f}ms")
print(f"orig={src.stat().st_size:,}B new={dst.stat().st_size:,}B byte-identical={src.read_bytes()==dst.read_bytes()}")
print("STRUCTURAL MATCH:", same)
if not same:
    import difflib
    da=json.dumps(a,indent=1,sort_keys=True).splitlines()
    db=json.dumps(b,indent=1,sort_keys=True).splitlines()
    print("\n".join(list(difflib.unified_diff(da,db,'orig','roundtrip',lineterm=''))[:60]))
else:
    print(json.dumps(a["drawables"][0]["lods"], indent=1))
    print("embedded:", json.dumps(a["drawables"][0]["embedded"], indent=1))
    print("materials:", json.dumps(a["drawables"][0]["materials"], indent=1))
