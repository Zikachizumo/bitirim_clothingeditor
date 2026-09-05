import fivefury as ff, inspect
from pathlib import Path
F = Path(r"C:\bcc\fixtures")
for f in ("jbib_diff_000_a_uni.ytd","jbib_diff_000_b_uni.ytd"):
    ytd = ff.read_ytd(F/f)
    print(f"=== {f}  game={ytd.game}")
    for t in ytd.textures:
        print(f"    {t.name:34} {t.width}x{t.height} {t.format_name:10} mips={t.mip_count} "
              f"usage={t.usage} data={len(t.data):,}B")
print()
print("Ytd.texture   ", inspect.signature(ff.Ytd.texture))
print("Ytd.build     ", inspect.signature(ff.Ytd.build))
print("Ytd.extract   ", inspect.signature(ff.Ytd.extract))
print("Texture.from_raw", inspect.signature(ff.Texture.from_raw))
print("Texture.save_dds", inspect.signature(ff.Texture.save_dds))
print("remove_texture", inspect.signature(ff.Ytd.remove_texture))
print("validate      ", inspect.signature(ff.Ytd.validate))
