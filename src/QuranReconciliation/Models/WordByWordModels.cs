namespace QuranReconciliation.Models;

internal sealed record WordByWordEntry(
    long WordId,
    string VerseKey,
    int Position,
    string Arabic,
    string EnglishMeaning,
    string Transliteration)
{
    public string DisplayLabel =>
        $"{Arabic} · {Transliteration} · {EnglishMeaning}";
}
