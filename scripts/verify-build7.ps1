$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot 'src/QuranReconciliation'
$xaml = Join-Path $sourceRoot 'MainWindow.xaml'
$main = Join-Path $sourceRoot 'MainWindow.xaml.cs'
$context = Join-Path $sourceRoot 'MainWindow.Context.cs'
$notes = Join-Path $sourceRoot 'MainWindow.Notes.cs'
$perf = Join-Path $sourceRoot 'MainWindow.Performance.cs'
$layout = Join-Path $sourceRoot 'MainWindow.Layout.cs'
$scroll = Join-Path $sourceRoot 'MainWindow.Scroll.cs'
$historyUi = Join-Path $sourceRoot 'MainWindow.History.cs'
$historyRepo = Join-Path $sourceRoot 'Infrastructure/HistoryRepository.cs'
$bookmarkRepo = Join-Path $sourceRoot 'Infrastructure/BookmarkRepository.cs'
$researchDb = Join-Path $sourceRoot 'Infrastructure/ResearchDatabase.cs'
$settings = Join-Path $sourceRoot 'Infrastructure/AppSettings.cs'
$scrollControls = Join-Path $sourceRoot 'Controls/OuterScrollControls.cs'
$corpus = Join-Path $sourceRoot 'Infrastructure/CorpusRepository.cs'
$models = Join-Path $sourceRoot 'Models/CorpusModels.cs'
$bnNames = Join-Path $sourceRoot 'Models/BengaliSurahNames.cs'

function Assert-Contains([string]$Path, [string]$Needle) {
    $text = Get-Content -Raw -LiteralPath $Path
    if (-not $text.Contains($Needle)) { throw "Expected '$Needle' in $Path" }
}
function Assert-NotContains([string]$Path, [string]$Needle) {
    $text = Get-Content -Raw -LiteralPath $Path
    if ($text.Contains($Needle)) { throw "Did not expect '$Needle' in $Path" }
}

Assert-Contains $xaml 'Build 7 · Responsive stabilization'
Assert-Contains $xaml 'WorkspaceContentPanel'
Assert-Contains $xaml 'x:Name="CenterWorkspaceBorder"'
Assert-Contains $xaml 'RightSidebarScrollViewer'
Assert-Contains $xaml 'HistoryWorkspaceGrid'
Assert-Contains $xaml 'Research History &amp; Navigation'
Assert-Contains $xaml 'Content="Ayah notes"'
Assert-Contains $xaml 'Content="Context notes"'
Assert-Contains $xaml 'Content="Boundary changes"'
Assert-Contains $xaml 'Content="Bookmarks"'
Assert-Contains $xaml 'HistorySurahFilter'
Assert-Contains $xaml 'HistoryJumpToTarget_Click'
Assert-Contains $xaml '<ItemsStackPanel/>'
Assert-Contains $xaml '← Back to research'
Assert-Contains $xaml 'EditAyahNoteButton'
Assert-Contains $xaml 'EditContextNoteButton'
Assert-Contains $xaml 'Content="History"'
Assert-Contains $xaml 'AyahNoteExpander'
Assert-Contains $xaml 'LeftSidebarColumn'
Assert-Contains $xaml 'RightSidebarColumn'
Assert-Contains $xaml 'LeftSidebarSplitter'
Assert-Contains $xaml 'RightSidebarSplitter'
Assert-Contains $xaml 'Top bar · Hidden'
Assert-Contains $xaml 'Content="Layout"'
Assert-NotContains $xaml 'ResearchHistoryExpander'

# Owner-passed R4/R5 zoom semantics remain layout-native.
Assert-Contains $xaml 'ZoomMode="Disabled"'
Assert-Contains $xaml 'HorizontalScrollMode="Disabled"'
Assert-Contains $perf 'UpdateWorkspaceContentWidth'
Assert-Contains $perf 'logicalViewport = viewport - 48'
Assert-Contains $perf 'ApplyRenderedVerseZoomMetrics'
Assert-Contains $perf 'ScheduleWorkspaceOffsetRestore'
Assert-Contains $perf 'RegisterZoomText'
Assert-Contains $perf 'CreateZoomSelectableText'
Assert-Contains $main 'private double Z(double value)'
Assert-Contains $main 'ApplyRenderedVerseZoomMetrics();'
Assert-Contains $main 'ScheduleWorkspaceOffsetRestore(previousOffset);'
Assert-Contains $main 'GetChapterCached'
Assert-Contains $main 'ChapterCacheLimit = 6'

$mainText = Get-Content -Raw -LiteralPath $main
$applyZoomStart = $mainText.IndexOf('private void ApplyZoom(bool rebuildContent = true)')
$applyZoomEnd = $mainText.IndexOf('private void SelectableText_RightTapped', $applyZoomStart)
if ($applyZoomStart -lt 0 -or $applyZoomEnd -le $applyZoomStart) {
    throw 'Could not isolate ApplyZoom.'
}
$applyZoomBody = $mainText.Substring($applyZoomStart, $applyZoomEnd - $applyZoomStart)
if ($applyZoomBody.Contains('LoadCurrentSurah') -or $applyZoomBody.Contains('UpdateLayout')) {
    throw 'Zoom regression: ApplyZoom must not rebuild the Surah or force synchronous UpdateLayout.'
}

