import fivefury as ff, time
from pathlib import Path
GTA = Path(r"D:\SteamLibrary\steamapps\common\Grand Theft Auto V")
OUT = Path(r"C:\bcc\fixtures"); OUT.mkdir(parents=True, exist_ok=True)
ff.load_game_keys(GTA)
arc = ff.load_rpf(GTA/"x64v.rpf")

def walk(d, p=""):
    for c in (getattr(d,'directories',[]) or []): yield from walk(c, p+c.name+"/")
    for f in (getattr(d,'files',[]) or []): yield p+f.name, f

entry = dict(walk(arc.root))["models/cdimages/streamedpeds_mp.rpf"]
t=time.time(); nested = arc.load_nested_archive(entry); print(f"nested loaded {time.time()-t:.2f}s")
items = dict(walk(nested.root))
print("entries:", len(items))
fm = sorted(n for n in items if "freemode" in n.lower())
print("freemode entries:", len(fm))
for n in fm[:10]: print("  ", n)
