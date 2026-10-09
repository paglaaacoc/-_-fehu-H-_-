using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

string repoRoot =
    GetArg(
        args,
        "--repo-root");

string corpusDatabase =
    GetArg(
        args,
        "--corpus");

repoRoot =
    Path.GetFullPath(
        repoRoot);

corpusDatabase =
    Path.GetFullPath(
        corpusDatabase);

ProbeSupport.Require(
    Directory.Exists(
        repoRoot),
    $"Repository root not found: {repoRoot}");

ProbeSupport.Require(
    File.Exists(
        corpusDatabase),
    $"Verified corpus database not found: {corpusDatabase}");

static void VerifyBuild17SessionSelector()
{
    string[] ids = ["built-in", "GPT6.1", "other"];
    var preserved = AtlasSessionSelection.Restore(ids, "GPT6.1", "other", "built-in");
    ProbeSupport.Require(preserved.Left == 2 && preserved.Right == 0 &&
        !preserved.RemovedSelectedCorpus, "Build 1.7: A/B IDs were not preserved.");

    var reordered = AtlasSessionSelection.Restore(["other", "built-in", "GPT6.1"],
        "GPT6.1", "other", "built-in");
    ProbeSupport.Require(reordered.Left == 0 && reordered.Right == 1 &&
        !reordered.RemovedSelectedCorpus, "Build 1.7: A/B reorder changed identities.");

    var deliberateSame = AtlasSessionSelection.Restore(ids,
        "GPT6.1", "other", "other");
    ProbeSupport.Require(deliberateSame.Left == 2 && deliberateSame.Right == 2 &&
        !deliberateSame.RemovedSelectedCorpus,
        "Build 1.7: intentionally identical A/B should stay selected.");

    var removed = AtlasSessionSelection.Restore(["built-in", "other"],
        "built-in", "missing", "other");
    ProbeSupport.Require(removed.Left == 0 && removed.Right == 1 &&
        removed.RemovedSelectedCorpus,
        "Build 1.7: removed A should fall back safely and reset disagreement.");

    var removedB = AtlasSessionSelection.Restore(["built-in", "other"],
        "built-in", "other", "missing");
    ProbeSupport.Require(removedB.Left == 1 && removedB.Right == 0 &&
        removedB.RemovedSelectedCorpus,
        "Build 1.7: removed B should select an available distinct fallback.");

    var firstOpen = AtlasSessionSelection.Restore(ids, "built-in", null, null);
    ProbeSupport.Require(firstOpen.Left == 0 && firstOpen.Right == 1 &&
        !firstOpen.RemovedSelectedCorpus, "Build 1.7: first open default mismatch.");

    var single = AtlasSessionSelection.Restore(["built-in"],
        "built-in", null, null);
    ProbeSupport.Require(single.Left == 0 && single.Right == 0,
        "Build 1.7: single-corpus fallback mismatch.");
    Console.WriteLine("Build 1.7 A/B session selection: 7/7 PASS");
}

VerifyBuild17SessionSelector();

static void VerifyBuild18WorkspaceAndExitPreference()
{
    // Real app policy under test: normal re-entry must preserve a dirty
    // editor; deliberate navigation and first open may reload.
    var cases = new (bool Initialized, long? Preferred, bool Refresh)[]
    {
        (false, null, true),
        (false, 9, true),
        (true, null, false),
        (true, 9, true),
        (true, 0, true)
    };
    foreach (var (initialized, preferred, expected) in cases)
        ProbeSupport.Require(
            WorkingSliceNavigation.ShouldRefresh(initialized, preferred) == expected,
            "Build 1.8 Working Slice re-entry policy mismatch.");

    // Roundtrip the portable opt-out preference without changing schema 3.
    var defaults = new AppSettings();
    ProbeSupport.Require(defaults.ConfirmBeforeExit &&
        defaults.SchemaVersion == 3, "Build 1.8 exit default/schema mismatch.");
    defaults.ConfirmBeforeExit = false;
    string json = System.Text.Json.JsonSerializer.Serialize(defaults);
    var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
    ProbeSupport.Require(restored is not null && !restored.ConfirmBeforeExit &&
        restored.SchemaVersion == 3, "Build 1.8 opt-out roundtrip failed.");
    Console.WriteLine("Build 1.8 Workspace re-entry + optional exit: 7/7 PASS");
}
VerifyBuild18WorkspaceAndExitPreference();



