$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/QuranReconciliation/QuranReconciliation.csproj'
$sourceRoot = Join-Path $repoRoot 'src/QuranReconciliation'
$xaml = Join-Path $sourceRoot 'MainWindow.xaml'
$main = Join-Path $sourceRoot 'MainWindow.xaml.cs'
$contextUi = Join-Path $sourceRoot 'MainWindow.Context.cs'
$contextRepo = Join-Path $sourceRoot 'Infrastructure/ContextRepository.cs'
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

Assert-Contains $researchDb 'SchemaVersion = 2'
Assert-Contains $researchDb '"origin"'
Assert-Contains $researchDb '"is_active"'

Assert-Contains $contextRepo 'EnsureSeeded'
Assert-Contains $contextRepo 'ValidateMap'
Assert-Contains $contextRepo 'SplitAfter'
Assert-Contains $contextRepo 'MergeWithNext'
Assert-Contains $contextRepo 'Accepted context blocks are locked'

Assert-Contains $xaml 'ContextList'
Assert-Contains $xaml 'ContextStatusSelector'
Assert-Contains $xaml 'ContextSplit_Click'
Assert-Contains $xaml 'Boundary history'
Assert-Contains $contextUi 'LoadContextMapForCurrentSurah'
Assert-Contains $contextUi 'ContextExtendStart_Click'
Assert-Contains $contextUi 'ContextShrinkStart_Click'
Assert-Contains $contextUi 'ContextExtendEnd_Click'
Assert-Contains $contextUi 'ContextShrinkEnd_Click'
Assert-Contains $contextUi 'ContextMergePrevious_Click'
Assert-Contains $contextUi 'ContextMergeNext_Click'

Assert-Contains $main 'LoadContextMapForCurrentSurah()'

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
            throw "Forbidden Build 4 dependency/state term '$term' found in $($file.FullName)"
        }
    }
}

Write-Host 'Build 4 native/context invariants: PASS'
