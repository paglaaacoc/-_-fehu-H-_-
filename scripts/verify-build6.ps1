$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot 'src/QuranReconciliation'
$project = Join-Path $sourceRoot 'QuranReconciliation.csproj'
$paths = Join-Path $sourceRoot 'Infrastructure/AppPaths.cs'
$research = Join-Path $sourceRoot 'Infrastructure/ResearchDatabase.cs'
$context = Join-Path $sourceRoot 'Infrastructure/ContextRepository.cs'
$notes = Join-Path $sourceRoot 'Infrastructure/NoteRepository.cs'

function Assert-Contains([string]$Path, [string]$Needle) {
    $text = Get-Content -Raw -LiteralPath $Path
    if (-not $text.Contains($Needle)) {
        throw "Expected '$Needle' in $Path"
    }
}

Assert-Contains $project '<UseWinUI>true</UseWinUI>'
Assert-Contains $project '<WindowsPackageType>None</WindowsPackageType>'
Assert-Contains $project '<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>'
Assert-Contains $paths 'AppContext.BaseDirectory'
Assert-Contains $paths '"Data"'
Assert-Contains $paths '"Backups"'
Assert-Contains $research 'SchemaVersion = 2'
Assert-Contains $context 'ValidateMap'
Assert-Contains $notes 'SaveAyahNote'
Assert-Contains $notes 'SaveContextNote'

$forbiddenTerms = @(
    'Environment.GetFolderPath',
    'ApplicationData.Current',
    'Microsoft.Win32.Registry'
)

$sourceFiles = Get-ChildItem -Path $sourceRoot -Recurse -File |
    Where-Object { $_.Extension -in @('.cs', '.xaml', '.csproj') }

foreach ($file in $sourceFiles) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($term in $forbiddenTerms) {
        if ($content.Contains($term)) {
            throw "Forbidden portability term '$term' found in $($file.FullName)"
        }
    }
}

Write-Host 'Build 6 portability invariants: PASS'
