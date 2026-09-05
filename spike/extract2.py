import fivefury as ff
from pathlib import Path
GTA = Path(r"D:\SteamLibrary\steamapps\common\Grand Theft Auto V")
OUT = Path(r"C:\bcc\fixtures"); OUT.mkdir(parents=True, exist_ok=True)
ff.load_game_keys(GTA)
arc = ff.load_rpf(GTA/"x64v.rpf")
def walk(d, p=""):
    for c in (getattr(d,'directories',[]) or []): yield from walk(c, p+c.name+"/")
    for f in (getattr(d,'files',[]) or []): yield p+f.name, f
items = dict(walk(arc.root))
nested = arc.load_nested_archive(items["models/cdimages/streamedpeds_mp.rpf"])
n_items = dict(walk(nested.root))

jbib = sorted(n for n in n_items if n.startswith("mp_m_freemode_01/jbib"))
print("male jbib entries:", len(jbib))
for n in jbib[:12]: print("  ", n, n_items[n].__class__.__name__)
print("...")
ytds = [n for n in jbib if n.endswith('.ytd')]
print("jbib ytd count:", len(ytds), ytds[:5])
