using Microsoft.Data.Sqlite;
using QuranReconciliation.Models;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuranReconciliation.Infrastructure;

internal sealed class ProposalHandoffExportService
{
    private const int SaheehInternationalId = 20;
    private const int AbdelHaleemId = 85;

    private readonly IReadOnlyList<ChapterSummary> _chapters;
    private readonly JuzRepository _juz;
    private readonly CorpusRepository _corpus;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    internal ProposalHandoffExportService(
        IReadOnlyList<ChapterSummary> chapters)
    {
        _chapters = chapters;
        _juz = new JuzRepository(chapters);
        _corpus = new CorpusRepository();
    }

    internal string ExportNewProposalHandoff()
    {
        string staging =
            CreateStagingDirectory();

        try
        {
            PopulateCommonEvidence(
                staging);

            CopyTemplate(
                "New/00-READ-ME-FIRST.md",
                staging,
                "00-READ-ME-FIRST.md");

            CopyTemplate(
                "New/01-MASTER-PROMPT.md",
                staging,
                "01-MASTER-PROMPT.md");

            WriteJson(
                Path.Combine(
                    staging,
                    "handoff-manifest.json"),
                new
                {
                    package_schema =
                        "thtrp.context-map-proposal-handoff",
                    package_schema_version = 1,
                    package_type =
                        "new_proposal_handoff",
                    date =
                        DateTimeOffset.UtcNow
                            .ToString("yyyy-MM-dd"),
                    proposal_schema =
                        "thtrp.context-map-proposal-corpus/1",
                    contains_parent_proposal =
                        false,
                    evidence_policy =
                        "Arabic-first; English only for exceptional local clarification"
                });

            return FinishExport(
                staging,
                "THTRP-New-Proposal-Handoff");
        }
        catch
        {
            TryDeleteDirectory(
                staging);
            throw;
        }
    }

