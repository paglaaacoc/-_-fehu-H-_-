# NivareQ Step 2 Pathfinder — Scratchpad S1 CI-only test installers

**Branch:** `ci/pathfinder-scratchpad-s1-20261009`  
**Purpose:** Temporarily build Windows x64 + ARM64 installer TEST candidates from approved Scratchpad S1 source.  
**Source of truth:** v1.2.1 Stable remains owner-accepted. S1 v1.2.2 remains unaccepted. This repository is NOT the authoritative source repository.

## One upload to start the Windows build

1. Download the attached **`SCRATCHPAD-S1-BUILD-INPUT.zip`** from the handoff chat. It is a **5,892,761-byte** build-only subset of the frozen Scratchpad S1 candidate.
2. Switch GitHub to **this branch**, not `main`. Open [ci-input on this branch](https://github.com/paglaaacoc/-_-fehu-H-_-/tree/ci/pathfinder-scratchpad-s1-20261009/ci-input). If the folder has not been created yet, use **Add file → Upload files** from this branch and set destination path `ci-input/SCRATCHPAD-S1-BUILD-INPUT.zip`. The branch name must remain `ci/pathfinder-scratchpad-s1-20261009`.
3. Upload the ZIP **without unzipping or changing it**. Commit the upload to this exact branch.
4. The workflow [Scratchpad S1 Windows installers](../.github/workflows/pathfinder-scratchpad-s1.yml) will start on the ZIP path's push. Open **Actions**, select that run, and inspect the job.
5. On green success, download **`NivareQ-Pathfinder-v1.2.2-Scratchpad-S1-TEST-x64-ARM64`** under workflow **Artifacts**. It contains both EXE installers and their SHA-256 manifest. It is a test candidate, NOT an accepted release.

## Integrity and isolation

Expected SHA-256 of input archive:
`282da643d184d4bc3e23e0e65098a367d5461f567175401f6080e3faae6ebc6e`.

Workflow hard-fails if the ZIP does not match that hash; checks medical payload/registry via inherited scripts, builds `win-x64` and `win-arm64` on a Windows runner, compiles Inno Setup EXEs, and uploads only test artifacts. The Windows job uses no repository secrets. It does not deploy, open PRs, release, merge, or modify `main`.

**Temporary public exposure:** The input ZIP contains the application source, including medical teaching HTML. Git commits are durable. Deleting the branch later does not guarantee all content becomes unrecoverable. Only upload if this public exposure is acceptable.

**Installer caution:** Candidate installer uses the same app identity as Stable. It may upgrade an existing installed Stable copy. First back up data; test preferably in a VM or separate Windows account. Real-device, backup/restore, and ARM64 runtime tests remain pending even if GitHub builds both installers successfully.

## Why one manual upload?

The connected GitHub app can write branch files and workflows but cannot take a binary file from this conversation's sandbox and upload it into GitHub. The supplied ZIP is therefore the only manual transfer step. No GitHub Actions build can start until the exact archive has arrived on this branch.

## Scope lock

- No medical changes, no new clinical evidence, no cockpit HTML edits
- No sidebar jumping fix
- No Public-Impact G0 or Four-Scope gate opened
- S1 tool registration, lower-left Guide-to-Scratchpad change, typing+ink, ruled spaces, multiple pads/pages, and persistence only
- No release cut or Stable promotion without explicit owner acceptance