string sourceAtlas =
    Path.Combine(
        repoRoot,
        "src",
        "QuranReconciliation",
        "Resources",
        "ContextAtlas");

string sourceBuiltIn =
    Path.Combine(
        sourceAtlas,
        "BuiltIn");

string sourceTemplates =
    Path.Combine(
        sourceAtlas,
        "HandoffTemplates");

string sourceJuz =
    Path.Combine(
        repoRoot,
        "src",
        "QuranReconciliation",
        "Resources",
        "juz-map.json");

ProbeSupport.Require(
    Directory.Exists(
        sourceBuiltIn),
    "Source built-in Atlas corpus directory is missing.");

ProbeSupport.Require(
    Directory.Exists(
        sourceTemplates),
    "Source Atlas handoff templates are missing.");

string temp =
    Path.Combine(
        Path.GetTempPath(),
        "THTRP-v14-ContextAtlasProbe-" +
        Guid.NewGuid()
            .ToString("N"));

Directory.CreateDirectory(
    temp);

try
{
    PrepareRuntimeCorpus(
        corpusDatabase,
        sourceJuz,
        sourceTemplates);

    var corpus =
        new CorpusRepository();

    IReadOnlyList<ChapterSummary> chapters =
        corpus.GetChapters();

    ProbeSupport.Require(
        chapters.Count == 114 &&
        chapters.Sum(
            x =>
                x.VersesCount) == 6236,
        "Probe requires the canonical 114-Surah / 6,236-ayah corpus.");

    string importedRoot =
        AppPaths.ContextAtlasImportedDirectory;

    if (Directory.Exists(
            importedRoot))
    {
        Directory.Delete(
            importedRoot,
            recursive: true);
    }

    Directory.CreateDirectory(
        importedRoot);

    var library =
        new ProposalCorpusRepository(
            chapters,
            sourceBuiltIn,
            importedRoot);

    ProposalCorpusPackage builtIn =
        library.LoadAll()
            .Single(
                x =>
                    x.IsBuiltIn);

    VerifyBuiltIn(
        builtIn);

    VerifyValidationFailures(
        builtIn,
        chapters,
        temp);

    VerifyImportIsolation(
        builtIn,
        chapters,
        sourceBuiltIn,
        importedRoot,
        temp);

    VerifyReadOnlySnapshot(
        temp);

    VerifyExports(
        builtIn,
        chapters);

    VerifyTransientReadingPreview(corpusDatabase);

    Console.WriteLine(
        "The Holy Quran TRP v1.4 Context Atlas dynamic contract probe: PASS");
}
finally
{
    try
    {
        Microsoft.Data.Sqlite.SqliteConnection
            .ClearAllPools();

        if (Directory.Exists(
                temp))
        {
            Directory.Delete(
                temp,
                recursive: true);
        }

        if (Directory.Exists(
                AppPaths.ContextAtlasDirectory))
        {
            Directory.Delete(
                AppPaths.ContextAtlasDirectory,
                recursive: true);
        }

        string data =
            AppPaths.DataDirectory;

        if (Directory.Exists(
                data))
        {
            Directory.Delete(
                data,
                recursive: true);
        }
    }
    catch
    {
    }
}

