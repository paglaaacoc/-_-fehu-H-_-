using System.Collections.ObjectModel;

namespace QuranReconciliation.Models;

internal sealed record HistoryRevisionItem(
    string Header,
    string Body,
    DateTimeOffset ChangedUtc);

internal sealed record ResearchHistoryEntry(
    string Category,
    string Kind,
    string Target,
    string Body,
    DateTimeOffset ChangedUtc,
    int SurahNumber,
    int? AyahNumber,
    long? ContextBlockId,
    long? WorkingSliceId,
    int? StartAyah,
    int? EndAyah,
    string EntityType,
    long? EntityId,
    int RevisionCount,
    IReadOnlyList<HistoryRevisionItem> Revisions)
{
    public string Header =>
        $"{Kind} · {Target}";

    public string Timestamp =>
        ChangedUtc.LocalDateTime.ToString("g");

    public int? JumpAyah =>
        AyahNumber ??
        StartAyah;

    public bool CanJump =>
        WorkingSliceId is not null ||
        (SurahNumber is >= 1 and <= 114 &&
         JumpAyah is > 0);

    public string JumpLabel =>
        Category == "WorkingSlices" &&
        WorkingSliceId is not null
            ? "Open Working Slice"
            : Category == "WorkingSlices"
                ? "Open parent context"
                : Category is "ContextNotes" or "Context"
                    ? "Open context"
                    : "Jump to ayah";

    public string Preview
    {
        get
        {
            string clean =
                (Body ?? string.Empty)
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();

            return clean.Length <= 220
                ? clean
                : clean[..217] + "…";
        }
    }

    public string DetailsHeader =>
        RevisionCount > 0
            ? $"Details · {RevisionCount} revision(s)"
            : "Details";
}

internal sealed record HistoryDisplayRow(
    ResearchHistoryEntry Entry,
    string GroupHeader,
    string SubgroupHeader)
{
    public string Header => Entry.Header;
    public string Timestamp => Entry.Timestamp;
    public string Preview => Entry.Preview;
    public string Body => Entry.Body;
    public bool CanJump => Entry.CanJump;
    public string JumpLabel => Entry.JumpLabel;
    public string DetailsHeader => Entry.DetailsHeader;
    public ObservableCollection<HistoryRevisionItem> Revisions { get; } =
        new(Entry.Revisions);

    public bool RevisionsLoaded { get; private set; }

    public void SetLazyRevisions(IReadOnlyList<HistoryRevisionItem> revisions)
    {
        if (RevisionsLoaded) return;
        foreach (HistoryRevisionItem revision in revisions)
        {
            Revisions.Add(revision);
        }
        RevisionsLoaded = true;
    }
    public double GroupHeaderHeight =>
        string.IsNullOrWhiteSpace(GroupHeader) ? 0 : 36;
    public double SubgroupHeaderHeight =>
        string.IsNullOrWhiteSpace(SubgroupHeader) ? 0 : 28;
}
