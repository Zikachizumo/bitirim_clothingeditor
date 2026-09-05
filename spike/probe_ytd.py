import fivefury as ff, inspect
from pathlib import Path
F = Path(r"C:\bcc\fixtures")
ytd = ff.read_ytd(F/"jbib_diff_000_a_uni.ytd")
print("type:", type(ytd).__name__)
print("attrs:", [a for a in dir(ytd) if not a.startswith('_')])
print()
tex = None
for attr in ("textures","entries","items"):
    if hasattr(ytd, attr):
        v = getattr(ytd, attr)
        print(f"ytd.{attr}: {type(v).__name__} len={len(v) if hasattr(v,'__len__') else '?'}")
        try:
            tex = list(v)[0] if len(v) else None
        except Exception: pass
if tex is not None:
    print("\nfirst texture type:", type(tex).__name__)
    print("texture attrs:", [a for a in dir(tex) if not a.startswith('_')])
