import fivefury as ff, hashlib, time, json
from pathlib import Path
F = Path(r"C:\bcc\fixtures"); O = Path(r"C:\bcc\out"); O.mkdir(exist_ok=True)
src = F/"jbib_diff_000_a_uni.ytd"

def summarize(p):
    y = ff.read_ytd(p)
    return {"game": str(y.game), "count": len(y.textures),
            "tex": [{"name":t.name,"w":t.width,"h":t.height,"fmt":t.format_name,
                     "mips":t.mip_count,"usage":t.usage,"bytes":len(t.data),
                     "sha":hashlib.sha256(t.data).hexdigest()[:16]} for t in y.textures]}

print("--- TEST 1: YTD round-trip (write unchanged) ---")
t=time.time(); y = ff.read_ytd(src); t_read=(time.time()-t)*1000
dst = O/"rt_jbib_diff_000_a_uni.ytd"
t=time.time(); ff.save_ytd(y, dst); t_write=(time.time()-t)*1000
a, b = summarize(src), summarize(dst)
print(json.dumps(a, indent=1)); print(json.dumps(b, indent=1))
print("structural match:", a==b)
print(f"orig={src.stat().st_size:,}B  new={dst.stat().st_size:,}B  "
      f"byte-identical={src.read_bytes()==dst.read_bytes()}")
print(f"read={t_read:.1f}ms write={t_write:.1f}ms")

print("\n--- TEST 2: texture replace (variant b blocks into variant a dictionary) ---")
yb = ff.read_ytd(F/"jbib_diff_000_b_uni.ytd")
donor = yb.textures[0]
ya = ff.read_ytd(src)
target_name = ya.textures[0].name
newtex = ff.Texture.from_raw(donor.data, donor.width, donor.height,
                             donor.format, donor.mip_count, name=target_name,
                             usage=donor.usage, usage_flags=donor.usage_flags)
t=time.time(); ya.texture(newtex, replace=True); t_mod=(time.time()-t)*1000
out2 = O/"swap_jbib_diff_000_a_uni.ytd"
t=time.time(); ff.save_ytd(ya, out2); t_w2=(time.time()-t)*1000
rep = ya.validate()
print("validate:", rep)
print(json.dumps(summarize(out2), indent=1))
print(f"modify={t_mod:.1f}ms write={t_w2:.1f}ms  size={out2.stat().st_size:,}B")
