import fivefury as ff
from pathlib import Path
GTA = Path(r"D:\SteamLibrary\steamapps\common\Grand Theft Auto V")
ff.load_game_keys(GTA)
arc = ff.load_rpf(GTA/"x64v.rpf")
print("archive methods:", [a for a in dir(arc) if not a.startswith('_')])
def walk(d, p=""):
    for c in (getattr(d,'directories',[]) or []): yield from walk(c, p+c.name+"/")
    for f in (getattr(d,'files',[]) or []): yield p+f.name, f
items=list(walk(arc.root))
print("\ntotal entries:", len(items))
for n,_ in items: print("  ", n)
