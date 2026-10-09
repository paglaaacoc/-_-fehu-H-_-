$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repoRoot 'src/QuranReconciliation'

function Assert-Contains([string]$Path, [string]$Needle) {
    $text = Get-Content -Raw -LiteralPath $Path
    if (-not $text.Contains($Needle)) {
        throw "Expected '$Needle' in $Path"
    }
}

function Assert-NotContains([string]$Path, [string]$Needle) {
    $text = Get-Content -Raw -LiteralPath $Path
    if ($text.Contains($Needle)) {
        throw "Did not expect '$Needle' in $Path"
    }
}

$main = Join-Path $src 'MainWindow.xaml.cs'
$xaml = Join-Path $src 'MainWindow.xaml'
$app = Join-Path $src 'App.xaml.cs'
$perf = Join-Path $src 'MainWindow.Performance.cs'
$working = Join-Path $src 'MainWindow.WorkingSlices.cs'
$textMenus = Join-Path $src 'MainWindow.ContextMenus.cs'
$settingsModel = Join-Path $src 'Infrastructure/AppSettings.cs'
$settingsStore = Join-Path $src 'Infrastructure/SettingsStore.cs'
$researchDb = Join-Path $src 'Infrastructure/ResearchDatabase.cs'
$backupService = Join-Path $src 'Infrastructure/OwnerDataBackupService.cs'
$dataSafety = Join-Path $src 'MainWindow.DataSafety.cs'
$instanceGuard = Join-Path $src 'Infrastructure/PortableInstanceGuard.cs'
$buildIdentity = Join-Path $src 'Infrastructure/BuildIdentity.cs'
$historyRepo = Join-Path $src 'Infrastructure/HistoryRepository.cs'
$contextRepo = Join-Path $src 'Infrastructure/ContextRepository.cs'
$noteRepo = Join-Path $src 'Infrastructure/NoteRepository.cs'
$bookmarkRepo = Join-Path $src 'Infrastructure/BookmarkRepository.cs'
$workingRepo = Join-Path $src 'Infrastructure/WorkingSliceRepository.cs'
$safetyProbe = Join-Path $repoRoot 'tools/SafetyClosureProbe/Program.cs'

# Frozen R10 behavior that R11 must not reopen.
Assert-Contains $xaml 'x:Name="WorkspaceGrid"'
Assert-Contains $xaml 'x:Name="WorkingSliceWorkspaceGrid"'
Assert-Contains $xaml 'x:Name="HistoryWorkspaceGrid"'
Assert-Contains $xaml 'ZoomMode="Disabled"'
Assert-Contains $perf 'TafsirInitialVerseBatch = 6'
Assert-Contains $perf 'TafsirNextVerseBatch = 6'
Assert-Contains $working '_workingSliceEvidenceVerses'
Assert-Contains $working '_workingSliceRenderedVerseCount'
Assert-Contains $working 'AppendWorkingSliceEvidenceBatch'
Assert-Contains $working 'WorkingSliceEvidenceScrollViewer_ViewChanged'
Assert-Contains $working 'CaptureWorkingSliceZoomTargets('
Assert-Contains $textMenus 'BuildTextBoxEditMenu'
Assert-Contains $textMenus 'Text = "Cut"'
Assert-Contains $textMenus 'Text = "Copy"'
Assert-Contains $textMenus 'Text = "Paste"'
Assert-Contains $textMenus 'Text = "Select all"'
Assert-NotContains $settingsModel 'WritingAssistanceEnabled'
Assert-NotContains $xaml 'WritingAssistanceMenuItem'

# R11 identity + startup safety.
Assert-Contains $buildIdentity 'Build 10 R11 — Post-ANTARCTICA Safety Closure'
Assert-Contains $main 'BuildIdentity.Label'
Assert-Contains $instanceGuard 'FileShare.None'
Assert-Contains $instanceGuard '.quran-reconciliation.owner-state.lock'
Assert-Contains $app 'PortableInstanceGuard.Acquire()'
Assert-Contains $app 'RecoverInterruptedOperation()'
Assert-Contains $app 'new MainWindow()'
Assert-Contains $app 'No replacement research database was initialized after this failure.'

# Newer owner state must fail closed.
Assert-Contains $researchDb 'RejectNewerSchema(fullPath)'
Assert-Contains $researchDb 'Refusing to open newer owner state with an older build.'
Assert-Contains $settingsStore 'NotSupportedException'
Assert-Contains $settingsStore 'Refusing to overwrite newer owner settings with an older build.'

