#!/usr/bin/env python3
"""C2c R2: independent Arabic lemma-position alignment research gate.

Validates a pinned Quranic Arabic Corpus v0.4 morphological annotation against
the ACCEPTED THTRP canonical corpus and word-by-word positional database.
No mutation. No morphology data is bundled into the app by this tool.
QAC annotations: (C) 2011 Kais Dukes, GNU GPL. Source corpus.quran.com/download/
"""
from __future__ import annotations
import argparse
from collections import Counter, defaultdict
from pathlib import Path
import re
import sqlite3
import sys

ANNOTATION_BLOB = "b91cec6e95d5e0306550b4aedacc7380dc71152a"
EXPECTED_SOURCE_CORPUS_SHA256 = "188e29730b62efaff748e160f73bb2e6665769df4fa779ee1436af939c6e7862"
EXPECTED_WORD_BY_WORD_SHA256 = "d119f2f113e916a9968f7275f87d31f1f8027ddcf2a5b1ff80e60b7c1b122a31"
LOCATION = re.compile(r"^\((\d+):(\d+):(\d+):(\d+)\)\s+(\S+)\s+(\S+)\s+(.*)$")
TARGETS = {"{ll~ah": ("PN", 2699), "raHomap": ("N", 114)}

def sha256(path):
    import hashlib
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()

def git_blob_sha1(path):
    import hashlib
    p = Path(path)
    h = hashlib.sha1()
    h.update(f"blob {p.stat().st_size}\0".encode())
    with p.open("rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()

def read_annotation(path):
    slots = set()
    matched = defaultdict(set)
    total_segments = 0
    forms = Counter()
    examples = {}
    malformed = 0
    with open(path, "r", encoding="utf-8-sig") as f:
        for lineno, raw in enumerate(f, 1):
            line = raw.strip()
            if not line or line.startswith("#") or line.startswith("LOCATION"):
                continue
            m = LOCATION.match(line)
            if not m:
                if malformed < 5:
                    print(f"UNRECOGNIZED LINE {lineno}: {line[:180]}")
                malformed += 1
                continue
            surah, ayah, position, segment = map(int, m.group(1,2,3,4))
            form, tag, features = m.group(5,6,7)
            slot = (f"{surah}:{ayah}", position)
            slots.add(slot)
            total_segments += 1
            if "STEM" not in features.split("|"):
                continue
            fields = set(features.split("|"))
            for lemma, (expected_pos, _) in TARGETS.items():
                if f"LEM:{lemma}" in fields and f"POS:{expected_pos}" in fields:
                    if slot in matched[lemma]:
                        raise ValueError(f"Duplicate lemma word position {lemma} at {slot}")
                    matched[lemma].add(slot)
                    forms[(lemma, form)] += 1
                    examples.setdefault(lemma, (slot, form, tag, features))
    if malformed:
        raise ValueError(f"{malformed} unrecognized morphology rows")
    return slots, matched, total_segments, forms, examples

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--annotation", required=True)
    parser.add_argument("--corpus", required=True)
    parser.add_argument("--word-by-word", required=True)
    args = parser.parse_args()
    for path in [args.annotation, args.corpus, args.word_by_word]:
        if not Path(path).is_file():
            raise FileNotFoundError(path)

    blob = git_blob_sha1(args.annotation)
    if blob != ANNOTATION_BLOB:
        raise ValueError(f"Pinned morphology Git blob mismatch: {blob}")
    if sha256(args.corpus) != EXPECTED_SOURCE_CORPUS_SHA256:
        raise ValueError("Canonical corpus checksum mismatch")
    if sha256(args.word_by_word) != EXPECTED_WORD_BY_WORD_SHA256:
        raise ValueError("Word-by-word database checksum mismatch")

    slots, matches, segments, forms, examples = read_annotation(args.annotation)
    with sqlite3.connect(f"file:{Path(args.word_by_word).resolve()}?mode=ro", uri=True) as w:
        accepted = set(w.execute("SELECT verse_key,position FROM words"))
    with sqlite3.connect(f"file:{Path(args.corpus).resolve()}?mode=ro", uri=True) as c:
        verse_keys = {row[0] for row in c.execute("SELECT verse_key FROM verses")}
    absent_annotation = accepted - slots
    extra_annotation = slots - accepted
    print(f"Pinned morphology blob: {blob}")
    print(f"Annotation: {len(slots)} unique word positions, {segments} morphological segments")
    print(f"Accepted WBW: {len(accepted)} positions across {len(verse_keys)} verses")
    print(f"Alignment: missing annotations={len(absent_annotation)}; unexpected annotations={len(extra_annotation)}")
    if absent_annotation:
        print(f"Annotation missing example: {sorted(absent_annotation)[:12]}")
    if extra_annotation:
        print(f"Annotation extra example: {sorted(extra_annotation)[:12]}")

    if not {key for key, _ in slots}.issubset(verse_keys):
        raise ValueError("Annotation references non-canonical Ayat")
    for lemma, (pos, expected_count) in TARGETS.items():
        positions = matches[lemma]
        unique_ayat = {key for key, _ in positions}
        missing = positions - accepted
        print(f"LEMMA {lemma} ({pos}): {len(positions)} word positions, {len(unique_ayat)} distinct Ayat")
        print(f"  word-position misses against accepted WBW: {len(missing)}; example: {sorted(missing)[:10]}")
        print(f"  example: {examples.get(lemma)}")
        print(f"  most frequent written stem forms: {forms.most_common(1)[0] if lemma == '{ll~ah' else forms.most_common(8)}")
        if len(positions) != expected_count:
            raise ValueError(f"QAC lemma count mismatch for {lemma}: {len(positions)} != {expected_count}")
        if missing:
            raise ValueError(f"QAC {lemma} lemma has missing accepted positional evidence")
    if len(matches["{ll~ah"]) == 2699 and len({a for a,b in matches["{ll~ah"]}) != 1821:
        print("NOTICE: Allah distinct Ayah count differs from preliminary target 1821; verify rather than forcing target.")
    if extra_annotation or absent_annotation:
        print("ALIGNMENT NOT YET CLOSED: do not generate or package any derived positional index.")
        return 10
    print("C2c R2 morpho-positional SOURCE-ALIGNMENT GATE PASSED. No data written.")
    return 0

if __name__ == "__main__":
    sys.exit(main())