# Tafsir-heavy responsiveness is optimized without changing evidence text/reflow.
Assert-Contains $perf 'TafsirInitialVerseBatch = 6'
Assert-Contains $perf 'TafsirNextVerseBatch = 6'
Assert-Contains $perf 'CurrentInitialVerseBatch'
Assert-Contains $corpus 'IndexTafsirsByAnchor'
Assert-Contains $corpus 'tafsirsByAnchor.TryGetValue'
Assert-NotContains $corpus 'GetTafsirTextsForVerse'
Assert-Contains $corpus 'ToPlainText(reader.GetString(7))'

# Three visible regions explicitly own wheel input; no window-global wheel layer.
Assert-Contains $xaml 'controls:OuterScrollListView x:Name="SurahList"'
Assert-Contains $scroll 'LeftSidebarBorder.AddHandler'
Assert-Contains $scroll 'CenterWorkspaceBorder.AddHandler'
Assert-Contains $scroll 'RightSidebarBorder.AddHandler'
Assert-Contains $scroll 'handledEventsToo: true'
Assert-Contains $scroll 'RouteWheelToOwner'
Assert-Contains $scroll 'if (e.Handled)'
Assert-Contains $scroll 'FindDescendantScrollViewer'
Assert-Contains $main 'InitializeScrollOwnershipRouting();'
Assert-Contains $scrollControls 'protected override void OnPointerWheelChanged'
Assert-NotContains $scrollControls 'AddHandler('
Assert-NotContains $layout 'PointerWheelChanged'
Assert-NotContains $layout 'ScrollByWheel'

# Session position is portable and restored progressively.
Assert-Contains $settings 'LastWorkspaceVerticalOffset'
Assert-Contains $main '_settings.LastWorkspaceVerticalOffset'
Assert-Contains $main '_settings.LastWorkspaceVerticalOffset ='
Assert-Contains $perf 'ScheduleStartupWorkspaceRestore'
Assert-Contains $perf 'RestoreStartupWorkspaceOffset'
Assert-Contains $perf '_lastWorkspaceVerticalOffset'
Assert-Contains $perf 'EnsureVerseRenderedThrough'

# Surah display includes curated Bangla phonetic names alongside existing meaning.
Assert-Contains $models 'BengaliSurahNames.Get(Number)'
Assert-Contains $bnNames '"আল-বাকারা"'
Assert-Contains $bnNames '"আলে ইমরান"'
$bnText = Get-Content -Raw -LiteralPath $bnNames
$bnCount = [regex]::Matches(
    $bnText,
    '(?m)^\s*"[^"]+",\s*$').Count
if ($bnCount -ne 114) {
    throw "Expected exactly 114 curated Bangla phonetic Surah names; found $bnCount."
}

# Existing passed context/note contracts remain present.
Assert-Contains $context '_selectedContextId = target'
Assert-Contains $notes 'AyahNoteExpander.IsExpanded = true'
Assert-Contains $notes 'No earlier saved revisions yet.'
Assert-Contains $notes 'revision.PriorBody'
Assert-Contains $notes 'SetAyahNoteEditorState'
Assert-Contains $notes 'AyahNoteTextBox.IsReadOnly'
Assert-Contains $notes 'SetContextNoteEditorState'

# Research Hub: SQLite-backed filters, Surah grouping, bookmarks and direct navigation.
Assert-Contains $historyUi 'HistoryFilter_Checked'
Assert-Contains $historyUi 'HistorySurahFilter_SelectionChanged'
Assert-Contains $historyUi 'groupBySurah'
Assert-Contains $historyUi 'NavigateToResearchTarget'
Assert-Contains $historyUi 'BookmarkAyah_Click'
Assert-Contains $historyUi 'BookmarkRepository'
Assert-Contains $historyRepo "'AyahNotes' AS category"
Assert-Contains $historyRepo "'ContextNotes'"
Assert-Contains $historyRepo "'Bookmarks'"
Assert-Contains $historyRepo '$category'
Assert-Contains $historyRepo '$surah'
Assert-Contains $historyRepo 'LIMIT $limit'
Assert-Contains $bookmarkRepo 'ON CONFLICT(surah_number, ayah_number)'
Assert-Contains $researchDb 'CREATE TABLE IF NOT EXISTS bookmarks'
Assert-Contains $researchDb 'SchemaVersion = 3'
Assert-Contains $main 'bookmarkButton.Click += BookmarkAyah_Click'

# Source/corpus rendering and font invariants.
Assert-Contains $corpus 'string? anchor'
Assert-Contains $models 'ScopeLabel'
Assert-Contains $main 'ResetVerseRendering(verses)'
Assert-Contains $main 'scope {item.ScopeLabel}'
Assert-Contains $main 'indopak-nastaleeq-waqf-lazim-v4.2.1.ttf'
Assert-Contains $main 'AlQuran IndoPak by QuranWBW'
Assert-Contains $main 'UthmanicHafs1Ver18.ttf'
Assert-Contains $main 'KFGQPC HAFS Uthmanic Script'
Assert-Contains $main 'baseLineHeight: useIndoPakFont ? 84 : 70'
Assert-Contains $main 'baseMinHeight: useIndoPakFont ? 126 : 116'
Assert-Contains $main 'TextAlignment = TextAlignment.Center'
Assert-Contains $layout 'MinLeftSidebarWidth = 250'
Assert-Contains $layout 'MinRightSidebarWidth = 360'

$forbiddenExtensions = @('*.html', '*.htm', '*.css', '*.js', '*.mjs', '*.cjs')
foreach ($pattern in $forbiddenExtensions) {
    $hits = Get-ChildItem -Path $sourceRoot -Recurse -File -Filter $pattern
    if ($hits) { throw "Browser-layer file found: $($hits.FullName -join ', ')" }
}

Write-Host 'Build 7 R6 navigation/responsiveness invariants: PASS'
