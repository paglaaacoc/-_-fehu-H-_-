using NivareQ.Step2Pathfinder.Services;

namespace NivareQ.Step2Pathfinder.Services
{
    // Fake persistence result; the test never loads any live provider.
    internal sealed class PersistenceWriteResult
    {
        internal bool Success { get; init; }
    }
}

internal sealed class BlockingInput : Stream
{
    private readonly MemoryStream _inner;
    private readonly ManualResetEventSlim _entered = new(false);
    private readonly ManualResetEventSlim _release = new(false);
    private int _firstRead;
    public BlockingInput(byte[] bytes) => _inner = new MemoryStream(bytes, writable:false);
    public bool WaitEntered() => _entered.Wait(TimeSpan.FromSeconds(25));
    public void Unblock() => _release.Set();
    public override int Read(byte[] b, int offset, int count)
    {
        if (Interlocked.Exchange(ref _firstRead, 1) == 0)
        {
            _entered.Set();
            if (!_release.Wait(TimeSpan.FromSeconds(25)))
                throw new TimeoutException("Isolated attachment harness timed out.");
        }
        return _inner.Read(b, offset, count);
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] b, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    { if (disposing) { _release.Set(); _entered.Dispose(); _release.Dispose(); _inner.Dispose(); } base.Dispose(disposing); }
}

internal static class Program
{
    private static int _passes;
    private static void Check(bool ok, string label)
    { if (!ok) throw new Exception("B11 FAIL: " + label); Console.WriteLine($"PASS B11 {++_passes} {label}"); }
    private static void Refuse<T>(Action a, string label) where T:Exception
    { try { a(); } catch (T) { Check(true,label);return; } throw new Exception("B11 UNSAFE: " + label); }

    public static void Main()
    {
        var scratch=Path.Combine(Path.GetTempPath(),"nqpf-b11-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            Check(NotesVNextWriteInstrumentationV1.CaptureEpochIfAttached() is null,"instrumentation initially inactive");
            using var store=NotesStoreV1.CreateStaging(scratch);
            var notebook=store.CreateNotebook("Temporary B11 test");
            var note=store.CreateNote(notebook,"Notes in isolation");
            var page=store.ListPages(note).Single();
            Check(page.Revision==0,"dormant staging SQL unchanged");
            var partial=new NotesVNextWriterAdmissionV1();
            Refuse<InvalidOperationException>(()=>NotesVNextWriteInstrumentationV1.AttachForIsolatedValidation(partial),
                "partial admission cannot attach");
            var gate=new NotesVNextWriterAdmissionV1();
            foreach (var surface in Enum.GetValues<NotesVNextMutationSurface>()) gate.Enroll(surface);
            gate.SealEnrollment();
            NotesVNextWriteInstrumentationV1.AttachForIsolatedValidation(gate);
            var oldEpoch=gate.CurrentEpoch;
            var router=new NotesStagingPageRouterV1(store);
            var session=router.OpenStagingPage(note,page.Id);
            var oldWrite=new NotesPageWritePacket(NotesStagingPageRouterV1.Protocol,session.SessionId,1,note,page.Id,0,
                new NotesPageDraft("ruled",32,[new NotesInkCell("original save","[]")],[]));
            var ack=router.Write(oldWrite);
            Check(ack.PersistedRevision==1,"page saved within global mutation gate");
            Check(gate.Inspect().InFlight==0,"SQLite mutation lease retired");
            var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC");
            using(var slow=new BlockingInput(png))
            {
                var future=Task.Run(()=>store.PutAttachment(slow,"image/png"));
                try
                {
                    Check(slow.WaitEntered(),"blocked attachment writer holds lease");
                    Refuse<InvalidOperationException>(()=>gate.BeginRestore(),"restore rejects incomplete attachment publication");
                }
                finally { slow.Unblock(); }
                var image=future.GetAwaiter().GetResult();
                Check(image.ByteLength==png.Length,"immutable attachment published completely");
            }
            Check(gate.Inspect().InFlight==0 && gate.Inspect().PendingAcknowledgements==0,
                "completed attachment leaves no fake renderer ACK");
            using(var restore=gate.BeginRestore())
            {
                Refuse<InvalidOperationException>(()=>store.CreateNotebook("must reject"),"restore blocks notebook mutation");
                Refuse<InvalidOperationException>(()=>store.PutAttachment(new MemoryStream(png),"image/png"),"restore blocks attachment");
                Refuse<InvalidOperationException>(()=>store.SavePage(note,page.Id,1,"ruled",32,[new NotesInkCell("bad","[]")]),
                    "restore blocks SQLite page mutation");
            }
            Check(gate.CurrentEpoch!=oldEpoch,"precommit restore advances epoch");
            Refuse<InvalidOperationException>(()=>router.Write(oldWrite with { Sequence=2, ExpectedRevision=1,
                Page=new NotesPageDraft("ruled",32,[new NotesInkCell("stale renderer","[]")],[]) }),
                "old Notes session cannot save after canceled restore");
            Check(store.ReadPage(note,page.Id).Cells[0].Text=="original save","stale renderer leaves SQLite intact");
            var secondPage=store.AddPage(note,1);
            var fresh=router.OpenStagingPage(note,secondPage);
            Check(fresh.PersistedRevision==0,"fresh epoch can open new Notes page");
            store.SealStaging();
            using(var verifier=NotesStoreV1.OpenStagedGeneration(scratch,store.GenerationId))
                Check(verifier.ListPages(note).Count==2,"sealed generation reopens without data loss");
            using(var attempted=gate.BeginRestore()) attempted.MarkCommitAttempted();
            Refuse<InvalidOperationException>(()=>store.PutAttachment(new MemoryStream(png),"image/png"),
                "postcommit restart fence blocks attachments");
            Check(gate.Inspect().RestartRequired,"failed/interrupted commit remains blocked");
            Console.WriteLine($"B11 Notes + attachments + stale renderer: {_passes}/{_passes} PASS (isolated scratch only)");
        }
        finally { try { Directory.Delete(scratch,recursive:true); } catch {} }
    }
}
