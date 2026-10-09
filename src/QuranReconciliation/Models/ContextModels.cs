namespace QuranReconciliation.Models;

internal sealed record ContextBlock(
    long Id,
    int SurahNumber,
    int StartAyah,
    int EndAyah,
    string Status,
    string Origin,
    DateTimeOffset UpdatedUtc)
{
    public string RangeLabel =>
        StartAyah == EndAyah
            ? $"Ayah {StartAyah}"
            : $"Ayat {StartAyah}–{EndAyah}";

    public string DisplayLabel => $"{RangeLabel} · {Status}";
}

internal sealed record ContextBoundaryEvent(
    long Id,
    long ContextBlockId,
    int OldStartAyah,
    int OldEndAyah,
    int NewStartAyah,
    int NewEndAyah,
    string? Action,
    DateTimeOffset ChangedUtc)
{
    public string DisplayLabel
    {
        get
        {
            string oldRange = OldStartAyah == OldEndAyah
                ? OldStartAyah.ToString()
                : $"{OldStartAyah}–{OldEndAyah}";
            string newRange = NewStartAyah == NewEndAyah
                ? NewStartAyah.ToString()
                : $"{NewStartAyah}–{NewEndAyah}";

            return $"{ChangedUtc.LocalDateTime:g} · {Action ?? "Boundary edit"} · {oldRange} → {newRange}";
        }
    }
}


internal sealed record ContextProposalRange(
    int StartAyah,
    int EndAyah)
{
    public string RangeLabel =>
        StartAyah == EndAyah
            ? StartAyah.ToString(
                System.Globalization.CultureInfo.InvariantCulture)
            : $"{StartAyah}–{EndAyah}";
}

internal sealed record ContextProposalImportResult(
    long ImportId,
    long FirstContextBlockId,
    int BlockCount,
    string NormalizedRanges);


internal sealed record ContextProposalDiscardState(
    long ImportId,
    int BlockCount,
    string NormalizedRanges,
    bool CanDiscard,
    string Reason);
