using QuranReconciliation.Models;

namespace QuranReconciliation.Infrastructure;

internal sealed record ProposalCorpusImportResult(
    ProposalCorpusPackage Package,
    bool WasAlreadyPresent);

internal sealed class ProposalCorpusImportService
{
    private readonly IReadOnlyList<ChapterSummary> _chapters;
    private readonly string _builtInDirectory;
    private readonly string _importedDirectory;

    internal ProposalCorpusImportService(
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

    internal ProposalCorpusImportResult Import(
        string sourcePackagePath)
    {
        ProposalCorpusPackage incoming =
            ProposalCorpusValidator.LoadAndValidate(
                sourcePackagePath,
                _chapters,
                isBuiltIn: false);

        var repository =
            new ProposalCorpusRepository(
                _chapters,
                _builtInDirectory,
                _importedDirectory);

        ProposalCorpusPackage? existing =
            repository.LoadAll()
                .FirstOrDefault(
                    x =>
                        string.Equals(
                            x.Corpus.CorpusId,
                            incoming.Corpus.CorpusId,
                            StringComparison.Ordinal));

        if (existing is not null)
        {
            if (!string.Equals(
                    existing.PayloadSha256,
                    incoming.PayloadSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Corpus ID collision: {incoming.Corpus.CorpusId} already exists with a different payload.");
            }

            return new ProposalCorpusImportResult(
                existing,
                WasAlreadyPresent: true);
        }

        Directory.CreateDirectory(
            _importedDirectory);

        string safeName =
            MakeSafeFileName(
                incoming.Corpus.CorpusId) +
            ".zip";

        string target =
            Path.Combine(
                _importedDirectory,
                safeName);

        File.Copy(
            incoming.PackagePath,
            target,
            overwrite: false);

        ProposalCorpusPackage copied =
            ProposalCorpusValidator.LoadAndValidate(
                target,
                _chapters,
                isBuiltIn: false);

        if (!string.Equals(
                copied.PackageSha256,
                incoming.PackageSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(
                target);

            throw new IOException(
                "Imported proposal package bytes changed during copy.");
        }

        return new ProposalCorpusImportResult(
            copied,
            WasAlreadyPresent: false);
    }

    internal void RemoveImported(
        ProposalCorpusPackage package)
    {
        if (package.IsBuiltIn)
        {
            throw new InvalidOperationException(
                "The built-in Context Atlas corpus cannot be removed.");
        }

        string root =
            Path.GetFullPath(
                _importedDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        string target =
            Path.GetFullPath(
                package.PackagePath);

        if (!target.StartsWith(
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to remove a proposal package outside the Context Atlas imported library.");
        }

        if (File.Exists(
                target))
        {
            File.Delete(
                target);
        }
    }

    private static string MakeSafeFileName(
        string value)
    {
        var invalid =
            Path.GetInvalidFileNameChars()
                .ToHashSet();

        string safe =
            new(
                value.Select(
                    character =>
                        invalid.Contains(
                            character)
                            ? '_'
                            : character)
                    .ToArray());

        return string.IsNullOrWhiteSpace(
            safe)
            ? "proposal-corpus"
            : safe;
    }
}
