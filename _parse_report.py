import json
path = r"c:\Users\carls\Downloads\SeeDrift_v0_8_20_0_ran20260518_sess20260517.html"
with open(path, encoding="utf-8") as f:
    for line in f:
        if line.strip().startswith('{"SchemaVersion"'):
            data = json.loads(line.strip())
            break
    else:
        raise SystemExit("no json line")

print("Plugin", data.get("PluginVersion"))
for t in data.get("Targets", []):
    name = t.get("TargetName")
    a = t.get("Analysis") or {}
    dr = a.get("DriftRisk") or {}
    print("---", name, "---")
    for k in sorted(dr.keys()):
        print(f"  {k}: {dr[k]}")
    dithers = a.get("Dithers") or []
    assessed = [d for d in dithers if not d.get("IsSuspect")]
    moves = sorted(d.get("MoveArcSec", 0) for d in assessed)
    med = moves[len(moves) // 2] if moves else 0
    print(f"  dithers assessed: {len(assessed)}, median move: {med:.2f}\"")
    print(f"  weak: {sum(1 for d in assessed if d.get('Assessment')=='Weak')}, repeated: {sum(1 for d in assessed if d.get('Assessment')=='Repeated direction')}")
