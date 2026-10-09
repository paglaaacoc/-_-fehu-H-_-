$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repoRoot 'src/QuranReconciliation'
$xaml = Join-Path $src 'MainWindow.xaml'
$main = Join-Path $src 'MainWindow.xaml.cs'
$working = Join-Path $src 'MainWindow.WorkingSlices.cs'
$menus = Join-Path $src 'MainWindow.ReconciliationMenus.cs'
$textMenus = Join-Path $src 'MainWindow.ContextMenus.cs'
$context = Join-Path $src 'MainWindow.Context.cs'
$db = Join-Path $src 'Infrastructure/ResearchDatabase.cs'
$workingRepo = Join-Path $src 'Infrastructure/WorkingSliceRepository.cs'
$historyRepo = Join-Path $src 'Infrastructure/HistoryRepository.cs'
$wbwRepo = Join-Path $src 'Infrastructure/WordByWordRepository.cs'
$appPaths = Join-Path $src 'Infrastructure/AppPaths.cs'
$corpus = Join-Path $src 'Infrastructure/CorpusRepository.cs'
$perf = Join-Path $src 'MainWindow.Performance.cs'
$scroll = Join-Path $src 'MainWindow.Scroll.cs'
$settings = Join-Path $src 'Infrastructure/AppSettings.cs'
$project = Join-Path $src 'QuranReconciliation.csproj'
$manifest = Join-Path $src 'app.manifest'
$researchDb = Join-Path $src 'Infrastructure/ResearchDatabase.cs'
$contextRepo = Join-Path $src 'Infrastructure/ContextRepository.cs'
$history = Join-Path $src 'MainWindow.History.cs'
$portability = Join-Path $repoRoot 'tools/PortabilityProbe/Program.cs'
$portabilityProject = Join-Path $repoRoot 'tools/PortabilityProbe/PortabilityProbe.csproj'
$backupService = Join-Path $src 'Infrastructure/OwnerDataBackupService.cs'
$dataSafety = Join-Path $src 'MainWindow.DataSafety.cs'
$backupProbe = Join-Path $repoRoot 'tools/BackupWorkflowProbe/Program.cs'
$backupProbeProject = Join-Path $repoRoot 'tools/BackupWorkflowProbe/BackupWorkflowProbe.csproj'

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

# Build 10 identity and protected Research Viewer.
Assert-Contains $xaml 'Build 10 R10 · Working Slice stabilization'
Assert-Contains $main "Qur'an Reconciliation Workstation — Build 10 R10"
Assert-Contains $xaml 'x:Name="WorkspaceGrid"'
Assert-Contains $xaml 'x:Name="WorkingSliceWorkspaceGrid"'
Assert-Contains $xaml 'x:Name="HistoryWorkspaceGrid"'
Assert-Contains $xaml 'ZoomMode="Disabled"'
Assert-Contains $perf 'TafsirInitialVerseBatch = 6'
Assert-Contains $perf 'TafsirNextVerseBatch = 6'

# R6 DPI root-cause repair remains frozen.
Assert-Contains $project '<ApplicationManifest>app.manifest</ApplicationManifest>'
Assert-Contains $manifest '<dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/PM</dpiAware>'
Assert-Contains $manifest '<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2, PerMonitor</dpiAwareness>'

# R7 closure/hardening contracts.
Assert-Contains $researchDb 'using var tx = connection.BeginTransaction();'
Assert-Contains $researchDb 'backfill.Transaction = tx;'
Assert-Contains $researchDb 'version.Transaction = tx;'
Assert-Contains $researchDb 'SchemaVersion.ToString'
Assert-Contains $researchDb 'tx.Commit();'
Assert-Contains $contextRepo 'TryValidateProposalRestore'
Assert-Contains $contextRepo 'TryValidateRangeMap'
Assert-Contains $contextRepo 'ValidateMapShape'
Assert-Contains $context 'int? preferredAyah = null'
Assert-Contains $context 'preferredCoversAyah'
Assert-Contains $context 'new CornerRadius(6)'
Assert-Contains $context 'new Thickness(6, 5, 6, 5)'
Assert-Contains $context '_selectedContextId ='
Assert-Contains $history 'contextId,'
Assert-Contains $history 'ayahNumber);'
Assert-Contains $history 'Could not save bookmark for'
Assert-Contains $portability 'ResearchDatabase.SchemaVersion'
Assert-Contains $portabilityProject 'ResearchDatabase.cs'

