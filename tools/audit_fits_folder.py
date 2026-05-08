"""Audit FITS folder for RA/DEC presence and unique pointing pairs."""
import os
import sys
from collections import Counter

try:
    from astropy.io import fits
except ImportError:
    print("pip install astropy", file=sys.stderr)
    sys.exit(1)


def main():
    folder = sys.argv[1]
    files = sorted(
        f
        for f in os.listdir(folder)
        if f.lower().endswith((".fits", ".fit", ".fts"))
    )
    print("file count:", len(files))
    has_rd = 0
    pairs = []
    missing = []
    for fn in files:
        p = os.path.join(folder, fn)
        with fits.open(p) as hdul:
            h = hdul[0].header
            ra = h.get("RA")
            dec = h.get("DEC")
            if ra is None or dec is None:
                missing.append(fn)
                continue
            has_rd += 1
            pairs.append((float(ra), float(dec)))
    print("has RA+DEC:", has_rd, "missing RA or DEC:", len(missing))
    if missing[:5]:
        print("  example missing:", missing[:3])
    upairs = len(set((round(a, 10), round(b, 10)) for a, b in pairs))
    print("unique RA,DEC pairs (10dp):", upairs)
    c = Counter((round(a, 8), round(b, 8)) for a, b in pairs)
    print("top coord counts:", c.most_common(8))

    objs = []
    instr = []
    for fn in files:
        p = os.path.join(folder, fn)
        with fits.open(p) as hdul:
            h = hdul[0].header
            objs.append(str(h.get("OBJECT", "")).strip())
            instr.append(str(h.get("INSTRUME", "")).strip())
    print("unique OBJECT strings:", len(set(objs)), set(objs))
    print("unique INSTRUME strings:", len(set(instr)), list(set(instr))[:3])


if __name__ == "__main__":
    main()
