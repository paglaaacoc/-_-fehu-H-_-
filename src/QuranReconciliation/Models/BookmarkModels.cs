namespace QuranReconciliation.Models;

internal sealed record BookmarkEntry(
    long Id,
    int SurahNumber,
    int AyahNumber,
    string Title,
    string Note,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);
