$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repoRoot 'src/QuranReconciliation'
$main = Join-Path $src 'MainWindow.xaml.cs'
$xaml = Join-Path $src 'MainWindow.xaml'
$contextUi = Join-Path $src 'MainWindow.Context.cs'
$contextRepo = Join-Path $src 'Infrastructure/ContextRepository.cs'
$proposalParser = Join-Path $src 'Infrastructure/ContextProposalParser.cs'
$researchDb = Join-Path $src 'Infrastructure/ResearchDatabase.cs'
$historyRepo = Join-Path $src 'Infrastructure/HistoryRepository.cs'
$perf = Join-Path $src 'MainWindow.Performance.cs'
$settings = Join-Path $src 'Infrastructure/AppSettings.cs'
$corpus = Join-Path $src 'Infrastructure/CorpusRepository.cs'
$scroll = Join-Path $src 'MainWindow.Scroll.cs'

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

# Build 9 identity and protected native Research Viewer.
Assert-Contains $xaml 'Build 9 · Context proposal import'
Assert-Contains $main "Qur'an Reconciliation Workstation — Build 9"
Assert-Contains $xaml 'Context Map / প্রসঙ্গ মানচিত্র'
Assert-Contains $xaml 'Content="Import proposal…"'
Assert-Contains $xaml 'ZoomMode="Disabled"'
Assert-Contains $main 'ApplyRenderedVerseZoomMetrics();'
Assert-Contains $perf 'TafsirInitialVerseBatch = 6'
Assert-Contains $perf 'TafsirNextVerseBatch = 6'

# Build 8 source-fidelity behavior remains intact.
Assert-Contains $corpus 't.foot_notes_json'
Assert-Contains $corpus 'ParseTranslationPresentation'
Assert-Contains $main 'Footnotes / পাদটীকা'
Assert-Contains $settings 'LastSurahNumber'
Assert-NotContains $settings 'LastWorkspaceVerticalOffset'
Assert-NotContains $perf 'ScheduleStartupWorkspaceRestore'
Assert-NotContains $perf 'RestoreStartupWorkspaceOffset'

# Proposal parser: deterministic, contiguous, full-Surah coverage.
Assert-Contains $proposalParser 'ContextProposalParser'
Assert-Contains $proposalParser 'one range per line'
Assert-Contains $proposalParser 'must be contiguous'
Assert-Contains $proposalParser 'must begin at ayah 1'
Assert-Contains $proposalParser 'must end at ayah'

# Preview-before-commit and owner authority.
Assert-Contains $contextUi 'PrimaryButtonText = "Preview"'
Assert-Contains $contextUi 'Title = "Preview proposed Context Map"'
Assert-Contains $contextUi 'PrimaryButtonText = "Import proposal"'
Assert-Contains $contextUi 'Imported blocks start as Proposed'
Assert-Contains $contextUi 'Accepted / Locked'
Assert-Contains $contextRepo 'Proposal import is blocked because this Surah contains an Accepted / Locked context block'
Assert-Contains $contextRepo "'Proposed'"
Assert-Contains $contextRepo 'proposal_import_id'

# Durable provenance/history rather than destructive replacement.
Assert-Contains $researchDb 'SchemaVersion = 4'
Assert-Contains $researchDb 'context_proposal_imports'
Assert-Contains $researchDb 'proposal_import_id'
Assert-Contains $contextRepo 'superseded by proposal import'
Assert-Contains $contextRepo 'Created by proposal import'
Assert-Contains $historyRepo "'Context proposal import'"
Assert-Contains $historyRepo 'FROM context_proposal_imports'

# Existing soft-deferred implementation remains untouched, not falsely "fixed".
Assert-Contains $scroll 'InitializeScrollOwnershipRouting'
Assert-Contains $scroll 'RouteWheelToOwner'

# Architecture guardrail: no browser-layer files.
$forbiddenExtensions = @('*.html', '*.htm', '*.css', '*.js', '*.mjs', '*.cjs')
foreach ($pattern in $forbiddenExtensions) {
    $hits = Get-ChildItem -Path $src -Recurse -File -Filter $pattern
    if ($hits) {
        throw "Browser-layer file found: $($hits.FullName -join ', ')"
    }
}

Write-Host 'Build 9 Context proposal/import invariants: PASS'