    internal string ExportImproveExistingHandoff(
        ProposalCorpusPackage parent)
    {
        string staging =
            CreateStagingDirectory();

        try
        {
            PopulateCommonEvidence(
                staging);

            File.WriteAllText(
                Path.Combine(
                    staging,
                    "00-READ-ME-FIRST.md"),
                BuildImproveReadMe(
                    parent),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            File.WriteAllText(
                Path.Combine(
                    staging,
                    "01-MASTER-PROMPT.md"),
                BuildImprovePrompt(
                    parent),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            string parentDirectory =
                Path.Combine(
                    staging,
                    "parent-proposal-corpus");

            Directory.CreateDirectory(
                parentDirectory);

            File.WriteAllBytes(
                Path.Combine(
                    parentDirectory,
                    "manifest.json"),
                parent.ManifestBytes);

            File.WriteAllBytes(
                Path.Combine(
                    parentDirectory,
                    "proposal-corpus.json"),
                parent.PayloadBytes);

            WriteJson(
                Path.Combine(
                    staging,
                    "CHANGELOG.template.json"),
                new
                {
                    parent_corpus_id =
                        parent.Corpus.CorpusId,
                    new_corpus_id =
                        "<new corpus id>",
                    changes =
                        new object[]
                        {
                            new
                            {
                                type =
                                    "<boundary_added|boundary_removed|boundary_moved|context_rationale_revised|macro_group_split|macro_group_merged|macro_heading_revised|related_workset_revised>",
                                surah_number = 2,
                                old =
                                    "<old representation>",
                                @new =
                                    "<new representation>",
                                reason =
                                    "<concise Arabic-first reason>"
                            }
                        }
                });

            WriteJson(
                Path.Combine(
                    staging,
                    "handoff-manifest.json"),
                new
                {
                    package_schema =
                        "thtrp.context-map-proposal-handoff",
                    package_schema_version = 1,
                    package_type =
                        "proposal_revision_handoff",
                    date =
                        DateTimeOffset.UtcNow
                            .ToString("yyyy-MM-dd"),
                    proposal_schema =
                        "thtrp.context-map-proposal-corpus/1",
                    contains_parent_proposal =
                        true,
                    parent_corpus_id =
                        parent.Corpus.CorpusId,
                    parent_payload_sha256 =
                        parent.PayloadSha256,
                    evidence_policy =
                        "Arabic-first; English only for exceptional local clarification"
                });

            return FinishExport(
                staging,
                $"THTRP-Improve-Proposal-Handoff-E{parent.Corpus.Edition}");
        }
        catch
        {
            TryDeleteDirectory(
                staging);
            throw;
        }
    }

    private void PopulateCommonEvidence(
        string staging)
    {
        CopyTemplate(
            "Common/ISLAMIC-TERMINOLOGY-STANDARD.md",
            staging,
            "ISLAMIC-TERMINOLOGY-STANDARD.md");

        CopyTemplate(
            "Common/context-map-proposal-corpus-v1.schema.json",
            staging,
            "context-map-proposal-corpus-v1.schema.json");

        CopyTemplate(
            "Common/scripts/show_english_window.py",
            staging,
            "scripts/show_english_window.py");

        CopyTemplate(
            "Common/scripts/validate_context_proposals.py",
            staging,
            "scripts/validate_context_proposals.py");

        // Build 1.5: exact package guidance and a standalone ZIP preflight.
        // Both New and Improve exports carry these identical contracts.
        CopyTemplate(
            "Common/scripts/validate_proposal_package.py",
            staging,
            "scripts/validate_proposal_package.py");

        CopyTemplate(
            "Common/context-map-proposal-package-v1.schema.json",
            staging,
            "context-map-proposal-package-v1.schema.json");

        CopyTemplate(
            "Common/02-IMPORT-PACKAGE-CONTRACT.md",
            staging,
            "02-IMPORT-PACKAGE-CONTRACT.md");

        Directory.CreateDirectory(
            Path.Combine(
                staging,
                "evidence-arabic"));

        Directory.CreateDirectory(
            Path.Combine(
                staging,
                "clarification-english"));

        IReadOnlyDictionary<int, string> arabicNames =
            ReadArabicSurahNames();

        var index =
            new List<object>(
                114);

        foreach (ChapterSummary chapter
                 in _chapters)
        {
            string slug =
                MakeSlug(
                    chapter.NameSimple);

            string arabicRelative =
                $"evidence-arabic/{chapter.Number:000}-{slug}.txt";

            string englishRelative =
                $"clarification-english/{chapter.Number:000}-{slug}.txt";

            WriteArabicEvidence(
                staging,
                chapter,
                arabicNames[
                    chapter.Number],
                arabicRelative);

            WriteEnglishClarification(
                staging,
                chapter,
                englishRelative);

            IReadOnlyList<int> juzSpan =
                Enumerable.Range(
                        1,
                        30)
                    .Where(
                        number =>
                        {
                            JuzBoundary juz =
                                _juz.Get(
                                    number);

                            return RangeIntersectsSurah(
                                juz,
                                chapter.Number);
                        })
                    .ToList();

            IReadOnlyList<object> starts =
                _juz.GetAll()
                    .Where(
                        x =>
                            x.StartSurah ==
                            chapter.Number)
                    .Select(
                        x =>
                            (object)new
                            {
                                juz =
                                    x.Number,
                                ayah =
                                    x.StartAyah,
                                verse_key =
                                    x.StartVerseKey
                            })
                    .ToList();

            index.Add(
                new
                {
                    surah_number =
                        chapter.Number,
                    name_simple =
                        chapter.NameSimple,
                    name_arabic =
                        arabicNames[
                            chapter.Number],
                    verses_count =
                        chapter.VersesCount,
                    arabic_file =
                        arabicRelative,
                    english_clarification_file =
                        englishRelative,
                    juz_span =
                        juzSpan,
                    juz_starts_inside_surah =
                        starts
                });
        }

        WriteJson(
            Path.Combine(
                staging,
                "06-SURAH-INDEX.json"),
            index);

        File.Copy(
            AppPaths.JuzMapFile,
            Path.Combine(
                staging,
                "07-JUZ-MAP.json"),
            overwrite: true);

        WriteJson(
            Path.Combine(
                staging,
                "05-SOURCE-MANIFEST.json"),
            new
            {
                pack =
                    "The Holy Quran TRP — Context Proposal Research Handoff",
                date =
                    DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
                purpose =
                    "114-Surah Arabic-first independent or parent-revision research",
                primary_evidence =
                    "canonical Uthmani Arabic only",
                optional_english_clarification =
                    new object[]
                    {
                        new
                        {
                            resource_id =
                                SaheehInternationalId,
                            name =
                                "Saheeh International",
                            role =
                                "close/explicit clarification"
                        },
                        new
                        {
                            resource_id =
                                AbdelHaleemId,
                            name =
                                "M.A.S. Abdel Haleem",
                            role =
                                "discourse-flow clarification"
                        }
                    },
                explicitly_excluded =
                    new[]
                    {
                        "Bengali translations",
                        "tafsir/commentary",
                        "web research",
                        "all-translation comparison"
                    },
                source_identity_note =
                    "Pinned corpus/Juz hashes identify the canonical evidence, not any earlier proposal.",
                corpus_sha256 =
                    "188e29730b62efaff748e160f73bb2e6665769df4fa779ee1436af939c6e7862",
                juz_map_sha256 =
                    "3016bb4f94af9a42c8c1c24728bf6044e5b4ac13130e47d451c0c10ba35dd434",
                surahs = 114,
                ayat = 6236,
                workstation_baseline =
                    new
                    {
                        version =
                            "v1.4 R2 R1 (accepted predecessor of Build 1.5)",
                        sha =
                            "456fc58f1bcd00f655f10e8afec1416e809af3ce",
                        accepted_branch =
                            "accepted/v1.4-2026-10-08"
                    }
            });
    }

    private void WriteArabicEvidence(
        string staging,
        ChapterSummary chapter,
        string arabicName,
        string relativePath)
    {
        IReadOnlyList<VerseBundle> verses =
            _corpus.GetChapter(
                chapter.Number,
                Array.Empty<int>(),
                Array.Empty<int>());

        var text =
            new StringBuilder();

        text.AppendLine(
            $"# Surah {chapter.Number} — {chapter.NameSimple} — {arabicName}");

        text.AppendLine(
            $"# Ayat: {chapter.VersesCount}");

        text.AppendLine(
            "# PRIMARY EVIDENCE: canonical Uthmani Arabic. Juz markers are metadata only.");

        text.AppendLine();

        foreach (VerseBundle verse
                 in verses)
        {
            int juz =
                _juz.GetJuzNumber(
                    chapter.Number,
                    verse.VerseNumber);

            bool juzStart =
                _juz.IsJuzStart(
                    chapter.Number,
                    verse.VerseNumber,
                    out _);

            text.Append(
                verse.VerseNumber);

            text.Append(
                "\t[J");

            text.Append(
                juz);

            if (juzStart)
            {
                text.Append(
                    " START");
            }

            text.Append(
                "]\t");

            text.AppendLine(
                verse.Uthmani);
        }

        WriteUtf8(
            Path.Combine(
                staging,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)),
            text.ToString());
    }

    private void WriteEnglishClarification(
        string staging,
        ChapterSummary chapter,
        string relativePath)
    {
        IReadOnlyList<VerseBundle> verses =
            _corpus.GetChapter(
                chapter.Number,
                new[]
                {
                    SaheehInternationalId,
                    AbdelHaleemId
                },
                Array.Empty<int>());

        var text =
            new StringBuilder();

        text.AppendLine(
            $"# OPTIONAL ENGLISH CLARIFICATION — Surah {chapter.Number} — {chapter.NameSimple}");

        text.AppendLine(
            "# DO NOT read this file by default.");

        text.AppendLine(
            "# Consult only a small ayah window when an Arabic boundary remains genuinely ambiguous.");

        text.AppendLine(
            "# SI = Saheeh International (resource 20)");

        text.AppendLine(
            "# AH = M.A.S. Abdel Haleem (resource 85)");

        text.AppendLine();

        foreach (VerseBundle verse
                 in verses)
        {
            string si =
                verse.Translations
                    .FirstOrDefault(
                        x =>
                            x.ResourceId ==
                            SaheehInternationalId)
                    ?.Text ??
                string.Empty;

            string ah =
                verse.Translations
                    .FirstOrDefault(
                        x =>
                            x.ResourceId ==
                            AbdelHaleemId)
                    ?.Text ??
                string.Empty;

            text.Append(
                verse.VerseNumber);

            text.Append(
                "\tSI: ");

            text.Append(
                NormalizeOneLine(
                    si));

            text.Append(
                "\tAH: ");

            text.AppendLine(
                NormalizeOneLine(
                    ah));
        }

        WriteUtf8(
            Path.Combine(
                staging,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)),
            text.ToString());
    }

