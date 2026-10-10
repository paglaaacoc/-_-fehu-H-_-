using NivareQ.Step2Pathfinder.Services;

int checks = 0;
void Pass(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL " + label);
    Console.WriteLine("PASS B9 " + (++checks) + " " + label);
}
void Reject(Action action, string label)
{
    try { action(); }
    catch (InvalidOperationException) { Pass(true, label); return; }
    throw new Exception("Unsafe admission: " + label);
}
var admission = new NotesVNextWriterAdmissionV1();
Pass(admission.RequiredCount == 12, "closed surface inventory contains twelve authorities");
Reject(() => admission.BeginRestore(), "restore refuses unenrolled coordinator");
Reject(() => admission.BeginWrite(NotesVNextMutationSurface.ShellSettings, admission.CurrentEpoch),
    "writer refuses unenrolled coordinator");
admission.Enroll(NotesVNextMutationSurface.ShellSettings);
Reject(() => admission.Enroll(NotesVNextMutationSurface.ShellSettings), "duplicate enrollment rejected");
Reject(() => admission.Enroll((NotesVNextMutationSurface)1000), "unknown surface rejected");
Reject(admission.SealEnrollment, "partial provider coverage rejects sealing");
foreach(var surface in Enum.GetValues<NotesVNextMutationSurface>())
    if(surface != NotesVNextMutationSurface.ShellSettings) admission.Enroll(surface);
admission.SealEnrollment();
Pass(admission.Inspect().InFlight == 0, "full enrollment sealed with no in-flight writes");
Reject(admission.SealEnrollment, "cannot seal admission twice");
Reject(() => admission.Enroll(NotesVNextMutationSurface.PersonalStudy), "cannot change sealed enrollment");
var observed = admission.CurrentEpoch;
using(var writer = admission.BeginWrite(NotesVNextMutationSurface.NotesSqlite, observed))
{
    Reject(() => admission.BeginRestore(), "in-flight Notes SQLite write blocks restore");
    writer.MarkDurableAwaitingAck();
    Reject(() => admission.BeginRestore(), "durable Notes save without ACK blocks restore");
    writer.Dispose();
    Reject(() => admission.BeginRestore(), "discarded ACK token cannot reopen restore");
    writer.AcknowledgeDurableSave();
}
using(var restore = admission.BeginRestore())
{
    Pass(admission.Inspect().RestoreHeld, "full enrollment enables held restore lease");
    Reject(() => admission.BeginWrite(NotesVNextMutationSurface.JournalOperations, observed),
        "fence blocks journal append during restore");
    restore.RequirePreCommitOwnership();
}
Reject(() => admission.BeginWrite(NotesVNextMutationSurface.CockpitState, observed),
    "old WebView epoch still blocked after precommit cancellation");
Pass(admission.CurrentEpoch != observed, "cancelled restore invalidates old editor epoch");
using(var writer=admission.BeginWrite(NotesVNextMutationSurface.ShellSettings, admission.CurrentEpoch))
    Pass(admission.Inspect().InFlight == 1, "new epoch can write after safe precommit cancellation");
using(var restore=admission.BeginRestore()) restore.MarkCommitAttempted();
Pass(admission.Inspect().RestartRequired, "attempted commit stays restart-only");
Reject(() => admission.BeginWrite(NotesVNextMutationSurface.PreservedExtensions, admission.CurrentEpoch),
    "after commit attempt no provider can mutate");
Reject(() => admission.BeginRestore(), "after commit attempt no second restore");
Console.WriteLine($"B9 mutation enrollment: {checks}/{checks} PASS (uninstalled gate, not real writer coverage)");
