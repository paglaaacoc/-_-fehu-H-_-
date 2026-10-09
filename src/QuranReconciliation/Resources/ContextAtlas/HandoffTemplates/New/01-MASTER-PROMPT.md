# THTRP — Frontier Model INDEPENDENT Context Proposal Corpus

You are a sole, independent Qur'anic structural analyst. Generate a genuinely new, Arabic-first Context Proposal covering **114 Surahs and 6,236 Ayat**. Proceed with actual research; do not stop after a plan.

ABSOLUTE INDEPENDENCE: Do not access other proposal corpora, past THTRP chats, GitHub research, comparison statistics or previously suggested boundaries. Do not try to agree or disagree with any earlier model. This handoff intentionally contains no parent.

CONTROLLING EVIDENCE: `evidence-arabic/` is the canonical Uthmani Arabic. Juz markers are never automatic boundaries. Consult `scripts/show_english_window.py` only for genuinely ambiguous nearby Arabic passages, and log the exact question, Ayah and reason. Do not pre-read English files. No Bengali, tafsir, web or outside commentary.

RESEARCH: Analyze each Surah's actual Arabic and discourse, then partition it into meaningful consecutive fine Context Blocks (no arbitrary length target), each with specific coherence note and honest confidence. Give every internal boundary a concise Arabic-grounded reason and English-use flag. Derive contiguous Macro Groups and optional strongly justified cross-Surah Related Worksets. Apply THTRP Islamic Terminology v1. All findings remain proposals for human review.

PERSISTENCE: Save after EACH Surah in `OUTPUT/proposals/` and `OUTPUT/importer/`; maintain `OUTPUT/WORK-STATE.json`, `OUTPUT/WORKLOG.md`, audit ledger and uncertainties. Never mark audited without actual Arabic research. On interruption resume from persisted files.

PACKAGING: Read `02-IMPORT-PACKAGE-CONTRACT.md`, `context-map-proposal-corpus-v1.schema.json` and `context-map-proposal-package-v1.schema.json`. Preserve exact app-native field names, especially `context_block_id` (not generic `id`), `index`, `range`, `juz_start`, `juz_end`, `macro_group_id`, `context_block_ids`, `context_ranges` and `importer_ranges`. Use new unique corpus identity, truthful model/provider/reasoning/date; `lineage.kind` = `independent_initial_edition` and both parents null.

ZIP ROOT: exactly `manifest.json` and `proposal-corpus.json`. Manifest declares `package_schema: thtrp.context-map-proposal-package`, version 1, `package_type: proposal_corpus`, and the actual byte SHA-256 of final `proposal-corpus.json`. Never mix the research continuity files into the import ZIP.

VALIDATION: Run `python scripts/validate_context_proposals.py OUTPUT` and `python scripts/validate_proposal_package.py FINAL-IMPORT.zip`. A JSON-schema-only check is insufficient; the Windows importer remains final authority. Fix all reported discrepancies before delivery; do not claim native Windows import without testing it.

DELIVER: independent import ZIP, separate full research ZIP, actual final counts, hashes, evidence-use record, audit and unresolved limitations. Do not modify user research or workstation code.
