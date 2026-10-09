#!/usr/bin/env python3
"""THTRP Context Atlas v1 package preflight; stdlib-only partner of the native .NET importer.

Usage: python scripts/validate_proposal_package.py path/to/IMPORT.zip
This checks the actual package plus canonical Surah index present in the exported handoff.
The Windows application remains the final authority for importer compatibility.
"""
import hashlib
import json
import sys
import zipfile
from pathlib import Path

class Invalid(Exception):
    pass

def check(condition, message):
    if not condition:
        raise Invalid(message)

def required_str(obj, key, where):
    value = obj.get(key)
    check(isinstance(value, str) and bool(value.strip()), f"{where}: missing {key}")
    return value

def integer(obj, key, where):
    value = obj.get(key)
    check(type(value) is int, f"{where}: {key} must be an integer")
    return value

def items(obj, key, where):
    value = obj.get(key)
    check(isinstance(value, list), f"{where}: {key} must be a list")
    return value

def range_text(start, end):
    return str(start) if start == end else f"{start}-{end}"

def validate(package_path, index_path):
    path = Path(package_path)
    check(path.is_file(), f"missing import ZIP: {path}")
    index = json.loads(Path(index_path).read_text(encoding="utf-8"))
    check(len(index) == 114, "source Surah index is not 114 chapters")
    chapter_counts = {int(x["surah_number"]): int(x["verses_count"]) for x in index}
    check(set(chapter_counts) == set(range(1, 115)) and sum(chapter_counts.values()) == 6236,
          "source Surah index is not the canonical 6,236 Ayat")

    with zipfile.ZipFile(path) as z:
        members = z.namelist()
        check(len(members) == len(set(members)), "duplicate ZIP entry names")
        check("manifest.json" in members and "proposal-corpus.json" in members,
              "ZIP root must contain manifest.json and proposal-corpus.json")
        check(all(".." not in Path(name).parts and not name.startswith("/") for name in members),
              "unsafe ZIP entry path")
        check(z.testzip() is None, "ZIP entry CRC failure")
        manifest_data = z.read("manifest.json")
        payload_data = z.read("proposal-corpus.json")
    manifest = json.loads(manifest_data)
    corpus = json.loads(payload_data)
    check(manifest.get("package_schema") == "thtrp.context-map-proposal-package", "wrong package_schema")
    check(manifest.get("package_schema_version") == 1, "wrong package_schema_version")
    check(manifest.get("package_type") == "proposal_corpus", "wrong package_type")
    check(manifest.get("payload_file") == "proposal-corpus.json", "wrong payload_file")
    check(manifest.get("payload_sha256", "").lower() == hashlib.sha256(payload_data).hexdigest(),
          "manifest SHA-256 does not match actual payload bytes")
    required_str(manifest, "terminology_standard", "manifest")
    check(corpus.get("schema") == "thtrp.context-map-proposal-corpus", "wrong corpus schema")
    check(corpus.get("schema_version") == 1, "wrong corpus version")
    for key in ("corpus_id", "display_name", "published_date"):
        required_str(corpus, key, "corpus")
        check(manifest.get(key) == corpus[key], f"manifest/corpus mismatch: {key}")
    edition = integer(corpus, "edition", "corpus")
    check(edition >= 1 and manifest.get("edition") == edition, "bad/mismatched edition")
    check(corpus.get("status") in ("proposed", "revised-proposed"), "invalid status")
    contributors = items(corpus, "contributors", "corpus")
    check(bool(contributors), "no contributor metadata")
    for contributor in contributors:
        for key in ("model_name", "provider", "role"):
            required_str(contributor, key, "contributor")
    check(isinstance(corpus.get("terminology"), dict), "terminology must be object")
    check(isinstance(corpus.get("source_identity"), dict), "source_identity must be object")
    lineage = corpus.get("lineage")
    check(isinstance(lineage, dict), "lineage must be object")
    if lineage.get("kind") == "revision":
        required_str(lineage, "parent_corpus_id", "revision lineage")
        required_str(lineage, "parent_payload_sha256", "revision lineage")
    elif lineage.get("kind") in ("independent", "independent_initial_edition"):
        check(lineage.get("parent_corpus_id") is None and lineage.get("parent_payload_sha256") is None,
              "independent corpus must have null parent lineage")
    else:
        raise Invalid("unrecognized lineage.kind")

    surahs = items(corpus, "surahs", "corpus")
    check(len(surahs) == 114 and [x.get("surah_number") for x in surahs] == list(range(1, 115)),
          "Surahs must be ordered 1–114 exactly once")
    ids = set()
    actual_counts = dict(blocks=0, boundaries=0, macros=0, singles=0, ayat=0)
    blocks_by_surah = {}
    for surah in surahs:
        n = surah["surah_number"]
        where = f"Surah {n}"
        check(surah.get("verses_count") == chapter_counts[n], f"{where}: wrong verse count")
        required_str(surah, "surah_name", where)
        blocks = items(surah, "context_blocks", where)
        bounds = items(surah, "boundaries", where)
        macros = items(surah, "macro_groups", where)
        imp_ranges = items(surah, "importer_ranges", where)
        check(bool(blocks) and bool(macros), f"{where}: must have Contexts and Macro Groups")
        check(len(bounds) == len(blocks)-1, f"{where}: boundaries != blocks - 1")
        next_ayah, b_ranges, b_ids = 1, [], []
        for i, block in enumerate(blocks, 1):
            start, end = integer(block,"start",where), integer(block,"end",where)
            check(start == next_ayah and start <= end <= chapter_counts[n],
                  f"{where}: gap/overlap/invalid Context span at {start}–{end}")
            block_id = required_str(block, "context_block_id", where)
            check(block_id not in ids, f"{where}: duplicate Context ID {block_id}")
            ids.add(block_id)
            check(integer(block,"index",where) == i, f"{where}: Context index mismatch")
            check(block.get("range") == range_text(start, end), f"{where}: block.range mismatch")
            for field in ("juz_start", "juz_end"):
                check(1 <= integer(block,field,where) <= 30, f"{where}: bad {field}")
            check(block["juz_start"] <= block["juz_end"], f"{where}: reversed Juz span")
            required_str(block, "coherence_note", where)
            check(block.get("confidence") in ("high", "medium", "low"), f"{where}: invalid confidence")
            actual_counts["singles"] += int(start == end)
            b_ranges.append(range_text(start,end)); b_ids.append(block_id)
            next_ayah = end+1
        check(next_ayah == chapter_counts[n]+1, f"{where}: final Ayah missing")
        check(imp_ranges == b_ranges, f"{where}: importer_ranges mismatch")
        for i,b in enumerate(bounds):
            check(b.get("after_ayah") == blocks[i]["end"] and
                  b.get("next_ayah") == blocks[i+1]["start"] and
                  b.get("next_ayah") == b.get("after_ayah",0)+1,
                  f"{where}: boundary {i+1} not aligned")
            required_str(b,"boundary_id",where)
            required_str(b,"reason",where)
            check(b.get("confidence") in ("high", "medium", "low"), f"{where}: invalid boundary confidence")
            check(type(b.get("english_used")) is bool, f"{where}: english_used must be boolean")
        cursor = 0
        for macro in macros:
            mname = f"{where} Macro Group {cursor+1}"
            required_str(macro,"macro_group_id",mname)
            required_str(macro,"label",mname)
            mids = items(macro,"context_block_ids",mname)
            mranges = items(macro,"context_ranges",mname)
            mc = integer(macro,"context_block_count",mname)
            check(mc > 0 and mc == len(mids) == len(mranges), f"{mname}: count mismatch")
            check(cursor+mc <= len(blocks), f"{mname}: overrun")
            check(mids == b_ids[cursor:cursor+mc] and mranges == b_ranges[cursor:cursor+mc],
                  f"{mname}: not consecutive canonical Contexts")
            check(macro.get("start_ayah") == blocks[cursor]["start"] and
                  macro.get("end_ayah") == blocks[cursor+mc-1]["end"],
                  f"{mname}: endpoints mismatch")
            cursor += mc
        check(cursor == len(blocks), f"{where}: Macro Groups do not partition all Contexts")
        actual_counts["blocks"] += len(blocks)
        actual_counts["boundaries"] += len(bounds)
        actual_counts["macros"] += len(macros)
        actual_counts["ayat"] += chapter_counts[n]
        blocks_by_surah[n] = {b["context_block_id"]: b for b in blocks}

    worksets = items(corpus, "cross_surah_worksets", "corpus")
    workset_ids = set()
    for w in worksets:
        wid = required_str(w,"id","workset")
        check(wid not in workset_ids, f"duplicated workset id {wid}")
        workset_ids.add(wid)
        required_str(w,"label",wid)
        members = items(w,"members",wid)
        subsets = items(w,"subsets",wid)
        check(bool(members or subsets), f"{wid}: empty workset")
        collections = [(wid,members)]
        subset_labels = set()
        for subset in subsets:
            label = required_str(subset,"label",wid)
            check(label not in subset_labels,f"{wid}: duplicate subset label")
            subset_labels.add(label)
            entries=items(subset,"members",wid)
            check(bool(entries),f"{wid}: empty subset")
            collections.append((wid+" / "+label,entries))
        for scope, entries in collections:
            for entry in entries:
                n=integer(entry,"surah_number",scope)
                check(n in blocks_by_surah, f"{scope}: missing Surah {n}")
                refs=items(entry,"context_block_ids",scope)
                check(bool(refs) and len(refs)==len(set(refs)),f"{scope}: empty/repeated Context reference")
                bmap=blocks_by_surah[n]
                check(all(ref in bmap for ref in refs),f"{scope}: unresolved Context reference")
                found=[bmap[ref] for ref in refs]
                check(found[0]["start"] == entry.get("start_ayah") and
                      found[-1]["end"] == entry.get("end_ayah"),f"{scope}: member span mismatch")
                check(all(found[i]["index"]==found[i-1]["index"]+1 for i in range(1,len(found))),
                      f"{scope}: member refs are not consecutive")

    stat = corpus.get("statistics")
    check(isinstance(stat,dict),"missing statistics object")
    expected = dict(surahs_audited=114,ayat_covered=6236,total_context_blocks=actual_counts["blocks"],
                    total_internal_boundaries=actual_counts["boundaries"],macro_groups=actual_counts["macros"],
                    cross_surah_worksets=len(worksets))
    for key,value in expected.items():
        check(stat.get(key)==value,f"statistics.{key}: expected {value}, got {stat.get(key)}")
    # Additional statistics must be finite nonnegative integers when declared.
    for key in ("juz_crossing_blocks","single_ayah_blocks","juz_coincident_internal_boundaries"):
        check(type(stat.get(key)) is int and stat[key]>=0, f"statistics.{key}: invalid")
    check(stat["single_ayah_blocks"]==actual_counts["singles"],"single_ayah_blocks wrong")
    print(f"PASS: package SHA-256 {hashlib.sha256(path.read_bytes()).hexdigest()}")
    print(f"PASS: payload SHA-256 {hashlib.sha256(payload_data).hexdigest()}")
    print(f"PASS: {len(surahs)} Surahs, {actual_counts['ayat']} Ayat, {actual_counts['blocks']} Context Blocks, "
          f"{actual_counts['boundaries']} boundaries, {actual_counts['macros']} Macro Groups, {len(worksets)} worksets")
    print("PASS: structural package preflight (run native Windows import as final acceptance test)")

if __name__ == "__main__":
    try:
        check(len(sys.argv) in (2,3), "Usage: python scripts/validate_proposal_package.py IMPORT.zip [06-SURAH-INDEX.json]")
        root = Path(__file__).resolve().parents[1]
        index = Path(sys.argv[2]) if len(sys.argv)==3 else root/"06-SURAH-INDEX.json"
        validate(sys.argv[1], index)
    except (Invalid, KeyError, TypeError, ValueError, OSError, zipfile.BadZipFile, json.JSONDecodeError) as e:
        print("FAIL:",e,file=sys.stderr)
        raise SystemExit(1)
