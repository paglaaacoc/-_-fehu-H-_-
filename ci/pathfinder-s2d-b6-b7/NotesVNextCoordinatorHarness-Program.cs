using NivareQ.Step2Pathfinder.Services;
using System.Collections.Concurrent;

var tests = 0;
void Ok(bool valid, string what) { if (!valid) throw new Exception("FAIL: " + what); Console.WriteLine("PASS " + (++tests) + " " + what); }
void Reject(Action call, string what)
{
    try { call(); throw new Exception("FAILED TO REJECT: " + what); }
    catch (InvalidOperationException) { Ok(true, what); }
}
var gate = new NotesVNextMutationCoordinatorV1();
var firstEpoch = gate.CurrentEpoch;
Ok(firstEpoch > 0, "initial epoch valid");
var w = gate.BeginWriter(firstEpoch);
Ok(gate.Inspect().InFlight == 1, "writer counted");
Reject(() => gate.BeginRestore(), "active writer blocks restore");
w.MarkDurableAwaitingAck();
Ok(gate.Inspect().InFlight == 0 && gate.Inspect().PendingAcknowledgements == 1, "pending ACK counted separately");
w.Dispose();
Ok(gate.Inspect().PendingAcknowledgements == 1, "Dispose cannot fake durable acknowledgment");
Reject(() => gate.BeginRestore(), "pending ACK blocks restore");
w.AcknowledgeDurableSave();
Ok(gate.Inspect().PendingAcknowledgements == 0, "explicit ACK unblocks preflight");
Reject(w.AcknowledgeDurableSave, "double ACK rejected");
Reject(w.MarkDurableAwaitingAck, "double saved transition rejected");
using (var cancelled = gate.BeginRestore())
{
    Ok(gate.Inspect().RestoreHeld, "restore holds exclusive fence");
    Reject(() => gate.BeginWriter(firstEpoch), "all new writer requests rejected while fenced");
    Reject(() => gate.BeginRestore(), "no parallel restores");
    cancelled.RequirePreCommitOwnership();
}
Ok(!gate.Inspect().RestoreHeld, "precommit cancel reopens writes");
Reject(() => gate.BeginWriter(firstEpoch), "cancel does not validate stale editor epoch");
var e2 = gate.CurrentEpoch;
Ok(e2 != firstEpoch, "restore increments editor epoch");
using (var cancelledWrite = gate.BeginWriter(e2)) { Ok(gate.Inspect().InFlight == 1, "new-epoch writer accepted"); }
Ok(gate.Inspect().InFlight == 0, "cancelled unsaved write releases lease");
var savedWrite = gate.BeginWriter(e2);
savedWrite.MarkDurableAwaitingAck();
Reject(() => gate.BeginRestore(), "dropped save acknowledgment fails closed");
savedWrite.AcknowledgeDurableSave();
using (var finish = gate.BeginRestore())
{
    finish.RequirePreCommitOwnership();
    finish.MarkCommitAttempted();
    Ok(gate.Inspect().RestartRequired, "commit attempt fences all writes");
    Reject(finish.RequirePreCommitOwnership, "precommit planning refused after commit attempt");
    finish.MarkCommitSucceeded();
    Reject(finish.MarkCommitSucceeded, "double commit success refused");
}
Ok(gate.Inspect().RestartRequired, "success keeps restart-only block");
Reject(() => gate.BeginWriter(gate.CurrentEpoch), "postcommit writer forbidden");
Reject(() => gate.BeginRestore(), "second restore forbidden until restart");
var abandoned = new NotesVNextMutationCoordinatorV1();
using (var fail = abandoned.BeginRestore()) fail.MarkCommitAttempted();
Ok(abandoned.Inspect().RestartRequired, "failed or interrupted attempt remains blocked");
Reject(() => abandoned.BeginWriter(abandoned.CurrentEpoch), "abandoned-commit writer forbidden");
var stress = new NotesVNextMutationCoordinatorV1();
var errors = new ConcurrentQueue<Exception>();
Parallel.For(0, 200, _ =>
{
    try { using var lease = stress.BeginWriter(stress.CurrentEpoch); lease.MarkDurableAwaitingAck(); lease.AcknowledgeDurableSave(); }
    catch (Exception ex) { errors.Enqueue(ex); }
});
Ok(errors.IsEmpty && stress.Inspect().InFlight == 0 && stress.Inspect().PendingAcknowledgements == 0,
    "concurrent writers retire all leases without leaking pending ACKs");
using (var final = stress.BeginRestore()) Ok(stress.Inspect().RestoreHeld, "restore succeeds after concurrent writers drain");
Console.WriteLine($"B6 coordinator: {tests}/{tests} PASS");
