#!/usr/bin/env python3
"""Build an independent derived lemma-position index, not a Qur'an text authority.

Gate 2 of THTRP C2c R2. Reads ONLY pinned QAC morphology and accepted
portable Quran/WBW databases; writes a NEW staging SQLite database.
Annotation: Quranic Arabic Corpus v0.4, (C) 2011 Kais Dukes, GNU GPL.
https://corpus.quran.com/download/ . See separate licensing review.
"""
import argparse
from collections import Counter
import hashlib
from pathlib import Path
import re
import sqlite3
import sys

QAC_BLOB = "b91cec6e95d5e0306550b4aedacc7380dc71152a"
CORPUS_SHA = "188e29730b62efaff748e160f73bb2e6665769df4fa779ee1436af939c6e7862"
WBW_SHA = "d119f2f113e916a9968f7275f87d31f1f8027ddcf2a5b1ff80e60b7c1b122a31"
LOCATION = re.compile(r"^\((\d+):(\d+):(\d+):(\d+)\)$")
SCHEMA = "THTRP-morphology-positions-1"

def file_hash(path):
    hash_state = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            hash_state.update(chunk)
    return hash_state.hexdigest()

def qac_blob(path):
    path = Path(path)
    h = hashlib.sha1()
    h.update(f"blob {path.stat().st_size}\0".encode())
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()

def read_annotation(path):
    words = set()
    annotated = set()
    lemma_positions = set()
    segments = 0
    stem_count = 0
    for lineno, raw in enumerate(open(path, encoding="utf-8-sig"), 1):
        if raw.lstrip().startswith("#") or not raw.strip() or raw.startswith("LOCATION\t"):
            continue
        cells = raw.rstrip("\r\n").split("\t")
        m = LOCATION.fullmatch(cells[0]) if cells else None
        if m is None or len(cells) != 4:
            raise ValueError(f"Malformed QAC tab record at line {lineno}: {raw[:140]!r}")
        chapter, ayah, position, segment = map(int, m.groups())
        if min(chapter, ayah, position, segment) < 1:
            raise ValueError(f"Non-positive QAC position at line {lineno}")
        key = (f"{chapter}:{ayah}", position)
        words.add(key)
        if (key, segment) in annotated:
            raise ValueError(f"Duplicate QAC segment at {key}:{segment}")
        annotated.add((key, segment))
        segments += 1
        form, tag, features = cells[1:]
        flags = features.split("|")
        if "STEM" not in flags:
            continue
        stem_count += 1
        lemmas = [x[4:] for x in flags if x.startswith("LEM:")]
        poses = [x[4:] for x in flags if x.startswith("POS:")]
        if not lemmas:
            continue
        if len(lemmas) != 1 or len(poses) > 1:
            raise ValueError(f"Ambiguous QAC STEM annotation at {key}:{segment}")
        pos = poses[0] if poses else tag
        if not pos or not lemmas[0]:
            raise ValueError(f"Empty QAC lemma or POS at {key}:{segment}")
        lemma_positions.add((lemmas[0], pos, key[0], position))
    return words, lemma_positions, segments, stem_count

