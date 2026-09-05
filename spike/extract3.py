import fivefury as ff
from pathlib import Path
GTA = Path(r"D:\SteamLibrary\steamapps\common\Grand Theft Auto V")
OUT = Path(r"C:\bcc\fixtures"); OUT.mkdir(parents=True, exist_ok=True)
ff.load_game_keys(GTA)
arc = ff.load_rpf(GTA/"x64v.rpf")
def walk(d, p=""):
    for c in (getattr(d,'directories',[]) or []): yield from walk(c, p+c.name+"/")
    for f in (getattr(d,'files',[]) or []): yield p+f.name, f
nested = arc.load_nested_archive(dict(walk(arc.root))["models/cdimages/streamedpeds_mp.rpf"])
items = dict(walk(nested.root))

want = [
    "mp_m_freemode_01/jbib_000_u.ydd",
    "mp_m_freemode_01/jbib_diff_000_a_uni.ytd",
    "mp_m_freemode_01/jbib_diff_000_b_uni.ytd",
    "mp_m_freemode_01.ymt",
    "mp_m_freemode_01.yft",
]
for w in want:
    e = items.get(w)
    if e is None:
        print("MISSING", w); continue
    data = nested.read_entry_standalone(e)
    dst = OUT / Path(w).name
    dst.write_bytes(data)
    print(f"{dst.name:42} {len(data):>10,} bytes  magic={data[:4]!r}")
