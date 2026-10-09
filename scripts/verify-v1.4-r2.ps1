$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $r1 = '3afc1cda44c7430a7c9ced536babedf919360caf'
    $mirror = $env:THTRP_CI_MIRROR -eq '1'

    $bi = Get-Content -Raw 'src/QuranReconciliation/Infrastructure/BuildIdentity.cs'
    $xaml = Get-Content -Raw 'src/QuranReconciliation/MainWindow.xaml'
    $preview = Get-Content -Raw 'src/QuranReconciliation/MainWindow.ContextAtlasPreview.cs'
    $atlas = Get-Content -Raw 'src/QuranReconciliation/MainWindow.ContextAtlas.cs'
    $reader = Get-Content -Raw 'src/QuranReconciliation/Infrastructure/ContextAtlasVersePreviewReader.cs'
    $zoom = Get-Content -Raw 'src/QuranReconciliation/MainWindow.xaml.cs'
    $project = Get-Content -Raw 'src/QuranReconciliation/QuranReconciliation.csproj'

    if (-not $bi.Contains('"v1.4 R2 R1"') -or
        -not $xaml.Contains('Text="v1.4 R2 R1"') -or
        -not $project.Contains('<Version>1.4.2.1</Version>')) {
        throw 'v1.4 R2 explicit visible build identity is missing.'
    }
    if (-not $xaml.Contains('AtlasReadingPreviewGrid') -or
        -not $xaml.Contains('AtlasReadingScrollViewer_ViewChanged') -or
        -not $xaml.Contains('AtlasLiveResearchScrollViewer_SizeChanged')) {
        throw 'R2 preview or responsive sidebar UI wiring is missing.'
    }
    foreach ($s in @(
        'SqliteOpenMode.ReadOnly', 'Pooling = false',
        'verse_number BETWEEN $start AND $end',
        'SELECT verse_key', 'SELECT t.verse_key'
    )) {
        if (-not $reader.Contains($s)) { throw "Read-only bounded evidence gate missing: $s" }
    }
    foreach ($s in @(
        'Task.Run', '_atlasPreviewRequest', 'AppendAtlasReadingCards(6)',
        'AppendAtlasReadingCards(5)', 'ApplyAtlasReadingZoomMetrics',
        'AtlasReadingCardsPanel.Width', 'AtlasLiveResearchPanel.Width'
    )) {
        if (-not $preview.Contains($s)) { throw "Lazy/reflow gate missing: $s" }
    }
    if (-not $preview.Contains('RefreshAtlasReadingCardWidths()') -or
        -not $preview.Contains('double.IsNaN(AtlasReadingCardsPanel.Width)') -or
        -not $preview.Contains('text.MaxWidth = textWidth') -or
        -not $preview.Contains('en.TextAlignment = TextAlignment.Center') -or
        -not $preview.Contains('bn.TextAlignment = TextAlignment.Center')) {
        throw 'Multilingual reading preview centered reflow regression.'
    }
    if (-not $xaml.Contains('AtlasContentScrollViewer_ViewChanged') -or
        -not $atlas.Contains('AppendAtlasReviewCards(18)') -or
        -not $atlas.Contains('AppendAtlasReviewCards(15)') -or
        -not $atlas.Contains('_atlasReviewEntries.Add') -or
        -not $atlas.Contains('AtlasContentScrollViewer_ViewChanged')) {
        throw 'Review index incremental rendering regression.'
    }
    if (-not $zoom.Contains('ApplyAtlasReadingZoomMetrics();')) {
        throw 'Accepted zoom control is not connected to in-place Atlas rescale.'
    }

    # No Atlas preview may reference research mutation repositories or app-state writers.
    foreach ($s in @(
        'ResearchDatabase', 'ResearchActivityWriter', 'WorkingSliceRepository',
        'ContextRepository', 'SettingsStore', 'OwnerDataBackupService',
        'AppPaths.ResearchDatabase', 'AppPaths.SettingsFile', 'working_slices'
    )) {
        if ($reader.Contains($s) -or $preview.Contains($s)) {
            throw "Transient preview includes forbidden write-related dependency: $s"
        }
    }
    $mutatingSql = '(?im)\b(INSERT\s+INTO|UPDATE\s+\w+\s+SET|DELETE\s+FROM|REPLACE\s+INTO|CREATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE|VACUUM)\b'
    if ($reader -match $mutatingSql) {
        throw 'R2 preview reader must remain SELECT-only.'
    }

    $zoomBody = $preview.Substring($preview.IndexOf('private void ApplyAtlasReadingZoomMetrics()'))
    if ($zoomBody.Contains('ReadRange(') -or $zoomBody.Contains('Children.Add(') -or
        $zoomBody.Contains('RefreshContextAtlas(')) {
        throw 'Zoom path must not requery or rebuild preview cards.'
    }

    # Restored owner artwork must have valid native icon offsets and six sizes.
    $icon = [System.IO.File]::ReadAllBytes('src/QuranReconciliation/Assets/QuranReconciliation.ico')
    $iconSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($icon)).ToLowerInvariant()
    if ($iconSha -ne '4f79cad3ac2bcdb2634cd51789b83211a7c80c82aabd323c5c970f8a8b99d5e2') {
        throw 'Owner-supplied icon artwork mismatch.'
    }
    if ([BitConverter]::ToUInt16($icon, 4) -ne 6) { throw 'Expected six native icon sizes.' }
    $iconSizes = @(16,24,32,48,64,128)
    for ($i = 0; $i -lt $iconSizes.Count; $i++) {
        $p = 6 + 16 * $i
        if ([int]$icon[$p] -ne $iconSizes[$i] -or [int]$icon[$p+1] -ne $iconSizes[$i]) {
            throw "Owner icon frame dimension mismatch at $i."
        }
        $len = [BitConverter]::ToUInt32($icon, $p + 8)
        $offset = [BitConverter]::ToUInt32($icon, $p + 12)
        if ($len -le 0 -or ($offset + $len) -gt $icon.Length -or
            $icon[$offset] -ne 137 -or $icon[$offset + 1] -ne 80) {
            throw "Owner icon frame invalid at $i."
        }
    }

    $protectedBlobs = @{
        'src/QuranReconciliation/Infrastructure/ResearchDatabase.cs' = '11ba7d1ab4ea16455aff6cff319dc9cf6e81c47a'
        'src/QuranReconciliation/Infrastructure/WorkingSliceRepository.cs' = '25d5abbf1203600a6e80dccbb0eb50345f775f21'
        'src/QuranReconciliation/Infrastructure/OwnerDataBackupService.cs' = '2a7d799282b0d3799d8a00fa30d46993fd73e453'
        'src/QuranReconciliation/Infrastructure/ResearchActivityWriter.cs' = 'e9ac90626bed1e9ab24d23cdb8203c5d3c5db55a'
        'src/QuranReconciliation/Infrastructure/ProposalCorpusRepository.cs' = 'f7ebf347d9246944021745967a1d4d5f47690fbf'
        'src/QuranReconciliation/Infrastructure/ProposalCorpusValidator.cs' = '76e2cb394bbfb0ac5e5ec53126b643621c3cb036'
        'src/QuranReconciliation/Infrastructure/ContextAtlasResearchSnapshotRepository.cs' = '7f4007b4a88ada12f6df95ea8f2b87b7a96a541d'
        'src/QuranReconciliation/Infrastructure/CorpusRepository.cs' = 'bb3c1173d11027f5c935fcd60591c2a2b7920c45'
        'src/QuranReconciliation/Resources/juz-map.json' = '1a1034f63aeedd0500023c435e31707bfbf89436'
    }
    foreach ($key in $protectedBlobs.Keys) {
        $hash = (git hash-object -- $key).Trim()
        if ($hash -ne $protectedBlobs[$key]) { throw "R1 protected file changed: $key" }
    }

    if ($mirror) {
        $p = Get-Content -Raw 'CI-SOURCE-PROVENANCE.json' | ConvertFrom-Json
        if ($p.accepted_base_sha -ne '66cf48a99127b9498764eaba0af42a7f4c76a4f3' -or
            $p.owner_test_parent_sha -ne $r1) { throw 'CI source/parent provenance mismatch.' }
    }
    else {
        $allowed = @(
            'scripts/verify-v1.4-r2.ps1',
            'src/QuranReconciliation/MainWindow.xaml',
            'src/QuranReconciliation/MainWindow.xaml.cs',
            'src/QuranReconciliation/MainWindow.ContextAtlas.cs',
            'src/QuranReconciliation/MainWindow.ContextAtlasPreview.cs',
            'src/QuranReconciliation/Infrastructure/ContextAtlasVersePreviewReader.cs',
            'src/QuranReconciliation/Models/ContextAtlasPreviewVerse.cs',
            'src/QuranReconciliation/Infrastructure/BuildIdentity.cs',
            'src/QuranReconciliation/QuranReconciliation.csproj',
            'src/QuranReconciliation/Assets/QuranReconciliation.ico',
            'tools/V14ContextAtlasProbe/Program.cs',
            'tools/V14ContextAtlasProbe/V14ContextAtlasProbe.csproj'
        )
        $changes = @(git diff --name-only "$r1..HEAD" | Where-Object { $_ })
        $unexpected = @($changes | Where-Object { $_ -notin $allowed })
        if ($unexpected.Count) {
            throw "Out-of-scope v1.4 R2 change: $($unexpected -join ', ')"
        }
        $wf = @($changes | Where-Object { $_.StartsWith('.github/workflows/') })
        if ($wf.Count) { throw 'Source repository must not run v1.4 R2 Actions.' }
    }

    Write-Host 'v1.4 R2 transient preview, performance and research-isolation gates: PASS'
}
finally {
    Pop-Location
}