static void VerifyBuiltIn(
    ProposalCorpusPackage builtIn)
{
    ProbeSupport.Require(
        builtIn.PackageSha256 ==
            ProposalCorpusRepository
                .BuiltInEdition1PackageSha256,
        "Built-in package SHA-256 does not match the pinned Edition 1 package.");

    ProbeSupport.Require(
        builtIn.PayloadSha256 ==
            ProposalCorpusRepository
                .BuiltInEdition1PayloadSha256,
        "Built-in payload SHA-256 does not match the pinned Edition 1 payload.");

    ProposalCorpusDocument corpus =
        builtIn.Corpus;

    ProbeSupport.Require(
        corpus.Surahs.Count == 114 &&
        corpus.Statistics.AyatCovered == 6236 &&
        corpus.Statistics.TotalContextBlocks == 1304 &&
        corpus.Statistics.TotalInternalBoundaries == 1190 &&
        corpus.Statistics.MacroGroups == 376 &&
        corpus.CrossSurahWorksets.Count == 17,
        "Built-in Edition 1 statistics do not match the authoritative fixture.");

    ProbeSupport.Require(
        corpus.Surahs
            .SelectMany(
                x =>
                    x.ContextBlocks)
            .Select(
                x =>
                    x.ContextBlockId)
            .Distinct(
                StringComparer.Ordinal)
            .Count() == 1304,
        "Built-in Context Block IDs must be globally unique.");

    ProbeSupport.Require(
        corpus.Contributors.Any(
            x =>
                x.ModelName ==
                "GPT-6 Astra") &&
        corpus.Contributors.Any(
            x =>
                x.ModelName ==
                "GPT-5.6 Sol"),
        "Built-in Edition 1 provenance lost its two authoritative model contributors.");

    ProposalWorkset companion =
        corpus.CrossSurahWorksets
            .Single(
                x =>
                    x.Id ==
                    "X17");

    ProbeSupport.Require(
        companion.Members.Count == 0 &&
        companion.Subsets.Count == 3 &&
        companion.AllMembers.Count() == 6 &&
        companion.Subsets.All(
            x =>
                !string.IsNullOrWhiteSpace(
                    x.Label) &&
                x.Members.Count == 2),
        "Built-in Edition 1 X17 companion-Surah subset structure was not preserved.");
}

static void VerifyValidationFailures(
    ProposalCorpusPackage builtIn,
    IReadOnlyList<ChapterSummary> chapters,
    string temp)
{
    string validPath =
        Path.Combine(
            temp,
            "edition1-valid.zip");

    ProbeSupport.WriteBytes(
        validPath,
        ProbeSupport.DecodeBuiltIn(
            Path.Combine(
                Path.GetFullPath(
                    GetArg(
                        Environment.GetCommandLineArgs()
                            .Skip(1)
                            .ToArray(),
                        "--repo-root")),
                "src",
                "QuranReconciliation",
                "Resources",
                "ContextAtlas",
                "BuiltIn")));

    ProposalCorpusPackage validated =
        ProposalCorpusValidator
            .LoadAndValidate(
                validPath,
                chapters,
                isBuiltIn: false);

    ProbeSupport.Require(
        validated.PackageSha256 ==
            builtIn.PackageSha256,
        "Valid Edition 1 package did not validate byte-for-byte.");

    string corrupt =
        Path.Combine(
            temp,
            "corrupt.zip");

    ProbeSupport.WriteBytes(
        corrupt,
        new byte[]
        {
            0x01,
            0x02,
            0x03,
            0x04
        });

    ProbeSupport.RequireThrows(
        () =>
            ProposalCorpusValidator
                .LoadAndValidate(
                    corrupt,
                    chapters,
                    false),
        "Corrupt ZIP must be refused.");

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "hash-mismatch",
        mutatePayload: null,
        mutateManifest:
            manifest =>
                manifest["payload_sha256"] =
                    new string(
                        '0',
                        64),
        syncHash: false);

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "unsupported-schema",
        payload =>
            payload["schema_version"] =
                999);

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "missing-model",
        payload =>
            payload["contributors"]!
                .AsArray()[0]!
                .AsObject()
                .Remove(
                    "model_name"));

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "missing-date",
        payload =>
            payload["published_date"] =
                string.Empty);

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "bad-surah-count",
        payload =>
            ProbeSupport
                .Surahs(
                    payload)
                .RemoveAt(113));

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "gap-overlap",
        payload =>
        {
            JsonObject surah2 =
                FindSurah(
                    payload,
                    2);

            JsonArray blocks =
                surah2["context_blocks"]!
                    .AsArray();

            JsonObject second =
                blocks[1]!
                    .AsObject();

            second["start"] =
                second["start"]!
                    .GetValue<int>() +
                1;
        });

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "bad-boundary",
        payload =>
        {
            JsonObject surah2 =
                FindSurah(
                    payload,
                    2);

            surah2["boundaries"]!
                .AsArray()[0]!
                .AsObject()["after_ayah"] =
                    4;
        });

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "bad-macro-ref",
        payload =>
        {
            JsonObject surah2 =
                FindSurah(
                    payload,
                    2);

            surah2["macro_groups"]!
                .AsArray()[0]!
                .AsObject()["context_block_ids"]!
                .AsArray()[0] =
                    "NO-SUCH-CONTEXT";
        });

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "bad-workset-ref",
        payload =>
            payload["cross_surah_worksets"]!
                .AsArray()[0]!
                .AsObject()["members"]!
                .AsArray()[0]!
                .AsObject()["context_block_ids"]!
                .AsArray()[0] =
                    "NO-SUCH-CONTEXT");

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "bad-workset-subset-ref",
        payload =>
            payload["cross_surah_worksets"]!
                .AsArray()[16]!
                .AsObject()["subsets"]!
                .AsArray()[0]!
                .AsObject()["members"]!
                .AsArray()[0]!
                .AsObject()["context_block_ids"]!
                .AsArray()[0] =
                    "NO-SUCH-CONTEXT");

    ValidateVariantFails(
        builtIn,
        chapters,
        temp,
        "revision-missing-parent",
        payload =>
        {
            JsonObject lineage =
                payload["lineage"]!
                    .AsObject();

            lineage["kind"] =
                "revision";

            lineage["parent_corpus_id"] =
                null;

            lineage["parent_payload_sha256"] =
                null;
        });
}

