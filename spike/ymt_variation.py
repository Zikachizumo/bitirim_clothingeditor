import fivefury as ff, inspect
from pathlib import Path
F = Path(r"C:\bcc\fixtures")
y = ff.read_ymt(F/"mp_m_freemode_01.ymt")
pv = y.ped_variation
print("ped_variation type:", type(pv).__name__)
print("attrs:", [a for a in dir(pv) if not a.startswith('_')])
print()
for a in [a for a in dir(pv) if not a.startswith('_')]:
    try:
        v = getattr(pv, a)
        if callable(v): continue
        s = str(v)
        print(f"  {a:26} {type(v).__name__:12} {s[:150]}")
    except Exception as e:
        print(f"  {a:26} ERR {e}")
print()
print("PedComponent members:", [m.name for m in ff.PedComponent])
