using QuranReconciliation.Infrastructure;
using System.Text.Json;

string root = Path.Combine(Path.GetTempPath(),
    "THTRP-DraftRecoveryProbe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    string path = Path.Combine(root, "Data", "recoverable-drafts.json");
    var writer = new ResearchDraftStore(path);

    Require(!writer.Load().HasDrafts, "New draft store must be empty.");
    string arabic = "تجربة ﷽ বাংলা · preserved across unexpected shutdown";
    var snapshot = new RecoverableResearchDrafts(
        ResearchDraftStore.SupportedSchema,
        new()
        {
            new SavedSliceDraft(17, "Draft 1", "In Review", arabic, "Conclusion"),
            new SavedSliceDraft(18, "Draft 2", "Draft", "Long " + new string('x', 120000), "")
        },
        new()
        {
            new SavedAyahDraft(2, 255, arabic),
            new SavedAyahDraft(1, 1, "")
        },
        new()
        {
            new SavedContextDraft(9005, "Archived parent note · " + arabic)
        });

    writer.Save(snapshot);
    // Simulate application termination by reconstructing store independently.
    var relaunched = new ResearchDraftStore(path);
    RecoverableResearchDrafts restored = relaunched.Load();
    Require(restored.WorkingSlices.Count == 2 &&
            restored.AyahNotes.Count == 2 &&
            restored.ContextNotes.Count == 1,
        "Restart must recover all original draft identities.");
    Require(restored.WorkingSlices[0].ResearchNotes == arabic &&
            restored.WorkingSlices[1].ResearchNotes.Length > 120000 &&
            restored.AyahNotes[0].Body == arabic,
        "Restart must recover multilingual text byte-for-byte.");

    writer.Save(restored with {
        WorkingSlices = restored.WorkingSlices.Where(x => x.Id == 18).ToList()
    });
    Require(relaunched.Load().WorkingSlices.Count == 1 &&
            relaunched.Load().WorkingSlices[0].Id == 18,
        "Save/discard of one draft must not erase another.");

    writer.Save(RecoverableResearchDrafts.Empty());
    Require(!File.Exists(path) && !relaunched.Load().HasDrafts,
        "All explicitly resolved drafts must cleanly clear the sidecar.");

    // Malformed and future-schema owner drafts are never overwritten.
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, "{ this is not a valid owner draft }");
    string malformed = File.ReadAllText(path);
    ExpectFailure(() => relaunched.Load(), "Malformed draft read must fail closed.");
    ExpectFailure(() => writer.Save(snapshot),
        "Malformed existing owner drafts must block replacement.");
    Require(File.ReadAllText(path) == malformed,
        "Malformed owner draft must remain byte-for-byte untouched.");

    File.WriteAllText(path, JsonSerializer.Serialize(snapshot with {
        SchemaVersion = 99
    }));
    string future = File.ReadAllText(path);
    ExpectFailure(() => relaunched.Load(), "Future draft schema must be refused.");
    ExpectFailure(() => writer.Save(snapshot),
        "Older app must never overwrite future-schema drafts.");
    Require(File.ReadAllText(path) == future,
        "Future-schema file must remain untouched.");

    Console.WriteLine("Build 1.9 portable crash-draft recovery: PASS");
}
finally
{
    try { Directory.Delete(root, recursive: true); }
    catch { }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static void ExpectFailure(Action action, string message)
{
    try { action(); }
    catch (JsonException) { return; }
    catch (InvalidDataException) { return; }
    throw new Exception(message);
}
