$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$repoRoot = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repoRoot 'src/QuranReconciliation'
$base = '66cf48a99127b9498764eaba0af42a7f4c76a4f3'
$ciMirror = $env:THTRP_CI_MIRROR -eq '1'

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

function Assert-RegexAbsent([string]$Path, [string]$Pattern) {
    $text = Get-Content -Raw -LiteralPath $Path
    if ($text -match $Pattern) {
        throw "Forbidden pattern '$Pattern' found in $Path"
    }
}

function Assert-Unchanged([string]$RelativePath) {
    $diff = @(git diff --name-only "$base..HEAD" -- $RelativePath)
    if ($diff.Count -gt 0) {
        throw "Accepted v1.3 protected path changed in v1.4: $RelativePath"
    }
}

function Assert-CanonicalTextSha256([string]$Path, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required file missing: $Path"
    }

    # Git may materialize text as CRLF on Windows even when the canonical
    # repository blob is LF. Golden handoff identity is defined over the
    # canonical UTF-8/LF bytes, not checkout-specific line endings.
    $text = [System.IO.File]::ReadAllText($Path)
    $normalized = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($normalized)
    $actual = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($bytes)
    ).ToLowerInvariant()

    if ($actual -ne $Expected) {
        throw "Canonical SHA-256 mismatch for $Path. Expected $Expected, got $actual"
    }
}

# ----------------------------------------------------------------------
# A. Accepted v1.3 preservation / v1.4 identity
# ----------------------------------------------------------------------

$buildIdentity = Join-Path $src 'Infrastructure/BuildIdentity.cs'
$researchDb = Join-Path $src 'Infrastructure/ResearchDatabase.cs'
$appPaths = Join-Path $src 'Infrastructure/AppPaths.cs'
$snapshotRepo = Join-Path $src 'Infrastructure/ContextAtlasResearchSnapshotRepository.cs'
$corpusRepo = Join-Path $src 'Infrastructure/ProposalCorpusRepository.cs'
$validator = Join-Path $src 'Infrastructure/ProposalCorpusValidator.cs'
$importService = Join-Path $src 'Infrastructure/ProposalCorpusImportService.cs'
$exportService = Join-Path $src 'Infrastructure/ProposalHandoffExportService.cs'
$atlasUi = Join-Path $src 'MainWindow.ContextAtlas.cs'
$xaml = Join-Path $src 'MainWindow.xaml'
$project = Join-Path $src 'QuranReconciliation.csproj'

Assert-Contains $buildIdentity '"v1.4"'
Assert-Contains $buildIdentity '"The Holy Quran TRP v1.4"'
Assert-Contains $xaml 'Text="v1.4"'
Assert-Contains $project '<Version>1.4.0</Version>'
Assert-Contains $project '<AssemblyVersion>1.4.0.0</AssemblyVersion>'
Assert-Contains $project '<FileVersion>1.4.0.0</FileVersion>'

Assert-Contains $researchDb 'internal const int SchemaVersion = 7;'

