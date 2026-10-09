using QuranReconciliation.Models;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace QuranReconciliation.Infrastructure;

internal static class ProposalCorpusValidator
{
    internal const string CorpusSchema =
        "thtrp.context-map-proposal-corpus";

    internal const int CorpusSchemaVersion = 1;

    internal const string PackageSchema =
        "thtrp.context-map-proposal-package";

    internal const int PackageSchemaVersion = 1;

    // Untrusted imported ZIP resource ceilings. Both compressed and expanded
    // streams are bounded; decompressed limits are enforced while reading.
    internal const long MaxPackageBytes = 32L * 1024 * 1024;
    internal const long MaxManifestBytes = 1024 * 1024;
    internal const long MaxPayloadBytes = 64L * 1024 * 1024;
    internal const int MaxArchiveEntries = 32;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    internal static ProposalCorpusPackage LoadAndValidate(
        string packagePath,
        IReadOnlyList<ChapterSummary> chapters,
        bool isBuiltIn)
    {
        string fullPath =
            Path.GetFullPath(packagePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Context Atlas proposal package is missing.",
                fullPath);
        }

        if (new FileInfo(fullPath).Length > MaxPackageBytes)
        {
            throw new InvalidDataException(
                "Context Atlas proposal ZIP exceeds the 32 MiB input limit.");
        }

