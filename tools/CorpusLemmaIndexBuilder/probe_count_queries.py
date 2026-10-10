#!/usr/bin/env python3
"""Independent read-only count-contract probe for derived R2 Gate2 SQLite."""
import argparse
from collections import Counter
from pathlib import Path
import sqlite3

parser = argparse.ArgumentParser()
parser.add_argument("--index", required=True)
parser.add_argument("--corpus", required=True)
args = parser.parse_args()
index = sqlite3.connect(f"file:{Path(args.index).resolve()}?mode=ro", uri=True)
corpus = sqlite3.connect(f"file:{Path(args.corpus).resolve()}?mode=ro", uri=True)
meta = dict(index.execute("SELECT key,value FROM metadata"))
assert meta["schema"] == "THTRP-morphology-positions-1"
assert meta["word_positions"] == "77429"

def count_lemma(lemma, pos, words, ayat):
    actual = index.execute(
        "SELECT COUNT(*),COUNT(DISTINCT verse_key) FROM lemma_positions "
        "WHERE lemma=? AND pos=?", (lemma, pos)).fetchone()
    assert actual == (words, ayat), (lemma, actual)
    verse_rows = index.execute(
        "SELECT verse_key, COUNT(*) FROM lemma_positions WHERE lemma=? AND pos=? "
        "GROUP BY verse_key ORDER BY verse_key", (lemma, pos)).fetchall()
    assert len(verse_rows) == ayat
    assert sum(n for _,n in verse_rows) == words
    # Verify a single Ayah can have multiple distinct occurrence positions.
    assert any(n > 1 for _, n in verse_rows), "Repeated word within an Ayah was collapsed"
    print(f"LEMMA {lemma}: {words} word positions, {ayat} distinct Ayat, "
          f"{sum(n-1 for _,n in verse_rows)} additional same-Ayah occurrences")

count_lemma("{ll~ah", "PN", 2699, 1821)
count_lemma("raHomap", "N", 114, 112)

# Distinct query boundary: exact adjacent word sequence (do not call this
# morphological or orthographically normalized phrase matching).
phrase = index.execute(
    "SELECT first.text_uthmani,second.text_uthmani FROM word_positions first "
    "JOIN word_positions second ON first.verse_key=second.verse_key "
    "AND second.position=first.position+1 WHERE first.verse_key='1:1' "
    "AND first.position=1").fetchone()
assert phrase and all(phrase), "Missing authoritative 1:1 word tokens"
occurrences = index.execute(
    "SELECT first.verse_key,first.position FROM word_positions first "
    "JOIN word_positions second ON first.verse_key=second.verse_key "
    "AND second.position=first.position+1 "
    "WHERE first.text_uthmani=? AND second.text_uthmani=? "
    "ORDER BY first.word_id", phrase).fetchall()
assert ("1:1", 1) in occurrences
assert len(occurrences) == len(set(occurrences))
print(f"EXACT 2-word Uthmani phrase: {len(occurrences)} contiguous word-position occurrences "
      f"across {len({v for v,_ in occurrences})} distinct Ayat")

# Exact full-Ayah repetition is defined by the source script and exact
# canonical verse text, not by concatenated word forms or repeated scripts.
source_text = corpus.execute(
    "SELECT text_uthmani FROM verses WHERE verse_key='1:1'").fetchone()[0]
same_ayat = corpus.execute(
    "SELECT verse_key FROM verses WHERE text_uthmani=? ORDER BY chapter_number,verse_number",
    (source_text,)).fetchall()
assert ("1:1",) in same_ayat
print(f"EXACT full Uthmani Ayah 1:1: {len(same_ayat)} canonical Ayat with identical text")
print("C2c R2 Gate2 evidence-count query contract PASS (lemma, exact phrase, identical Ayah).")