$protectedBlobs = [ordered]@{
    'src/QuranReconciliation/Infrastructure/ResearchDatabase.cs' = '11ba7d1ab4ea16455aff6cff319dc9cf6e81c47a'
    'src/QuranReconciliation/Infrastructure/ResearchActivityWriter.cs' = 'e9ac90626bed1e9ab24d23cdb8203c5d3c5db55a'
    'src/QuranReconciliation/Infrastructure/ContextRepository.cs' = '2f1f3d872efb76953313c1802bb0b5d8b2c3c5f8'
    'src/QuranReconciliation/Infrastructure/NoteRepository.cs' = '62854dd2e36f92414f573d28c462c3058b09cc3e'
    'src/QuranReconciliation/Infrastructure/BookmarkRepository.cs' = 'a70974e66bd5ff79999da8bc6f6361dae9ded32b'
    'src/QuranReconciliation/Infrastructure/WorkingSliceRepository.cs' = '25d5abbf1203600a6e80dccbb0eb50345f775f21'
    'src/QuranReconciliation/Infrastructure/OwnerDataBackupService.cs' = '2a7d799282b0d3799d8a00fa30d46993fd73e453'
    'src/QuranReconciliation/Infrastructure/SettingsStore.cs' = 'a164895b44005ee901554a52269460aed0c0f7d1'
    'src/QuranReconciliation/Infrastructure/AppSettings.cs' = '21d5a689257b019095a38b33a9ffe90b18ed6901'
    'src/QuranReconciliation/Infrastructure/CorpusRepository.cs' = 'bb3c1173d11027f5c935fcd60591c2a2b7920c45'
    'src/QuranReconciliation/Infrastructure/JuzRepository.cs' = 'd0a5edd77c3476a6868a2af5929394e81c5c5cb7'
    'src/QuranReconciliation/Infrastructure/WordByWordRepository.cs' = '07e2fe86ccbd2b57902bc86ff0eb32e1e54211b8'
    'src/QuranReconciliation/MainWindow.DataSafety.cs' = '0472fbfe280a1191a8727e58b7a58b814353f9ed'
    'src/QuranReconciliation/MainWindow.Context.cs' = 'a263c12c1390ddd0bab1be0b9b3b817cecd7329d'
    'src/QuranReconciliation/MainWindow.Notes.cs' = '51fcf244e2321a31e11a0112753e4fec4b6328b8'
    'src/QuranReconciliation/MainWindow.Juz.cs' = '2e650b85303ff43b6f8dbe5bf8e0ceb5a865f905'
    'src/QuranReconciliation/MainWindow.Performance.cs' = '957bedb9989ac6e08f0adf06362b93db863440e3'
    'src/QuranReconciliation/MainWindow.ReconciliationMenus.cs' = '92270f08eb8a8944e3082c55f85002718bf7f5c5'
    'src/QuranReconciliation/MainWindow.Scroll.cs' = '6c67c520b7bcfbce1203504c9ab9be52d569ca6e'
    'src/QuranReconciliation/Resources/juz-map.json' = '1a1034f63aeedd0500023c435e31707bfbf89436'
    'tools/CorpusBuilder/CorpusBuilder.csproj' = 'f3c30e0b21d2173fa8f422e9814f0d04cadb9193'
    'tools/CorpusBuilder/Program.cs' = '9ea99940a67cb8f8bceea5a3a4b3fed1fafbda81'
    'tools/WordByWordBuilder/WordByWordBuilder.csproj' = 'bdd62ef8cc9c40077c61ce0f5a6b86c29bf8442b'
    'tools/WordByWordBuilder/Program.cs' = '5e7c01c9a4824ca83d86bd759d7997eb62182f91'
    'tools/JuzMetadataProbe/JuzMetadataProbe.csproj' = '58851dc1439dfdc6f42192706be41b5f8f02db88'
    'tools/JuzMetadataProbe/Program.cs' = '93a49d97934e8f982b3e4e1146628e4a60733451'
    'tools/JuzMetadataProbe/ChapterSummaryStub.cs' = '9b4c94204acd19f66c39b5f44b948234cb2517d1'
}

if ($ciMirror) {
    foreach ($entry in $protectedBlobs.GetEnumerator()) {
        if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $entry.Key))) {
            throw "CI mirror missing accepted protected file: $($entry.Key)"
        }

        $actualBlob = (git hash-object -- $entry.Key).Trim()

        if ($actualBlob -ne $entry.Value) {
            throw "CI mirror protected blob mismatch for $($entry.Key). Expected $($entry.Value), got $actualBlob"
        }
    }

    $provenancePath = Join-Path $repoRoot 'CI-SOURCE-PROVENANCE.json'
    if (-not (Test-Path -LiteralPath $provenancePath)) {
        throw 'CI mirror is missing CI-SOURCE-PROVENANCE.json.'
    }

    $provenance = Get-Content -Raw -LiteralPath $provenancePath | ConvertFrom-Json
    if ($provenance.accepted_base_sha -ne $base) {
        throw "CI mirror provenance does not anchor accepted v1.3 SHA $base."
    }
}
else {
    foreach ($path in $protectedBlobs.Keys) {
        Assert-Unchanged $path
    }

    # The source-of-truth repo must not gain a v1.4 Actions workflow.
    $sourceWorkflowChanges = @(
        git diff --name-only "$base..HEAD" -- '.github/workflows'
    )
    if ($sourceWorkflowChanges.Count -gt 0) {
        throw "v1.4 source branch changed GitHub Actions workflows. CI must run only in bond0013/THTRP1.4: $($sourceWorkflowChanges -join ', ')"
    }
}

# ----------------------------------------------------------------------
# B. Research-state / navigation firewall
# ----------------------------------------------------------------------

