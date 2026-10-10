using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private readonly NoteRepository _notes = new();
    private int? _noteSurah;
    private int? _noteAyah;
    private long? _noteContextId;
    private bool _loadingNoteUi;
    private bool _ayahNoteDirty;
    private bool _contextNoteDirty;
    private string _ayahNoteSavedBody = string.Empty;
    private string _contextNoteSavedBody = string.Empty;
    // Unsaved drafts follow original note targets, not current selection.
    // Separate from committed revisioned research; same-launch only.
    private readonly Dictionary<(int Surah, int Ayah), string> _ayahNoteDrafts = new();
    private readonly Dictionary<long, string> _contextNoteDrafts = new();

    private bool HasUnsavedNoteDrafts =>
        _ayahNoteDirty || _contextNoteDirty ||
        _ayahNoteDrafts.Count > 0 || _contextNoteDrafts.Count > 0;

    private void DiscardNoteDraftsForSafety()
    {
        _ayahNoteDrafts.Clear();
        _contextNoteDrafts.Clear();
        // Do not recapture drafts while deliberate destructive-operation
        // discard reloads each saved note target.
        _ayahNoteDirty = false;
        _contextNoteDirty = false;
        if (_noteSurah is int surah &&
            _noteAyah is int ayah)
        {
            SelectAyahNoteTarget(
                surah,
                ayah);
        }
        else
        {
            _ayahNoteDirty = false;
        }

        if (_noteContextId is long contextId)
        {
            LoadContextNoteTarget(
                contextId);
        }
        else
        {
            _contextNoteDirty = false;
        }

        TryPersistRecoverableDrafts();
    }

    private void AttachResearchDirtyTracking()
    {
        AyahNoteTextBox.TextChanged +=
            (_, _) =>
            {
                if (!_loadingNoteUi &&
                    !AyahNoteTextBox.IsReadOnly)
                {
                    _ayahNoteDirty = !string.Equals(
                        AyahNoteTextBox.Text ?? string.Empty,
                        _ayahNoteSavedBody,
                        StringComparison.Ordinal);
                    if (!_ayahNoteDirty &&
                        _noteSurah is int surah && _noteAyah is int ayah)
                    {
                        _ayahNoteDrafts.Remove((surah, ayah));
                    }
                    ScheduleRecoverableDraftSave();
                }
            };

        ContextNoteTextBox.TextChanged +=
            (_, _) =>
            {
                if (!_loadingNoteUi &&
                    !ContextNoteTextBox.IsReadOnly)
                {
                    _contextNoteDirty = !string.Equals(
                        ContextNoteTextBox.Text ?? string.Empty,
                        _contextNoteSavedBody,
                        StringComparison.Ordinal);
                    if (!_contextNoteDirty && _noteContextId is long id)
                    {
                        _contextNoteDrafts.Remove(id);
                    }
                    ScheduleRecoverableDraftSave();
                }
            };
    }

    private void SelectAyahForNote_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not int ayah)
        {
            return;
        }

        SelectAyahNoteTarget(_currentSurah, ayah);
        AyahNoteExpander.IsExpanded = true;

        ResearchNote? saved =
            _notes.GetAyahNote(_currentSurah, ayah);

        if (saved is null)
        {
            AyahNoteTextBox.Focus(FocusState.Programmatic);
            StatusText.Text =
                $"New note ready for ayah {_currentSurah}:{ayah}.";
        }
        else
        {
            StatusText.Text =
                $"Saved note opened for ayah {_currentSurah}:{ayah}. Press Edit to revise it.";
        }
    }

    private void EnsureAyahNoteTargetForSurah(int surahNumber)
    {
        if (_noteSurah == surahNumber &&
            _noteAyah is int existing &&
            existing >= 1 &&
            existing <= _chapters[surahNumber - 1].VersesCount)
        {
            return;
        }

        SelectAyahNoteTarget(surahNumber, 1);
    }

    private void SelectAyahNoteTarget(
        int surahNumber,
        int ayahNumber)
    {
        if (_ayahNoteDirty && _noteSurah is int oldSurah &&
            _noteAyah is int oldAyah)
        {
            _ayahNoteDrafts[(oldSurah, oldAyah)] =
                AyahNoteTextBox.Text ?? string.Empty;
            TryPersistRecoverableDrafts();
        }

        _noteSurah = surahNumber;
        _noteAyah = ayahNumber;

        ResearchNote? note =
            _notes.GetAyahNote(surahNumber, ayahNumber);
        _ayahNoteSavedBody = note?.Body ?? string.Empty;

        AyahNoteTargetText.Text =
            $"Selected ayah / নির্বাচিত আয়াত · {surahNumber}:{ayahNumber}";

        _loadingNoteUi = true;
        try
        {
            AyahNoteTextBox.Text =
                note?.Body ?? string.Empty;
            _ayahNoteDirty = false;
        }
        finally
        {
            _loadingNoteUi = false;
        }

        SetAyahNoteEditorState(
            editing: note is null,
            hasSavedNote: note is not null);

        if (_ayahNoteDrafts.TryGetValue((surahNumber, ayahNumber), out string? draft))
        {
            _loadingNoteUi = true;
            try
            {
                AyahNoteTextBox.Text = draft;
                _ayahNoteDirty = true;
            }
            finally
            {
                _loadingNoteUi = false;
            }
            SetAyahNoteEditorState(editing: true, hasSavedNote: note is not null);
        }

        FillNoteHistory(
            AyahNoteHistoryPanel,
            _notes.GetAyahHistory(surahNumber, ayahNumber));
    }

    private void LoadContextNoteTarget(long? contextBlockId)
    {
        if (_contextNoteDirty && _noteContextId is long priorId)
        {
            _contextNoteDrafts[priorId] =
                ContextNoteTextBox.Text ?? string.Empty;
            TryPersistRecoverableDrafts();
        }

        _noteContextId = contextBlockId;

        if (contextBlockId is not long id)
        {
            ContextNoteTargetText.Text = "Select a context block.";
            _contextNoteSavedBody = string.Empty;
            _loadingNoteUi = true;
            try { ContextNoteTextBox.Text = string.Empty; }
            finally { _loadingNoteUi = false; }
            _contextNoteDirty = false;
            ContextNoteHistoryPanel.Items.Clear();
            SetContextNoteEditorState(
                editing: false,
                hasSavedNote: false,
                hasTarget: false);
            return;
        }

        try
        {
            ContextBlock block = _contexts.GetBlock(id);
            ResearchNote? note = _notes.GetContextNote(id);
            _contextNoteSavedBody = note?.Body ?? string.Empty;

            ContextNoteTargetText.Text =
                $"{block.RangeLabel} · {block.Status}";

            _loadingNoteUi = true;
            try
            {
                ContextNoteTextBox.Text =
                    note?.Body ?? string.Empty;
                _contextNoteDirty = false;
            }
            finally
            {
                _loadingNoteUi = false;
            }

            SetContextNoteEditorState(
                editing: note is null,
                hasSavedNote: note is not null,
                hasTarget: true);

            if (_contextNoteDrafts.TryGetValue(id, out string? draft))
            {
                _loadingNoteUi = true;
                try
                {
                    ContextNoteTextBox.Text = draft;
                    _contextNoteDirty = true;
                }
                finally
                {
                    _loadingNoteUi = false;
                }
                SetContextNoteEditorState(
                    editing: true, hasSavedNote: note is not null, hasTarget: true);
            }

            FillNoteHistory(
                ContextNoteHistoryPanel,
                _notes.GetContextHistory(id));
        }
        catch (Exception ex)
        {
            ContextNoteTargetText.Text = "Context note unavailable.";
            _contextNoteSavedBody = string.Empty;
            _loadingNoteUi = true;
            try { ContextNoteTextBox.Text = string.Empty; }
            finally { _loadingNoteUi = false; }
            _contextNoteDirty = false;
            ContextNoteHistoryPanel.Items.Clear();
            SetContextNoteEditorState(
                editing: false,
                hasSavedNote: false,
                hasTarget: false);
            StatusText.Text =
                $"Context note error: {ex.Message}";
        }
    }

    private void EditAyahNote_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_noteSurah is null ||
            _noteAyah is null)
        {
            return;
        }

        SetAyahNoteEditorState(
            editing: true,
            hasSavedNote: true);

        AyahNoteTextBox.Focus(FocusState.Programmatic);
        StatusText.Text =
            $"Editing ayah note {_noteSurah}:{_noteAyah}. Saving will preserve the previous version in history.";
    }

    private void EditContextNote_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_noteContextId is null)
        {
            return;
        }

        SetContextNoteEditorState(
            editing: true,
            hasSavedNote: true,
            hasTarget: true);

        ContextNoteTextBox.Focus(FocusState.Programmatic);
        StatusText.Text =
            "Editing context note. Saving will preserve the previous version in history.";
    }

    private void SaveAyahNote_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ownerStateOperationActive)
        {
            StatusText.Text = "Note save blocked during owner-data operation.";
            return;
        }

        if (_noteSurah is not int surah ||
            _noteAyah is not int ayah)
        {
            StatusText.Text =
                "Choose an ayah before saving a note.";
            return;
        }

        try
        {
            ResearchNote? existing =
                _notes.GetAyahNote(surah, ayah);

            string body =
                AyahNoteTextBox.Text ?? string.Empty;

            if (existing is null &&
                string.IsNullOrWhiteSpace(body))
            {
                StatusText.Text =
                    "Type a note before saving.";
                return;
            }

            bool changed = _notes.SaveAyahNote(
                surah,
                ayah,
                body);

            FillNoteHistory(
                AyahNoteHistoryPanel,
                _notes.GetAyahHistory(surah, ayah));

            _ayahNoteDirty = false;
            _ayahNoteSavedBody = body;
            _ayahNoteDrafts.Remove((surah, ayah));

            SetAyahNoteEditorState(
                editing: false,
                hasSavedNote: true);

            RefreshResearchHistory();

            StatusText.Text = changed
                ? existing is null
                    ? $"Ayah note saved and locked for {surah}:{ayah}."
                    : $"Ayah note revision saved for {surah}:{ayah}; the previous version is in history."
                : $"Ayah note for {surah}:{ayah} is unchanged and remains locked.";

            TryPersistRecoverableDrafts();
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Could not save ayah note: {ex.Message}";
        }
    }

    private void SaveContextNote_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ownerStateOperationActive)
        {
            StatusText.Text = "Note save blocked during owner-data operation.";
            return;
        }

        if (_noteContextId is not long id)
        {
            StatusText.Text =
                "Select a context block before saving a context note.";
            return;
        }

        try
        {
            ResearchNote? existing =
                _notes.GetContextNote(id);

            string body =
                ContextNoteTextBox.Text ?? string.Empty;

            if (existing is null &&
                string.IsNullOrWhiteSpace(body))
            {
                StatusText.Text =
                    "Type a context note before saving.";
                return;
            }

            bool changed = _notes.SaveContextNote(id, body);

            FillNoteHistory(
                ContextNoteHistoryPanel,
                _notes.GetContextHistory(id));

            _contextNoteDirty = false;
            _contextNoteSavedBody = body;
            _contextNoteDrafts.Remove(id);

            SetContextNoteEditorState(
                editing: false,
                hasSavedNote: true,
                hasTarget: true);

            RefreshResearchHistory();

            StatusText.Text = changed
                ? existing is null
                    ? "Context note saved and locked."
                    : "Context note revision saved; the previous version is in history."
                : "Context note is unchanged and remains locked.";

            TryPersistRecoverableDrafts();
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Could not save context note: {ex.Message}";
        }
    }

    private void SetAyahNoteEditorState(
        bool editing,
        bool hasSavedNote)
    {
        AyahNoteTextBox.IsReadOnly = !editing;

        SaveAyahNoteButton.Visibility =
            editing ? Visibility.Visible : Visibility.Collapsed;

        EditAyahNoteButton.Visibility =
            !editing && hasSavedNote
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void SetContextNoteEditorState(
        bool editing,
        bool hasSavedNote,
        bool hasTarget)
    {
        ContextNoteTextBox.IsReadOnly =
            !editing || !hasTarget;

        SaveContextNoteButton.Visibility =
            editing && hasTarget
                ? Visibility.Visible
                : Visibility.Collapsed;

        EditContextNoteButton.Visibility =
            !editing && hasSavedNote && hasTarget
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void FillNoteHistory(
        ItemsControl panel,
        IReadOnlyList<NoteRevision> revisions)
    {
        panel.Items.Clear();

        if (revisions.Count == 0)
        {
            panel.Items.Add(new TextBlock
            {
                Text = "No earlier saved revisions yet.",
                Foreground = Brush("MutedTextBrush"),
                FontSize = 11.5,
                FontStyle = Windows.UI.Text.FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (NoteRevision revision in revisions)
        {
            var priorText = CreateSelectableText(
                revision.PriorBody,
                fontSize: 13.5,
                lineHeight: 21);

            var expander = new Expander
            {
                Header = revision.DisplayLabel,
                IsExpanded = false,
                Margin = new Thickness(0, 2, 0, 2),
                Content = new Border
                {
                    Background = Brush("PanelBrush"),
                    BorderBrush = Brush("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Child = priorText
                }
            };

            panel.Items.Add(expander);
        }
    }
}
