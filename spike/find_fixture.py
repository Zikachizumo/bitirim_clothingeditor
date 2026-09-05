import sys, time, fivefury as ff
from pathlib import Path

GTA = Path(r"D:\SteamLibrary\steamapps\common\Grand Theft Auto V")
t0=time.time()
crypto = ff.load_game_keys(GTA)
print(f"keys loaded in {time.time()-t0:.1f}s  aes={len(crypto.aes_key)}B ng_keys={len(crypto.ng_keys)}")

def walk(entry, prefix=""):
    for child in getattr(entry, "directories", []) or []:
        yield from walk(child, prefix + getattr(child,"name","") + "/")
    for f in getattr(entry, "files", []) or []:
        yield prefix + getattr(f,"name",""), f

targets = ["x64v.rpf", "x64u.rpf", "x64w.rpf"]
for name in targets:
    p = GTA / name
    if not p.exists():
        continue
    try:
        t=time.time()
        arc = ff.load_rpf(p)
        paths = [n for n,_ in walk(arc.root)]
        hits = [n for n in paths if "freemode" in n.lower()]
        print(f"\n{name}: {len(paths)} entries in {time.time()-t:.1f}s, freemode hits={len(hits)}")
        for h in hits[:8]: print("   ", h)
        # nested archives
        for child in arc.children[:40]:
            cn = getattr(child,'name','')
            if 'ped' in cn.lower() or 'freemode' in cn.lower():
                print("   nested:", cn)
    except Exception as e:
        print(f"{name}: FAILED {type(e).__name__}: {e}")