Assert-Contains $snapshotRepo 'Mode ='
Assert-Contains $snapshotRepo 'SqliteOpenMode.ReadOnly'
Assert-Contains $snapshotRepo 'Pooling ='
Assert-Contains $snapshotRepo 'false'
Assert-RegexAbsent $snapshotRepo '(?im)\b(INSERT|UPDATE|DELETE|REPLACE|CREATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE|VACUUM)\b'

$atlasCapabilityFiles = @(
    $snapshotRepo,
    $corpusRepo,
    $validator,
    $importService,
    $exportService,
    $atlasUi
)

$mutationDependencies = @(
    'ContextRepository',
    'WorkingSliceRepository',
    'NoteRepository',
    'BookmarkRepository',
    'HistoryRepository',
    'ResearchActivityWriter',
    'OwnerDataBackupService'
)

foreach ($file in $atlasCapabilityFiles) {
    foreach ($dependency in $mutationDependencies) {
        Assert-NotContains $file $dependency
    }

    Assert-NotContains $file 'SettingsStore'
    Assert-NotContains $file 'SaveCurrentSettings'
    Assert-NotContains $file 'AppPaths.SettingsFile'
}

Assert-Contains $importService 'AppPaths.ContextAtlasImportedDirectory'
Assert-Contains $importService 'File.Copy'
Assert-Contains $exportService 'AppPaths.ContextAtlasExportsDirectory'
Assert-NotContains $importService 'AppPaths.ResearchDatabase'
Assert-NotContains $exportService 'AppPaths.ResearchDatabase'

foreach ($needle in @(
    'NavigateToResearchTarget',
    'OpenWorkingSlices(',
    'OpenHistory_Click(',
    'LoadCurrentSurah(',
    'LoadContextMapForCurrentSurah(',
    'CreateWorkingSliceFromSelectedContext',
    'SetStatus(',
    'SplitAfter(',
    'MergeWithNext('
)) {
    Assert-NotContains $atlasUi $needle
}

Assert-Contains $xaml 'x:Name="ContextAtlasWorkspaceGrid"'
Assert-Contains $xaml 'x:Name="ContextAtlasNavButton"'
Assert-Contains $xaml 'Content="Context Atlas"'
Assert-Contains $xaml 'KeyboardAcceleratorPlacementMode="Hidden"'

# Existing shortcuts remain present; only their automatic visual hint is hidden.
$workspaceUi = Join-Path $src 'MainWindow.Workspace.cs'
foreach ($needle in @(
    'VirtualKey.Number1',
    'VirtualKey.Number2',
    'VirtualKey.Number3',
    'VirtualKey.F',
    'VirtualKey.S'
)) {
    Assert-Contains $workspaceUi $needle
}

$atlasXamlStart = (Select-String -LiteralPath $xaml -SimpleMatch '<Grid x:Name="ContextAtlasWorkspaceGrid"' | Select-Object -First 1).LineNumber
$bottomBarStart = (Select-String -LiteralPath $xaml -SimpleMatch '<Border Grid.Row="2"' | Select-Object -Last 1).LineNumber
if (-not $atlasXamlStart -or -not $bottomBarStart -or $bottomBarStart -le $atlasXamlStart) {
    throw 'Could not isolate Context Atlas XAML surface.'
}
$xamlLines = Get-Content -LiteralPath $xaml
$atlasXaml = ($xamlLines[($atlasXamlStart - 1)..($bottomBarStart - 2)] -join [Environment]::NewLine)
if ($atlasXaml -match 'Content="(?:Open|Navigate|Create|Apply|Accept|Edit)\b') {
    throw "Context Atlas exposes a forbidden research mutation/navigation command: $($Matches[0])"
}

Assert-Contains $appPaths 'ContextAtlasImportedDirectory'
Assert-Contains $appPaths 'ContextAtlasExportsDirectory'
Assert-Contains $appPaths 'ContextAtlasBuiltInDirectory'
Assert-Contains $appPaths 'ContextAtlasHandoffTemplatesDirectory'

# R11 research backup boundary remains unchanged and knows nothing about Atlas.
$backup = Join-Path $src 'Infrastructure/OwnerDataBackupService.cs'
Assert-NotContains $backup 'ContextAtlas'

# ----------------------------------------------------------------------
# C. Built-in Edition 1 exactness and golden templates
# ----------------------------------------------------------------------

$builtIn = Join-Path $src 'Resources/ContextAtlas/BuiltIn'
$parts = @(
    Get-ChildItem -LiteralPath $builtIn -Filter '*.b64part' -File |
        Sort-Object Name
)

if ($parts.Count -ne 42) {
    throw "Expected exactly 42 Edition 1 Base64 transport parts, found $($parts.Count)."
}

