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

$buildIdentity = Join-Path $src 'Infrastructure/BuildIdentity.cs'
$researchDb = Join-Path $src 'Infrastructure/ResearchDatabase.cs'
$activityWriter = Join-Path $src 'Infrastructure/ResearchActivityWriter.cs'
$contextRepo = Join-Path $src 'Infrastructure/ContextRepository.cs'
$historyRepo = Join-Path $src 'Infrastructure/HistoryRepository.cs'
$workingRepo = Join-Path $src 'Infrastructure/WorkingSliceRepository.cs'
$backup = Join-Path $src 'Infrastructure/OwnerDataBackupService.cs'
$xaml = Join-Path $src 'MainWindow.xaml'
$historyUi = Join-Path $src 'MainWindow.History.cs'
$contextUi = Join-Path $src 'MainWindow.Context.cs'
$workingUi = Join-Path $src 'MainWindow.WorkingSlices.cs'
$workspaceUi = Join-Path $src 'MainWindow.Workspace.cs'
$project = Join-Path $src 'QuranReconciliation.csproj'
$juzMap = Join-Path $src 'Resources/juz-map.json'

# Product identity.
Assert-Contains $buildIdentity '"v1.3"'
Assert-Contains $buildIdentity '"The Holy Quran TRP v1.3"'
Assert-Contains $xaml 'Text="v1.3"'
Assert-Contains $project '<Version>1.3.0</Version>'
Assert-Contains $project '<AssemblyVersion>1.3.0.0</AssemblyVersion>'

# Schema-7 first-class provenance.
Assert-Contains $researchDb 'internal const int SchemaVersion = 7;'
Assert-Contains $researchDb 'CREATE TABLE IF NOT EXISTS research_activity_events'
Assert-Contains $researchDb 'CREATE TABLE IF NOT EXISTS context_operations'
Assert-Contains $researchDb 'CREATE TABLE IF NOT EXISTS context_operation_blocks'
Assert-Contains $researchDb '"changed_fields_json"'
Assert-Contains $researchDb '"change_summary"'
Assert-Contains $researchDb 'BackfillLegacyActivity'
Assert-Contains $activityWriter 'INSERT INTO research_activity_events'

# Context logical operations: one semantic action around all affected block rows.
Assert-Contains $contextRepo '"StatusChange"'
Assert-Contains $contextRepo '"Split"'
Assert-Contains $contextRepo '"Merge"'
Assert-Contains $contextRepo '"ProposalImport"'
Assert-Contains $contextRepo '"ProposalDiscard"'
Assert-Contains $contextRepo '"ContextStatusChanged"'
Assert-Contains $contextRepo '"ContextProposalImported"'
Assert-Contains $contextRepo '"ContextProposalDiscarded"'
Assert-Contains $contextRepo 'AddOperationBlock'
Assert-Contains $contextRepo 'AddOperationActivity'

# History split into Organized object view + immutable Timeline.
Assert-Contains $historyRepo 'SearchOrganized'
Assert-Contains $historyRepo 'SearchTimeline'
Assert-Contains $historyRepo 'FROM research_activity_events e'
Assert-Contains $historyRepo 'LoadRevisions'
Assert-Contains $historyRepo 'Math.Clamp(limit, 1, 10000)'
Assert-Contains $historyUi 'TimeSpan.FromMilliseconds(250)'
Assert-Contains $historyUi '"Organized"'
Assert-Contains $historyUi '"Timeline"'
Assert-Contains $xaml 'x:Name="HistoryModeSelector"'
Assert-Contains $xaml 'Height="{Binding GroupHeaderHeight}"'
Assert-Contains $xaml 'Header="{Binding DetailsHeader}"'

# Working Slice project navigator + explicit dirty/revision meaning.
Assert-Contains $workingRepo 'changed_fields_json'
Assert-Contains $workingRepo 'change_summary'
Assert-Contains $workingRepo 'NormalizeSort'
Assert-Contains $workingRepo 'SET is_active=0'
Assert-NotContains $workingRepo 'DELETE FROM working_slices'
Assert-Contains $xaml 'x:Name="WorkingSliceSearchTextBox"'
Assert-Contains $xaml 'x:Name="WorkingSliceSortSelector"'
Assert-Contains $xaml 'x:Name="WorkingSliceDirtyText"'
Assert-Contains $workingUi 'AddWorkingSliceNavigatorHeader'
Assert-Contains $workingUi 'Revision {revision.Id} · {revision.ChangeSummary}'

# Context sidebar organization and state-aware affordances.
Assert-Contains $xaml 'Header="Boundary tools"'
Assert-Contains $xaml 'Header="Activity &amp; notes"'
Assert-Contains $xaml 'Header="Evidence sources"'
Assert-Contains $xaml 'x:Name="ContextExtendStartButton"'
Assert-Contains $xaml 'x:Name="ContextMergeNextButton"'
Assert-Contains $xaml 'x:Name="ContextSplitButton"'
Assert-Contains $contextUi 'RefreshContextActionState'
Assert-Contains $contextUi 'Accepted / Locked: boundary editing is disabled'

