"""B11 deterministic Notes SQLite/attachment/renderer epoch overlay.
Run ONLY in fresh B10 R3 Windows CI extracted source after parent hash checking.
No user data or live app startup paths are accessed.
"""
from pathlib import Path
import sys,re,hashlib
src=Path(sys.argv[1])/'src/NivareQ.Step2Pathfinder/Services'
expected={
 'NotesStoreV1.cs':('ef66299c68a8418453b41e191cb431ba5861404ff4824ec8638c794c2976c8aa','d7a035e7ce1e584847e494b550725677f314ed41a9fd91ec7bcd7db1d80849ac'),
 'NotesStoreV1.Collections.cs':('7bb4c9128f6f3c178ec6203afc4611007e5db6bd06ef95a6f09340b8df5c63ca','e1b186ce90a26d547ba1e3f4f3a04ddbff518a35e0e7fdf5c61c93bbd09781bf'),
 'NotesStagingPageRouterV1.cs':('f39b1af173346ba28e6e4e3b8221ac4920d55fbc02d13344a356ddac78e56a89','ebceb17b79bf41962c0eddf1de5aee8f01868e2f32e2f5689d4d90fc68ca7b6e'),
 'NotesVNextWriteInstrumentationV1.cs':('da0a27ed56cfd8d82d2b1bd86d4b229dfc089d7b13d59c7be80b5401f709a55f','447976f473e756c19dbd29476cf5a707110357b5783310b799f05aaf3a1f55d0')
}
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
for name,(before,_) in expected.items():
 assert digest(src/name)==before,f'B11 parent mismatch: {name}'
inst=src/'NotesVNextWriteInstrumentationV1.cs';s=inst.read_text()
needle='    internal static PersistenceWriteResult RunSynchronous('
assert s.count(needle)==1
addition='''    // B11 staging-only guard: holds the B6 writer lease through an entire
    // SQLite transaction or immutable-attachment publication. Does NOT
    // manufacture a renderer ACK: that transport receipt remains a B12 gate.
    // This is fail-open ONLY while the global instrumentation is intentionally
    // unattached; it may not be installed in the product without full coverage.
    internal static IDisposable EnterNotesMutation(
        NotesVNextMutationSurface surface, long? scheduledEpoch = null)
    {
        if (surface is not (NotesVNextMutationSurface.NotesSqlite or
                            NotesVNextMutationSurface.NotesAttachments or
                            NotesVNextMutationSurface.LegacyS1Persistence))
            throw new InvalidOperationException("Notes mutation must use its declared Notes write surface.");
        var admission = Volatile.Read(ref _admission);
        if (admission is null) return NoopMutationLease.Instance;
        return admission.BeginWrite(surface, scheduledEpoch ?? admission.CurrentEpoch);
    }

    private sealed class NoopMutationLease : IDisposable
    {
        internal static readonly NoopMutationLease Instance = new();
        public void Dispose() { }
    }

'''
inst.write_text(s.replace(needle,addition+needle))
store=src/'NotesStoreV1.cs';s=store.read_text()
methods=[('CreateNotebook','NotesSqlite'),('CreateNote','NotesSqlite'),('AddPage','NotesSqlite'),('SavePage','NotesSqlite'),('PutAttachment','NotesAttachments'),('SealStaging','NotesSqlite'),('ImportS1','LegacyS1Persistence')]
for name,surface in methods:
 pattern=r'(    public (?:string|long|NotesAttachment|void|S1ImportReport) '+name+r'\([\s\S]*?\)\s*\{\n)'
 found=list(re.finditer(pattern,s))
 assert len(found)==1,(name,len(found))
 scope=f'        using var vNextNotesWrite = NotesVNextWriteInstrumentationV1.EnterNotesMutation(NotesVNextMutationSurface.{surface}'
 if name=='SavePage':
  header=found[0].group(1)
  assert 'IReadOnlyList<NotesImageObject>? images = null)' in header
  s=s[:found[0].start()]+header.replace('IReadOnlyList<NotesImageObject>? images = null)', 'IReadOnlyList<NotesImageObject>? images = null, long? queuedEpoch = null)')+s[found[0].end():]
  scope += ', queuedEpoch);\n'
 else: scope += ');\n'
 found=list(re.finditer(pattern,s));assert len(found)==1
 idx=found[0].end();s=s[:idx]+scope+s[idx:]
store.write_text(s)
collections=src/'NotesStoreV1.Collections.cs';s=collections.read_text()
for name in ['UpdateNoteMetadata','SetActivePage','SetTrashed']:
 pattern=r'(    public long '+name+r'\([\s\S]*?\)\s*\{\n)'
 found=list(re.finditer(pattern,s));assert len(found)==1,(name,len(found))
 i=found[0].end()
 s=s[:i]+'        using var vNextNotesWrite = NotesVNextWriteInstrumentationV1.EnterNotesMutation(NotesVNextMutationSurface.NotesSqlite);\n'+s[i:]
collections.write_text(s)
router=src/'NotesStagingPageRouterV1.cs';s=router.read_text()
assert s.count('PendingSession(string noteId, string pageId, long revision)')==1
s=s.replace('PendingSession(string noteId, string pageId, long revision)', 'PendingSession(string noteId, string pageId, long revision, long? mutationEpoch)')
s=s.replace('        public long Revision { get; set; } = revision;', '        public long Revision { get; set; } = revision;\n        public long? MutationEpoch { get; } = mutationEpoch;')
assert s.count('new PendingSession(noteId, pageId, page.Revision)')==1
s=s.replace('new PendingSession(noteId, pageId, page.Revision)','new PendingSession(noteId, pageId, page.Revision,\n                NotesVNextWriteInstrumentationV1.CaptureEpochIfAttached())')
assert s.count('packet.Page.Cells, packet.Page.Images);')==1
s=s.replace('packet.Page.Cells, packet.Page.Images);', 'packet.Page.Cells, packet.Page.Images, session.MutationEpoch);')
router.write_text(s)
for name,(_,after) in expected.items():
 assert digest(src/name)==after,f'B11 output source mismatch: {name}: {digest(src/name)}'
print('PASS B11 exact source parity; no runtime Attach, no live generation.')