$encoded = [string]::Concat(
    @(
        $parts |
            ForEach-Object {
                Get-Content -Raw -LiteralPath $_.FullName
            }
    )
)

if ($encoded.Length -ne 233300) {
    throw "Edition 1 Base64 transport length mismatch. Expected 233300, got $($encoded.Length)."
}

try {
    $packageBytes = [Convert]::FromBase64String($encoded)
}
catch {
    throw "Edition 1 Base64 transport cannot be decoded: $($_.Exception.Message)"
}

$packageHash = [Convert]::ToHexString(
    [System.Security.Cryptography.SHA256]::HashData($packageBytes)
).ToLowerInvariant()

$expectedPackageHash = 'd4f572bdf94ff4a3d708258530f3d4bec5793fe0f061cc893ad682bf7150307f'
if ($packageHash -ne $expectedPackageHash) {
    throw "Built-in Edition 1 package hash mismatch. Expected $expectedPackageHash, got $packageHash."
}

$stream = [System.IO.MemoryStream]::new($packageBytes, $false)
$zip = [System.IO.Compression.ZipArchive]::new(
    $stream,
    [System.IO.Compression.ZipArchiveMode]::Read,
    $false
)

try {
    $manifestEntry = $zip.GetEntry('manifest.json')
    $payloadEntry = $zip.GetEntry('proposal-corpus.json')

    if ($null -eq $manifestEntry -or $null -eq $payloadEntry) {
        throw 'Built-in Edition 1 ZIP is missing manifest.json or proposal-corpus.json.'
    }

    $payloadStream = $payloadEntry.Open()
    $payloadMemory = [System.IO.MemoryStream]::new()
    try {
        $payloadStream.CopyTo($payloadMemory)
        $payloadBytes = $payloadMemory.ToArray()
    }
    finally {
        $payloadStream.Dispose()
        $payloadMemory.Dispose()
    }

    $payloadHash = [Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData($payloadBytes)
    ).ToLowerInvariant()

    $expectedPayloadHash = '7c0cfb8d3f11ae1a5458e084ed59d111f35d07ab7ee53eb87b0311b414b4b6aa'
    if ($payloadHash -ne $expectedPayloadHash) {
        throw "Built-in Edition 1 payload hash mismatch. Expected $expectedPayloadHash, got $payloadHash."
    }

    $payloadJson = [System.Text.Encoding]::UTF8.GetString($payloadBytes)
    $payload = $payloadJson | ConvertFrom-Json -Depth 100

    if ($payload.surahs.Count -ne 114) {
        throw "Edition 1 must contain exactly 114 Surahs."
    }

    $ayat = 0
    $blocks = 0
    $boundaries = 0
    $macros = 0
    $blockIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    foreach ($surah in $payload.surahs) {
        $ayat += [int]$surah.verses_count
        $blocks += $surah.context_blocks.Count
        $boundaries += $surah.boundaries.Count
        $macros += $surah.macro_groups.Count

        foreach ($block in $surah.context_blocks) {
            if (-not $blockIds.Add([string]$block.context_block_id)) {
                throw "Edition 1 duplicate Context Block ID: $($block.context_block_id)"
            }
        }

        foreach ($macro in $surah.macro_groups) {
            foreach ($id in $macro.context_block_ids) {
                if (-not $blockIds.Contains([string]$id)) {
                    # Because blocks in later Surahs have not been visited yet, resolve against payload instead.
                    $resolved = $false
                    foreach ($candidateSurah in $payload.surahs) {
                        if ($candidateSurah.context_blocks.context_block_id -contains [string]$id) {
                            $resolved = $true
                            break
                        }
                    }
                    if (-not $resolved) {
                        throw "Edition 1 unresolved Macro Context Block reference: $id"
                    }
                }
            }
        }
    }

    if ($ayat -ne 6236 -or
        $blocks -ne 1304 -or
        $boundaries -ne 1190 -or
        $macros -ne 376 -or
        $payload.cross_surah_worksets.Count -ne 17) {
        throw "Edition 1 statistics mismatch: ayat=$ayat blocks=$blocks boundaries=$boundaries macros=$macros worksets=$($payload.cross_surah_worksets.Count)"
    }

    if ($blockIds.Count -ne 1304) {
        throw "Edition 1 Context Block IDs are not globally unique."
    }
}
finally {
    $zip.Dispose()
    $stream.Dispose()
}

