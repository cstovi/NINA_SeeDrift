"""Mirror C# FitsCoordinates primary header parse; audit folder."""
from __future__ import annotations

import os
import re
import sys


def read_primary_cards(path: str) -> dict[str, str]:
    cards: list[str] = []
    with open(path, "rb") as fs:
        while True:
            buf = fs.read(2880)
            if len(buf) != 2880:
                break
            for i in range(0, 2880, 80):
                line = buf[i : i + 80].decode("ascii", errors="replace").rstrip()
                # Match C#: StartsWith("END ") || line == "END" (TrimEnd applied)
                if line.startswith("END ") or line == "END":
                    return parse_cards(cards)
                cards.append(line)
    return parse_cards(cards)


def parse_cards(raw_lines: list[str]) -> dict[str, str]:
    d: dict[str, str] = {}
    seen: set[str] = set()
    for line in raw_lines:
        if len(line) < 9:
            continue
        key = line[:8].rstrip()
        lk = key.upper()
        eq = line.find("=")
        if eq < 0:
            continue
        value = line[eq + 1 :].strip()
        value = re.sub(r"/.*$", "", value).strip()
        value = value.strip("' ").strip('"')
        if lk not in seen:
            seen.add(lk)
            d[key] = value
    return d


def try_parse_ra_dec(cards: dict[str, str]) -> tuple[float, float] | None:
    if "RA" not in cards or "DEC" not in cards:
        return None
    try:
        rv_ra = float(cards["RA"].split()[0])
        rv_dec = float(cards["DEC"].split()[0])
    except (ValueError, IndexError):
        return None
    if rv_ra <= 24.0 and abs(rv_ra) < 25.0:
        return rv_ra, rv_dec
    return rv_ra / 15.0, rv_dec


def main() -> None:
    folder = sys.argv[1]
    files = sorted(
        f for f in os.listdir(folder) if f.lower().endswith((".fits", ".fit", ".fts"))
    )
    ok_hdr = 0
    ok_coords = 0
    empty_hdr = []
    for fn in files:
        p = os.path.join(folder, fn)
        try:
            cards = read_primary_cards(p)
        except OSError as e:
            empty_hdr.append((fn, str(e)))
            continue
        if len(cards) == 0:
            empty_hdr.append((fn, "empty dict"))
            continue
        ok_hdr += 1
        pt = try_parse_ra_dec(cards)
        if pt:
            ok_coords += 1
    print("files:", len(files))
    print("non-empty header dict:", ok_hdr)
    print("parsed RA/DEC like C#:", ok_coords)
    print("failed:", len(empty_hdr))
    if empty_hdr[:10]:
        print("examples:", empty_hdr[:10])


if __name__ == "__main__":
    main()