# Workspace state + deliberately small shortcut surface.
Assert-Contains $xaml 'x:Name="ResearchNavButton"'
Assert-Contains $xaml 'x:Name="WorkingSlicesNavButton"'
Assert-Contains $xaml 'x:Name="HistoryNavButton"'
Assert-Contains $workspaceUi 'VirtualKey.Number1'
Assert-Contains $workspaceUi 'VirtualKey.Number2'
Assert-Contains $workspaceUi 'VirtualKey.Number3'
Assert-Contains $workspaceUi 'VirtualKey.F'
Assert-Contains $workspaceUi 'VirtualKey.S'
Assert-Contains $workspaceUi 'UpdateWorkspaceNavigationState'

# R11 safety remains fail-closed, with schema-7-aware backup verification.
Assert-Contains $researchDb 'RejectNewerSchema(fullPath)'
Assert-Contains $backup 'PreparedStage = "Prepared"'
Assert-Contains $backup 'QuarantinedStage = "Quarantined"'
Assert-Contains $backup 'CommittedStage = "Committed"'
Assert-Contains $backup 'Schema7ResearchColumns'
Assert-Contains $backup 'version >= 7'

$portabilityProject = Join-Path $repoRoot 'tools/PortabilityProbe/PortabilityProbe.csproj'
Assert-Contains $portabilityProject 'ResearchActivityWriter.cs'

# Juz remains immutable metadata and never forces Context boundaries.
$juzHash = (Get-FileHash -LiteralPath $juzMap -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedJuzHash = '3016bb4f94af9a42c8c1c24728bf6044e5b4ac13130e47d451c0c10ba35dd434'
if ($juzHash -ne $expectedJuzHash) {
    throw "Pinned Juz map changed. Expected $expectedJuzHash, got $juzHash."
}

$base = '9a56469e36d3789da2cfb56736dce36ea2bf54d3'

$protected = @(
    'src/QuranReconciliation/Resources/juz-map.json',
    'src/QuranReconciliation/Infrastructure/CorpusRepository.cs',
    'src/QuranReconciliation/Infrastructure/WordByWordRepository.cs',
    'src/QuranReconciliation/MainWindow.Juz.cs',
    'src/QuranReconciliation/MainWindow.Performance.cs',
    'tools/CorpusBuilder',
    'tools/WordByWordBuilder',
    'tools/JuzMetadataProbe'
)

foreach ($path in $protected) {
    $diff = @(git diff --name-only "$base..HEAD" -- $path)
    if ($diff.Count -gt 0) {
        throw "Protected v1.2 Juz/corpus/rendering path changed in v1.3: $($diff -join ', ')"
    }
}

# Exact v1.3 changed-file boundary.
$allowed = @(
    '.github/workflows/v1.3-windows.yml',
    'scripts/verify-v1.3.ps1',
    'src/QuranReconciliation/Infrastructure/BookmarkRepository.cs',
    'src/QuranReconciliation/Infrastructure/BuildIdentity.cs',
    'src/QuranReconciliation/Infrastructure/ContextRepository.cs',
    'src/QuranReconciliation/Infrastructure/HistoryRepository.cs',
    'src/QuranReconciliation/Infrastructure/NoteRepository.cs',
    'src/QuranReconciliation/Infrastructure/OwnerDataBackupService.cs',
    'src/QuranReconciliation/Infrastructure/ResearchActivityWriter.cs',
    'src/QuranReconciliation/Infrastructure/ResearchDatabase.cs',
    'src/QuranReconciliation/Infrastructure/WorkingSliceRepository.cs',
    'src/QuranReconciliation/MainWindow.Context.cs',
    'src/QuranReconciliation/MainWindow.History.cs',
    'src/QuranReconciliation/MainWindow.WorkingSlices.cs',
    'src/QuranReconciliation/MainWindow.Workspace.cs',
    'src/QuranReconciliation/MainWindow.xaml',
    'src/QuranReconciliation/MainWindow.xaml.cs',
    'src/QuranReconciliation/Models/HistoryModels.cs',
    'src/QuranReconciliation/Models/WorkingSliceModels.cs',
    'src/QuranReconciliation/QuranReconciliation.csproj',
    'tools/ContextWorkflowProbe/ContextWorkflowProbe.csproj',
    'tools/ContextWorkflowProbe/Program.cs',
    'tools/HistoryWorkflowProbe/HistoryWorkflowProbe.csproj',
    'tools/HistoryWorkflowProbe/Program.cs',
    'tools/NoteWorkflowProbe/NoteWorkflowProbe.csproj',
    'tools/NoteWorkflowProbe/Program.cs',
    'tools/PortabilityProbe/PortabilityProbe.csproj',
    'tools/SafetyClosureProbe/Program.cs',
    'tools/V13ResearchOrganizationProbe/Program.cs',
    'tools/V13ResearchOrganizationProbe/V13ResearchOrganizationProbe.csproj',
    'tools/WorkingSliceWorkflowProbe/Program.cs',
    'tools/WorkingSliceWorkflowProbe/WorkingSliceWorkflowProbe.csproj'
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
    throw "v1.3 changed files outside the approved research-organization boundary: $($unexpected -join ', ')"
}

Write-Host 'v1.3 changed-file boundary:'
$changed | ForEach-Object { Write-Host "  $_" }

Write-Host 'The Holy Quran TRP v1.3 Research Organization & Workstation Polish invariant checks: PASS'
