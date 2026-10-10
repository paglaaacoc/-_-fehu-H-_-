using NivareQ.Step2Pathfinder.Services;
using System.Collections.ObjectModel;

// B8 native test doubles below are ONLY here so tests cannot touch AppData.
// WinUI lane separately compiles the REAL product types and service bindings.
namespace NivareQ.Step2Pathfinder.Services
{
    internal sealed record NotesVNextSevenProviderCandidateV1(
        IReadOnlyDictionary<string,string> Targets,string SourceGenerationId,
        string StagedGenerationId,long RestoreEpoch);
    internal static class NotesStoreV1
    {
        internal static bool IsValidGenerationId(string? id) => id is {Length:32} &&
            id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
    internal static class PathfinderPaths
    {
        public static string SettingsPath => "state/settings.json";
        public static string PersonalStudyPath => "state/personal.json";
        public static string BackupExtensionsPath => "state/extensions.json";
        public static string CockpitStatePath => "state/cockpit.json";
        public static string JournalSnapshotPath => "state/journal.json";
        public static string JournalLogPath => "state/journal-log.json";
        public static string UWorldSelectorStatePath => "state/selector.json";
    }
    internal sealed class PersistenceWriteResult
    {
        public bool Success {get;init;}
        public static PersistenceWriteResult Ok() => new(){Success=true};
        public static PersistenceWriteResult Fail() => new(){Success=false};
    }
    internal static class RestoreTransactionService
    {
        internal static PersistenceWriteResult Commit(IReadOnlyDictionary<string,string> _) =>
            throw new Exception("Forbidden: B8 native harness must not invoke the real commit path");
        internal static void BlockMutationsUntilRestart(string _) =>
            throw new Exception("Forbidden: no global runtime mutations in native harness");
    }
}

internal static class Program
{
    private static int tests;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
        Console.WriteLine($"PASS B8 {++tests} {name}");
    }
    private static void Reject<T>(Action act, string name) where T:Exception
    {
        try {act();}
        catch (T) {Check(true,name);return;}
        throw new Exception("Unexpectedly permitted " + name);
    }
    private static IReadOnlyDictionary<string,string> Seven()
    {
        return new ReadOnlyDictionary<string,string>(new Dictionary<string,string>
        {
            [PathfinderPaths.SettingsPath]="settings",
            [PathfinderPaths.PersonalStudyPath]="personal",
            [PathfinderPaths.BackupExtensionsPath]="extensions",
            [PathfinderPaths.CockpitStatePath]="cockpit",
            [PathfinderPaths.JournalSnapshotPath]="journal",
            [PathfinderPaths.JournalLogPath]="journal-log",
            [PathfinderPaths.UWorldSelectorStatePath]="selector"
        });
    }
    private static void Main()
    {
        var src=Guid.NewGuid().ToString("N");
        var staged=Guid.NewGuid().ToString("N");
        var gate=new NotesVNextMutationCoordinatorV1();
        using(var lease=gate.BeginRestore())
        {
            var valid=new NotesVNextSevenProviderCandidateV1(Seven(),src,staged,lease.Epoch);
            var calls=0;var block=0;
            Reject<InvalidDataException>(() => NotesVNextCommitBoundaryV1.CommitCore(valid,lease,Guid.NewGuid().ToString("N"),
                _ => {calls++;return PersistenceWriteResult.Ok();}, () => block++), "active pointer mismatch rejects before commit");
            Check(calls==0 && block==0,"pointer mismatch preserves journal boundary");
            Reject<InvalidOperationException>(() => NotesVNextCommitBoundaryV1.CommitCore(valid with {RestoreEpoch=lease.Epoch+1},lease,src,
                _ => {calls++;return PersistenceWriteResult.Ok();}, () => block++),"stale restore epoch rejected");
            var bad=Seven().ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
            bad.Remove(PathfinderPaths.JournalLogPath);bad["state/injected.json"]="evil";
            Reject<InvalidDataException>(() => NotesVNextCommitBoundaryV1.CommitCore(valid with {Targets=bad},lease,src,
                _ => {calls++;return PersistenceWriteResult.Ok();}, () => block++),"injected target rejected, even with seven keys");
            Check(calls==0,"no mutation before full validation");
            var result=NotesVNextCommitBoundaryV1.CommitCore(valid,lease,src,
                snapshot => {calls++;Check(snapshot.Count==7 && snapshot[PathfinderPaths.BackupExtensionsPath]=="extensions",
                    "seven canonical targets passed to journal delegate");return PersistenceWriteResult.Ok();}, () => block++);
            Check(result.Success && calls==1 && block==1,"successful commit invokes restart-block callback exactly once");
            Check(gate.Inspect().RestartRequired,"successful commit keeps B6 restart fence");
        }
        var failGate=new NotesVNextMutationCoordinatorV1();
        using(var lease=failGate.BeginRestore())
        {
            var candidate=new NotesVNextSevenProviderCandidateV1(Seven(),src,staged,lease.Epoch);
            var block=0;
            var response=NotesVNextCommitBoundaryV1.CommitCore(candidate,lease,src,_=>PersistenceWriteResult.Fail(),()=>block++);
            Check(!response.Success && block==0,"journal failure must not signal success");
            Check(failGate.Inspect().RestartRequired,"journal failure conservatively locks until restart");
        }
        var crashGate=new NotesVNextMutationCoordinatorV1();
        using(var lease=crashGate.BeginRestore())
        {
            var candidate=new NotesVNextSevenProviderCandidateV1(Seven(),src,staged,lease.Epoch);
            Reject<IOException>(()=>NotesVNextCommitBoundaryV1.CommitCore(candidate,lease,src,
                _=>throw new IOException("fault injection"),()=>throw new Exception("no callback")),
                "journal exception propagated");
        }
        Check(crashGate.Inspect().RestartRequired,"journal exception persists restart fence");
        Console.WriteLine($"B8 native transaction boundary: {tests}/{tests} PASS (fake journal only)");
    }
}
