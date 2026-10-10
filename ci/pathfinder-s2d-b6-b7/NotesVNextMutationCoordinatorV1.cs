namespace NivareQ.Step2Pathfinder.Services;

// S2-D B6 INTERNAL. This coordinator is NOT installed in any production writer.
// All seven existing text-provider writes, Notes writes, WebView durable ACKs and
// restore must join ONE INSTANCE before it can offer process-wide protection.
// This type is a fail-closed ownership contract, NOT a replacement for the
// existing journal's power-loss / multi-file transaction recovery.
public sealed class NotesVNextMutationCoordinatorV1
{
    private readonly object _sync = new();
    private long _epoch = 1;
    private int _inFlight;
    private int _pendingAcknowledgements;
    private Phase _phase;
    private enum Phase { Writable, RestoreHeld, CommitAttempted, RestartRequired }

    public readonly record struct Snapshot(long Epoch, int InFlight, int PendingAcknowledgements,
        bool RestoreHeld, bool RestartRequired);

    public Snapshot Inspect()
    {
        lock (_sync)
            return new Snapshot(_epoch, _inFlight, _pendingAcknowledgements,
                _phase is Phase.RestoreHeld or Phase.CommitAttempted,
                _phase is Phase.CommitAttempted or Phase.RestartRequired);
    }

    public long CurrentEpoch
    {
        get { lock (_sync) return _epoch; }
    }

    public WriterLease BeginWriter(long observedEpoch)
    {
        lock (_sync)
        {
            if (_phase != Phase.Writable || observedEpoch != _epoch)
                throw new InvalidOperationException("Stale editor or restore fence rejects this writer.");
            _inFlight = checked(_inFlight + 1);
            return new WriterLease(this);
        }
    }

    public RestoreLease BeginRestore()
    {
        lock (_sync)
        {
            if (_phase != Phase.Writable || _inFlight != 0 || _pendingAcknowledgements != 0)
                throw new InvalidOperationException("Restore cannot begin while writers or durable save acknowledgments remain outstanding.");
            _phase = Phase.RestoreHeld;
            _epoch = checked(_epoch + 1); // invalidate EVERY already-open WebView token
            return new RestoreLease(this);
        }
    }

    public sealed class WriterLease : IDisposable
    {
        private readonly NotesVNextMutationCoordinatorV1 _owner;
        private int _state; // 0=writing, 1=durably saved & awaiting ack, 2=complete/cancelled
        internal WriterLease(NotesVNextMutationCoordinatorV1 owner) => _owner = owner;

        // Only call after the underlying target has reported a DURABLE save.
        // A pending ACK deliberately survives Dispose; it must be explicitly
        // acknowledged before restore may freeze the provider graph.
        public void MarkDurableAwaitingAck()
        {
            lock (_owner._sync)
            {
                if (_state != 0) throw new InvalidOperationException("Mutation is not active.");
                if (_owner._phase != Phase.Writable)
                    throw new InvalidOperationException("Mutation coordinator was fenced during a write.");
                _owner._inFlight--;
                _owner._pendingAcknowledgements = checked(_owner._pendingAcknowledgements + 1);
                _state = 1;
            }
        }

        public void AcknowledgeDurableSave()
        {
            lock (_owner._sync)
            {
                if (_state != 1) throw new InvalidOperationException("No durable save acknowledgment is pending.");
                _owner._pendingAcknowledgements--;
                _state = 2;
            }
        }

        // Disposal of an uncompleted write releases it; disposal of a persisted
        // but unacknowledged write DOES NOT invent an ACK or permit restore.
        public void Dispose()
        {
            lock (_owner._sync)
            {
                if (_state == 0) { _owner._inFlight--; _state = 2; }
            }
        }
    }

    public sealed class RestoreLease : IDisposable
    {
        private readonly NotesVNextMutationCoordinatorV1 _owner;
        private bool _disposed;
        internal RestoreLease(NotesVNextMutationCoordinatorV1 owner) => _owner = owner;

        public long Epoch
        {
            get { lock (_owner._sync) { RequireLive(); return _owner._epoch; } }
        }

        // Used by offline candidate builders. Must remain held through commit.
        public void RequirePreCommitOwnership()
        {
            lock (_owner._sync)
            {
                RequireLive();
                if (_owner._phase != Phase.RestoreHeld)
                    throw new InvalidOperationException("Restore is not in the precommit phase.");
            }
        }

        // The only permitted transition prior to invoking the existing
        // RestoreTransactionService.Commit. Even a failed commit attempt may
        // have written a journal; NEVER unfreeze writers automatically.
        public void MarkCommitAttempted()
        {
            lock (_owner._sync)
            {
                RequireLive();
                if (_owner._phase != Phase.RestoreHeld)
                    throw new InvalidOperationException("Restore commit is not in a valid phase.");
                _owner._phase = Phase.CommitAttempted;
            }
        }

        public void MarkCommitSucceeded()
        {
            lock (_owner._sync)
            {
                RequireLive();
                if (_owner._phase != Phase.CommitAttempted)
                    throw new InvalidOperationException("No commit was attempted.");
                _owner._phase = Phase.RestartRequired;
            }
        }

        private void RequireLive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RestoreLease));
        }

        public void Dispose()
        {
            lock (_owner._sync)
            {
                if (_disposed) return;
                _disposed = true;
                // Cancel BEFORE a commit attempt may reopen the gate, but the
                // epoch remains bumped; existing UI tokens stay invalid.
                if (_owner._phase == Phase.RestoreHeld) _owner._phase = Phase.Writable;
                // During/after commit, the blocked phase survives this lease.
                else if (_owner._phase == Phase.CommitAttempted)
                    _owner._phase = Phase.RestartRequired;
            }
        }
    }
}
