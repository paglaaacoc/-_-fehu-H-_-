# Build 1.5 — Application-Compatible Context Atlas Import Contract

This contract applies to BOTH **New** (no parent) and **Improve Existing** (one selected parent) handoffs.

## Final import package (exact ZIP root)
```
FINAL-IMPORT.zip
  manifest.json
  proposal-corpus.json
```
Keep changelogs, working Surah files, supporting analyses and other research records in a separate research ZIP.

Manifest: `package_schema = thtrp.context-map-proposal-package`, `package_schema_version = 1`, `package_type = proposal_corpus`, `payload_file = proposal-corpus.json`. The `corpus_id`, `display_name`, `edition`, `published_date` must match the payload exactly. `payload_sha256` must hash the exact final UTF-8 payload bytes. `terminology_standard` must be non-empty.

Corpus: `schema = thtrp.context-map-proposal-corpus`, `schema_version = 1`, unique `corpus_id`, valid edition, source identity, terminology, contributor/model/reasoning/date and lineage metadata. New must use `independent_initial_edition` with null parent fields; Improve must use `revision`, a new ID and exact parent corpus ID/SHA, with meaningful changelog in separate research ZIP.

**Exactly 114 ordered Surahs and 6,236 Ayat**, each with a complete, gap-free, overlap-free Context partition. Fine Context MUST use `context_block_id` (NOT `id`), 1-based `index`, `start`, `end`, `range` (e.g. `1` or `1-5`), `juz_start`, `juz_end`, `coherence_note`, `confidence`. For each adjacent pair create `boundary_id`, `after_ayah`, `next_ayah`, `reason`, `confidence`, Boolean `english_used`. Every Surah has `importer_ranges` matching fine blocks in their exact order.

Macro Groups must form a non-overlapping consecutive partition of the fine blocks using `macro_group_id`, endpoints, `label`, `context_block_count`, exact consecutive `context_block_ids` and `context_ranges`. Workset and subset members must refer to real consecutive Context IDs in the designated Surah and exactly matching endpoints. Complete statistics must match computed data.

## Two-stage validation
```
python scripts/validate_context_proposals.py OUTPUT
python scripts/validate_proposal_package.py FINAL-IMPORT.zip
```
The first checks local Surah drafts; the second performs complete import ZIP preflight against native importer requirements using only the Python standard library. JSON Schema by itself cannot check cross-reference integrity, canonical adjacency or hashes. A Python PASS **does not certify** actual native Windows import.

No mutation of workstation or owner research state is authorized by the handoff.
