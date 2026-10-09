$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot 'src/QuranReconciliation'
$project = Join-Path $sourceRoot 'QuranReconciliation.csproj'
$windowXaml = Join-Path $sourceRoot 'MainWindow.xaml'
$windowCode = Join-Path $sourceRoot 'MainWindow.xaml.cs'
$repoCode = Join-Path $sourceRoot 'Infrastructure/CorpusRepository.cs'
$settings = Join-Path $sourceRoot 'Infrastructure/AppSettings.cs'

function Assert-Contains([string]$Path, [string]$Needle) {
    $text = Get-Content -Raw -LiteralPath $Path
    if (-not $text.Contains($Needle)) {
        throw "Expected '$Needle' in $Path"
    }
}

Assert-Contains $project '<UseWinUI>true</UseWinUI>'
Assert-Contains $project '<WindowsPackageType>None</WindowsPackageType>'
Assert-Contains $project '<EnableMsixTooling>true</EnableMsixTooling>'
Assert-Contains $project '<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>'

Assert-Contains $windowXaml 'x:Name="SurahList"'
Assert-Contains $windowXaml 'x:Name="VersePanel"'
Assert-Contains $windowXaml 'x:Name="SourceListPanel"'
Assert-Contains $windowXaml 'Evidence sources'

Assert-Contains $windowCode 'DefaultTranslationIds'
Assert-Contains $windowCode 'LoadCurrentSurah'
Assert-Contains $windowCode 'CreateVerseCard'
Assert-Contains $windowCode 'CreateArabicArea'
Assert-Contains $windowCode 'CreateBilingualSourceArea'
Assert-Contains $windowCode 'SelectedTranslationIds'
Assert-Contains $windowCode 'SelectedTafsirIds'

Assert-Contains $repoCode 'SqliteOpenMode.ReadOnly'
Assert-Contains $repoCode 'GetChapters()'
Assert-Contains $repoCode 'GetResources()'
Assert-Contains $repoCode 'GetChapter('
Assert-Contains $repoCode 'ToPlainText'

Assert-Contains $settings 'SourceSelectionInitialized'
Assert-Contains $settings 'LastSurahNumber'
Assert-Contains $settings 'SelectedTranslationIds'
Assert-Contains $settings 'SelectedTafsirIds'

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
    'Microsoft.Win32.Registry',
    'HttpClient'
)

foreach ($file in $sourceFiles) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($term in $forbiddenTerms) {
        if ($content.Contains($term)) {
            throw "Forbidden runtime dependency/state term '$term' found in $($file.FullName)"
        }
    }
}

Write-Host 'Build 3 comparison-workstation invariants: PASS'