    private static string NormalizeOneLine(
        string value) =>
        Regex.Replace(
            WebUtility.HtmlDecode(
                    value ?? string.Empty)
                .Replace(
                    '\r',
                    ' ')
                .Replace(
                    '\n',
                    ' ')
                .Replace(
                    '\t',
                    ' '),
            @"\s{2,}",
            " ")
        .Trim();

    private static bool RangeIntersectsSurah(
        JuzBoundary juz,
        int surahNumber) =>
        juz.StartSurah <=
            surahNumber &&
        juz.EndSurah >=
            surahNumber;

    private IReadOnlyDictionary<int, string>
        ReadArabicSurahNames()
    {
        var result =
            new Dictionary<int, string>();

        using var connection =
            new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource =
                        AppPaths.CorpusDatabase,
                    Mode =
                        SqliteOpenMode.ReadOnly,
                    Cache =
                        SqliteCacheMode.Private,
                    Pooling =
                        false
                }.ToString());

        connection.Open();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
        """
        SELECT chapter_number, name_arabic
        FROM chapters
        ORDER BY chapter_number;
        """;

        using SqliteDataReader reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            result[
                reader.GetInt32(0)] =
                reader.IsDBNull(1)
                    ? string.Empty
                    : reader.GetString(1);
        }

        if (result.Count != 114)
        {
            throw new InvalidDataException(
                $"Handoff export requires 114 Arabic Surah names; found {result.Count}.");
        }

        return result;
    }

    private static string BuildImproveReadMe(
        ProposalCorpusPackage parent) =>
