namespace NivareQ.Step2Pathfinder.Services;

// S2-D B10 INTERNAL / INACTIVE. Single optional hook at real persistence
// entrypoints. There is intentionally NO call to Attach from the product.
// A future installation is prohibited until every state mutation, delayed
// callback, Notes SQLite/attachment write and renderer ACK is audited.
internal static class NotesVNextWriteInstrumentationV1
{
    private static readonly object Sync = new();
    private static NotesVNextWriterAdmissionV1? _admission;

    // Native isolated test only. Do not call from MainWindow or a release.
    internal static void AttachForIsolatedValidation(NotesVNextWriterAdmissionV1 admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        lock (Sync)
        {
            if (_admission is not null) throw new InvalidOperationException("Writer instrumentation already attached.");
            // A sealed test admission is required; this does not prove real
            // product-wide writer coverage or authorize restoration.
            using (admission.BeginWrite(NotesVNextMutationSurface.ShellSettings, admission.CurrentEpoch)) { }
            _admission = admission;
        }
    }

    internal static long? CaptureEpochIfAttached() => Volatile.Read(ref _admission)?.CurrentEpoch;

    internal static PersistenceWriteResult RunSynchronous(
        NotesVNextMutationSurface surface,
        Func<PersistenceWriteResult> save,
        long? scheduledEpoch = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        var admission = Volatile.Read(ref _admission);
        if (admission is null) return save(); // byte-for-byte behavior of original save route
        var observed = scheduledEpoch ?? admission.CurrentEpoch;
        using var writer = admission.BeginWrite(surface, observed);
        var result = save();
        // An ordinary synchronous save has no WebView ACK. Marking it
        // durable and acknowledging atomically is correct ONLY for a
        // completed PersistenceWriteResult.Success.
        if (result.Success)
        {
            writer.MarkDurableAwaitingAck();
            writer.AcknowledgeDurableSave();
        }
        return result;
    }
}
