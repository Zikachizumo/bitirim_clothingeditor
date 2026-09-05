import fivefury as ff
from pathlib import Path
F = Path(r"C:\bcc\fixtures")
ydd = ff.read_ydd(F/"jbib_000_u.ydd")
d = ydd.drawables[0].drawable
print(f"drawable name={d.name!r}  version={d.version}")
print(f"bbox min={d.bounding_box_min} max={d.bounding_box_max} r={d.bounding_sphere_radius:.3f}")
print(f"lod_distances={d.lod_distances}  render_mask={d.render_mask_flags}")
print(f"has_skeleton={d.has_skeleton}  has_joints={d.has_joints}")

sk = d.skeleton
if sk: print(f"\nskeleton: {len(sk.bones)} bones; first 6:",
             [(b.name, b.index, getattr(b,'tag',None)) for b in sk.bones[:6]])

print(f"\nLODs: {list(d.lods.keys()) if hasattr(d.lods,'keys') else d.lods}")
for lod in ff.DRAWABLE_LOD_ORDER:
    ms = d.get_lod_meshes(lod)
    if not ms: continue
    tv = sum(len(m.positions) for m in ms)
    ti = sum(len(m.indices) for m in ms)
    print(f"  {str(lod):24} meshes={len(ms):2} verts={tv:6,} idx={ti:7,}")

m = d.primary_meshes[0]
print(f"\nfirst mesh attrs: {[a for a in dir(m) if not a.startswith('_')]}")
print(f"  positions={len(m.positions)} indices={len(m.indices)}")
for f_ in ('normals','texcoords','colours','colors','blend_indices','blend_weights','tangents'):
    v = getattr(m, f_, None)
    if v is not None:
        try: print(f"  {f_}: len={len(v)}")
        except Exception: print(f"  {f_}: {type(v).__name__}")

print(f"\nmaterials: {len(d.materials)}")
for i,mat in enumerate(d.materials[:4]):
    print(f"  [{i}] {mat}")
print(f"\ntexture_names: {d.texture_names}")
print(f"embedded_textures: {len(d.embedded_textures)}")
for t in d.embedded_textures[:6]:
    print(f"   {t.name:32} {t.width}x{t.height} {t.format_name} mips={t.mip_count}")
