using QuranReconciliation.Models;

namespace QuranReconciliation.Infrastructure;

internal sealed class ProposalCorpusRepository
{
    internal const string BuiltInEdition1PackageSha256 =
        "d4f572bdf94ff4a3d708258530f3d4bec5793fe0f061cc893ad682bf7150307f";

    internal const string BuiltInEdition1PayloadSha256 =
        "7c0cfb8d3f11ae1a5458e084ed59d111f35d07ab7ee53eb87b0311b414b4b6aa";
    private readonly IReadOnlyList<ChapterSummary> _chapters;
    private readonly string _builtInDirectory;
    private readonly string _importedDirectory;
    private readonly List<string> _rejectedImportedPackages = new();

    internal IReadOnlyList<string> RejectedImportedPackages =>
        _rejectedImportedPackages.AsReadOnly();

    internal ProposalCorpusRepository(
        IReadOnlyList<ChapterSummary> chapters,
        string? builtInDirectory = null,
        string? importedDirectory = null)
    {
        _chapters = chapters;
        _builtInDirectory =
            Path.GetFullPath(
                builtInDirectory ??
                AppPaths.ContextAtlasBuiltInDirectory);
        _importedDirectory =
            Path.GetFullPath(
                importedDirectory ??
                AppPaths.ContextAtlasImportedDirectory);
    }

    internal IReadOnlyList<ProposalCorpusPackage> LoadAll()
    {
        _rejectedImportedPackages.Clear();
        var packages =
            new List<ProposalCorpusPackage>();

        if (!Directory.Exists(
                _builtInDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Context Atlas built-in corpus directory is missing: {_builtInDirectory}");
        }

        IReadOnlyList<string> builtInZips =
            Directory
                .EnumerateFiles(
                    _builtInDirectory,
                    "*.zip",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(
                    x =>
                        x,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        foreach (string path in builtInZips)
        {
            packages.Add(
                ProposalCorpusValidator.LoadAndValidate(
                    path,
                    _chapters,
                    isBuiltIn: true));
        }

        if (builtInZips.Count == 0)
        {
            IReadOnlyList<string> encodedParts =
                Directory
                    .EnumerateFiles(
                        _builtInDirectory,
                        "*.b64part",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(
                        x =>
                            x,
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (encodedParts.Count > 0)
            {
                string encoded =
                    string.Concat(
                        encodedParts.Select(
                            File.ReadAllText));

                byte[] bytes;

                try
                {
                    bytes =
                        Convert.FromBase64String(
                            encoded);
                }
                catch (FormatException ex)
                {
                    throw new InvalidDataException(
                        "Built-in Context Atlas corpus encoding is invalid.",
                        ex);
                }

                packages.Add(
                    ProposalCorpusValidator.LoadAndValidateBytes(
                        bytes,
                        "built-in://THTRP-Context-Map-Proposal-Corpus-E1.zip",
                        _chapters,
                        isBuiltIn: true));
            }
        }

        if (packages.Count == 0)
        {
            throw new InvalidDataException(
                "Context Atlas has no built-in Proposal Corpus.");
        }

        ProposalCorpusPackage edition1 =
            packages[0];

        if (!string.Equals(
                edition1.PackageSha256,
                BuiltInEdition1PackageSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                edition1.PayloadSha256,
                BuiltInEdition1PayloadSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Built-in Context Atlas Edition 1 bytes do not match the pinned package/payload hashes.");
        }

        if (Directory.Exists(
                _importedDirectory))
        {
            foreach (string path in Directory
                         .EnumerateFiles(
                             _importedDirectory,
                             "*.zip",
                             SearchOption.TopDirectoryOnly)
                         .OrderBy(
                             x =>
                                 x,
                             StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    ProposalCorpusPackage imported =
                        ProposalCorpusValidator.LoadAndValidate(
                            path,
                            _chapters,
                            isBuiltIn: false);
                    packages.Add(imported);
                }
                catch (Exception ex) when (
                    ex is InvalidDataException or IOException or
                          UnauthorizedAccessException)
                {
                    // Imported material is isolated and never authoritative.
                    // Keep original ZIP untouched; preserve healthy corpora.
                    _rejectedImportedPackages.Add(
                        $"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }

        ValidateIdentityCollisions(
            packages);

        return packages;
    }

    internal ProposalCorpusPackage GetById(
        string corpusId) =>
        LoadAll().Single(
            x =>
                string.Equals(
                    x.Corpus.CorpusId,
                    corpusId,
                    StringComparison.Ordinal));

    internal static ProposalCorpusComparison Compare(
        ProposalCorpusDocument left,
        ProposalCorpusDocument right)
    {
        int shared = 0;
        int leftOnly = 0;
        int rightOnly = 0;
        int exactBlocks = 0;

        for (int surahNumber = 1;
             surahNumber <= 114;
             surahNumber++)
        {
            ProposalSurah a =
                left.Surahs.Single(
                    x =>
                        x.SurahNumber ==
                        surahNumber);

            ProposalSurah b =
                right.Surahs.Single(
                    x =>
                        x.SurahNumber ==
                        surahNumber);

            var aBoundaries =
                a.Boundaries
                    .Select(
                        x =>
                            x.AfterAyah)
                    .ToHashSet();

            var bBoundaries =
                b.Boundaries
                    .Select(
                        x =>
                            x.AfterAyah)
                    .ToHashSet();

            shared +=
                aBoundaries.Intersect(
                    bBoundaries).Count();

            leftOnly +=
                aBoundaries.Except(
                    bBoundaries).Count();

            rightOnly +=
                bBoundaries.Except(
                    aBoundaries).Count();

            var bBlocks =
                b.ContextBlocks
                    .Select(
                        x =>
                            (x.StartAyah,
                             x.EndAyah))
                    .ToHashSet();

            exactBlocks +=
                a.ContextBlocks.Count(
                    x =>
                        bBlocks.Contains(
                            (x.StartAyah,
                             x.EndAyah)));
        }

        return new ProposalCorpusComparison(
            shared,
            leftOnly,
            rightOnly,
            exactBlocks,
            left.Statistics.TotalContextBlocks,
            right.Statistics.TotalContextBlocks);
    }

    private static void ValidateIdentityCollisions(
        IReadOnlyList<ProposalCorpusPackage> packages)
    {
        foreach (IGrouping<string, ProposalCorpusPackage> group
                 in packages.GroupBy(
                     x =>
                         x.Corpus.CorpusId,
                     StringComparer.Ordinal))
        {
            IReadOnlyList<ProposalCorpusPackage> matches =
                group.ToList();

            if (matches.Count <= 1)
            {
                continue;
            }

            if (matches.Select(
                    x =>
                        x.PayloadSha256)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count() > 1)
            {
                throw new InvalidDataException(
                    $"Context Atlas corpus ID collision: {group.Key} exists with different payload bytes.");
            }
        }
    }
}
