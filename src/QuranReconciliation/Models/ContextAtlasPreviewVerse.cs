namespace QuranReconciliation.Models;

internal sealed record ContextAtlasPreviewVerse(
    string VerseKey,
    int VerseNumber,
    string Uthmani,
    string IndoPak,
    string IndoPakNastaleeq,
    string? English,
    string? Bengali);