static void VerifyImportIsolation(
    ProposalCorpusPackage builtIn,
    IReadOnlyList<ChapterSummary> chapters,
    string sourceBuiltIn,
    string importedRoot,
    string temp)
{
    var service =
        new ProposalCorpusImportService(
            chapters,
            sourceBuiltIn,
            importedRoot);

    string originalPath =
        Path.Combine(
            temp,
            "original.zip");

    ProbeSupport.WriteBytes(
        originalPath,
        ProbeSupport.DecodeBuiltIn(
            sourceBuiltIn));

    ProposalCorpusImportResult duplicate =
        service.Import(
            originalPath);

    ProbeSupport.Require(
        duplicate.WasAlreadyPresent &&
        duplicate.Package.IsBuiltIn,
        "Same corpus ID + same payload must dedupe as an already-present no-op.");

    byte[] collisionBytes =
        ProbeSupport.BuildVariant(
            builtIn,
            payload =>
                payload["formal_title"] =
                    "Collision probe with same identity");

    string collisionPath =
        Path.Combine(
            temp,
            "collision.zip");

    ProbeSupport.WriteBytes(
        collisionPath,
        collisionBytes);

    ProbeSupport.RequireThrows(
        () =>
            service.Import(
                collisionPath),
        "Same corpus ID + different payload must be refused.");

    Directory.CreateDirectory(
        AppPaths.DataDirectory);

    string researchSentinel =
        AppPaths.ResearchDatabase;

    string settingsSentinel =
        AppPaths.SettingsFile;

    File.WriteAllText(
        researchSentinel,
        "OWNER_RESEARCH_SECRET_71F4");

    File.WriteAllText(
        settingsSentinel,
        "OWNER_SETTINGS_SECRET_71F4");

    string researchBefore =
        ProbeSupport.Sha256File(
            researchSentinel);

    string settingsBefore =
        ProbeSupport.Sha256File(
            settingsSentinel);

    byte[] importBytes =
        ProbeSupport.BuildVariant(
            builtIn,
            payload =>
            {
                payload["corpus_id"] =
                    "thtrp-context-atlas-import-probe";

                payload["display_name"] =
                    "Context Atlas Import Probe";

                payload["edition"] =
                    2;
            });

    string importPath =
        Path.Combine(
            temp,
            "import-probe.zip");

    ProbeSupport.WriteBytes(
        importPath,
        importBytes);

    ProposalCorpusImportResult imported =
        service.Import(
            importPath);

    ProbeSupport.Require(
        !imported.WasAlreadyPresent &&
        !imported.Package.IsBuiltIn,
        "A distinct valid Proposal Corpus must be admitted as imported.");

    ProbeSupport.Require(
        ProbeSupport.Sha256File(
            importPath) ==
        ProbeSupport.Sha256File(
            imported.Package.PackagePath),
        "Accepted import must be copied byte-for-byte.");

    ProbeSupport.Require(
        ProbeSupport.Sha256File(
            researchSentinel) ==
            researchBefore &&
        ProbeSupport.Sha256File(
            settingsSentinel) ==
            settingsBefore,
        "Proposal Corpus import altered owner research/settings state.");

    string importedHash =
        ProbeSupport.Sha256File(
            imported.Package.PackagePath);

    File.Delete(
        researchSentinel);

    File.WriteAllText(
        researchSentinel,
        "SIMULATED_RESET_OR_RESTORE");

    ProbeSupport.Require(
        File.Exists(
            imported.Package.PackagePath) &&
        ProbeSupport.Sha256File(
            imported.Package.PackagePath) ==
            importedHash,
        "Research reset/restore simulation altered isolated Atlas imported bytes.");

    string portableClone =
        Path.Combine(
            temp,
            "portable-clone",
            "ContextAtlas",
            "Imported");

    ProbeSupport.CopyDirectory(
        importedRoot,
        portableClone);

    string cloned =
        Directory
            .EnumerateFiles(
                portableClone,
                "*.zip")
            .Single();

    ProbeSupport.Require(
        ProbeSupport.Sha256File(
            cloned) ==
            importedHash,
        "Whole-folder portability did not preserve imported Atlas bytes.");

    service.RemoveImported(
        imported.Package);

    ProbeSupport.Require(
        !File.Exists(
            imported.Package.PackagePath),
        "Removing an imported corpus did not remove the isolated ZIP.");

    ProbeSupport.RequireThrows(
        () =>
            service.RemoveImported(
                builtIn),
        "Built-in Proposal Corpus must be immutable/non-removable.");
}

