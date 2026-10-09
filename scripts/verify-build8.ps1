$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repoRoot 'src/QuranReconciliation'
$main = Join-Path $src 'MainWindow.xaml.cs'
$xaml = Join-Path $src 'MainWindow.xaml'
$perf = Join-Path $src 'MainWindow.Performance.cs'
$history = Join-Path $src 'MainWindow.History.cs'
$settings = Join-Path $src 'Infrastructure/AppSettings.cs'
$corpus = Join-Path $src 'Infrastructure/CorpusRepository.cs'
$models = Join-Path $src 'Models/CorpusModels.cs'
$bookmarks = Join-Path $src 'Infrastructure/BookmarkRepository.cs'
$scroll = Join-Path $src 'MainWindow.Scroll.cs'
$builder = Join-Path $repoRoot 'tools/CorpusBuilder/Program.cs'

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

# Build 8 identity and preserved native architecture.
Assert-Contains $xaml 'Build 8 · Source fidelity &amp; navigation'
Assert-Contains $main "Qur'an Reconciliation Workstation — Build 8"
Assert-Contains $xaml 'ZoomMode="Disabled"'
Assert-Contains $main 'ApplyRenderedVerseZoomMetrics();'
Assert-Contains $perf 'TafsirInitialVerseBatch = 6'
Assert-Contains $perf 'TafsirNextVerseBatch = 6'
Assert-Contains $corpus 'IndexTafsirsByAnchor'

# Precise startup position restore is removed; last Surah remains.
Assert-Contains $settings 'LastSurahNumber'
Assert-NotContains $settings 'LastWorkspaceVerticalOffset'
Assert-Contains $main '_settings.LastSurahNumber'
Assert-Contains $main 'LoadCurrentSurah(resetScroll: true);'
Assert-NotContains $main 'ScheduleStartupWorkspaceRestore'
Assert-NotContains $main '_lastWorkspaceVerticalOffset'
Assert-NotContains $perf 'ScheduleStartupWorkspaceRestore'
Assert-NotContains $perf 'RestoreStartupWorkspaceOffset'
Assert-NotContains $perf '_lastWorkspaceVerticalOffset'
Assert-NotContains $perf '_restoringWorkspacePosition'
Assert-NotContains $history '_lastWorkspaceVerticalOffset'

# Existing Bookmarks remain the precise return mechanism.
Assert-Contains $main 'bookmarkButton.Click += BookmarkAyah_Click'
Assert-Contains $history 'BookmarkAyah_Click'
Assert-Contains $history 'NavigateToResearchTarget'
Assert-Contains $bookmarks 'ON CONFLICT(surah_number, ayah_number)'

# Translation footnote fidelity.
Assert-Contains $corpus 't.foot_notes_json'
Assert-Contains $corpus 'ParseTranslationPresentation'
Assert-Contains $corpus 'TranslationFootnoteMarkerRegex'
Assert-Contains $corpus 'JsonDocument.Parse'
Assert-Contains $models 'record SourceFootnote'
Assert-Contains $models 'StoredFootnotes'
Assert-Contains $main 'Footnotes / পাদটীকা'
Assert-Contains $main 'Content = footnoteStack'
Assert-Contains $main 'IsExpanded = false'

# Corpus regeneration may only reproduce the exact historical corpus.
Assert-Contains $builder 'QURAN_CORPUS_BUILD_UTC'
Assert-Contains $builder 'buildUtc'

# The soft-deferred scroll implementation remains present, but Build 8
# deliberately does not claim it is fixed.
Assert-Contains $scroll 'InitializeScrollOwnershipRouting'
Assert-Contains $scroll 'RouteWheelToOwner'

$forbiddenExtensions = @('*.html', '*.htm', '*.css', '*.js', '*.mjs', '*.cjs')
foreach ($pattern in $forbiddenExtensions) {
    $hits = Get-ChildItem -Path $src -Recurse -File -Filter $pattern
    if ($hits) {
        throw "Browser-layer file found: $($hits.FullName -join ', ')"
    }
}

Write-Host 'Build 8 source-fidelity/navigation invariants: PASS'
