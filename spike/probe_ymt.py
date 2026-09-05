import fivefury as ff, inspect
from pathlib import Path
F = Path(r"C:\bcc\fixtures")
y = ff.read_ymt(F/"mp_m_freemode_01.ymt")
print("type:", type(y).__name__)
print("attrs:", [a for a in dir(y) if not a.startswith('_')])
print()
for a in ('content_type','format','name','root'):
    if hasattr(y,a):
        v=getattr(y,a); print(f"  {a} = {type(v).__name__} {str(v)[:120]}")
print()
print("YmtPedMetadata attrs:", [a for a in dir(ff.YmtPedMetadata) if not a.startswith('_')])
print()
print("PedComponent:", inspect.signature(ff.PedComponent))
print()
print("PedDrawableVariation:", inspect.signature(ff.PedDrawableVariation))
