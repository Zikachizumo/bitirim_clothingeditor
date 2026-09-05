import fivefury as ff, time
from pathlib import Path
F = Path(r"C:\bcc\fixtures")
t=time.time(); ydd = ff.read_ydd(F/"jbib_000_u.ydd"); print(f"read {(time.time()-t)*1000:.1f}ms")
print("Ydd attrs:", [a for a in dir(ydd) if not a.startswith('_')])
ds = ydd.drawables if hasattr(ydd,'drawables') else None
print("\ndrawables:", type(ds).__name__, len(ds))
d = ds[0]
print("drawable type:", type(d).__name__)
print("drawable attrs:", [a for a in dir(d) if not a.startswith('_')])
