"""One-off: inspect pointing keywords in a folder of FITS (requires astropy)."""
import os
import sys

try:
    from astropy.io import fits
except ImportError:
    print("Install astropy: pip install astropy", file=sys.stderr)
    sys.exit(1)

KEYS = [
    "CRVAL1",
    "CRVAL2",
    "CTYPE1",
    "CTYPE2",
    "RA",
    "DEC",
    "OBJCTRA",
    "OBJCTDEC",
    "TELRA",
    "TELDEC",
    "INSTRUME",
    "OBJECT",
    "IMAGETYP",
    "DATE-OBS",
]


def main() -> None:
    folder = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\carls\Documents\N.I.N.A\Elephant's Trunk Nebula Panel 1\LIGHTs\2026-04-29"
    all_files = sorted(
        f
        for f in os.listdir(folder)
        if f.lower().endswith((".fits", ".fit", ".fts"))
    )
    sample = all_files[:15]
    print(f"Folder: {folder}")
    print(f"Total FITS: {len(all_files)}, analyzing first {len(sample)}\n")

    rows = []
    for fn in sample:
        path = os.path.join(folder, fn)
        with fits.open(path) as hdul:
            h = hdul[0].header
            rows.append({k: str(h.get(k, "")).strip() for k in KEYS if k in h})

    seen = set()
    for r in rows:
        seen.update(r.keys())
    print("Keywords present in primary HDU (sample):", sorted(seen))

    print("\n--- First file detail ---")
    path0 = os.path.join(folder, sample[0])
    with fits.open(path0) as hdul:
        h0 = hdul[0].header
        for k in KEYS:
            if k in h0:
                print(f"  {k} = {str(h0[k])[:90]}")
        print(f"\nHDUs: {len(hdul)}  names={[x.name for x in hdul]}")
        if len(hdul) > 1:
            h1 = hdul[1].header
            ek = [k for k in KEYS if k in h1]
            print(f"Extension 1 watchlist keys: {ek}")
            for k in ek:
                print(f"  [ext1] {k} = {str(h1[k])[:90]}")

    def float_or_none(s: str):
        if not s:
            return None
        try:
            return float(s.split()[0])
        except ValueError:
            return None

    print("\n--- Variation across sample (numeric where possible) ---")

    def collect(key: str):
        vals = []
        for fn in sample:
            path = os.path.join(folder, fn)
            with fits.open(path) as hdul:
                if key not in hdul[0].header:
                    return None
                v = float_or_none(str(hdul[0].header[key]))
                if v is not None:
                    vals.append(v)
        return vals

    for key in ["CRVAL1", "CRVAL2", "RA", "DEC"]:
        vals = collect(key)
        if vals is None:
            print(f"{key}: absent")
            continue
        u = len(set(round(v, 12) for v in vals))
        print(f"{key}: unique rounded values={u}  min={min(vals):.12g}  max={max(vals):.12g}")

    # Simulate plugin keyword priority (matches FitsCoordinates.TryParsePointing order)
    print("\n--- Simulated plugin parse (same priority as TryParsePointing) ---")

    def plugin_ra_dec_hours(path: str):
        with fits.open(path) as hdul:
            cards = {k: str(hdul[0].header[k]).strip() for k in hdul[0].header.keys()}
        # Simplified: only the branches we use
        if "RA" in cards and "DEC" in cards:
            rv_ra = float_or_none(cards["RA"])
            rv_dec = float_or_none(cards["DEC"])
            if rv_ra is not None and rv_dec is not None:
                if rv_ra <= 24.0 and abs(rv_ra) < 25.0:
                    return rv_ra, rv_dec, "RA/DEC as hours/deg"
                return rv_ra / 15.0, rv_dec, "RA/DEC as deg/deg"
        if "TELRA" in cards and "TELDEC" in cards:
            tr = float_or_none(cards["TELRA"])
            td = float_or_none(cards["TELDEC"])
            if tr is not None and td is not None:
                if abs(tr) > 24.0:
                    return tr / 15.0, td, "TELRA>24 deg"
                return tr, td, "TEL hours"
        if "CRVAL1" in cards and "CRVAL2" in cards:
            v1 = float_or_none(cards["CRVAL1"])
            v2 = float_or_none(cards["CRVAL2"])
            if v1 is not None and v2 is not None:
                ct1 = cards.get("CTYPE1", "")
                if "RA" in ct1.upper() and -360 <= v1 <= 360:
                    return v1 / 15.0, v2, "CRVAL+CTYPE RA"
                if abs(v1) <= 24 and abs(v2) <= 90:
                    return v1, v2, "CRVAL small"
                return v1 / 15.0, v2, "CRVAL default"
        return None, None, "fail"

    sim = []
    for fn in sample:
        ra_h, dec_d, how = plugin_ra_dec_hours(os.path.join(folder, fn))
        sim.append((fn, ra_h, dec_d, how))

    for fn, ra_h, dec_d, how in sim[:3]:
        print(f"  {fn[:55]}... -> {how}  ra_h={ra_h} dec={dec_d}")

    ok = [x for x in sim if x[1] is not None]
    if ok:
        ras = [x[1] for x in ok]
        decs = [x[2] for x in ok]
        print(f"\nSimulated pointing: RA_h unique={len(set(ras))} Dec unique={len(set(decs))}")
        dr = max(ras) - min(ras)
        dd = max(decs) - min(decs)
        print(f"RA span (hours): {dr:.12g}  Dec span (deg): {dd:.12g}")
        # arcsec at ~dec 35
        import math

        dec_mid = sum(decs) / len(decs)
        d_ra_arcsec = dr * 15 * 3600 * math.cos(dec_mid * math.pi / 180)
        d_dec_arcsec = dd * 3600
        print(f"Approx max delta arcsec (RA horiz, Dec vert): dRA~{d_ra_arcsec:.3f}\" dDec~{d_dec_arcsec:.3f}\"")


if __name__ == "__main__":
    main()
