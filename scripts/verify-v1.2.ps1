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

$gitAttributes = Join-Path $repoRoot '.gitattributes'
$buildIdentity = Join-Path $src 'Infrastructure/BuildIdentity.cs'
$appPaths = Join-Path $src 'Infrastructure/AppPaths.cs'
$juzRepo = Join-Path $src 'Infrastructure/JuzRepository.cs'
$juzUi = Join-Path $src 'MainWindow.Juz.cs'
$xaml = Join-Path $src 'MainWindow.xaml'
$main = Join-Path $src 'MainWindow.xaml.cs'
$context = Join-Path $src 'MainWindow.Context.cs'
$history = Join-Path $src 'MainWindow.History.cs'
$performance = Join-Path $src 'MainWindow.Performance.cs'
$working = Join-Path $src 'MainWindow.WorkingSlices.cs'
$project = Join-Path $src 'QuranReconciliation.csproj'
$attribution = Join-Path $src 'ATTRIBUTION.md'
$juzMap = Join-Path $src 'Resources/juz-map.json'
$researchDb = Join-Path $src 'Infrastructure/ResearchDatabase.cs'
$backup = Join-Path $src 'Infrastructure/OwnerDataBackupService.cs'
$workingRepo = Join-Path $src 'Infrastructure/WorkingSliceRepository.cs'
$probe = Join-Path $repoRoot 'tools/JuzMetadataProbe/Program.cs'

# Final product identity advances cleanly to v1.2.
Assert-Contains $buildIdentity '"The Holy Quran TRP"'
Assert-Contains $buildIdentity '"The Holy Translation Reconciliation Project"'
Assert-Contains $buildIdentity '"THTRP"'
Assert-Contains $buildIdentity '"v1.2"'
Assert-Contains $buildIdentity '"The Holy Quran TRP v1.2"'
Assert-Contains $xaml 'Text="The Holy Quran TRP"'
Assert-Contains $xaml 'Text="v1.2"'
Assert-Contains $project '<Version>1.2.0</Version>'
Assert-Contains $project '<AssemblyVersion>1.2.0.0</AssemblyVersion>'

# Juz is immutable metadata around canonical verse keys, not a research schema mutation.
Assert-Contains $gitAttributes 'src/QuranReconciliation/Resources/juz-map.json text eol=lf'
Assert-Contains $appPaths 'JuzMapFile'
Assert-Contains $project 'Resources\juz-map.json'
Assert-Contains $project 'Corpus\juz-map.json'
Assert-Contains $juzRepo 'Pinned Juz metadata must contain exactly Juz 1 through 30.'
Assert-Contains $juzRepo '6,236'
Assert-Contains $juzRepo 'GetJuzNumber'
Assert-Contains $juzRepo 'GetRangeLabel'
Assert-Contains $juzRepo 'GetStartsInsideRange'
Assert-Contains $juzMap '"number": 1, "start": "1:1", "end": "2:141"'
Assert-Contains $juzMap '"number": 30, "start": "78:1", "end": "114:6"'

$juzHash = (Get-FileHash -LiteralPath $juzMap -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedJuzHash = '3016bb4f94af9a42c8c1c24728bf6044e5b4ac13130e47d451c0c10ba35dd434'
if ($juzHash -ne $expectedJuzHash) {
    throw "Pinned Juz map changed. Expected $expectedJuzHash, got $juzHash."
}

# Navigation and structural awareness.
Assert-Contains $xaml 'x:Name="JuzSelector"'
Assert-Contains $xaml 'JuzSelector_SelectionChanged'
Assert-Contains $xaml 'x:Name="CurrentJuzText"'
Assert-NotContains $juzUi 'NavigateToResearchTarget'
Assert-Contains $juzUi 'NavigateToJuzStart'
Assert-Contains $juzUi 'ResetVerseRenderingAtAyah'
Assert-Contains $juzUi 'UpdateJuzFromResearchViewport'
Assert-Contains $performance '_renderedVerseStartIndex'
Assert-Contains $performance 'ResetVerseRenderingAtAyah'
Assert-Contains $performance 'FindRenderedVerseElement'
Assert-Contains $performance 'renderedEnd ='
Assert-Contains $history 'FindRenderedVerseElement'
Assert-NotContains $history 'VersePanel.Children[index]'
Assert-Contains $main 'previousStartAyah'
Assert-Contains $main '_renderedVerseStartIndex + 1'
Assert-Contains $juzUi 'Juz boundaries are metadata only'
Assert-Contains $main 'InitializeJuzNavigation()'
Assert-Contains $main 'CreateResearchJuzBoundaryMarker'
Assert-Contains $main 'Tag = verse.VerseNumber'
Assert-Contains $performance 'UpdateJuzFromResearchViewport()'
Assert-Contains $history 'UpdateJuzUi('

# Context semantics remain independent from Juz.
Assert-Contains $context 'GetContextJuzLabel'
Assert-Contains $context 'GetJuzPromptMetadata'
Assert-Contains $juzUi 'Do not force a Context Block boundary'
Assert-Contains $working 'GetContextJuzLabel'
Assert-Contains $working 'CreateWorkingSliceJuzBoundaryMarker'
Assert-Contains $workingRepo 'SET is_active=0'
Assert-NotContains $workingRepo 'DELETE FROM working_slices'

# Accepted R11/R12 safety contracts remain present.
Assert-Contains $backup 'PreparedStage = "Prepared"'
Assert-Contains $backup 'QuarantinedStage = "Quarantined"'
Assert-Contains $backup 'CommittedStage = "Committed"'
Assert-Contains $researchDb 'RejectNewerSchema(fullPath)'

# Dedicated behavioral probe exists.
Assert-Contains $probe 'Expected exactly 30 Juz.'
Assert-Contains $probe 'juz.GetJuzNumber(2, 141) == 1'
Assert-Contains $probe 'juz.GetJuzNumber(2, 142) == 2'
Assert-Contains $probe '"Juz 1 → 2"'
Assert-Contains $probe '30 Juz cover all 6,236 canonical ayat exactly once.'

# Tight v1.2 boundary from exact accepted v1.0.
$base = '1017434d6b51fb86f51bae0ec0c33d97e3f6ecf8'
$allowed = @(
    '.github/workflows/v1.2-windows.yml',
    'docs/V1.2-JUZ-RESPONSIVENESS.md',
    'scripts/verify-v1.2.ps1',
    'src/QuranReconciliation/Infrastructure/BuildIdentity.cs',
    'src/QuranReconciliation/MainWindow.History.cs',
    'src/QuranReconciliation/MainWindow.Juz.cs',
    'src/QuranReconciliation/MainWindow.Performance.cs',
    'src/QuranReconciliation/MainWindow.xaml',
    'src/QuranReconciliation/MainWindow.xaml.cs',
    'src/QuranReconciliation/QuranReconciliation.csproj'
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
    throw "v1.2 changed files outside the approved Juz-responsive-navigation boundary: $($unexpected -join ', ')"
}

Write-Host 'v1.2 changed-file boundary:'
$changed | ForEach-Object { Write-Host "  $_" }

Write-Host 'The Holy Quran TRP v1.2 Juz-responsive-navigation invariant checks: PASS'