static void VerifyReadOnlySnapshot(
    string temp)
{
    string snapshotDb =
        Path.Combine(
            temp,
            "snapshot.sqlite");

    ProbeSupport.CreateSnapshotDatabase(
        snapshotDb);

    string before =
        ProbeSupport.Sha256File(
            snapshotDb);

    var repository =
        new ContextAtlasResearchSnapshotRepository(
            snapshotDb);

    ContextAtlasResearchSnapshot snapshot =
        repository.ReadRange(
            2,
            1,
            5);

    string after =
        ProbeSupport.Sha256File(
            snapshotDb);

    ProbeSupport.Require(
        before == after,
        "Read-only Atlas research snapshot changed the SQLite file.");

    ProbeSupport.Require(
        snapshot.HasExactContextMatch &&
        snapshot.Contexts.Count == 1 &&
        snapshot.WorkingSlices.Count == 1 &&
        snapshot.WorkingSlices[0].RevisionCount == 2 &&
        snapshot.AyahNoteCount == 1 &&
        snapshot.AyahNoteRevisionCount == 1 &&
        snapshot.ContextNoteCount == 1 &&
        snapshot.ContextNoteRevisionCount == 1,
        "Atlas read-only research snapshot returned incorrect live-state summary.");
}

static void VerifyExports(
    ProposalCorpusPackage builtIn,
    IReadOnlyList<ChapterSummary> chapters)
{
    Directory.CreateDirectory(
        AppPaths.DataDirectory);

    File.WriteAllText(
        AppPaths.ResearchDatabase,
        "OWNER_RESEARCH_EXPORT_SENTINEL_71F4");

    File.WriteAllText(
        AppPaths.SettingsFile,
        "OWNER_SETTINGS_EXPORT_SENTINEL_71F4");

    if (Directory.Exists(
            AppPaths.ContextAtlasExportsDirectory))
    {
        Directory.Delete(
            AppPaths.ContextAtlasExportsDirectory,
            recursive: true);
    }

    var exporter =
        new ProposalHandoffExportService(
            chapters);

    string newZip =
        exporter.ExportNewProposalHandoff();

    IReadOnlyList<string> newEntries =
        ProbeSupport.ListEntries(
            newZip);

    ProbeSupport.Require(
        newEntries.Count == 241,
        $"Build 1.5 New Proposal Handoff must contain 241 golden-contract entries; found {newEntries.Count}.");

    ProbeSupport.Require(
        newEntries.Count(
            x =>
                x.StartsWith(
                    "evidence-arabic/",
                    StringComparison.Ordinal)) == 114 &&
        newEntries.Count(
            x =>
                x.StartsWith(
                    "clarification-english/",
                    StringComparison.Ordinal)) == 114,
        "New Proposal Handoff must contain 114 Arabic + 114 optional English files.");

    foreach (string required in new[]
    {
        "00-READ-ME-FIRST.md",
        "01-MASTER-PROMPT.md",
        "05-SOURCE-MANIFEST.json",
        "06-SURAH-INDEX.json",
        "07-JUZ-MAP.json",
        "ISLAMIC-TERMINOLOGY-STANDARD.md",
        "context-map-proposal-corpus-v1.schema.json",
        "context-map-proposal-package-v1.schema.json",
        "02-IMPORT-PACKAGE-CONTRACT.md",
        "handoff-manifest.json",
        "scripts/show_english_window.py",
        "scripts/validate_context_proposals.py",
        "scripts/validate_proposal_package.py"
    })
    {
        ProbeSupport.Require(
            newEntries.Contains(
                required,
                StringComparer.Ordinal),
            $"New Proposal Handoff missing golden entry: {required}");
    }

    ProbeSupport.Require(
        !newEntries.Any(
            x =>
                x.Contains(
                    "proposal-corpus",
                    StringComparison.OrdinalIgnoreCase) &&
                !x.EndsWith(
                    "schema.json",
                    StringComparison.OrdinalIgnoreCase)) &&
        !newEntries.Any(
            x =>
                x.Contains(
                    "research.sqlite",
                    StringComparison.OrdinalIgnoreCase) ||
                x.Contains(
                    "settings.json",
                    StringComparison.OrdinalIgnoreCase)),
        "New Proposal Handoff leaked an existing proposal or owner-state file.");

    string firstArabic =
        Encoding.UTF8.GetString(
            ProbeSupport.ReadEntry(
                newZip,
                "evidence-arabic/001-Al-Fatihah.txt"));

    ProbeSupport.Require(
        firstArabic.Contains(
            "1\t[J1 START]\t",
            StringComparison.Ordinal),
        "New Handoff Arabic evidence lost the golden Juz-start marker.");

    string firstEnglish =
        Encoding.UTF8.GetString(
            ProbeSupport.ReadEntry(
                newZip,
                "clarification-english/001-Al-Fatihah.txt"));

    ProbeSupport.Require(
        firstEnglish.Contains(
            "DO NOT read this file by default.",
            StringComparison.Ordinal) &&
        firstEnglish.Contains(
            "Saheeh International",
            StringComparison.Ordinal) &&
        firstEnglish.Contains(
            "Abdel Haleem",
            StringComparison.Ordinal),
        "Optional English clarification is not clearly gated/identified.");

    string newMasterPrompt = Encoding.UTF8.GetString(
        ProbeSupport.ReadEntry(newZip, "01-MASTER-PROMPT.md"));
    ProbeSupport.Require(
        newMasterPrompt.Contains("ABSOLUTE INDEPENDENCE", StringComparison.Ordinal) &&
        newMasterPrompt.Contains("validate_proposal_package.py", StringComparison.Ordinal) &&
        newMasterPrompt.Contains("context_block_id", StringComparison.Ordinal),
        "Build 1.5 New master prompt must enforce independence and actual import contract.");

    string packageContract = Encoding.UTF8.GetString(
        ProbeSupport.ReadEntry(newZip, "02-IMPORT-PACKAGE-CONTRACT.md"));
    ProbeSupport.Require(
        packageContract.Contains("context_block_id", StringComparison.Ordinal) &&
        packageContract.Contains("manifest.json", StringComparison.Ordinal) &&
        packageContract.Contains("proposal-corpus.json", StringComparison.Ordinal) &&
        packageContract.Contains("payload_sha256", StringComparison.Ordinal),
        "Build 1.5 final package contract is incomplete.");

    RequireNoOwnerSentinel(
        newZip);

    string improveZip =
        exporter.ExportImproveExistingHandoff(
            builtIn);

    IReadOnlyList<string> improveEntries =
        ProbeSupport.ListEntries(
            improveZip);

    ProbeSupport.Require(
        improveEntries.Count == 244,
        $"Build 1.5 Improve Existing Handoff must contain 244 golden-contract entries; found {improveEntries.Count}.");

    foreach (string required in new[]
    {
        "CHANGELOG.template.json",
        "parent-proposal-corpus/manifest.json",
        "parent-proposal-corpus/proposal-corpus.json"
    })
    {
        ProbeSupport.Require(
            improveEntries.Contains(
                required,
                StringComparer.Ordinal),
            $"Improve Existing Handoff missing golden entry: {required}");
    }

    ProbeSupport.Require(
        ProbeSupport.ReadEntry(
            improveZip,
            "parent-proposal-corpus/manifest.json")
            .SequenceEqual(
                builtIn.ManifestBytes) &&
        ProbeSupport.ReadEntry(
            improveZip,
            "parent-proposal-corpus/proposal-corpus.json")
            .SequenceEqual(
                builtIn.PayloadBytes),
        "Improve Existing Handoff did not embed the exact selected parent bytes.");

    using JsonDocument manifest =
        JsonDocument.Parse(
            ProbeSupport.ReadEntry(
                improveZip,
                "handoff-manifest.json"));

    ProbeSupport.Require(
        manifest.RootElement
            .GetProperty(
                "parent_corpus_id")
            .GetString() ==
            builtIn.Corpus.CorpusId &&
        manifest.RootElement
            .GetProperty(
                "parent_payload_sha256")
            .GetString() ==
            builtIn.PayloadSha256,
        "Improve Existing Handoff did not record exact parent identity/hash.");

    string prompt =
        Encoding.UTF8.GetString(
            ProbeSupport.ReadEntry(
                improveZip,
                "01-MASTER-PROMPT.md"));

    foreach (string required in new[]
    {
        "new corpus ID",
        "new edition/date",
        "model/provider/reasoning mode",
        "lineage.kind = revision",
        builtIn.Corpus.CorpusId,
        builtIn.PayloadSha256
    })
    {
        ProbeSupport.Require(
            prompt.Contains(
                required,
                StringComparison.Ordinal),
            $"Improve prompt lost revision-output requirement: {required}");
    }

    string changeLog =
        Encoding.UTF8.GetString(
            ProbeSupport.ReadEntry(
                improveZip,
                "CHANGELOG.template.json"));

    ProbeSupport.Require(
        changeLog.Contains(
            builtIn.Corpus.CorpusId,
            StringComparison.Ordinal) &&
        changeLog.Contains(
            "boundary_added",
            StringComparison.Ordinal) &&
        changeLog.Contains(
            "related_workset_revised",
            StringComparison.Ordinal),
        "Improve change-log template does not match the golden revision contract.");

    RequireNoOwnerSentinel(
        improveZip);
}

