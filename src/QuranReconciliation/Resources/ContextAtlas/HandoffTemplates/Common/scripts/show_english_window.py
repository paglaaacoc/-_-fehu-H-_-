#!/usr/bin/env python3
from pathlib import Path
import sys, re

if len(sys.argv) < 3:
    raise SystemExit("Usage: show_english_window.py <surah> <ayah> [radius]")

surah=int(sys.argv[1]); ayah=int(sys.argv[2]); radius=int(sys.argv[3]) if len(sys.argv)>3 else 3
pack=Path(__file__).resolve().parents[1]
index=__import__("json").loads((pack/"06-SURAH-INDEX.json").read_text(encoding="utf-8"))
item=next((x for x in index if x["surah_number"]==surah),None)
if item is None: raise SystemExit("Unknown Surah")
path=pack/item["english_clarification_file"]
lo=max(1,ayah-radius); hi=min(item["verses_count"],ayah+radius)

for line in path.read_text(encoding="utf-8").splitlines():
    m=re.match(r"^(\d+)\t",line)
    if m and lo <= int(m.group(1)) <= hi:
        print(line)