# R8 verified owner-data safety contracts.
Assert-Contains $settings 'CurrentSchemaVersion = 3'
Assert-Contains $researchDb 'InitializeAt('
Assert-Contains $backupService 'source.BackupDatabase'
Assert-Contains $backupService 'PRAGMA integrity_check'
Assert-Contains $backupService 'PRAGMA foreign_key_check'
Assert-Contains $backupService 'ResearchSha256'
Assert-Contains $backupService 'SettingsSha256'
Assert-Contains $backupService 'PreRestoreReason'
Assert-Contains $backupService 'PreResetReason'
Assert-Contains $backupService 'VerifyBackupMatchesCurrentState'
Assert-Contains $backupService 'TimeSpan.FromMinutes(30)'
Assert-Contains $backupService 'Reset requires the exact verified PreReset backup'
Assert-Contains $backupService 'Owner state changed after the PreReset backup'
Assert-Contains $backupService 'RestoreFilesFromQuarantine'
Assert-Contains $dataSafety 'CreateVerifiedBackup_Click'
Assert-Contains $dataSafety 'RestoreVerifiedBackup_Click'
Assert-Contains $dataSafety 'ResetResearchData_Click'
Assert-Contains $dataSafety 'Type RESET'
Assert-Contains $dataSafety '_suppressSettingsSaveOnClose'
Assert-Contains $xaml 'Content="Data safety"'
Assert-Contains $xaml 'Text="Create verified backup"'
Assert-Contains $xaml 'Text="Restore verified backup…"'
Assert-Contains $xaml 'Text="Reset research data…"'
Assert-Contains $backupProbe 'Verified backup / restore / reset contract: PASS'
Assert-Contains $backupProbeProject 'OwnerDataBackupService.cs'

# R10 Working Slice stabilization contracts.
Assert-Contains $textMenus 'BuildTextBoxEditMenu'
Assert-Contains $textMenus 'Text = "Cut"'
Assert-Contains $textMenus 'Text = "Copy"'
Assert-Contains $textMenus 'Text = "Paste"'
Assert-Contains $textMenus 'Text = "Select all"'
Assert-NotContains $settings 'WritingAssistanceEnabled'
Assert-NotContains $xaml 'WritingAssistanceMenuItem'
Assert-NotContains $textMenus 'IsSpellCheckEnabled'
Assert-NotContains $textMenus 'IsTextPredictionEnabled'

Assert-Contains $working '_workingSliceEvidenceVerses'
Assert-Contains $working '_workingSliceRenderedVerseCount'
Assert-Contains $working 'AppendWorkingSliceEvidenceBatch'
Assert-Contains $working 'WorkingSliceEvidenceScrollViewer_ViewChanged'
Assert-Contains $working 'CurrentInitialVerseBatch'
Assert-Contains $working 'CurrentNextVerseBatch'
Assert-Contains $working 'CaptureWorkingSliceZoomTargets('
Assert-Contains $working 'previouslyRendered'
Assert-Contains $xaml 'ViewChanged="WorkingSliceEvidenceScrollViewer_ViewChanged"'

Assert-Contains $main 'ApplyWorkingSliceZoomMetrics();'
Assert-Contains $main 'ScheduleWorkingSliceOffsetRestore('
Assert-Contains $perf '_workingSliceZoomTextTargets'

Assert-Contains $project '<ApplicationIcon>Assets\QuranReconciliation.ico</ApplicationIcon>'
Assert-Contains $main 'TryApplyApplicationIcon'
Assert-Contains $main 'AppWindow.SetIcon(icon)'

