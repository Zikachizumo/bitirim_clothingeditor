import fivefury as ff, json, time
from pathlib import Path
F = Path(r"C:\bcc\fixtures"); O = Path(r"C:\bcc\out")
src = F/"mp_m_freemode_01.ymt"

def struct(p):
    y = ff.read_ymt(p); pv = y.ped_variation
    return {
      "content_type": str(y.content_type), "format": str(y.format),
      "res_version": y.resource_version, "root": pv.get("_meta_name"),
      "availComp": list(pv["availComp"]),
      "flags": {k: pv[k] for k in ("bHasTexVariations","bHasDrawblVariations","bHasLowLODs","bIsSuperLOD")},
      "compInfos": len(pv["compInfos"]),
      "aComponentData3": [{"numAvailTex": c["numAvailTex"],
                           "drawables": len(c["aDrawblData3"])} for c in pv["aComponentData3"]],
      "dlcName": pv["dlcName"],
    }

t=time.time(); y = ff.read_ymt(src); t_read=(time.time()-t)*1000
dst = O/"rt_mp_m_freemode_01.ymt"
t=time.time(); ff.save_ymt(y, dst); t_write=(time.time()-t)*1000
a,b = struct(src), struct(dst)
print(f"read={t_read:.1f}ms write={t_write:.1f}ms")
print(f"orig={src.stat().st_size:,}B new={dst.stat().st_size:,}B byte-identical={src.read_bytes()==dst.read_bytes()}")
print("STRUCTURAL MATCH:", a==b)
print(json.dumps(a, indent=1)[:1400])
if a!=b:
    import difflib
    print("\n".join(difflib.unified_diff(json.dumps(a,indent=1,sort_keys=True).splitlines(),
                                         json.dumps(b,indent=1,sort_keys=True).splitlines(),lineterm=''))[:2000])
