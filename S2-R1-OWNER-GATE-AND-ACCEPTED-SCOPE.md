# Scratchpad S2 R1 — Owner-approved scope / build gate (2026-10-09)

**Gate:** OPEN / implementation authorized. **Release:** NOT built or owner accepted.  
**Accepted Windows truth:** Pathfinder v1.2.1 Stable (frozen).  
**Previous working TEST candidate:** Scratchpad S1 CI-Corrected R2, successful x64/ARM64 CI run [37996254406](https://github.com/paglaaacoc/-_-fehu-H-_-/actions/runs/37996254406), CI commit `0e783499f8a0b398df363d215eab2de9cb0ea080`.  
**S2 branch:** `dev/pathfinder-scratchpad-s2-r1-20261009`, separate from the S1 test build and the Public-Impact G0. Never merge to main without owner instruction.

## Owner-approved scope

- Paste clipboard screenshots via Ctrl+V and a touch-accessible **Paste Image** control; provide a fallback where clipboard APIs differ. Add a distinct **Arrange** mode for move, resize, delete, and layer control. Preserve Type/Paste and Draw/Pen; text and handwriting remain independent and saved.
- Replace S1's monolithic 12 MB extension-state document with a durable **SQLite transactional metadata store + separate content-addressed image files**, with per-asset validation, safe writes, durable errors, no arbitrary fixed overall 1 GB/5 GB total cap, and disk-space safeguards. S1 live notes/ink must be migrated without silent loss.
- Replace the global 100 MB single-JSON canonical backup workflow with a **complete versioned streaming ZIP64 .nqpfbackup** containing the current Windows study providers and all Scratchpad metadata/images. Verify checksums, safe extraction, consistent provider snapshots, disk-space requirements and crash-safe rollback covering both normal providers and Scratchpad. Old backup-format compatibility is OPTIONAL by owner's explicit authorization; safeguarding *live study data* is NOT optional.
- Maintain all 132 medical payload files byte-for-byte, registry identity, the 23 cockpits, study-state behavior, Search, Journal, sidebar, split view, and Windows backup authority. No PWA development.

## S1 owner-tested observations and S2 design decisions

1. **Theme mismatch — FIX, deliberately restrained:** Theme Scratchpad's outside controls/background using the selected native Pathfinder palette; leave ruled writing paper light with dark text for readability. Do not inject general medical cockpit CSS. Handle dynamic changes, startup and high contrast.
2. **Loss of rich HTML formatting when pasting — DEFER:** Plain-text editor currently preserves complete copied clinical wording and normal keyboard editing. Full rich editing requires contenteditable, sanitization, caret/history/HTML persistence and image interactions; it poses complexity/clutter/regression risk disproportional to S2. Reevaluate later, not in S2.
3. **Text and ruled-paper baselines drift, especially after scrolling with Extra-wide spacing — FIX:** Synchronize guide position with the textarea scroll offset, align text baselines across 24/32/44/60px spacings and keep scroll position when switching guides/modes. Test one/two/four spaces, Split View, resize, 125% scale, long notes.
4. **Microsoft Word screenshot is UX reference only:** S2 floating images movable and resizable without committing to full Word paragraph-wrapping / rich document layout.

## Internal checkpoints (no premature candidate)

S2-A: Theme and guide repair, with browser regression checks.  
S2-B: Scalable SQLite storage, image file ownership, safe S1 migration and failure recovery.  
S2-C: Image paste + Arrange UX + touch/stylus and editor isolation.  
S2-D: Whole-app streaming ZIP64 backup and crash-safe restore (needs destructive-path audit).  
S2-E: CI and release-readiness checks, Windows x64/ARM64 TEST installers, hashes, owner test list.

**Stop rule:** If global backup restore cannot be proven atomic and recoverable, stop at the internal checkpoint; do NOT issue owner-test installers. Compilation alone is insufficient. Owner acceptance needed before stable promotion.

**Current progress:** S2-A working-copy edits (native palette messages + guide scroll synchronization) have been authored and browser-smoke tested locally, but NOT compiled on Windows or promoted to S2 R1. Medical HTML untouched. Source package checkpoint remains local and separate from this CI setup branch. User screenshots are not published.

**Public repository rule:** temporary CI workspace only; no publication/release/deployment and no changes to default branch.