$r9Runtime = 'b832f5462b4be29ed73ba122bb8fbafef27651bd'
$allowedR10Changes = @(
    '.github/workflows/build10-windows.yml',
    'scripts/verify-build10.ps1',
    'src/QuranReconciliation/Assets/QuranReconciliation.ico',
    'src/QuranReconciliation/Infrastructure/AppSettings.cs',
    'src/QuranReconciliation/MainWindow.ContextMenus.cs',
    'src/QuranReconciliation/MainWindow.Layout.cs',
    'src/QuranReconciliation/MainWindow.WorkingSlices.cs',
    'src/QuranReconciliation/MainWindow.xaml',
    'src/QuranReconciliation/MainWindow.xaml.cs'
)

$changedR10 = @(
    git diff --name-only $r9Runtime HEAD |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ }
)

$unexpectedR10 = @($changedR10 | Where-Object { $_ -notin $allowedR10Changes })
if ($unexpectedR10.Count -gt 0) {
    throw "R10 changed files outside the stabilization allowlist: $($unexpectedR10 -join ', ')"
}

$missingR10 = @($allowedR10Changes | Where-Object { $_ -notin $changedR10 })
if ($missingR10.Count -gt 0) {
    throw "R10 expected change missing from diff: $($missingR10 -join ', ')"
}

# Build 8 source fidelity and startup behavior preserved.
Assert-Contains $corpus 't.foot_notes_json'
Assert-Contains $corpus 'ParseTranslationPresentation'
Assert-Contains $main 'Footnotes / পাদটীকা'
Assert-Contains $settings 'LastSurahNumber'
Assert-NotContains $settings 'LastWorkspaceVerticalOffset'

# Build 9 proposal workflow retained plus prompt helper and safe discard.
Assert-Contains $xaml 'Content="Copy AI prompt"'
Assert-Contains $xaml 'Content="Import proposal…"'
Assert-Contains $xaml 'Discard unworked proposal…'
Assert-Contains $context 'Cover ayah 1 through'
Assert-Contains $context 'Return ONLY one ayah range per line'
Assert-Contains $context 'Discard and restore previous map'
Assert-Contains $context 'The proposal itself remains recorded in History as discarded'
Assert-Contains $db 'context_proposal_replaced_blocks'
Assert-Contains $db 'discarded_utc'
Assert-Contains $historyRepo "Created by proposal import #%"
Assert-Contains $historyRepo "Superseded by proposal import #%"
Assert-Contains $historyRepo "'Context proposal import · discarded'"

# Persistent multi-Working-Slice architecture.
Assert-Contains $db 'SchemaVersion = 6'
Assert-Contains $db 'CREATE TABLE IF NOT EXISTS working_slices'
Assert-Contains $db 'CREATE TABLE IF NOT EXISTS working_slice_revisions'
Assert-Contains $workingRepo '"Draft"'
Assert-Contains $workingRepo '"In Review"'
Assert-Contains $workingRepo '"Resolved"'
Assert-Contains $workingRepo 'prior_research_notes'
Assert-Contains $workingRepo 'return false;'
Assert-Contains $xaml 'Content="Working Slices"'
Assert-Contains $xaml 'Working Slice / Reconciliation Workspace'
Assert-Contains $working 'Multiple Working Slices may belong to this Context Block.'
Assert-Contains $working 'Working Slice saved; the previous state is preserved in revision history.'
Assert-Contains $historyRepo "'WorkingSlices'"

# Working Slice evidence reuses immutable corpus and current source selections.
Assert-Contains $working 'GetChapterCached'
Assert-Contains $working 'verse.Translations'
Assert-Contains $working 'verse.Tafsirs'
Assert-Contains $working 'Footnotes / পাদটীকা'
Assert-Contains $xaml 'Linked Ayah / Context notes'
Assert-Contains $xaml 'Corpus evidence remains immutable'

