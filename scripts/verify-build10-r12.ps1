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
$working = Join-Path $src 'MainWindow.WorkingSlices.cs'
$workingRepo = Join-Path $src 'Infrastructure/WorkingSliceRepository.cs'
$historyRepo = Join-Path $src 'Infrastructure/HistoryRepository.cs'
$historyModels = Join-Path $src 'Models/HistoryModels.cs'
$buildIdentity = Join-Path $src 'Infrastructure/BuildIdentity.cs'
$backupService = Join-Path $src 'Infrastructure/OwnerDataBackupService.cs'
$app = Join-Path $src 'App.xaml.cs'
$safety = Join-Path $src 'MainWindow.DataSafety.cs'
$workingProbe = Join-Path $repoRoot 'tools/WorkingSliceWorkflowProbe/Program.cs'
$historyProbe = Join-Path $repoRoot 'tools/HistoryWorkflowProbe/Program.cs'

# R11 safety baseline must remain present and untouched in behavior.
Assert-Contains $app 'PortableInstanceGuard.Acquire()'
Assert-Contains $app 'RecoverInterruptedOperation()'
Assert-Contains $backupService 'PreparedStage = "Prepared"'
Assert-Contains $backupService 'QuarantinedStage = "Quarantined"'
Assert-Contains $backupService 'CommittedStage = "Committed"'
Assert-Contains $backupService 'InstallVerifiedBackupWithoutPreBackup'
Assert-Contains $backupService 'Recovery-Unverified'
Assert-Contains $safety '_ownerStateOperationActive'
Assert-Contains $safety 'ResolveDirtyDraftsBeforeDestructiveOperationAsync'
Assert-Contains $main 'AppWindow.Closing += AppWindow_Closing'

# R10/R11 Working Slice rendering and context menu behavior remain frozen.
Assert-Contains $working '_workingSliceEvidenceVerses'
Assert-Contains $working '_workingSliceRenderedVerseCount'
Assert-Contains $working 'AppendWorkingSliceEvidenceBatch'
Assert-Contains $working 'WorkingSliceEvidenceScrollViewer_ViewChanged'
Assert-Contains $working 'CaptureWorkingSliceZoomTargets('

# R12 identity and user actions.
Assert-Contains $buildIdentity 'Build 10 R12 — Working Slice Removal Closure'
Assert-Contains $xaml 'Text="Build 10 R12 · Working Slice removal closure"'
Assert-Contains $xaml 'x:Name="WorkingSliceDiscardButton"'
Assert-Contains $xaml 'Content="Discard unsaved changes"'
Assert-Contains $xaml 'x:Name="WorkingSliceRemoveButton"'
Assert-Contains $xaml 'Content="Remove Working Slice…"'
Assert-Contains $working 'DiscardWorkingSliceChanges_Click'
Assert-Contains $working 'RemoveWorkingSlice_Click'
Assert-Contains $working 'Remove & discard unsaved changes'
Assert-Contains $working 'This does not hard-delete the research record.'

# R12 persistence contract: soft removal only.
Assert-Contains $workingRepo 'internal WorkingSlice Remove('
Assert-Contains $workingRepo 'SET is_active=0'
Assert-NotContains $workingRepo 'DELETE FROM working_slices'

# Removed records remain represented in History without reopening as active.
Assert-Contains $historyRepo "Working Slice · removed · "
Assert-Contains $historyRepo 'WHEN is_active=1 THEN id'
Assert-Contains $historyModels '"Open parent context"'

# Behavioral probes must cover persistence + History semantics.
Assert-Contains $workingProbe 'Soft removal must not delete the Working Slice row.'
Assert-Contains $workingProbe 'Soft removal must preserve Working Slice revision history.'
Assert-Contains $historyProbe 'Removed Working Slice must remain searchable in History without reopening as an active slice.'

# Tight R12 diff boundary from the exact accepted R11 safety baseline.
$base = '0575fad71389bea827e58e38dfc219d6340b2746'
$allowed = @(
    '.github/workflows/build10-r12-windows.yml',
    'docs/BUILD-10-R12.md',
    'scripts/verify-build10-r12.ps1',
    'src/QuranReconciliation/Infrastructure/BuildIdentity.cs',
    'src/QuranReconciliation/Infrastructure/HistoryRepository.cs',
    'src/QuranReconciliation/Infrastructure/WorkingSliceRepository.cs',
    'src/QuranReconciliation/MainWindow.WorkingSlices.cs',
    'src/QuranReconciliation/MainWindow.xaml',
    'src/QuranReconciliation/Models/HistoryModels.cs',
    'tools/HistoryWorkflowProbe/Program.cs',
    'tools/WorkingSliceWorkflowProbe/Program.cs'
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
    throw "R12 changed files outside the approved tiny closure boundary: $($unexpected -join ', ')"
}

Write-Host "R12 changed-file boundary:"
$changed | ForEach-Object { Write-Host "  $_" }

Write-Host 'Build 10 R12 invariant checks: PASS'
