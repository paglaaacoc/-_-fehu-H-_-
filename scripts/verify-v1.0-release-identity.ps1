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

$buildIdentity = Join-Path $src 'Infrastructure/BuildIdentity.cs'
$xaml = Join-Path $src 'MainWindow.xaml'
$main = Join-Path $src 'MainWindow.xaml.cs'
$app = Join-Path $src 'App.xaml.cs'
$guard = Join-Path $src 'Infrastructure/PortableInstanceGuard.cs'
$project = Join-Path $src 'QuranReconciliation.csproj'
$backup = Join-Path $src 'Infrastructure/OwnerDataBackupService.cs'
$working = Join-Path $src 'MainWindow.WorkingSlices.cs'
$workingRepo = Join-Path $src 'Infrastructure/WorkingSliceRepository.cs'
$historyRepo = Join-Path $src 'Infrastructure/HistoryRepository.cs'

# Final release identity.
Assert-Contains $buildIdentity '"The Holy Quran TRP"'
Assert-Contains $buildIdentity '"The Holy Translation Reconciliation Project"'
Assert-Contains $buildIdentity '"THTRP"'
Assert-Contains $buildIdentity '"v1.0"'
Assert-Contains $buildIdentity '"The Holy Quran TRP v1.0"'
Assert-Contains $xaml 'Text="The Holy Quran TRP"'
Assert-Contains $xaml 'Text="v1.0"'
Assert-NotContains $xaml 'Build 10 R12'
Assert-NotContains $xaml 'Qur''an Reconciliation Workstation'
Assert-Contains $main 'BuildIdentity.WorkstationName'
Assert-Contains $main 'BuildIdentity.Version'
Assert-Contains $app 'BuildIdentity.WorkstationName'
Assert-Contains $guard 'BuildIdentity.WorkstationName'

# Windows file/product identity.
Assert-Contains $project '<AssemblyName>The Holy Quran TRP</AssemblyName>'
Assert-Contains $project '<AssemblyTitle>The Holy Quran TRP</AssemblyTitle>'
Assert-Contains $project '<Product>The Holy Quran TRP</Product>'
Assert-Contains $project '<Version>1.0.0</Version>'
Assert-Contains $project '<FileVersion>1.0.0.0</FileVersion>'
Assert-Contains $project 'The Holy Translation Reconciliation Project (THTRP)'

# Compatibility-sensitive internals remain stable.
Assert-Contains $guard '".quran-reconciliation.owner-state.lock"'
Assert-Contains $backup 'QuranReconciliation-Backup-'
Assert-Contains $backup 'PreparedStage = "Prepared"'
Assert-Contains $backup 'QuarantinedStage = "Quarantined"'
Assert-Contains $backup 'CommittedStage = "Committed"'

# Accepted R12 behavior remains present.
Assert-Contains $working 'DiscardWorkingSliceChanges_Click'
Assert-Contains $working 'RemoveWorkingSlice_Click'
Assert-Contains $working 'Remove & discard unsaved changes'
Assert-Contains $workingRepo 'SET is_active=0'
Assert-NotContains $workingRepo 'DELETE FROM working_slices'
Assert-Contains $historyRepo 'Working Slice · removed · '

# Release-identity-only diff boundary from accepted R12.
$base = '8a1a11f2b6b10e7974d5f038f59ad6b1f66bdf37'
$allowed = @(
    '.github/workflows/v1.0-windows.yml',
    'docs/V1.0-RELEASE-IDENTITY.md',
    'scripts/verify-v1.0-release-identity.ps1',
    'src/QuranReconciliation/App.xaml.cs',
    'src/QuranReconciliation/Infrastructure/BuildIdentity.cs',
    'src/QuranReconciliation/Infrastructure/PortableInstanceGuard.cs',
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
    throw "v1.0 release identity cut changed files outside the approved identity-only boundary: $($unexpected -join ', ')"
}

Write-Host 'v1.0 identity changed-file boundary:'
$changed | ForEach-Object { Write-Host "  $_" }

Write-Host 'The Holy Quran TRP v1.0 release identity checks: PASS'