# Functional native context menu and word-by-word aid.
Assert-Contains $working 'BuildAyahContextMenu'
Assert-Contains $working 'Copy ayah reference'
Assert-Contains $working 'Open ayah in Research'
Assert-Contains $working 'Ayah Note'
Assert-Contains $working 'Bookmark ayah'
Assert-Contains $menus 'Text = "Word-by-word"'
Assert-Contains $menus 'Open full ayah beside this text…'
Assert-Contains $menus 'BuildResearchAyahContextMenu'
Assert-Contains $menus 'ShowVerseWordByWordFlyout'
Assert-Contains $menus 'Open selected Arabic word only…'
Assert-Contains $menus 'NormalizeArabicToken'
Assert-Contains $menus 'NormalizeArabicLetter'
Assert-Contains $menus 'sourceWords.Count == words.Count'
Assert-Contains $menus 'QueueVerseWordByWordFlyout'
Assert-Contains $menus 'RootGrid.DispatcherQueue.TryEnqueue'
Assert-Contains $menus 'AppWindow.Size.Width * 0.42'
Assert-Contains $menus 'new GridLength(205)'
Assert-Contains $menus 'new GridLength(225)'
Assert-Contains $menus 'FlyoutPlacementMode.Left'
Assert-Contains $menus 'FlyoutPresenterStyle'
Assert-Contains $menus 'FrameworkElement.MaxWidthProperty'
Assert-Contains $menus 'flyoutWidth + 48'
Assert-Contains $menus 'RefreshSelectedWordOnlyAvailability'
Assert-Contains $menus 'selectionSource.SelectedText'
Assert-Contains $working 'RefreshSelectedWordOnlyAvailability'
Assert-Contains $menus 'Text = "Copy selection"'
Assert-Contains $textMenus 'Text = "Copy selection"'
Assert-Contains $working 'Copy selection'
Assert-Contains $menus 'BuildWordFlyoutContextMenu'
Assert-Contains $menus 'Word meaning…'
Assert-Contains $appPaths 'word-by-word.sqlite'
Assert-Contains $appPaths 'WordByWordDatabase'
Assert-Contains $menus 'Quran Foundation word-level glosses'
Assert-Contains $xaml 'Content="Research"'
Assert-Contains $xaml 'Padding="14"'
Assert-Contains $xaml 'Width="{Binding ViewportWidth, ElementName=WorkingSliceEvidenceScrollViewer}"'
Assert-Contains $xaml 'x:Name="WorkingSliceRecordPanel"'
Assert-Contains $xaml 'Width="{Binding ViewportWidth, ElementName=WorkingSliceRecordScrollViewer}"'
Assert-Contains $xaml 'Grid.Column="2"'
Assert-Contains $xaml 'Content="Working Slices"'
Assert-Contains $xaml 'Grid.Column="3"'
Assert-Contains $xaml 'Content="History"'
Assert-Contains $xaml 'x:Name="WorkingSliceEvidenceBorder"'
Assert-Contains $xaml 'x:Name="WorkingSliceRecordBorder"'
Assert-Contains $xaml 'HorizontalContentAlignment="Stretch"'
Assert-Contains $scroll 'WorkingSliceEvidenceRegion_PointerWheelChanged'
Assert-Contains $scroll 'WorkingSliceRecordRegion_PointerWheelChanged'
Assert-Contains $scroll 'ClipRegionToBounds_SizeChanged'

# Soft-deferred implementations remain carried forward rather than falsely "fixed".
Assert-Contains $scroll 'InitializeScrollOwnershipRouting'
Assert-Contains $scroll 'RouteWheelToOwner'

# No browser layer.
$forbiddenExtensions = @('*.html', '*.htm', '*.css', '*.js', '*.mjs', '*.cjs')
foreach ($pattern in $forbiddenExtensions) {
    $hits = Get-ChildItem -Path $src -Recurse -File -Filter $pattern
    if ($hits) {
        throw "Browser-layer file found: $($hits.FullName -join ', ')"
    }
}

Write-Host 'Build 10 R10 Working Slice stabilization invariants: PASS'
