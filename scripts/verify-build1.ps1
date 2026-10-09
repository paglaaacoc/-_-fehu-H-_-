$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/QuranReconciliation/QuranReconciliation.csproj'
$sourceRoot = Join-Path $repoRoot 'src/QuranReconciliation'

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
Assert-Contains $project 'Microsoft.Data.Sqlite'

Assert-Contains (Join-Path $sourceRoot 'Infrastructure/AppPaths.cs') 'AppContext.BaseDirectory'
Assert-Contains (Join-Path $sourceRoot 'Infrastructure/AppPaths.cs') '"Data"'
Assert-Contains (Join-Path $sourceRoot 'Infrastructure/AppPaths.cs') '"Backups"'

Assert-Contains (Join-Path $sourceRoot 'Infrastructure/AppSettings.cs') 'public float Zoom { get; set; } = 1.00f;'
Assert-Contains (Join-Path $sourceRoot 'Infrastructure/SettingsStore.cs') 'File.Move(temp, AppPaths.SettingsFile, true)'

$db = Join-Path $sourceRoot 'Infrastructure/ResearchDatabase.cs'
Assert-Contains $db 'context_blocks'
Assert-Contains $db 'context_boundary_history'
Assert-Contains $db 'ayah_notes'
Assert-Contains $db 'ayah_note_history'
Assert-Contains $db 'context_notes'
Assert-Contains $db 'context_note_history'

$main = Join-Path $sourceRoot 'MainWindow.xaml.cs'
Assert-Contains $main 'private const float DefaultZoom = 1.00f;'
Assert-Contains $main 'SettingsStore.Load()'
Assert-Contains $main 'ResearchDatabase.Initialize()'
Assert-Contains $main 'SaveCurrentSettings()'

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
            throw "Forbidden Build 1 dependency/state term '$term' found in $($file.FullName)"
        }
    }
}

Write-Host 'Build 1 native/portable invariants: PASS'
