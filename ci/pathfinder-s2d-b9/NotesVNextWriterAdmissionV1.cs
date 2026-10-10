namespace NivareQ.Step2Pathfinder.Services;

// S2-D B9 INTERNAL / INACTIVE. This closed-set admission barrier is a
// prerequisite, NOT proof that production write sites are enrolled. No app
// code constructs it yet. Actual call-site coverage and durable ACK draining
// must be evidenced separately before Backup VNext can use its restore lease.
public enum NotesVNextMutationSurface
{
    ShellSettings, PersonalStudy, CockpitState, JournalOperations,
    JournalSnapshot, UWorldSelector, PreservedExtensions, ShellSessionTimers,
    NotesSqlite, NotesAttachments, NotesWebViewAcknowledgements,
    LegacyS1Persistence
}

public sealed class NotesVNextWriterAdmissionV1
{
    private readonly object _lock = new();
    private readonly NotesVNextMutationCoordinatorV1 _gate = new();
    private readonly HashSet<NotesVNextMutationSurface> _enrolled = [];
    private bool _sealed;

    public int RequiredCount => Enum.GetValues<NotesVNextMutationSurface>().Length;
    public long CurrentEpoch => _gate.CurrentEpoch;
    public NotesVNextMutationCoordinatorV1.Snapshot Inspect() => _gate.Inspect();

    // Only the future controlled application composition root can call this.
    // Enrollment without audited use at every real write site is insufficient.
    public void Enroll(NotesVNextMutationSurface surface)
    {
        lock (_lock)
        {
            if (_sealed) throw new InvalidOperationException("Cannot alter a sealed mutation enrollment.");
            if (!Enum.IsDefined(surface) || !_enrolled.Add(surface))
                throw new InvalidOperationException("Unknown or duplicate mutation surface enrollment.");
        }
    }

    public void SealEnrollment()
    {
        lock (_lock)
        {
            if (_sealed || _enrolled.Count != RequiredCount)
                throw new InvalidOperationException("Restore requires enrollment of every known mutation surface.");
            foreach (var surface in Enum.GetValues<NotesVNextMutationSurface>())
                if (!_enrolled.Contains(surface))
                    throw new InvalidOperationException("Missing required mutation surface: " + surface);
            _sealed = true;
        }
    }

    public NotesVNextMutationCoordinatorV1.WriterLease BeginWrite(
        NotesVNextMutationSurface surface, long observedEpoch)
    {
        lock (_lock)
        {
            if (!_sealed || !Enum.IsDefined(surface) || !_enrolled.Contains(surface))
                throw new InvalidOperationException("Writer is not enrolled in the sealed global mutation contract.");
            return _gate.BeginWriter(observedEpoch);
        }
    }

    public NotesVNextMutationCoordinatorV1.RestoreLease BeginRestore()
    {
        lock (_lock)
        {
            if (!_sealed || _enrolled.Count != RequiredCount)
                throw new InvalidOperationException("Restore denied: complete mutation enrollment not established.");
            return _gate.BeginRestore();
        }
    }
}
