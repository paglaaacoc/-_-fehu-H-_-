using QuranReconciliation.Models;
using System.Text.RegularExpressions;

namespace QuranReconciliation.Infrastructure;

internal static partial class ContextProposalParser
{
    internal static IReadOnlyList<ContextProposalRange> Parse(
        string? input,
        int versesCount)
    {
        if (versesCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versesCount));
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidDataException(
                "Paste at least one proposed ayah range.");
        }

        var ranges = new List<ContextProposalRange>();
        int sourceLine = 0;

        using var reader = new StringReader(input);

        while (reader.ReadLine() is string line)
        {
            sourceLine++;
            string trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            Match match = RangeLineRegex().Match(trimmed);
            if (!match.Success)
            {
                throw new InvalidDataException(
                    $"Line {sourceLine} is not a supported range. Use one range per line, for example 1–5 or 6.");
            }

            int start = int.Parse(
                match.Groups["start"].Value,
                System.Globalization.CultureInfo.InvariantCulture);

            int end =
                match.Groups["end"].Success
                    ? int.Parse(
                        match.Groups["end"].Value,
                        System.Globalization.CultureInfo.InvariantCulture)
                    : start;

            if (start < 1 ||
                end < start ||
                end > versesCount)
            {
                throw new InvalidDataException(
                    $"Line {sourceLine} has an invalid range {start}–{end}. This Surah has {versesCount} ayat.");
            }

            ranges.Add(
                new ContextProposalRange(
                    start,
                    end));
        }

        if (ranges.Count == 0)
        {
            throw new InvalidDataException(
                "No proposal ranges were found.");
        }

        if (ranges[0].StartAyah != 1)
        {
            throw new InvalidDataException(
                "A proposed Context Map must begin at ayah 1.");
        }

        for (int i = 1; i < ranges.Count; i++)
        {
            int expected =
                ranges[i - 1].EndAyah + 1;

            if (ranges[i].StartAyah != expected)
            {
                throw new InvalidDataException(
                    $"The proposal must be contiguous. Expected ayah {expected} after {ranges[i - 1].RangeLabel}, but found {ranges[i].RangeLabel}.");
            }
        }

        if (ranges[^1].EndAyah != versesCount)
        {
            throw new InvalidDataException(
                $"A proposed Context Map must end at ayah {versesCount}.");
        }

        return ranges;
    }

    internal static string Normalize(
        IReadOnlyList<ContextProposalRange> ranges) =>
        string.Join(
            ", ",
            ranges.Select(x => x.RangeLabel));

    [GeneratedRegex(
        @"^\s*(?:[-*•]\s*)?(?:(?:\d+)[.)]\s*)?(?:ayah|ayat|verse|verses)?\s*(?<start>\d+)\s*(?:(?:-|–|—|to)\s*(?<end>\d+))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RangeLineRegex();
}
