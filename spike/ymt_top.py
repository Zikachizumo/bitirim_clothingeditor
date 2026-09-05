import fivefury as ff
from pathlib import Path
F = Path(r"C:\bcc\fixtures")
pv = ff.read_ymt(F/"mp_m_freemode_01.ymt").ped_variation
print("TOP-LEVEL KEYS of CPedVariationInfo:")
for k,v in pv.items():
    if isinstance(v,(list,tuple)): print(f"  {k:22} list[{len(v)}]")
    elif isinstance(v,dict):       print(f"  {k:22} dict keys={list(v.keys())[:8]}")
    else:                          print(f"  {k:22} {type(v).__name__} = {str(v)[:70]}")

print("\n--- availComp ---")
for k in pv:
    if 'avail' in k.lower(): print(f"  {k}: {pv[k]}")

comp = None
for k,v in pv.items():
    if isinstance(v,list) and v and isinstance(v[0],dict) and any('DrawblData' in str(x) or 'aDrawblData' in str(v[0]) for x in [1]):
        comp = k
print("\ncandidate component array key:", comp)
for k,v in pv.items():
    if isinstance(v,list) and v and isinstance(v[0],dict):
        print(f"\n  ARRAY {k}: len={len(v)}  first-entry keys={list(v[0].keys())}")