$"""
# Improve Existing Context Proposal Handoff — Build 1.5

Parent proposal:
{parent.Corpus.DisplayName}

Review and improve the selected parent corpus. Preserve sound decisions. Change only where the canonical Arabic materially supports a better segmentation, Macro grouping, heading, rationale, or Related Workset.

The result must be a **new** Proposal Corpus with explicit parent lineage and a change log.
""";

    private static string BuildImprovePrompt(
        ProposalCorpusPackage parent) =>
$"""
# Frontier Model Master Prompt — Improve Existing Proposal Corpus

Parent corpus ID:

`{parent.Corpus.CorpusId}`

Parent payload SHA-256:

`{parent.PayloadSha256}`

Read the canonical Arabic first when adjudicating any proposed change.

The existing Proposal Corpus is a serious prior proposal, not a target that must be rewritten.

Preserve sound decisions. Change only where a materially stronger Arabic-first analysis justifies it.

Do not use Bengali, tafsir/commentary, or web research unless a future owner explicitly creates a different methodology.

Optional English clarification remains local and exceptional.

Use THTRP Islamic Terminology v1 for all newly written headings/rationale.

Deliver a final **import-ready ZIP** with precisely `manifest.json` and
`proposal-corpus.json` at its root, plus a SEPARATE research continuity ZIP
containing `CHANGELOG.json`, evidence logs and Surah-level checkpoints.

Read `02-IMPORT-PACKAGE-CONTRACT.md`, BOTH shipped JSON Schemas and the
application-native field names, including `context_block_id`,
`macro_group_id`, `context_block_ids` and `importer_ranges`.

Validate the final package using:
`python scripts/validate_proposal_package.py FINAL-IMPORT.zip`
The old per-Surah validator is necessary but NOT sufficient.
Schema validation alone is not actual Windows-import compatibility.
Never claim live Windows acceptance without a real Windows import.

The new corpus MUST:
- have a new corpus ID;
- have a new edition/date;
- identify your model/provider/reasoning mode;
- set `lineage.kind = revision`;
- set `parent_corpus_id = {parent.Corpus.CorpusId}`;
- set `parent_payload_sha256 = {parent.PayloadSha256}`;
- preserve unchanged source material where no improvement is justified.

Work one Surah at a time with persistent state and a truthful change log.
Do not compare against any unrelated Proposal Corpus.
Do not overwrite or mutate the parent corpus.
""";

    private static string CreateStagingDirectory()
    {
        Directory.CreateDirectory(
            AppPaths.ContextAtlasExportsDirectory);

        string path =
            Path.Combine(
                AppPaths.ContextAtlasExportsDirectory,
                ".staging-" +
                Guid.NewGuid()
                    .ToString("N"));

        Directory.CreateDirectory(
            path);

        return path;
    }

    private static string FinishExport(
        string staging,
        string prefix)
    {
        string output =
            Path.Combine(
                AppPaths.ContextAtlasExportsDirectory,
                $"{prefix}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip");

        if (File.Exists(
                output))
        {
            throw new IOException(
                $"Export target already exists: {output}");
        }

        ZipFile.CreateFromDirectory(
            staging,
            output,
            CompressionLevel.Optimal,
            includeBaseDirectory: false);

        TryDeleteDirectory(
            staging);

        return output;
    }

    private static void CopyTemplate(
        string templateRelative,
        string staging,
        string outputRelative)
    {
        string source =
            Path.Combine(
                AppPaths.ContextAtlasHandoffTemplatesDirectory,
                templateRelative.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        if (!File.Exists(
                source))
        {
            throw new FileNotFoundException(
                "Context Atlas handoff template is missing.",
                source);
        }

        string target =
            Path.Combine(
                staging,
                outputRelative.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        string? directory =
            Path.GetDirectoryName(
                target);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        File.Copy(
            source,
            target,
            overwrite: true);
    }

    private static void WriteJson(
        string path,
        object value)
    {
        string json =
            JsonSerializer.Serialize(
                value,
                JsonOptions);

        WriteUtf8(
            path,
            json +
            Environment.NewLine);
    }

    private static void WriteUtf8(
        string path,
        string text)
    {
        string? directory =
            Path.GetDirectoryName(
                path);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        File.WriteAllText(
            path,
            text,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));
    }

    private static string MakeSlug(
        string name) =>
        Regex.Replace(
            name,
            @"[^A-Za-z0-9]+",
            "-")
        .Trim('-');

    private static void TryDeleteDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(
                    path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
            }
        }
        catch
        {
            // A finished ZIP is authoritative; stale staging is harmless
            // and remains isolated under ContextAtlas/Exports.
        }
    }
}
