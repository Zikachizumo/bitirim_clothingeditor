import fivefury as ff, json
from pathlib import Path
pv = ff.read_ymt(Path(r"C:\bcc\fixtures\mp_m_freemode_01.ymt")).ped_variation
print("=== aComponentData3[11] (JBIB) ===")
c = pv["aComponentData3"][11]
print("keys:", list(c.keys()), "numAvailTex:", c["numAvailTex"], "drawables:", len(c["aDrawblData3"]))
print("first aDrawblData3 entry:")
print(json.dumps(c["aDrawblData3"][0], indent=1, default=str))
print("\n=== compInfos[0] ===")
print(json.dumps(pv["compInfos"][0], indent=1, default=str))
print("\n=== compInfos: how many per component? ===")
from collections import Counter
# try to find which field indicates component
ci = pv["compInfos"]
for key in ci[0].keys():
    vals = [e.get(key) for e in ci]
    if all(isinstance(v,int) for v in vals):
        uniq = sorted(set(vals))
        if 1 < len(uniq) <= 13:
            print(f"  {key}: uniq={uniq} counts={dict(Counter(vals))}")
