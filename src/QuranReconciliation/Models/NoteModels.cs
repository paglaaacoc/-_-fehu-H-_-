namespace QuranReconciliation.Models;

internal sealed record ResearchNote(
    string Body,
    DateTimeOffset UpdatedUtc);

internal sealed record NoteRevision(
    long Id,
    string PriorBody,
    DateTimeOffset ChangedUtc)
{
    public string DisplayLabel =>
        $"{ChangedUtc.LocalDateTime:g} · {Preview(PriorBody)}";

    private static string Preview(string value)
    {
        string singleLine = value
            .Replace("\r", " ")
            .Replace("\n", " ");

        return singleLine.Length <= 90
            ? singleLine
            : singleLine[..87] + "…";
    }
}