static void RequireNoOwnerSentinel(
    string zipPath)
{
    using var archive =
        ZipFile.OpenRead(
            zipPath);

    foreach (ZipArchiveEntry entry
             in archive.Entries)
    {
        if (entry.Length == 0)
        {
            continue;
        }

        using Stream stream =
            entry.Open();

        using var memory =
            new MemoryStream();

        stream.CopyTo(
            memory);

        string text =
            Encoding.UTF8.GetString(
                memory.ToArray());

        ProbeSupport.Require(
            !text.Contains(
                "OWNER_RESEARCH",
                StringComparison.Ordinal) &&
            !text.Contains(
                "OWNER_SETTINGS",
                StringComparison.Ordinal),
            $"Export leaked owner-state sentinel through {entry.FullName}.");
    }
}

static void ValidateVariantFails(
    ProposalCorpusPackage builtIn,
    IReadOnlyList<ChapterSummary> chapters,
    string temp,
    string label,
    Action<JsonObject>? mutatePayload,
    Action<JsonObject>? mutateManifest = null,
    bool syncHash = true)
{
    byte[] bytes =
        ProbeSupport.BuildVariant(
            builtIn,
            mutatePayload,
            mutateManifest,
            syncManifestIdentity: true,
            syncPayloadHash: syncHash);

    string path =
        Path.Combine(
            temp,
            label + ".zip");

    ProbeSupport.WriteBytes(
        path,
        bytes);

    ProbeSupport.RequireThrows(
        () =>
            ProposalCorpusValidator
                .LoadAndValidate(
                    path,
                    chapters,
                    false),
        $"Invalid package '{label}' must be refused.");
}