        return LoadAndValidateBytes(
            File.ReadAllBytes(fullPath),
            fullPath,
            chapters,
            isBuiltIn);
    }

    internal static ProposalCorpusPackage LoadAndValidateBytes(
        byte[] packageBytes,
        string packageIdentity,
        IReadOnlyList<ChapterSummary> chapters,
        bool isBuiltIn)
    {
        if (packageBytes.Length == 0)
        {
            throw new InvalidDataException(
                "Context Atlas proposal package is empty.");
        }

        if (packageBytes.LongLength > MaxPackageBytes)
        {
            throw new InvalidDataException(
                "Context Atlas proposal ZIP exceeds the 32 MiB input limit.");
        }

        string packageSha =
            Sha256(packageBytes);

        byte[] manifestBytes;
        byte[] payloadBytes;

        try
        {
            using var stream =
                new MemoryStream(
                    packageBytes,
                    writable: false);

            using var archive =
                new ZipArchive(
                    stream,
                    ZipArchiveMode.Read,
                    leaveOpen: false);

            if (archive.Entries.Count > MaxArchiveEntries)
            {
                throw new InvalidDataException(
                    "Context Atlas proposal ZIP contains too many entries.");
            }

            ZipArchiveEntry manifestEntry =
                RequireEntry(
                    archive,
                    "manifest.json");

            manifestBytes =
                ReadEntry(
                    manifestEntry,
                    MaxManifestBytes);

            ProposalPackageManifest manifest =
                Deserialize<ProposalPackageManifest>(
                    manifestBytes,
                    "manifest.json");

            if (string.IsNullOrWhiteSpace(
                    manifest.PayloadFile))
            {
                throw new InvalidDataException(
                    "Proposal package manifest is missing payload_file.");
            }

            ZipArchiveEntry payloadEntry =
                RequireEntry(
                    archive,
                    manifest.PayloadFile);

            payloadBytes =
                ReadEntry(
                    payloadEntry,
                    MaxPayloadBytes);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                "Proposal package ZIP is corrupt or unreadable.",
                ex);
        }

        ProposalPackageManifest packageManifest =
            Deserialize<ProposalPackageManifest>(
                manifestBytes,
                "manifest.json");

        ProposalCorpusDocument corpus =
            Deserialize<ProposalCorpusDocument>(
                payloadBytes,
                packageManifest.PayloadFile);

        ValidatePackageManifest(
            packageManifest,
            corpus,
            payloadBytes);

        ValidateCorpus(
            corpus,
            chapters);

        return new ProposalCorpusPackage(
            packageIdentity,
            packageSha,
            Sha256(payloadBytes),
            packageManifest,
            corpus,
            manifestBytes,
            payloadBytes,
            isBuiltIn);
    }

    internal static void ValidateCorpus(
        ProposalCorpusDocument corpus,
        IReadOnlyList<ChapterSummary> chapters)
    {
        if (!string.Equals(
                corpus.Schema,
                CorpusSchema,
                StringComparison.Ordinal) ||
            corpus.SchemaVersion !=
                CorpusSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported proposal corpus schema: {corpus.Schema} v{corpus.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(
                corpus.CorpusId) ||
            string.IsNullOrWhiteSpace(
                corpus.DisplayName) ||
            corpus.Edition < 1 ||
            string.IsNullOrWhiteSpace(
                corpus.PublishedDate) ||
            corpus.Contributors.Count == 0 ||
            corpus.Contributors.Any(
                x =>
                    string.IsNullOrWhiteSpace(
                        x.ModelName) ||
                    string.IsNullOrWhiteSpace(
                        x.Provider) ||
                    string.IsNullOrWhiteSpace(
                        x.Role)))
        {
            throw new InvalidDataException(
                "Proposal corpus identity/model/date metadata is incomplete.");
        }

        if (corpus.Terminology.ValueKind != JsonValueKind.Object ||
            corpus.SourceIdentity.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Proposal corpus terminology/source identity metadata is incomplete.");
        }

        if (string.Equals(
                corpus.Lineage.Kind,
                "revision",
                StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(
                 corpus.Lineage.ParentCorpusId) ||
             string.IsNullOrWhiteSpace(
                 corpus.Lineage.ParentPayloadSha256)))
        {
            throw new InvalidDataException(
                "Revision proposal corpus is missing complete parent lineage.");
        }

        if (chapters.Count != 114 ||
            corpus.Surahs.Count != 114)
        {
            throw new InvalidDataException(
                "Proposal corpus must contain exactly 114 Surahs.");
        }

        var blockIds =
            new HashSet<string>(
                StringComparer.Ordinal);

        int totalAyat = 0;
        int totalBlocks = 0;
        int totalBoundaries = 0;
        int totalMacros = 0;

        for (int number = 1;
             number <= 114;
             number++)
        {
            ProposalSurah surah =
                corpus.Surahs.SingleOrDefault(
                    x =>
                        x.SurahNumber == number)
                ?? throw new InvalidDataException(
                    $"Proposal corpus is missing Surah {number}.");

            ChapterSummary chapter =
                chapters[number - 1];

            if (chapter.Number != number ||
                surah.VersesCount !=
                    chapter.VersesCount)
            {
                throw new InvalidDataException(
                    $"Surah {number} verse-count mismatch. Expected {chapter.VersesCount}, found {surah.VersesCount}.");
            }

            ValidateSurah(
                surah,
                blockIds);

            totalAyat +=
                surah.VersesCount;

            totalBlocks +=
                surah.ContextBlocks.Count;

            totalBoundaries +=
                surah.Boundaries.Count;

            totalMacros +=
                surah.MacroGroups.Count;
        }

        ValidateWorksets(
            corpus.CrossSurahWorksets,
            corpus.Surahs,
            blockIds);

        if (totalAyat != 6236)
        {
            throw new InvalidDataException(
                $"Proposal corpus must cover the canonical 6,236 ayat, found {totalAyat}.");
        }

        if (corpus.Statistics.SurahsAudited != 114 ||
            corpus.Statistics.AyatCovered != totalAyat ||
            corpus.Statistics.TotalContextBlocks != totalBlocks ||
            corpus.Statistics.TotalInternalBoundaries != totalBoundaries ||
            corpus.Statistics.MacroGroups != totalMacros ||
            corpus.Statistics.CrossSurahWorksets !=
                corpus.CrossSurahWorksets.Count)
        {
            throw new InvalidDataException(
                "Proposal corpus statistics do not match the validated payload.");
        }
    }

    private static void ValidatePackageManifest(
        ProposalPackageManifest manifest,
        ProposalCorpusDocument corpus,
        byte[] payloadBytes)
    {
        if (!string.Equals(
                manifest.PackageSchema,
                PackageSchema,
                StringComparison.Ordinal) ||
            manifest.PackageSchemaVersion !=
                PackageSchemaVersion ||
            !string.Equals(
                manifest.PackageType,
                "proposal_corpus",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Unsupported Context Atlas proposal package schema/type.");
        }

        if (!string.Equals(
                manifest.PayloadFile,
                "proposal-corpus.json",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Proposal package payload_file must be proposal-corpus.json.");
        }

        string actualPayloadHash =
            Sha256(payloadBytes);

        if (!string.Equals(
                manifest.PayloadSha256,
                actualPayloadHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Proposal package payload SHA-256 mismatch. Expected {manifest.PayloadSha256}, got {actualPayloadHash}.");
        }

        if (!string.Equals(
                manifest.CorpusId,
                corpus.CorpusId,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.DisplayName,
                corpus.DisplayName,
                StringComparison.Ordinal) ||
            manifest.Edition !=
                corpus.Edition ||
            !string.Equals(
                manifest.PublishedDate,
                corpus.PublishedDate,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Proposal package manifest identity does not match proposal-corpus.json.");
        }

        if (string.IsNullOrWhiteSpace(
                manifest.TerminologyStandard))
        {
            throw new InvalidDataException(
                "Proposal package does not declare its terminology standard.");
        }
    }

    private static void ValidateSurah(
        ProposalSurah surah,
        HashSet<string> allBlockIds)
    {
        if (surah.ContextBlocks.Count == 0)
        {
            throw new InvalidDataException(
                $"Surah {surah.SurahNumber} has no Context Blocks.");
        }

        int nextExpected = 1;

        foreach (ProposalContextBlock block
                 in surah.ContextBlocks)
        {
            if (block.StartAyah != nextExpected ||
                block.EndAyah <
                    block.StartAyah ||
                block.EndAyah >
                    surah.VersesCount)
            {
                throw new InvalidDataException(
                    $"Surah {surah.SurahNumber} Context Blocks contain a gap, overlap, or out-of-range span at {block.StartAyah}–{block.EndAyah}.");
            }

            if (string.IsNullOrWhiteSpace(
                    block.ContextBlockId) ||
                !allBlockIds.Add(
                    block.ContextBlockId))
            {
                throw new InvalidDataException(
                    $"Duplicate or missing Context Block ID in Surah {surah.SurahNumber}.");
            }

            string canonicalRange =
                Range(
                    block.StartAyah,
                    block.EndAyah);

            if (!string.Equals(
                    block.Range,
                    canonicalRange,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Context Block {block.ContextBlockId} range metadata does not match its ayah endpoints.");
            }

            nextExpected =
                block.EndAyah + 1;
        }

        if (nextExpected !=
            surah.VersesCount + 1)
        {
            throw new InvalidDataException(
                $"Surah {surah.SurahNumber} Context Blocks do not cover the final ayah.");
        }

        if (surah.Boundaries.Count !=
            surah.ContextBlocks.Count - 1)
        {
            throw new InvalidDataException(
                $"Surah {surah.SurahNumber} boundary count does not equal Context Blocks minus one.");
        }

        for (int index = 0;
             index < surah.Boundaries.Count;
             index++)
        {
            ProposalContextBlock left =
                surah.ContextBlocks[index];

            ProposalContextBlock right =
                surah.ContextBlocks[index + 1];

            ProposalBoundary boundary =
                surah.Boundaries[index];

            if (boundary.AfterAyah !=
                    left.EndAyah ||
                boundary.NextAyah !=
                    right.StartAyah ||
                boundary.NextAyah !=
                    boundary.AfterAyah + 1 ||
                string.IsNullOrWhiteSpace(
                    boundary.Reason))
            {
                throw new InvalidDataException(
                    $"Surah {surah.SurahNumber} boundary {boundary.BoundaryId} is not aligned to neighboring Context Blocks.");
            }
        }

        IReadOnlyList<string> ranges =
            surah.ContextBlocks
                .Select(
                    x =>
                        Range(
                            x.StartAyah,
                            x.EndAyah))
                .ToList();

        if (!surah.ImporterRanges.SequenceEqual(
                ranges,
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Surah {surah.SurahNumber} importer_ranges do not match the validated Context Blocks.");
        }

        ValidateMacroGroups(
            surah);
    }

    private static void ValidateMacroGroups(
        ProposalSurah surah)
    {
        if (surah.MacroGroups.Count == 0)
        {
            throw new InvalidDataException(
                $"Surah {surah.SurahNumber} has no Macro Groups.");
        }

        int blockCursor = 0;

        foreach (ProposalMacroGroup macro
                 in surah.MacroGroups)
        {
            if (string.IsNullOrWhiteSpace(
                    macro.MacroGroupId) ||
                string.IsNullOrWhiteSpace(
                    macro.Label) ||
                macro.ContextBlockIds.Count == 0 ||
                macro.ContextBlockIds.Count !=
                    macro.ContextBlockCount ||
                macro.ContextRanges.Count !=
                    macro.ContextBlockCount)
            {
                throw new InvalidDataException(
                    $"Surah {surah.SurahNumber} Macro Group metadata is incomplete.");
            }

            if (blockCursor +
                    macro.ContextBlockCount >
                surah.ContextBlocks.Count)
            {
                throw new InvalidDataException(
                    $"Surah {surah.SurahNumber} Macro Groups do not partition Context Blocks.");
            }

            IReadOnlyList<ProposalContextBlock> expected =
                surah.ContextBlocks
                    .Skip(blockCursor)
                    .Take(
                        macro.ContextBlockCount)
                    .ToList();

            if (!macro.ContextBlockIds.SequenceEqual(
                    expected.Select(
                        x =>
                            x.ContextBlockId),
                    StringComparer.Ordinal) ||
                !macro.ContextRanges.SequenceEqual(
                    expected.Select(
                        x =>
                            Range(
                                x.StartAyah,
                                x.EndAyah)),
                    StringComparer.Ordinal) ||
                macro.StartAyah !=
                    expected[0].StartAyah ||
                macro.EndAyah !=
                    expected[^1].EndAyah)
            {
                throw new InvalidDataException(
                    $"Surah {surah.SurahNumber} Macro Group {macro.MacroGroupId} does not reference one consecutive run of fine Context Blocks.");
            }

            blockCursor +=
                macro.ContextBlockCount;
        }

        if (blockCursor !=
            surah.ContextBlocks.Count)
        {
            throw new InvalidDataException(
                $"Surah {surah.SurahNumber} Macro Groups do not partition all fine Context Blocks.");
        }
    }

    private static void ValidateWorksets(
        IReadOnlyList<ProposalWorkset> worksets,
        IReadOnlyList<ProposalSurah> surahs,
        IReadOnlySet<string> allBlockIds)
    {
        var worksetIds =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (ProposalWorkset workset
                 in worksets)
        {
            if (string.IsNullOrWhiteSpace(
                    workset.Id) ||
                !worksetIds.Add(
                    workset.Id) ||
                string.IsNullOrWhiteSpace(
                    workset.Label) ||
                (workset.Members.Count == 0 &&
                 workset.Subsets.Count == 0))
            {
                throw new InvalidDataException(
                    "Related Workset metadata is incomplete or duplicated.");
            }

            ValidateWorksetMembers(
                workset.Id,
                workset.Members,
                surahs,
                allBlockIds);

            var subsetLabels =
                new HashSet<string>(
                    StringComparer.Ordinal);

            foreach (ProposalWorksetSubset subset
                     in workset.Subsets)
            {
                if (string.IsNullOrWhiteSpace(
                        subset.Label) ||
                    !subsetLabels.Add(
                        subset.Label) ||
                    subset.Members.Count == 0)
                {
                    throw new InvalidDataException(
                        $"Related Workset {workset.Id} has incomplete or duplicated subset metadata.");
                }

                ValidateWorksetMembers(
                    $"{workset.Id} / {subset.Label}",
                    subset.Members,
                    surahs,
                    allBlockIds);
            }
        }
    }

    private static void ValidateWorksetMembers(
        string worksetIdentity,
        IReadOnlyList<ProposalWorksetMember> members,
        IReadOnlyList<ProposalSurah> surahs,
        IReadOnlySet<string> allBlockIds)
    {
        foreach (ProposalWorksetMember member
                 in members)
        {
            ProposalSurah surah =
                surahs.SingleOrDefault(
                    x =>
                        x.SurahNumber ==
                        member.SurahNumber)
                ?? throw new InvalidDataException(
                    $"Related Workset {worksetIdentity} refers to missing Surah {member.SurahNumber}.");

            if (member.StartAyah < 1 ||
                member.EndAyah <
                    member.StartAyah ||
                member.EndAyah >
                    surah.VersesCount ||
                member.ContextBlockIds.Count == 0 ||
                member.ContextBlockIds.Any(
                    id =>
                        !allBlockIds.Contains(
                            id)))
            {
                throw new InvalidDataException(
                    $"Related Workset {worksetIdentity} has an unresolved or invalid member.");
            }

            IReadOnlyList<ProposalContextBlock> blocks =
                member.ContextBlockIds
                    .Select(
                        id =>
                            surah.ContextBlocks.SingleOrDefault(
                                x =>
                                    string.Equals(
                                        x.ContextBlockId,
                                        id,
                                        StringComparison.Ordinal))
                            ?? throw new InvalidDataException(
                                $"Related Workset {worksetIdentity} refers to a Context Block outside Surah {member.SurahNumber}."))
                    .ToList();

            if (blocks[0].StartAyah !=
                    member.StartAyah ||
                blocks[^1].EndAyah !=
                    member.EndAyah)
            {
                throw new InvalidDataException(
                    $"Related Workset {worksetIdentity} member range does not match its Context Block references.");
            }

            for (int i = 1;
                 i < blocks.Count;
                 i++)
            {
                if (blocks[i].Index !=
                    blocks[i - 1].Index + 1)
                {
                    throw new InvalidDataException(
                        $"Related Workset {worksetIdentity} member Context Blocks are not consecutive.");
                }
            }
        }
    }

    private static ZipArchiveEntry RequireEntry(
        ZipArchive archive,
        string name) =>
        archive.Entries.FirstOrDefault(
            x =>
                string.Equals(
                    x.FullName.Replace(
                        '\\',
                        '/'),
                    name,
                    StringComparison.Ordinal))
        ?? throw new InvalidDataException(
            $"Proposal package is missing required file: {name}");

    private static byte[] ReadEntry(
        ZipArchiveEntry entry,
        long maxBytes)
    {
        if (entry.Length > maxBytes)
        {
            throw new InvalidDataException(
                "Context Atlas proposal ZIP entry exceeds its expanded-size limit.");
        }

        using Stream source = entry.Open();
        using var target = new MemoryStream();
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException(
                    "Context Atlas proposal ZIP expanded beyond its allowed limit.");
            }

            target.Write(buffer, 0, read);
        }

        return target.ToArray();
    }

    private static T Deserialize<T>(
        byte[] bytes,
        string label) =>
        JsonSerializer.Deserialize<T>(
            bytes,
            JsonOptions)
        ?? throw new InvalidDataException(
            $"{label} could not be parsed.");

    internal static string Sha256(
        byte[] bytes) =>
        Convert.ToHexString(
            SHA256.HashData(
                bytes))
            .ToLowerInvariant();

    private static string Range(
        int start,
        int end) =>
        start == end
            ? start.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            : $"{start}-{end}";
}
