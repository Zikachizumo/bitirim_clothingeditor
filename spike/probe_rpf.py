import fivefury as ff
from pathlib import Path
GTA = Path(r"D:\SteamLibrary\steamapps\common\Grand Theft Auto V")
ff.load_game_keys(GTA)
arc = ff.load_rpf(GTA/"x64v.rpf")
print("root type:", type(arc.root).__name__)
print("root attrs:", [a for a in dir(arc.root) if not a.startswith('_')])
print("children:", len(arc.children))
d = arc.root
print("dirs:", [getattr(x,'name','?') for x in (getattr(d,'directories',[]) or [])][:20])
print("files:", [getattr(x,'name','?') for x in (getattr(d,'files',[]) or [])][:20])
