$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/QuranReconciliation/QuranReconciliation.csproj'
$sourceRoot = Join-Path $repoRoot 'src/QuranReconciliation'
$xaml = Join-Path $sourceRoot 'MainWindow.xaml'
$main = Join-Path $sourceRoot 'MainWindow.xaml.cs'
$notesUi = Join-Path $sourceRoot 'MainWindow.Notes.cs'
$notesRepo = Join-Path $sourceRoot 'Infrastructure/NoteRepository.cs'
$researchDb = Join-Path $sourceRoot 'Infrastructure/ResearchDatabase.cs'

function Assert-Contains([string]$Path, [string]$Needle) {
    $text = Get-Content -Raw -LiteralPath $Path
    if (-not $text.Contains($Needle)) {
        throw "Expected '$Needle' in $Path"
    }
}

Assert-Contains $project '<UseWinUI>true</UseWinUI>'
Assert-Contains $project '<WindowsPackageType>None</WindowsPackageType>'
Assert-Contains $project '<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>'

Assert-Contains $researchDb 'ayah_notes'
Assert-Contains $researchDb 'ayah_note_history'
Assert-Contains $researchDb 'context_notes'
Assert-Contains $researchDb 'context_note_history'

Assert-Contains $notesRepo 'SaveAyahNote'
Assert-Contains $notesRepo 'SaveContextNote'
Assert-Contains $notesRepo 'GetAyahHistory'
Assert-Contains $notesRepo 'GetContextHistory'

Assert-Contains $xaml 'AyahNoteTextBox'
Assert-Contains $xaml 'ContextNoteTextBox'
Assert-Contains $xaml 'SaveAyahNote_Click'
Assert-Contains $xaml 'SaveContextNote_Click'
Assert-Contains $notesUi 'SelectAyahForNote_Click'
Assert-Contains $main 'SelectAyahForNote_Click'
Assert-Contains $main 'EnsureAyahNoteTargetForSurah'

$forbiddenExtensions = @('*.html', '*.htm', '*.css', '*.js', '*.mjs', '*.cjs')
foreach ($pattern in $forbiddenExtensions) {
    $hits = Get-ChildItem -Path $sourceRoot -Recurse -File -Filter $pattern
    if ($hits) {
        throw "Browser-layer file found: $($hits.FullName -join ', ')"
    }
}

$sourceFiles = Get-ChildItem -Path $sourceRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.cs', '.xaml', '.csproj') }

$forbiddenTerms = @(
    'WebView2',
    'Microsoft.Web.WebView2',
    'Environment.GetFolderPath',
    'ApplicationData.Current',
    'Microsoft.Win32.Registry'
)

foreach ($file in $sourceFiles) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($term in $forbiddenTerms) {
        if ($content.Contains($term)) {
            throw "Forbidden Build 5 dependency/state term '$term' found in $($file.FullName)"
        }
    }
}

Write-Host 'Build 5 native/notes invariants: PASS'
