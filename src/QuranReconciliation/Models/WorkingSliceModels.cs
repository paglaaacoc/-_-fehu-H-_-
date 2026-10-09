namespace QuranReconciliation.Models;

internal sealed record WorkingSlice(
    long Id,
    int SurahNumber,
    long ContextBlockId,
    int StartAyah,
    int EndAyah,
    string Title,
    string Status,
    string ResearchNotes,
    string Conclusion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public string RangeLabel =>
        StartAyah == EndAyah
            ? $"Ayah {StartAyah}"
            : $"Ayat {StartAyah}–{EndAyah}";

    public string DisplayLabel =>
        $"{Title} · {RangeLabel} · {Status}";

    public string UpdatedLabel =>
        $"Updated {UpdatedUtc.LocalDateTime:g}";
}

internal sealed record WorkingSliceRevision(
    long Id,
    long WorkingSliceId,
    string PriorTitle,
    string PriorStatus,
    string PriorResearchNotes,
    string PriorConclusion,
    DateTimeOffset ChangedUtc,
    string ChangedFieldsJson,
    string ChangeSummary)
{
    public string Header =>
        $"{ChangedUtc.LocalDateTime:g} · {ChangeSummary}";

    public string SnapshotLabel =>
        $"Prior state · {PriorStatus}";
}

internal sealed record WorkingSliceCreateRequest(
    int SurahNumber,
    long ContextBlockId,
    int StartAyah,
    int EndAyah,
    string Title);

internal sealed record WorkingSliceSaveRequest(
    long Id,
    string Title,
    string Status,
    string ResearchNotes,
    string Conclusion);
