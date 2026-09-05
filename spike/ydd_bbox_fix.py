import fivefury as ff
from pathlib import Path
F = Path(r"C:\bcc\fixtures"); O = Path(r"C:\bcc\out")
src = F/"jbib_000_u.ydd"

y = ff.read_ydd(src)
d = y.drawables[0].drawable
orig = (d.bounding_box_min, d.bounding_box_max, d.bounding_center, d.bounding_sphere_radius)
print("orig bbox:", orig[0], orig[1], "center", orig[2], "r", orig[3])

# attempt 1: plain save then re-read (baseline already known bad)
# attempt 2: explicitly re-assert bbox right before save
dst = O/"jbib_000_u.ydd"   # same stem to keep dictionary name identical
try:
    d.bounding_box_min = orig[0]
    d.bounding_box_max = orig[1]
    d.bounding_center = orig[2]
    d.bounding_sphere_radius = orig[3]
    print("bbox attrs are writable")
except Exception as e:
    print("bbox NOT writable:", type(e).__name__, e)

ff.save_ydd(y, dst)
d2 = ff.read_ydd(dst).drawables[0].drawable
print("after save:", d2.bounding_box_min, d2.bounding_box_max, "r", d2.bounding_sphere_radius)
ok = (abs(d2.bounding_box_min.z - orig[0].z) < 1e-3 and abs(d2.bounding_sphere_radius-orig[3]) < 1e-3)
print("BBOX PRESERVED:", ok)
print("dictionary name preserved:", ff.read_ydd(dst).name)
