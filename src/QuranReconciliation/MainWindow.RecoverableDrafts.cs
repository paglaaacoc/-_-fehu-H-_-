using Microsoft.UI.Dispatching;
using QuranReconciliation.Infrastructure;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private readonly ResearchDraftStore _recoverableDraftStore = new();
    private DispatcherQueueTimer? _recoverableDraftTimer;

    private void LoadRecoverableDraftsAtStartup()
    {
        // Load only after the portable research database has been verified.
        // This is not an automatic canonical Save; original research remains
        // unchanged until the owner explicitly chooses Save in an editor.
        RecoverableResearchDrafts snapshot = _recoverableDraftStore.Load();

        foreach (SavedSliceDraft d in snapshot.WorkingSlices)
            _workingSliceDrafts[d.Id] =
                new WorkingSliceDraft(
                    d.Title, d.Status, d.ResearchNotes, d.Conclusion);

        foreach (SavedAyahDraft d in snapshot.AyahNotes)
            _ayahNoteDrafts[(d.Surah, d.Ayah)] = d.Body;

        foreach (SavedContextDraft d in snapshot.ContextNotes)
            _contextNoteDrafts[d.ContextId] = d.Body;
    }

    private void ScheduleRecoverableDraftSave()
    {
        if (!_ready || _ownerStateOperationActive)
        {
            return;
        }

        if (_recoverableDraftTimer is null)
        {
            _recoverableDraftTimer = RootGrid.DispatcherQueue.CreateTimer();
            _recoverableDraftTimer.IsRepeating = false;
            _recoverableDraftTimer.Interval = TimeSpan.FromMilliseconds(650);
            _recoverableDraftTimer.Tick += (_, _) =>
                TryPersistRecoverableDrafts();
        }

        _recoverableDraftTimer.Stop();
        _recoverableDraftTimer.Start();
    }

    // Use immediately at navigation/exit/safety boundaries. Keyboard entry
    // is debounced, so the editor avoids full-file disk writes per keystroke.
    private bool TryPersistRecoverableDrafts()
    {
        try
        {
            _recoverableDraftTimer?.Stop();
            RememberWorkingSliceDraft();

            if (_ayahNoteDirty &&
                _noteSurah is int surah && _noteAyah is int ayah)
            {
                _ayahNoteDrafts[(surah, ayah)] =
                    AyahNoteTextBox.Text ?? string.Empty;
            }

            if (_contextNoteDirty && _noteContextId is long contextId)
            {
                _contextNoteDrafts[contextId] =
                    ContextNoteTextBox.Text ?? string.Empty;
            }

            var snapshot = new RecoverableResearchDrafts(
                ResearchDraftStore.SupportedSchema,
                _workingSliceDrafts.Select(x =>
                    new SavedSliceDraft(
                        x.Key, x.Value.Title, x.Value.Status,
                        x.Value.ResearchNotes, x.Value.Conclusion)).ToList(),
                _ayahNoteDrafts.Select(x =>
                    new SavedAyahDraft(x.Key.Surah, x.Key.Ayah, x.Value)).ToList(),
                _contextNoteDrafts.Select(x =>
                    new SavedContextDraft(x.Key, x.Value)).ToList());

            _recoverableDraftStore.Save(snapshot);
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "Recoverable research draft write FAILED. Do not close " +
                "or reset the app before saving research. " + ex.Message;
            return false;
        }
    }
}
