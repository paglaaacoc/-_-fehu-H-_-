namespace QuranReconciliation.Models;

internal sealed record ChapterSummary(
    int Number,
    int VersesCount,
    string NameSimple,
    string? BengaliName);
