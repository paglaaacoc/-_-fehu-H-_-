$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/QuranReconciliation/QuranReconciliation.csproj'
$xaml = Join-Path $repoRoot 'src/QuranReconciliation/MainWindow.xaml'
$sourceRoot = Join-Path $repoRoot 'src'

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
Assert-Contains $project 'Microsoft.WindowsAppSDK'
Assert-Contains $xaml 'FlowDirection="RightToLeft"'
Assert-Contains $xaml 'Uthmani'
Assert-Contains $xaml 'IndoPak'
Assert-Contains $xaml 'IndoPak Nastaleeq'
Assert-Contains $xaml 'ThemeSelector'
Assert-Contains $xaml 'ZoomMode="Enabled"'
Assert-Contains $xaml 'SelectableText_RightTapped'
Assert-Contains $xaml 'Bangla comparison'

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
            throw "Forbidden Build 0 dependency/state term '$term' found in $($file.FullName)"
        }
    }
}

Write-Host 'Build 0 static invariants: PASS'
