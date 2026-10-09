using System.Text.Json;

namespace QuranReconciliation.Infrastructure;

// Uncommitted research recovery is never part of the revisioned research DB.
// The file lives beside portable owner data, but verified owner-state backups
// deliberately contain committed research only.
internal sealed record SavedSliceDraft(
    long Id, string Title, string Status, string ResearchNotes, string Conclusion);
internal sealed record SavedAyahDraft(int Surah, int Ayah, string Body);
internal sealed record SavedContextDraft(long ContextId, string Body);

internal sealed record RecoverableResearchDrafts(
    int SchemaVersion,
    List<SavedSliceDraft> WorkingSlices,
    List<SavedAyahDraft> AyahNotes,
    List<SavedContextDraft> ContextNotes)
{
    internal static RecoverableResearchDrafts Empty() =>
        new(1, new(), new(), new());

    internal bool HasDrafts =>
        WorkingSlices.Count > 0 || AyahNotes.Count > 0 || ContextNotes.Count > 0;
}

internal sealed class ResearchDraftStore
{
    internal const int SupportedSchema = 1;
    private readonly string _path;
    private static readonly JsonSerializerOptions Options =
        new() { WriteIndented = true };

    internal ResearchDraftStore(string? path = null)
    {
        _path = Path.GetFullPath(
            path ?? Path.Combine(AppPaths.DataDirectory, "recoverable-drafts.json"));
    }

    internal RecoverableResearchDrafts Load()
    {
        if (!File.Exists(_path))
        {
            return RecoverableResearchDrafts.Empty();
        }

        // Fail closed: don't overwrite unreadable or future-schema drafts.
        string json = File.ReadAllText(_path);
        RecoverableResearchDrafts saved =
            JsonSerializer.Deserialize<RecoverableResearchDrafts>(json, Options)
            ?? throw new InvalidDataException("Recoverable research drafts are invalid.");

        if (saved.SchemaVersion != SupportedSchema ||
            saved.WorkingSlices is null ||
            saved.AyahNotes is null ||
            saved.ContextNotes is null ||
            saved.WorkingSlices.Any(d => d.Id <= 0 || d.Title is null ||
                d.Status is null || d.ResearchNotes is null || d.Conclusion is null) ||
            saved.AyahNotes.Any(d => d.Surah is < 1 or > 114 ||
                d.Ayah <= 0 || d.Body is null) ||
            saved.ContextNotes.Any(d => d.ContextId <= 0 || d.Body is null))
        {
            throw new InvalidDataException(
                "Recoverable research draft schema or content is unsupported. Original file preserved.");
        }

        return saved;
    }

    internal void Save(RecoverableResearchDrafts snapshot)
    {
        if (snapshot.SchemaVersion != SupportedSchema)
        {
            throw new InvalidDataException("Refusing to write unsupported draft schema.");
        }

        // Validate the current authority before writing; this also prevents a
        // future-schema draft file from being overwritten by an older app.
        _ = Load();

        if (!snapshot.HasDrafts)
        {
            if (File.Exists(_path)) File.Delete(_path);
            return;
        }

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, Options));
            // Rename/replace only after a complete valid temporary write.
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
