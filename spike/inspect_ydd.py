import fivefury as ff
from pathlib import Path
F = Path(r"C:\bcc\fixtures")
ydd = ff.read_ydd(F/"jbib_000_u.ydd")
print(f"ydd.name={ydd.name!r} version={ydd.version} game={ydd.game} count={ydd.drawable_count}")
wrap = ydd.drawables[0]
d = wrap.drawable
print(f"\nwrapper name={wrap.name!r} hash={wrap.name_hash}")
print("Drawable type:", type(d).__name__)
print("attrs:", [a for a in dir(d) if not a.startswith('_')])
