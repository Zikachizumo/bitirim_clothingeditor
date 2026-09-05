import fivefury as ff, inspect
names = [n for n in dir(ff) if not n.startswith('_')]

def sig(n):
    o = getattr(ff, n)
    try: return f"{n}{inspect.signature(o)}"
    except Exception: return n

print("=== RPF / archive ===")
for n in names:
    if any(k in n.lower() for k in ('rpf','archive','extract')): print(" ", sig(n))
print("\n=== GameFileCache ===")
for n in names:
    if 'cache' in n.lower() or 'gamefile' in n.lower(): print(" ", sig(n))
print("\n=== ytd read/write ===")
for n in ('read_ytd','save_ytd','read_ytd_catalog','read_embedded_ytd_catalog'):
    print(" ", sig(n))
print("\n=== ydd read/write ===")
for n in ('read_ydd','create_ydd','save_ydd','build_ydd_bytes'):
    print(" ", sig(n))
print("\n=== ymt read/write ===")
for n in ('read_ymt','save_ymt'):
    print(" ", sig(n))