def build(args):
    if not all(Path(p).is_file() for p in [args.annotation, args.corpus, args.word_by_word]):
        raise FileNotFoundError("One or more pinned input files are missing.")
    if file_hash(args.corpus) != CORPUS_SHA or file_hash(args.word_by_word) != WBW_SHA:
        raise ValueError("R5 source corpus or WBW digest mismatch.")
    if qac_blob(args.annotation) != QAC_BLOB:
        raise ValueError("Morphology source is not the pinned QAC v0.4 Git blob.")

    slots, lemma_rows, segments, stems = read_annotation(args.annotation)
    corpus = sqlite3.connect(f"file:{Path(args.corpus).resolve()}?mode=ro", uri=True)
    wbw = sqlite3.connect(f"file:{Path(args.word_by_word).resolve()}?mode=ro", uri=True)
    canon = {v for (v,) in corpus.execute("SELECT verse_key FROM verses")}
    accepted = wbw.execute(
        "SELECT verse_key,position,word_id,text_uthmani FROM words "
        "ORDER BY word_id").fetchall()
    accepted_slots = {(v, p) for v, p, _, _ in accepted}
    if len(accepted) != 77429 or len(canon) != 6236:
        raise ValueError("Accepted Quran positional/cardinality contract failed.")
    if len(accepted_slots) != len(accepted) or slots != accepted_slots:
        raise ValueError(f"QAC-WBW alignment failed: missing={len(accepted_slots-slots)}, "
                         f"extra={len(slots-accepted_slots)}")
    if any((verse_key, position) not in accepted_slots for _, _, verse_key, position in lemma_rows):
        raise ValueError("Lemma positions outside accepted WBW authority.")
    if any(v not in canon for v,_,_,_ in accepted):
        raise ValueError("Word position references an absent canonical Ayah.")
    target = {("{ll~ah", "PN"): (2699, 1821),
              ("raHomap", "N"): (114, 112)}
    for (lemma, pos), (expected_words, expected_ayat) in target.items():
        rows = {(v, p) for l, k, v, p in lemma_rows if l == lemma and k == pos}
        count, verses = len(rows), len({v for v, _ in rows})
        if (count, verses) != (expected_words, expected_ayat):
            raise ValueError(f"Verified {lemma} lemma count changed: {count}/{verses}")
        print(f"{lemma} ({pos}): {count} word occurrences, {verses} distinct Ayat")

    output = Path(args.output).resolve()
    if output.exists():
        raise FileExistsError("Refuse to overwrite existing derived index: " + str(output))
    output.parent.mkdir(parents=True, exist_ok=True)
    staged = output.with_suffix(".staging.sqlite")
    if staged.exists():
        raise FileExistsError("Existing staged index; refusing implicit overwrite.")
    db = None
    try:
        with sqlite3.connect(staged) as db:
            db.execute("PRAGMA foreign_keys=ON")
            db.execute("PRAGMA journal_mode=DELETE")
            db.executescript("""
                CREATE TABLE metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE word_positions (
                  verse_key TEXT NOT NULL, position INTEGER NOT NULL,
                  word_id INTEGER NOT NULL UNIQUE, text_uthmani TEXT NOT NULL,
                  PRIMARY KEY (verse_key, position)
                ) WITHOUT ROWID;
                CREATE TABLE lemma_positions (
                  lemma TEXT NOT NULL, pos TEXT NOT NULL,
                  verse_key TEXT NOT NULL, position INTEGER NOT NULL,
                  PRIMARY KEY (lemma,pos,verse_key,position),
                  FOREIGN KEY (verse_key,position)
                    REFERENCES word_positions(verse_key,position)
                ) WITHOUT ROWID;
                CREATE INDEX idx_lemma_by_verse ON
                   lemma_positions(verse_key,position,lemma);
            """)
            db.executemany("INSERT INTO word_positions VALUES (?,?,?,?)", accepted)
            db.executemany(
                "INSERT INTO lemma_positions VALUES (?,?,?,?)", sorted(lemma_rows))
            meta = {
                "schema": SCHEMA,
                "canonical_corpus_sha256": CORPUS_SHA,
                "accepted_wbw_sha256": WBW_SHA,
                "qac_morphology_blob_sha1": QAC_BLOB,
                "qac_attribution": "Quranic Arabic Corpus v0.4 (C) 2011 Kais Dukes; GNU GPL; corpus.quran.com/download/",
                "copyright_review": "Derived QAC annotations; do not bundle in closed-source application without appropriate license review.",
                "word_positions": str(len(accepted)),
                "morphological_segments": str(segments),
                "morphological_stems": str(stems),
                "lemma_positions": str(len(lemma_rows)),
            }
            db.executemany("INSERT INTO metadata VALUES (?,?)", sorted(meta.items()))
            integrity = db.execute("PRAGMA quick_check").fetchone()[0]
            if integrity != "ok":
                raise ValueError("Derived SQLite integrity failed: " + integrity)
        # sqlite3.Connection context manager COMMITs but DOES NOT CLOSE.
        # Explicit close is required before Windows staging-file rename.
        db.close()
        staged.replace(output)
    except BaseException:
        if db is not None:
            db.close()
        staged.unlink(missing_ok=True)
        raise
    finally:
        corpus.close()
        wbw.close()
    with sqlite3.connect(f"file:{output}?mode=ro", uri=True) as db:
        actual = db.execute("SELECT COUNT(*) FROM lemma_positions").fetchone()[0]
        if actual != len(lemma_rows):
            raise ValueError("Read-back lemma index row count mismatch.")
        for (lemma, pos), (expected, verses) in target.items():
            actual = db.execute(
                "SELECT COUNT(*),COUNT(DISTINCT verse_key) FROM lemma_positions "
                "WHERE lemma=? AND pos=?", (lemma, pos)).fetchone()
            if actual != (expected, verses):
                raise ValueError("Persistent lemma query mismatch: " + str(actual))
    print(f"Verified source positions={len(slots)}, morphological segments={segments}, "
          f"stems={stems}, indexed lemma positions={len(lemma_rows)}")
    print(f"Derived research index: {output.stat().st_size} bytes, SHA256 {file_hash(output)}")
    print("R2 Gate 2 positional SQLite build PASS; no accepted corpus/research data modified.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--annotation", required=True)
    parser.add_argument("--corpus", required=True)
    parser.add_argument("--word-by-word", required=True)
    parser.add_argument("--output", required=True)
    build(parser.parse_args())