static JsonObject FindSurah(
    JsonObject payload,
    int number) =>
    ProbeSupport
        .Surahs(
            payload)
        .Select(
            x =>
                x!.AsObject())
        .Single(
            x =>
                x["surah_number"]!
                    .GetValue<int>() ==
                number);

static void PrepareRuntimeCorpus(
    string corpusDatabase,
    string sourceJuz,
    string sourceTemplates)
{
    string corpusDirectory =
        AppPaths.CorpusDirectory;

    Directory.CreateDirectory(
        corpusDirectory);

    File.Copy(
        corpusDatabase,
        AppPaths.CorpusDatabase,
        overwrite: true);

    File.Copy(
        sourceJuz,
        AppPaths.JuzMapFile,
        overwrite: true);

    if (Directory.Exists(
            AppPaths.ContextAtlasHandoffTemplatesDirectory))
    {
        Directory.Delete(
            AppPaths.ContextAtlasHandoffTemplatesDirectory,
            recursive: true);
    }

    ProbeSupport.CopyDirectory(
        sourceTemplates,
        AppPaths.ContextAtlasHandoffTemplatesDirectory);
}

static string GetArg(
    string[] args,
    string name)
{
    for (int index = 0;
         index < args.Length - 1;
         index++)
    {
        if (string.Equals(
                args[index],
                name,
                StringComparison.Ordinal))
        {
            return args[index + 1];
        }
    }

    throw new ArgumentException(
        $"Missing required argument: {name}");
}

