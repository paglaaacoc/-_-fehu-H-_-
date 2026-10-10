using NivareQ.Step2Pathfinder.Services;
using System.Threading;

namespace NivareQ.Step2Pathfinder.Services
{
    internal sealed class PersistenceWriteResult
    {
        public bool Success { get; init; }
        public static PersistenceWriteResult Ok() => new() { Success = true };
        public static PersistenceWriteResult Fail() => new() { Success = false };
    }
}

internal static class Program
{
    private static int _checks;
    private static void Check(bool pass, string label)
    {
        if (!pass) throw new Exception("FAIL B10: " + label);
        Console.WriteLine("PASS B10 " + (++_checks) + " " + label);
    }
    private static void Reject<T>(Action call, string label) where T : Exception
    {
        try { call(); }
        catch (T) { Check(true, label); return; }
        throw new Exception("B10 unsafe admission: " + label);
    }
    private static void Main()
    {
        var calls = 0;
        Check(NotesVNextWriteInstrumentationV1.CaptureEpochIfAttached() is null, "product hook is initially inactive");
        var unchanged = NotesVNextWriteInstrumentationV1.RunSynchronous(
            NotesVNextMutationSurface.ShellSettings, () => { calls++; return PersistenceWriteResult.Ok(); });
        Check(unchanged.Success && calls == 1, "disabled hook preserves original call");
        var partial = new NotesVNextWriterAdmissionV1();
        Reject<InvalidOperationException>(
            () => NotesVNextWriteInstrumentationV1.AttachForIsolatedValidation(partial),
            "partial admission never attaches");
        Check(NotesVNextWriteInstrumentationV1.CaptureEpochIfAttached() is null,
            "rejected attach does not change default mode");
        var admission = new NotesVNextWriterAdmissionV1();
        foreach (var kind in Enum.GetValues<NotesVNextMutationSurface>()) admission.Enroll(kind);
        admission.SealEnrollment();
        NotesVNextWriteInstrumentationV1.AttachForIsolatedValidation(admission);
        Check(admission.Inspect().InFlight == 0, "attach does not leak lease");
        Reject<InvalidOperationException>(
            () => NotesVNextWriteInstrumentationV1.AttachForIsolatedValidation(admission),
            "second attach rejected");
        var oldEpoch = admission.CurrentEpoch;
        Check(NotesVNextWriteInstrumentationV1.CaptureEpochIfAttached() == oldEpoch,
            "delayed callbacks can capture old epoch");
        foreach (var kind in Enum.GetValues<NotesVNextMutationSurface>())
        {
            var result = NotesVNextWriteInstrumentationV1.RunSynchronous(kind, PersistenceWriteResult.Ok, oldEpoch);
            Check(result.Success, "durable save admitted for " + kind);
        }
        Check(admission.Inspect().InFlight == 0 && admission.Inspect().PendingAcknowledgements == 0,
            "successful synchronous saves have no leaked writer or ACK");
        Check(!NotesVNextWriteInstrumentationV1.RunSynchronous(
            NotesVNextMutationSurface.JournalOperations, PersistenceWriteResult.Fail).Success,
            "failed save cannot be reported as durable");
        Reject<IOException>(
            () => NotesVNextWriteInstrumentationV1.RunSynchronous(
                NotesVNextMutationSurface.NotesAttachments, () => throw new IOException("fault")),
            "save exception propagates");
        Check(admission.Inspect().InFlight == 0, "exception retires writer");
        using var entered = new ManualResetEventSlim(false);
        using var finish = new ManualResetEventSlim(false);
        var task = Task.Run(() => NotesVNextWriteInstrumentationV1.RunSynchronous(
            NotesVNextMutationSurface.PersonalStudy,
            () => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException(); return PersistenceWriteResult.Ok(); }));
        Check(entered.Wait(TimeSpan.FromSeconds(20)), "in-flight provider write entered");
        Reject<InvalidOperationException>(() => admission.BeginRestore(), "in-flight writer blocks restore");
        finish.Set();
        Check(task.GetAwaiter().GetResult().Success, "in-flight writer completes");
        using (var cancelled = admission.BeginRestore()) Check(admission.Inspect().RestoreHeld, "restore holds exclusion");
        Check(admission.CurrentEpoch != oldEpoch, "cancelled restore bumps stale callback epoch");
        var leaked = false;
        Reject<InvalidOperationException>(() => NotesVNextWriteInstrumentationV1.RunSynchronous(
            NotesVNextMutationSurface.CockpitState,
            () => { leaked = true; return PersistenceWriteResult.Ok(); }, oldEpoch),
            "queued old cockpit callback blocked");
        Check(!leaked, "stale callback cannot execute storage");
        Check(NotesVNextWriteInstrumentationV1.RunSynchronous(
            NotesVNextMutationSurface.CockpitState, PersistenceWriteResult.Ok, admission.CurrentEpoch).Success,
            "newly queued callback permitted after safe precommit cancellation");
        using (var commit = admission.BeginRestore())
        {
            Reject<InvalidOperationException>(() => NotesVNextWriteInstrumentationV1.RunSynchronous(
                NotesVNextMutationSurface.PreservedExtensions, PersistenceWriteResult.Ok),
                "restore freeze blocks new save");
            commit.MarkCommitAttempted();
        }
        Reject<InvalidOperationException>(() => NotesVNextWriteInstrumentationV1.RunSynchronous(
            NotesVNextMutationSurface.ShellSettings, PersistenceWriteResult.Ok),
            "postcommit restart fence blocks every new save");
        Check(admission.Inspect().RestartRequired, "commit attempt requires restart");
        Console.WriteLine($"B10 real save admission: {_checks}/{_checks} PASS; no user files touched");
    }
}
