using Microsoft.UI.Xaml;

namespace QuranReconciliation.Models;

internal sealed record ChapterSummary(
    int Number,
    int VersesCount,
    string NameSimple,
    string? BengaliName)
{
    public string DisplayName
    {
        get
        {
            string phonetic = BengaliSurahNames.Get(Number);

            if (string.IsNullOrWhiteSpace(BengaliName))
            {
                return string.IsNullOrWhiteSpace(phonetic)
                    ? $"{Number}. {NameSimple}"
                    : $"{Number}. {NameSimple} | {phonetic}";
            }

            return string.IsNullOrWhiteSpace(phonetic)
                ? $"{Number}. {NameSimple} | {BengaliName}"
                : $"{Number}. {NameSimple} | {phonetic} · {BengaliName}";
        }
    }
}

internal sealed record ResourceSummary(
    string Kind,
    int Id,
    string DisplayType,
    string LanguageName,
    string Name,
    string? AuthorName)
{
    public bool IsBengali =>
        LanguageName.Equals("bengali", StringComparison.OrdinalIgnoreCase) ||
        LanguageName.Equals("bangla", StringComparison.OrdinalIgnoreCase);

    public string DisplayLabel =>
        DisplayType == "Translation"
            ? Name
            : $"{Name} · {DisplayType}";
}

internal sealed record SourceFootnote(
    string Marker,
    string Text);

internal sealed record SourceText(
    int ResourceId,
    string ResourceName,
    string DisplayType,
    string LanguageName,
    string Text,
    string? ScopeLabel = null,
    IReadOnlyList<SourceFootnote>? StoredFootnotes = null)
{
    public bool IsBengali =>
        LanguageName.Equals("bengali", StringComparison.OrdinalIgnoreCase) ||
        LanguageName.Equals("bangla", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<SourceFootnote> Footnotes =>
        StoredFootnotes ??
        Array.Empty<SourceFootnote>();
}

internal sealed class VerseBundle
{
    public required string VerseKey { get; init; }
    public required int VerseNumber { get; init; }
    public required string Uthmani { get; init; }
    public required string IndoPak { get; init; }
    public required string IndoPakNastaleeq { get; init; }
    public required IReadOnlyList<SourceText> Translations { get; init; }
    public required IReadOnlyList<SourceText> Tafsirs { get; init; }
}
