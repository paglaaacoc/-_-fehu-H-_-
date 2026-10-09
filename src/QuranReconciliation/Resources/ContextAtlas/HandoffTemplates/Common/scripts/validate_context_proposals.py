#!/usr/bin/env python3
from pathlib import Path
import json, re, sys

root=Path(sys.argv[1] if len(sys.argv)>1 else "OUTPUT")
pack=Path(__file__).resolve().parents[1]
index=json.loads((pack/"06-SURAH-INDEX.json").read_text(encoding="utf-8"))
errors=[]; total_blocks=0; low=0; eng=0

for item in index:
    n=item["surah_number"]; vc=item["verses_count"]; name=item["name_simple"]
    slug=re.sub(r"[^A-Za-z0-9]+","-",name).strip("-")
    jf=root/"proposals"/f"{n:03d}-{slug}.json"
    tf=root/"importer"/f"{n:03d}-{slug}.txt"
    if not jf.exists() or not tf.exists():
        errors.append(f"Surah {n}: missing output"); continue
    d=json.loads(jf.read_text(encoding="utf-8"))
    blocks=d.get("blocks",[]); bounds=d.get("boundaries",[])
    if not blocks:
        errors.append(f"Surah {n}: no blocks"); continue
    if len(bounds)!=len(blocks)-1:
        errors.append(f"Surah {n}: boundary count mismatch")
    expected=1; txt=[]
    for i,b in enumerate(blocks):
        a,z=b["start"],b["end"]
        if a!=expected: errors.append(f"Surah {n}: expected {expected}, got {a}")
        if z<a or z>vc: errors.append(f"Surah {n}: invalid {a}-{z}")
        if not b.get("coherence_note","").strip(): errors.append(f"Surah {n}: missing block note")
        if b.get("confidence") not in {"high","medium","low"}: errors.append(f"Surah {n}: invalid block confidence")
        if b.get("confidence")=="low": low+=1
        expected=z+1; txt.append(str(a) if a==z else f"{a}-{z}")
    if blocks[0]["start"]!=1 or blocks[-1]["end"]!=vc:
        errors.append(f"Surah {n}: incomplete coverage")
    for i,b in enumerate(bounds):
        if b.get("after_ayah")!=blocks[i]["end"] or b.get("next_ayah")!=blocks[i+1]["start"]:
            errors.append(f"Surah {n}: boundary {i+1} does not align blocks")
        if not b.get("reason","").strip(): errors.append(f"Surah {n}: missing boundary reason")
        if b.get("confidence") not in {"high","medium","low"}: errors.append(f"Surah {n}: invalid boundary confidence")
        if b.get("confidence")=="low": low+=1
        if b.get("english_used"): eng+=1
    actual=[x.strip() for x in tf.read_text(encoding="utf-8").splitlines() if x.strip()]
    if actual!=txt: errors.append(f"Surah {n}: importer TXT mismatch")
    total_blocks += len(blocks)

if errors:
    print("\n".join("FAIL: "+x for x in errors))
    raise SystemExit(1)

print("PASS: 114 Surahs")
print("Total Context Blocks:",total_blocks)
print("Low-confidence block/boundary records:",low)
print("Boundaries using English clarification:",eng)
