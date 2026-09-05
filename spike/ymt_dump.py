import fivefury as ff, json
from pathlib import Path
F = Path(r"C:\bcc\fixtures")
y = ff.read_ymt(F/"mp_m_freemode_01.ymt")
pv = y.ped_variation
def shape(v, d=0):
    pad="  "*d
    if isinstance(v, dict):
        return "{\n" + "\n".join(f"{pad}  {k}: {shape(x,d+1)}" for k,x in list(v.items())[:14]) + f"\n{pad}}}"
    if isinstance(v, (list,tuple)):
        return f"[{len(v)}] " + (shape(v[0], d+1) if v else "")
    return f"{type(v).__name__}={str(v)[:90]}"
print("ped_variation keys:", list(pv.keys()))
print(shape(pv))
print("\n--- PedComponent values ---")
for m in ff.PedComponent: print(f"  {m.name:12} = {m.value}")