# R11 destructive-operation recovery contract.
Assert-Contains $backupService 'PreparedStage = "Prepared"'
Assert-Contains $backupService 'QuarantinedStage = "Quarantined"'
Assert-Contains $backupService 'CommittedStage = "Committed"'
Assert-Contains $backupService 'owner-state-operation.json'
Assert-Contains $backupService 'RecoverInterruptedOperation'
Assert-Contains $backupService 'RecoverPreOperationState'
Assert-Contains $backupService 'InstallVerifiedBackupWithoutPreBackup'
Assert-Contains $backupService 'Recovery-Unverified'
Assert-Contains $backupService 'UNVERIFIED-RECOVERY-MATERIAL.txt'
Assert-Contains $backupService 'RequiredResearchColumns'
Assert-Contains $backupService 'SourceRevision'
Assert-Contains $backupService 'owner-state-recovery.log'
Assert-Contains $backupService 'DeleteLiveOwnerStateFilesStrict'
Assert-NotContains $backupService 'RestoreFilesFromQuarantine'

# UI exclusivity + dirty-state handling.
Assert-Contains $dataSafety '_ownerStateOperationActive'
Assert-Contains $dataSafety 'RootGrid.IsHitTestVisible'
Assert-Contains $dataSafety 'ResolveDirtyDraftsBeforeDestructiveOperationAsync'
Assert-Contains $dataSafety 'Discard drafts & continue'
Assert-Contains $dataSafety 'Recovery required'
Assert-Contains $main '_ownerStateOperationActive'
Assert-Contains $working '_workingSliceDirty'
Assert-Contains $working 'preferredId is not null'
Assert-Contains (Join-Path $src 'MainWindow.Notes.cs') '_ayahNoteDirty'
Assert-Contains (Join-Path $src 'MainWindow.Notes.cs') '_contextNoteDirty'

# History browse ceiling and safe path construction.
Assert-Contains $historyRepo '2000'
foreach ($repo in @($contextRepo, $noteRepo, $bookmarkRepo, $workingRepo, $historyRepo)) {
    Assert-Contains $repo 'SqliteConnectionStringBuilder'
    Assert-NotContains $repo 'Data Source={_databasePath}'
}

# Fault-oriented safety probe must cover the audit findings.
Assert-Contains $safetyProbe 'Second process/guard for the same portable folder was not rejected.'
Assert-Contains $safetyProbe 'R11 did not fail closed on a newer research schema.'
Assert-Contains $safetyProbe 'Startup recovery did not restore the exact pre-operation research state.'
Assert-Contains $safetyProbe 'Ambiguous recovery created a replacement research database.'
Assert-Contains $safetyProbe 'Backup verification accepted a database missing a runtime-required column.'

# Tight changed-file boundary from exact Antarctica.
$base = '5a423e30b76a7d40d672fc82a148bffa8cedcf60'
$allowed = @(
    '.github/workflows/build10-r11-windows.yml',
    'docs/BUILD-10-R11.md',
    'scripts/verify-build10-r11.ps1',
    'src/QuranReconciliation/App.xaml.cs',
    'src/QuranReconciliation/Infrastructure/BuildIdentity.cs',
    'src/QuranReconciliation/Infrastructure/PortableInstanceGuard.cs',
    'src/QuranReconciliation/Infrastructure/ResearchDatabase.cs',
    'src/QuranReconciliation/Infrastructure/SettingsStore.cs',
    'src/QuranReconciliation/Infrastructure/OwnerDataBackupService.cs',
    'src/QuranReconciliation/Infrastructure/ContextRepository.cs',
    'src/QuranReconciliation/Infrastructure/NoteRepository.cs',
    'src/QuranReconciliation/Infrastructure/BookmarkRepository.cs',
    'src/QuranReconciliation/Infrastructure/WorkingSliceRepository.cs',
    'src/QuranReconciliation/Infrastructure/HistoryRepository.cs',
    'src/QuranReconciliation/MainWindow.DataSafety.cs',
    'src/QuranReconciliation/MainWindow.Notes.cs',
    'src/QuranReconciliation/MainWindow.WorkingSlices.cs',
    'src/QuranReconciliation/MainWindow.xaml',
    'src/QuranReconciliation/MainWindow.xaml.cs',
    'tools/BackupWorkflowProbe/BackupWorkflowProbe.csproj',
    'tools/SafetyClosureProbe/Program.cs',
    'tools/SafetyClosureProbe/SafetyClosureProbe.csproj'
)

$changed = @(
    git diff --name-only "$base..HEAD" |
        Where-Object { $_ -and $_.Trim() -ne '' } |
        Sort-Object -Unique
)

$unexpected = @(
    $changed |
        Where-Object { $_ -notin $allowed }
)

if ($unexpected.Count -gt 0) {
    throw "R11 changed files outside the approved closure boundary: $($unexpected -join ', ')"
}

Write-Host "R11 changed-file boundary:"
$changed | ForEach-Object { Write-Host "  $_" }

Write-Host 'Build 10 R11 invariant checks: PASS'