Assert-Contains $corpusRepo $expectedPackageHash
Assert-Contains $corpusRepo '7c0cfb8d3f11ae1a5458e084ed59d111f35d07ab7ee53eb87b0311b414b4b6aa'

$templateRoot = Join-Path $src 'Resources/ContextAtlas/HandoffTemplates'
Assert-CanonicalTextSha256 (Join-Path $templateRoot 'Common/ISLAMIC-TERMINOLOGY-STANDARD.md') '4683c957f247c6dca29986aa4c54564145d44ee2a30cd55281aade3d437f852e'
Assert-CanonicalTextSha256 (Join-Path $templateRoot 'Common/context-map-proposal-corpus-v1.schema.json') 'bdd3039accebe418554a766f2b2814746861dc1a2850ee79c5d5900dd850a939'
Assert-CanonicalTextSha256 (Join-Path $templateRoot 'New/00-READ-ME-FIRST.md') 'fe7fe9099c5805ccd3cc7e98d693ddc6ff1cc3c4b254f436407289ba7fbf7598'
Assert-CanonicalTextSha256 (Join-Path $templateRoot 'New/01-MASTER-PROMPT.md') 'ed69d2a59cd8cf49b753adf1d82e5cd489fec2ba14eee2998789d02eb1f03b4a'
Assert-CanonicalTextSha256 (Join-Path $templateRoot 'Common/scripts/show_english_window.py') '21debc0a9e0505467418fafcb63fc9419b4aac0516e2ac6ffd097bc124353596'
Assert-CanonicalTextSha256 (Join-Path $templateRoot 'Common/scripts/validate_context_proposals.py') '1fda76ed2b33743b604898b72bee901499a8f0bcbcbe3af1dbee3f623b33d459'

Assert-Contains $exportService '"THTRP Astra Context Proposals LEAN R2"'
Assert-Contains $exportService '"2026-10-06"'
Assert-Contains $exportService '"9a56469e36d3789da2cfb56736dce36ea2bf54d3"'
Assert-Contains $exportService '"accepted/v1.2-2026-10-06"'
Assert-Contains $exportService '" START"'

# ----------------------------------------------------------------------
# D. Exact v1.4 changed-file boundary
# ----------------------------------------------------------------------

$allowedExact = @(
    'scripts/verify-v1.4.ps1',
    'src/QuranReconciliation/Infrastructure/AppPaths.cs',
    'src/QuranReconciliation/Infrastructure/BuildIdentity.cs',
    'src/QuranReconciliation/Infrastructure/ContextAtlasResearchSnapshotRepository.cs',
    'src/QuranReconciliation/Infrastructure/ProposalCorpusImportService.cs',
    'src/QuranReconciliation/Infrastructure/ProposalCorpusRepository.cs',
    'src/QuranReconciliation/Infrastructure/ProposalCorpusValidator.cs',
    'src/QuranReconciliation/Infrastructure/ProposalHandoffExportService.cs',
    'src/QuranReconciliation/MainWindow.ContextAtlas.cs',
    'src/QuranReconciliation/MainWindow.History.cs',
    'src/QuranReconciliation/MainWindow.WorkingSlices.cs',
    'src/QuranReconciliation/MainWindow.Workspace.cs',
    'src/QuranReconciliation/MainWindow.xaml',
    'src/QuranReconciliation/Models/ContextAtlasModels.cs',
    'src/QuranReconciliation/QuranReconciliation.csproj'
)

if (-not $ciMirror) {
    $changed = @(
        git diff --name-only "$base..HEAD" |
            Where-Object { $_ -and $_.Trim() -ne '' } |
            Sort-Object -Unique
    )

    $unexpected = @(
        $changed |
            Where-Object {
                $_ -notin $allowedExact -and
                -not $_.StartsWith('src/QuranReconciliation/Resources/ContextAtlas/', [StringComparison]::Ordinal) -and
                -not $_.StartsWith('tools/V14ContextAtlasProbe/', [StringComparison]::Ordinal)
            }
    )

    if ($unexpected.Count -gt 0) {
        throw "v1.4 changed files outside the approved Context Atlas boundary: $($unexpected -join ', ')"
    }

    Write-Host 'v1.4 changed-file boundary:'
    $changed | ForEach-Object { Write-Host "  $_" }
}
else {
    Write-Host 'CI mirror mode: accepted protected blobs verified by Git object identity.'
}

Write-Host 'The Holy Quran TRP v1.4 Context Atlas invariant checks: PASS'