static void VerifyTransientReadingPreview(string corpusDatabase)
{
    var reader = new ContextAtlasVersePreviewReader(corpusDatabase);
    string before = ProbeSupport.Sha256File(corpusDatabase);

    // Precise ayah bounds, no whole-chapter preloading and two local source choices.
    var first = reader.ReadRange(2, 1, 5, 20, 161);
    ProbeSupport.Require(first.Count == 5 &&
        first[0].VerseKey == "2:1" &&
        first[^1].VerseKey == "2:5" &&
        first.All(x => !string.IsNullOrWhiteSpace(x.Uthmani)) &&
        first.Any(x => !string.IsNullOrWhiteSpace(x.English)) &&
        first.Any(x => !string.IsNullOrWhiteSpace(x.Bengali)),
        "Transient Atlas reading preview failed canonical bounded bilingual load.");

    var next = reader.ReadRange(2, 6, 10, -1, -1);
    ProbeSupport.Require(next.Count == 5 &&
        next[0].VerseNumber == 6 &&
        next[^1].VerseNumber == 10 &&
        next.All(x => x.English is null && x.Bengali is null),
        "Neighbour Context reading preview failed source-free selection.");

    // Independent queries can overlap; results are not dependent on UI/saved drafts.
    var concurrent = Task.WhenAll(
        Task.Run(() => reader.ReadRange(1, 1, 7, 20, 161)),
        Task.Run(() => reader.ReadRange(2, 11, 20, 20, 161)))
        .GetAwaiter().GetResult();
    ProbeSupport.Require(concurrent[0].Count == 7 &&
        concurrent[1].Count == 10,
        "Concurrent reading-range requests failed.");

    ProbeSupport.RequireThrows(
        () => reader.ReadRange(2, 10, 9, 20, 161),
        "An invalid preview range must be refused.");

    ProbeSupport.Require(before == ProbeSupport.Sha256File(corpusDatabase),
        "Transient Atlas Quran preview unexpectedly changed canonical SQLite.");
    Console.WriteLine("Context Atlas transient reading preview: PASS");
}
