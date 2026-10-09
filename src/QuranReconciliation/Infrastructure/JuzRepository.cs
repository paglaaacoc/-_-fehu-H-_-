using QuranReconciliation.Models;
using System.Text.Json;

namespace QuranReconciliation.Infrastructure;

internal sealed record JuzBoundary(
    int Number,
    int StartSurah,
    int StartAyah,
    int EndSurah,
    int EndAyah)
{
    internal string StartVerseKey =>
        $"{StartSurah}:{StartAyah}";

    internal string EndVerseKey =>
        $"{EndSurah}:{EndAyah}";

    internal string DisplayLabel =>
        $"Juz {Number} · {StartVerseKey}–{EndVerseKey}";
}

internal sealed class JuzRepository
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true
        };

    private readonly IReadOnlyList<JuzBoundary> _juzs;
    private readonly IReadOnlyList<ChapterSummary> _chapters;
    private readonly int[] _surahOffsets;

    internal JuzRepository(
        IReadOnlyList<ChapterSummary> chapters,
        string? mapPath = null)
    {
        _chapters = chapters;

        _surahOffsets =
            new int[
                chapters.Count + 1];

        for (int i = 1;
             i <= chapters.Count;
             i++)
        {
            _surahOffsets[i] =
                _surahOffsets[i - 1] +
                chapters[i - 1].VersesCount;
        }

        string resolvedMapPath =
            Path.GetFullPath(
                mapPath ??
                AppPaths.JuzMapFile);

        if (!File.Exists(resolvedMapPath))
        {
            throw new FileNotFoundException(
                "Pinned Juz metadata is missing from the portable app folder.",
                resolvedMapPath);
        }

        JuzMapDocument document =
            JsonSerializer.Deserialize<JuzMapDocument>(
                File.ReadAllText(resolvedMapPath),
                JsonOptions)
            ?? throw new InvalidDataException(
                "Pinned Juz metadata is invalid.");

        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported Juz metadata schema: {document.SchemaVersion}.");
        }

        _juzs =
            document.Juzs
                .Select(
                    row =>
                    {
                        (int startSurah, int startAyah) =
                            ParseVerseKey(row.Start);

                        (int endSurah, int endAyah) =
                            ParseVerseKey(row.End);

                        return new JuzBoundary(
                            row.Number,
                            startSurah,
                            startAyah,
                            endSurah,
                            endAyah);
                    })
                .OrderBy(x => x.Number)
                .ToList();

        ValidateCompleteCoverage();
    }

    internal IReadOnlyList<JuzBoundary> GetAll() =>
        _juzs;

    internal JuzBoundary Get(
        int juzNumber) =>
        _juzs.FirstOrDefault(
            x => x.Number == juzNumber)
        ?? throw new ArgumentOutOfRangeException(
            nameof(juzNumber));

    internal int GetJuzNumber(
        int surah,
        int ayah)
    {
        ValidateAyah(
            surah,
            ayah);

        int ordinal =
            ToOrdinal(
                surah,
                ayah);

        foreach (JuzBoundary juz in _juzs)
        {
            if (ordinal >=
                    ToOrdinal(
                        juz.StartSurah,
                        juz.StartAyah) &&
                ordinal <=
                    ToOrdinal(
                        juz.EndSurah,
                        juz.EndAyah))
            {
                return juz.Number;
            }
        }

        throw new InvalidDataException(
            $"No pinned Juz contains {surah}:{ayah}.");
    }

    internal string GetRangeLabel(
        int surah,
        int startAyah,
        int endAyah)
    {
        int start =
            GetJuzNumber(
                surah,
                startAyah);

        int end =
            GetJuzNumber(
                surah,
                endAyah);

        if (start == end)
        {
            return $"Juz {start}";
        }

        return end == start + 1
            ? $"Juz {start} → {end}"
            : $"Juz {start}–{end}";
    }

    internal IReadOnlyList<JuzBoundary>
        GetStartsInsideRange(
            int surah,
            int startAyah,
            int endAyah,
            bool includeRangeStart = false)
    {
        return _juzs
            .Where(
                juz =>
                    juz.StartSurah == surah &&
                    juz.StartAyah >= startAyah &&
                    juz.StartAyah <= endAyah &&
                    (includeRangeStart ||
                     juz.StartAyah != startAyah))
            .ToList();
    }

    internal bool IsJuzStart(
        int surah,
        int ayah,
        out int juzNumber)
    {
        JuzBoundary? match =
            _juzs.FirstOrDefault(
                x =>
                    x.StartSurah == surah &&
                    x.StartAyah == ayah);

        juzNumber =
            match?.Number ?? 0;

        return match is not null;
    }

    private void ValidateCompleteCoverage()
    {
        if (_chapters.Count != 114)
        {
            throw new InvalidDataException(
                $"Juz metadata validation requires 114 Surahs, found {_chapters.Count}.");
        }

        if (_juzs.Count != 30 ||
            !_juzs.Select(x => x.Number)
                .SequenceEqual(
                    Enumerable.Range(1, 30)))
        {
            throw new InvalidDataException(
                "Pinned Juz metadata must contain exactly Juz 1 through 30.");
        }

        int totalAyat =
            _chapters.Sum(
                chapter =>
                    chapter.VersesCount);

        if (totalAyat != 6236)
        {
            throw new InvalidDataException(
                $"Pinned Juz metadata expected the canonical 6,236-ayah corpus, found {totalAyat}.");
        }

        if (_juzs[0].StartSurah != 1 ||
            _juzs[0].StartAyah != 1 ||
            _juzs[^1].EndSurah != 114 ||
            _juzs[^1].EndAyah != 6)
        {
            throw new InvalidDataException(
                "Pinned Juz metadata does not span Qur'an 1:1 through 114:6.");
        }

        int previousEnd = 0;

        foreach (JuzBoundary juz in _juzs)
        {
            ValidateAyah(
                juz.StartSurah,
                juz.StartAyah);

            ValidateAyah(
                juz.EndSurah,
                juz.EndAyah);

            int start =
                ToOrdinal(
                    juz.StartSurah,
                    juz.StartAyah);

            int end =
                ToOrdinal(
                    juz.EndSurah,
                    juz.EndAyah);

            if (start != previousEnd + 1 ||
                end < start)
            {
                throw new InvalidDataException(
                    $"Pinned Juz {juz.Number} is not contiguous with the canonical verse sequence.");
            }

            previousEnd = end;
        }

        if (previousEnd != totalAyat)
        {
            throw new InvalidDataException(
                "Pinned Juz metadata does not cover all 6,236 ayat exactly once.");
        }
    }

    private int ToOrdinal(
        int surah,
        int ayah) =>
        _surahOffsets[surah - 1] +
        ayah;

    private void ValidateAyah(
        int surah,
        int ayah)
    {
        if (surah < 1 ||
            surah > _chapters.Count)
        {
            throw new InvalidDataException(
                $"Invalid Surah in Juz metadata: {surah}.");
        }

        int count =
            _chapters[surah - 1]
                .VersesCount;

        if (ayah < 1 ||
            ayah > count)
        {
            throw new InvalidDataException(
                $"Invalid ayah in Juz metadata: {surah}:{ayah}.");
        }
    }

    private static (int Surah, int Ayah)
        ParseVerseKey(
            string value)
    {
        string[] parts =
            value.Split(
                ':',
                2);

        if (parts.Length != 2 ||
            !int.TryParse(
                parts[0],
                out int surah) ||
            !int.TryParse(
                parts[1],
                out int ayah))
        {
            throw new InvalidDataException(
                $"Invalid Juz verse key: {value}.");
        }

        return (surah, ayah);
    }

    private sealed class JuzMapDocument
    {
        public int SchemaVersion { get; set; }
        public string? Source { get; set; }
        public string? SourceUrl { get; set; }
        public string? SnapshotDate { get; set; }
        public List<JuzMapRow> Juzs { get; set; } = [];
    }

    private sealed class JuzMapRow
    {
        public int Number { get; set; }
        public string Start { get; set; } = string.Empty;
        public string End { get; set; } = string.Empty;
    }
}
