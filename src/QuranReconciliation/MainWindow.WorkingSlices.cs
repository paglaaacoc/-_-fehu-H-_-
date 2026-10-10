using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;
using Windows.ApplicationModel.DataTransfer;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private readonly WorkingSliceRepository _workingSlices = new();
    private readonly WordByWordRepository _wordByWord = new();

    private long? _selectedWorkingSliceId;
    private string _workingSliceFilter = "All";
    private string _workingSliceQuery = string.Empty;
    private string _workingSliceSort = "Research";
    private bool _loadingWorkingSliceUi;
    private bool _refreshingWorkingSliceEvidence;
    private IReadOnlyList<VerseBundle> _workingSliceEvidenceVerses =
        Array.Empty<VerseBundle>();
    private WorkingSlice? _workingSliceEvidenceDefinition;
    private int _workingSliceRenderedVerseCount;
    private bool _appendingWorkingSliceEvidence;
    private bool _workingSliceDirty;
    // Unsaved research is retained by its original identity across ordinary
    // same-launch navigation without creating committed SQLite revisions.
    private sealed record WorkingSliceDraft(
        string Title, string Status, string ResearchNotes, string Conclusion);
    private readonly Dictionary<long, WorkingSliceDraft> _workingSliceDrafts = new();

    private void RememberWorkingSliceDraft()
    {
        if (!_workingSliceDirty || _loadingWorkingSliceUi ||
            _selectedWorkingSliceId is not long id)
        {
            return;
        }

        string status =
            (WorkingSliceStatusSelector.SelectedItem as ComboBoxItem)?
                .Tag?.ToString() ?? "Draft";
        _workingSliceDrafts[id] = new WorkingSliceDraft(
            WorkingSliceTitleTextBox.Text ?? string.Empty,
            status,
            WorkingSliceResearchTextBox.Text ?? string.Empty,
            WorkingSliceConclusionTextBox.Text ?? string.Empty);
    }

    // Initialized once per launch. Navigation must never reload an unsaved
    // slice draft or discard its list, filters or independently scrolled evidence.
    private bool _workingSliceWorkspaceInitialized;

    private bool HasUnsavedWorkingSliceDraft =>
        _workingSliceDirty || _workingSliceDrafts.Count > 0;

    private void DiscardWorkingSliceDraftForSafety()
    {
        _workingSliceDrafts.Clear();
        if (_selectedWorkingSliceId is long id)
        {
            LoadWorkingSlice(
                id);
        }
        else
        {
            _workingSliceDirty = false;
        }
    }

    private void OpenWorkingSlices_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenWorkingSlices();
    }

    private void OpenWorkingSlices(
        long? preferredId = null)
    {
        if (preferredId is not null)
        {
            if (!string.Equals(
                    _workingSliceFilter,
                    "All",
                    StringComparison.Ordinal))
            {
                _workingSliceFilter = "All";
                WorkingSliceFilter.SelectedIndex = 0;
            }

            if (!string.IsNullOrWhiteSpace(
                    WorkingSliceSearchTextBox.Text))
            {
                WorkingSliceSearchTextBox.Text =
                    string.Empty;
            }

            _workingSliceQuery =
                string.Empty;
        }

        WorkspaceGrid.Visibility =
            Visibility.Collapsed;
        HistoryWorkspaceGrid.Visibility =
            Visibility.Collapsed;
        ContextAtlasWorkspaceGrid.Visibility =
            Visibility.Collapsed;
        CorpusSearchWorkspaceGrid.Visibility =
            Visibility.Collapsed;
        WorkingSliceWorkspaceGrid.Visibility =
            Visibility.Visible;

        // Explicit navigation to a particular slice is a deliberate state
        // change. Plain workspace re-entry resumes the existing live editor.
        if (WorkingSliceNavigation.ShouldRefresh(
            _workingSliceWorkspaceInitialized, preferredId))
        {
            RefreshWorkingSliceList(preferredId);
            _workingSliceWorkspaceInitialized = true;
        }

        UpdateWorkspaceNavigationState();

        StatusText.Text =
            "Working Slice / Reconciliation · persistent research workspace.";
    }

    private void CloseWorkingSlices_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowResearchWorkspace();

        StatusText.Text =
            $"{_chapters[_currentSurah - 1].DisplayName} · research workspace";
    }

    private async void NewWorkingSlice_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CreateWorkingSliceFromSelectedContext();
    }

    private async void CreateWorkingSliceFromContext_Click(
        object sender,
        RoutedEventArgs e)
    {
        await CreateWorkingSliceFromSelectedContext();
    }

    private async Task CreateWorkingSliceFromSelectedContext(
        int? preferredAyah = null)
    {
        if (_selectedContextId is not long contextId)
        {
            await ShowWorkingSliceMessage(
                "Select a Context Block",
                "Choose a Context Block in the Research Viewer before creating a Working Slice.");
            return;
        }

        ContextBlock block;

        try
        {
            block =
                _contexts.GetBlock(contextId);
        }
        catch (Exception ex)
        {
            await ShowWorkingSliceMessage(
                "Context unavailable",
                ex.Message);
            return;
        }

        int start =
            preferredAyah is int ayah &&
            ayah >= block.StartAyah &&
            ayah <= block.EndAyah
                ? ayah
                : block.StartAyah;

        int end =
            preferredAyah is int
                ? start
                : block.EndAyah;

        await CreateWorkingSlice(
            block,
            start,
            end);
    }

    private async Task CreateWorkingSlice(
        ContextBlock block,
        int defaultStart,
        int defaultEnd)
    {
        ChapterSummary chapter =
            _chapters[block.SurahNumber - 1];

        var titleBox =
            new TextBox
            {
                Header = "Working Slice title",
                PlaceholderText =
                    $"{chapter.NameSimple} · {defaultStart}–{defaultEnd}"
            };

        var startBox =
            new TextBox
            {
                Header = "Start ayah",
                Text = defaultStart.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            };

        var endBox =
            new TextBox
            {
                Header = "End ayah",
                Text = defaultEnd.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            };

        AttachTextBoxContextMenu(titleBox);
        AttachTextBoxContextMenu(startBox);
        AttachTextBoxContextMenu(endBox);

        var rangeGrid =
            new Grid
            {
                ColumnSpacing = 10
            };

        rangeGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        rangeGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        Grid.SetColumn(
            endBox,
            1);

        rangeGrid.Children.Add(
            startBox);
        rangeGrid.Children.Add(
            endBox);

        var content =
            new StackPanel
            {
                Spacing = 10,
                MinWidth = 500
            };

        content.Children.Add(
            new TextBlock
            {
                Text =
                    $"{chapter.DisplayName} · parent {block.RangeLabel} · {block.Status}",
                Foreground =
                    Brush("MutedTextBrush"),
                TextWrapping =
                    TextWrapping.Wrap
            });

        content.Children.Add(
            titleBox);

        content.Children.Add(
            rangeGrid);

        content.Children.Add(
            new TextBlock
            {
                Text =
                    "The slice range is saved as its own durable research definition. Multiple Working Slices may belong to this Context Block.",
                Foreground =
                    Brush("MutedTextBrush"),
                FontSize = 12,
                TextWrapping =
                    TextWrapping.Wrap
            });

        var dialog =
            new ContentDialog
            {
                XamlRoot =
                    RootGrid.XamlRoot,
                Title =
                    "Create Working Slice",
                Content =
                    content,
                PrimaryButtonText =
                    "Create",
                CloseButtonText =
                    "Cancel",
                DefaultButton =
                    ContentDialogButton.Primary
            };

        ContentDialogResult result =
            await dialog.ShowAsync();

        if (result !=
            ContentDialogResult.Primary)
        {
            return;
        }

        if (!int.TryParse(
                startBox.Text,
                out int startAyah) ||
            !int.TryParse(
                endBox.Text,
                out int endAyah))
        {
            await ShowWorkingSliceMessage(
                "Invalid ayah range",
                "Enter valid numeric start and end ayah values.");
            return;
        }

        string title =
            string.IsNullOrWhiteSpace(
                titleBox.Text)
                ? $"{chapter.NameSimple} {block.SurahNumber}:{startAyah}" +
                  (startAyah == endAyah
                      ? string.Empty
                      : $"–{endAyah}")
                : titleBox.Text.Trim();

        try
        {
            WorkingSlice created =
                _workingSlices.Create(
                    new WorkingSliceCreateRequest(
                        block.SurahNumber,
                        block.Id,
                        startAyah,
                        endAyah,
                        title));

            RefreshResearchHistory(
                force: true);

            OpenWorkingSlices(
                created.Id);

            StatusText.Text =
                $"Working Slice created · {created.Title} · {created.RangeLabel}.";
        }
        catch (Exception ex)
        {
            await ShowWorkingSliceMessage(
                "Working Slice not created",
                ex.Message);
        }
    }

    private void WorkingSliceFilter_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (WorkingSliceFilter.SelectedItem
                is ComboBoxItem item &&
            item.Tag?.ToString()
                is string filter)
        {
            _workingSliceFilter =
                filter;
        }
        else
        {
            _workingSliceFilter =
                "All";
        }

        if (_ready &&
            WorkingSliceWorkspaceGrid.Visibility ==
                Visibility.Visible)
        {
            RefreshWorkingSliceList(
                _selectedWorkingSliceId);
        }
    }

    private void WorkingSliceSearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_loadingWorkingSliceUi)
        {
            return;
        }

        _workingSliceQuery =
            WorkingSliceSearchTextBox.Text?.Trim()
            ?? string.Empty;

        if (_ready &&
            WorkingSliceWorkspaceGrid.Visibility ==
                Visibility.Visible)
        {
            RefreshWorkingSliceList(
                _selectedWorkingSliceId);
        }
    }

    private void WorkingSliceSort_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (WorkingSliceSortSelector.SelectedItem
                is ComboBoxItem item &&
            item.Tag?.ToString() is string sort)
        {
            _workingSliceSort =
                sort is "Recent" or "Status"
                    ? sort
                    : "Research";
        }
        else
        {
            _workingSliceSort =
                "Research";
        }

        if (_ready &&
            WorkingSliceWorkspaceGrid.Visibility ==
                Visibility.Visible)
        {
            RefreshWorkingSliceList(
                _selectedWorkingSliceId);
        }
    }

    private void RefreshWorkingSliceList(
        long? preferredId = null)
    {
        RememberWorkingSliceDraft();
        if (_workingSliceDirty) TryPersistRecoverableDrafts();
        if (WorkingSliceList is null)
        {
            return;
        }

        _loadingWorkingSliceUi = true;

        try
        {
            IReadOnlyList<WorkingSlice> slices =
                _workingSlices.List(
                    _workingSliceFilter,
                    _workingSliceQuery,
                    _workingSliceSort);

            WorkingSliceList.Items.Clear();

            long? target =
                preferredId ??
                _selectedWorkingSliceId;

            int lastSurah = -1;
            long lastContext = -1;

            foreach (WorkingSlice slice in slices)
            {
                if (_workingSliceSort == "Research")
                {
                    if (slice.SurahNumber != lastSurah)
                    {
                        AddWorkingSliceNavigatorHeader(
                            _chapters[slice.SurahNumber - 1]
                                .DisplayName,
                            major: true);

                        lastSurah =
                            slice.SurahNumber;

                        lastContext =
                            -1;
                    }

                    if (slice.ContextBlockId !=
                        lastContext)
                    {
                        string contextLabel;

                        try
                        {
                            ContextBlock parent =
                                _contexts.GetBlock(
                                    slice.ContextBlockId);

                            contextLabel =
                                $"Context · {parent.RangeLabel} · {parent.Status}";
                        }
                        catch
                        {
                            contextLabel =
                                $"Context #{slice.ContextBlockId}";
                        }

                        AddWorkingSliceNavigatorHeader(
                            contextLabel,
                            major: false);

                        lastContext =
                            slice.ContextBlockId;
                    }
                }

                var stack =
                    new StackPanel
                    {
                        Spacing = 3
                    };

                stack.Children.Add(
                    new TextBlock
                    {
                        Text =
                            slice.Title,
                        Foreground =
                            Brush("TextBrush"),
                        FontWeight =
                            FontWeights.SemiBold,
                        FontSize = 14,
                        TextWrapping =
                            TextWrapping.Wrap
                    });

                stack.Children.Add(
                    new TextBlock
                    {
                        Text =
                            $"{slice.RangeLabel} · {slice.Status}",
                        Foreground =
                            Brush("MutedTextBrush"),
                        FontSize = 12,
                        TextWrapping =
                            TextWrapping.Wrap
                    });

                stack.Children.Add(
                    new TextBlock
                    {
                        Text =
                            slice.UpdatedLabel,
                        Foreground =
                            Brush("MutedTextBrush"),
                        FontSize = 11.5,
                        TextWrapping =
                            TextWrapping.Wrap
                    });

                var item =
                    new ListViewItem
                    {
                        Content = stack,
                        Tag = slice,
                        Padding =
                            new Thickness(
                                8,
                                7,
                                8,
                                7),
                        HorizontalContentAlignment =
                            HorizontalAlignment.Stretch
                    };

                WorkingSliceList.Items.Add(
                    item);

                if (slice.Id == target)
                {
                    WorkingSliceList.SelectedItem =
                        item;
                }
            }

            if (WorkingSliceList.SelectedItem
                    is null)
            {
                ListViewItem? firstSlice =
                    WorkingSliceList.Items
                        .OfType<ListViewItem>()
                        .FirstOrDefault(
                            item =>
                                item.Tag is WorkingSlice);

                if (firstSlice is not null)
                {
                    WorkingSliceList.SelectedItem =
                        firstSlice;
                }
            }

            WorkingSliceListEmptyText.Visibility =
                slices.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            if (slices.Count == 0)
            {
                _selectedWorkingSliceId =
                    null;
                ClearWorkingSliceEditor();
            }
        }
        finally
        {
            _loadingWorkingSliceUi = false;
        }

        if (WorkingSliceList.SelectedItem
                is ListViewItem selected &&
            selected.Tag is WorkingSlice selectedSlice)
        {
            _selectedWorkingSliceId =
                selectedSlice.Id;

            LoadWorkingSlice(
                selectedSlice.Id);
        }
    }

    private void AddWorkingSliceNavigatorHeader(
        string text,
        bool major)
    {
        WorkingSliceList.Items.Add(
            new ListViewItem
            {
                IsHitTestVisible = false,
                IsTabStop = false,
                Padding =
                    major
                        ? new Thickness(4, 12, 4, 4)
                        : new Thickness(10, 7, 4, 3),
                Content =
                    new TextBlock
                    {
                        Text = text,
                        Foreground =
                            major
                                ? Brush("TextBrush")
                                : Brush("MutedTextBrush"),
                        FontWeight =
                            major
                                ? FontWeights.SemiBold
                                : FontWeights.Normal,
                        FontSize =
                            major
                                ? 14
                                : 12,
                        TextWrapping =
                            TextWrapping.Wrap
                    }
            });
    }

    private void WorkingSliceList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loadingWorkingSliceUi)
        {
            return;
        }

        RememberWorkingSliceDraft();
        TryPersistRecoverableDrafts();
        if (WorkingSliceList.SelectedItem
                is ListViewItem item &&
            item.Tag is WorkingSlice slice)
        {
            _selectedWorkingSliceId =
                slice.Id;

            LoadWorkingSlice(
                slice.Id);
        }
        else
        {
            _selectedWorkingSliceId =
                null;

            ClearWorkingSliceEditor();
        }
    }

    private void LoadWorkingSlice(
        long id)
    {
        _loadingWorkingSliceUi = true;

        try
        {
            WorkingSlice slice =
                _workingSlices.Get(id);

            _selectedWorkingSliceId =
                slice.Id;
            _workingSliceDirty = false;
            WorkingSliceDirtyText.Visibility =
                Visibility.Collapsed;

            WorkingSliceTitleTextBox.Text =
                slice.Title;

            WorkingSliceResearchTextBox.Text =
                slice.ResearchNotes;

            WorkingSliceConclusionTextBox.Text =
                slice.Conclusion;

            string originalParentDescription;
            try
            {
                ContextBlock original = _contexts.GetHistoricalBlock(slice.ContextBlockId);
                bool active = _contexts.IsBlockActive(slice.ContextBlockId);
                originalParentDescription =
                    $"original parent Context #{original.Id} · {original.RangeLabel} · {(active ? "active" : "ARCHIVED")} · {original.Origin}";
            }
            catch
            {
                originalParentDescription =
                    $"original parent Context #{slice.ContextBlockId} · historical record unavailable";
            }

            WorkingSliceMetaText.Text =
                $"{_chapters[slice.SurahNumber - 1].DisplayName} · {slice.RangeLabel} · {GetContextJuzLabel(slice.SurahNumber, slice.StartAyah, slice.EndAyah)} · {originalParentDescription} · updated {slice.UpdatedUtc.LocalDateTime:g}";

            WorkingSliceStatusSelector.SelectedIndex =
                slice.Status switch
                {
                    "Draft" => 0,
                    "In Review" => 1,
                    "Resolved" => 2,
                    _ => 0
                };

            WorkingSliceSaveButton.IsEnabled =
                false;

            WorkingSliceDiscardButton.IsEnabled =
                false;

            WorkingSliceRemoveButton.IsEnabled =
                true;

            WorkingSliceOpenParentButton.IsEnabled =
                true;

            if (_workingSliceDrafts.TryGetValue(slice.Id, out WorkingSliceDraft? draft))
            {
                WorkingSliceTitleTextBox.Text = draft.Title;
                WorkingSliceResearchTextBox.Text = draft.ResearchNotes;
                WorkingSliceConclusionTextBox.Text = draft.Conclusion;
                WorkingSliceStatusSelector.SelectedIndex = draft.Status switch
                {
                    "Draft" => 0,
                    "In Review" => 1,
                    "Resolved" => 2,
                    _ => 0
                };
                _workingSliceDirty = true;
                WorkingSliceDirtyText.Visibility = Visibility.Visible;
                WorkingSliceSaveButton.IsEnabled = true;
                WorkingSliceDiscardButton.IsEnabled = true;
            }

            FillWorkingSliceRevisions(
                _workingSlices.GetRevisions(
                    slice.Id,
                    12));

            FillWorkingSliceLinkedNotes(
                slice);

            RefreshWorkingSliceEvidence(
                preserveOffset: false);
        }
        catch (Exception ex)
        {
            ClearWorkingSliceEditor();

            StatusText.Text =
                $"Working Slice unavailable: {ex.Message}";
        }
        finally
        {
            _loadingWorkingSliceUi = false;
        }
    }

    private void ClearWorkingSliceEditor()
    {
        _workingSliceDirty = false;
        WorkingSliceDirtyText.Visibility =
            Visibility.Collapsed;

        WorkingSliceMetaText.Text =
            "Select or create a Working Slice.";

        WorkingSliceTitleTextBox.Text =
            string.Empty;

        WorkingSliceStatusSelector.SelectedIndex =
            -1;

        WorkingSliceResearchTextBox.Text =
            string.Empty;

        WorkingSliceConclusionTextBox.Text =
            string.Empty;

        WorkingSliceRevisionPanel.Items.Clear();
        WorkingSliceLinkedNotesPanel.Children.Clear();
        ClearWorkingSliceZoomTargets();
        _workingSliceEvidenceVerses =
            Array.Empty<VerseBundle>();
        _workingSliceEvidenceDefinition =
            null;
        _workingSliceRenderedVerseCount =
            0;
        WorkingSliceEvidencePanel.Children.Clear();

        WorkingSliceSaveButton.IsEnabled =
            false;

        WorkingSliceDiscardButton.IsEnabled =
            false;

        WorkingSliceRemoveButton.IsEnabled =
            false;

        WorkingSliceOpenParentButton.IsEnabled =
            false;
    }

    private void WorkingSliceEditorChanged(
        object sender,
        object e)
    {
        if (_loadingWorkingSliceUi)
        {
            return;
        }

        bool hasSlice =
            _selectedWorkingSliceId
                is not null;

        _workingSliceDirty = hasSlice;

        WorkingSliceDirtyText.Visibility =
            hasSlice
                ? Visibility.Visible
                : Visibility.Collapsed;

        WorkingSliceSaveButton.IsEnabled =
            hasSlice;

        WorkingSliceDiscardButton.IsEnabled =
            hasSlice && _workingSliceDirty;

        WorkingSliceRemoveButton.IsEnabled =
            hasSlice;

        WorkingSliceOpenParentButton.IsEnabled =
            hasSlice;

        if (hasSlice) ScheduleRecoverableDraftSave();
    }

    private void WorkingSliceStatusSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        WorkingSliceEditorChanged(
            sender,
            e);
    }

    private void WorkingSliceTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        WorkingSliceEditorChanged(
            sender,
            e);
    }

    private void SaveWorkingSlice_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveActiveWorkingSlice();
    }

    private void SaveActiveWorkingSlice()
    {
        if (_ownerStateOperationActive)
        {
            StatusText.Text = "Save blocked while an owner-data operation is active.";
            return;
        }

        if (_selectedWorkingSliceId
                is not long id ||
            WorkingSliceStatusSelector.SelectedItem
                is not ComboBoxItem statusItem ||
            statusItem.Tag?.ToString()
                is not string status)
        {
            StatusText.Text =
                "Select a Working Slice before saving.";
            return;
        }

        try
        {
            bool changed =
                _workingSlices.Save(
                    new WorkingSliceSaveRequest(
                        id,
                        WorkingSliceTitleTextBox.Text,
                        status,
                        WorkingSliceResearchTextBox.Text,
                        WorkingSliceConclusionTextBox.Text));

            _workingSliceDirty = false;
            _workingSliceDrafts.Remove(id);
            WorkingSliceDirtyText.Visibility =
                Visibility.Collapsed;

            RefreshWorkingSliceList(
                id);

            RefreshResearchHistory(
                force: true);

            StatusText.Text =
                changed
                    ? "Working Slice saved; the previous state is preserved in revision history."
                    : "Working Slice is unchanged.";

            TryPersistRecoverableDrafts();
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Could not save Working Slice: {ex.Message}";
        }
    }

    private void DiscardWorkingSliceChanges_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedWorkingSliceId is not long id)
        {
            StatusText.Text =
                "Select a Working Slice before discarding changes.";
            return;
        }

        if (!_workingSliceDirty)
        {
            StatusText.Text =
                "Working Slice has no unsaved changes.";
            return;
        }

        _workingSliceDrafts.Remove(id);
        LoadWorkingSlice(id);

        StatusText.Text =
            "Unsaved Working Slice changes discarded; the last saved state was reloaded.";
        TryPersistRecoverableDrafts();
    }

    private async void RemoveWorkingSlice_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedWorkingSliceId is not long id)
        {
            StatusText.Text =
                "Select a Working Slice before removing it.";
            return;
        }

        WorkingSlice slice;

        try
        {
            slice =
                _workingSlices.Get(id);
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Working Slice unavailable: {ex.Message}";
            return;
        }

        bool hasUnsaved =
            _workingSliceDirty;

        var message =
            new TextBlock
            {
                Text =
                    hasUnsaved
                        ? "This Working Slice contains unsaved text/data. Removing it will permanently discard those unsaved changes and remove the Working Slice from active use. The last saved Working Slice record and its saved revision history will remain preserved in History.\n\nThis does not hard-delete the research record."
                        : "This removes the Working Slice from active use without hard-deleting its research record. Its last saved state and saved revision history remain preserved in History.",
                TextWrapping =
                    TextWrapping.Wrap,
                MinWidth = 440
            };

        var dialog =
            new ContentDialog
            {
                XamlRoot =
                    RootGrid.XamlRoot,
                Title =
                    $"Remove Working Slice — {slice.Title}",
                Content =
                    message,
                PrimaryButtonText =
                    hasUnsaved
                        ? "Remove & discard unsaved changes"
                        : "Remove Working Slice",
                CloseButtonText =
                    "Cancel",
                DefaultButton =
                    ContentDialogButton.Close
            };

        ContentDialogResult result =
            await dialog.ShowAsync();

        if (result !=
            ContentDialogResult.Primary)
        {
            StatusText.Text =
                "Working Slice removal cancelled.";
            return;
        }

        try
        {
            WorkingSlice removed =
                _workingSlices.Remove(id);

            _workingSliceDrafts.Remove(id);
            _selectedWorkingSliceId =
                null;

            _workingSliceDirty =
                false;
            WorkingSliceDirtyText.Visibility =
                Visibility.Collapsed;

            RefreshWorkingSliceList();
            RefreshResearchHistory(
                force: true);

            StatusText.Text =
                hasUnsaved
                    ? $"Removed “{removed.Title}” from active Working Slices; unsaved changes were deliberately discarded and saved history was preserved."
                    : $"Removed “{removed.Title}” from active Working Slices; saved history was preserved.";

            TryPersistRecoverableDrafts();
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Could not remove Working Slice: {ex.Message}";
        }
    }

    private async void WorkingSliceOpenParent_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_selectedWorkingSliceId is not long id)
        {
            return;
        }

        try
        {
            WorkingSlice slice = _workingSlices.Get(id);

            if (!_contexts.IsBlockActive(slice.ContextBlockId))
            {
                ContextBlock original =
                    _contexts.GetHistoricalBlock(slice.ContextBlockId);

                var dialog = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = "Original parent Context is archived",
                    Content = new TextBlock
                    {
                        Text =
                            $"This Working Slice was created under original Context #{original.Id}, Surah {original.SurahNumber}, {original.RangeLabel} ({original.Status}, {original.Origin}). That original identity remains attached to this research. The current Context Map may have different boundaries. No reparenting will occur.",
                        TextWrapping = TextWrapping.Wrap,
                        MinWidth = 420
                    },
                    PrimaryButtonText = "Open current Context instead",
                    CloseButtonText = "Stay with original research",
                    DefaultButton = ContentDialogButton.Close
                };

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    StatusText.Text =
                        $"Historical parent Context #{original.Id} preserved; no navigation or reparenting.";
                    return;
                }

                NavigateToResearchTarget(
                    slice.SurahNumber, slice.StartAyah);
                return;
            }

            NavigateToResearchTarget(
                slice.SurahNumber,
                slice.StartAyah,
                slice.ContextBlockId);
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Could not open parent Context: {ex.Message}";
        }
    }

    private void FillWorkingSliceRevisions(
        IReadOnlyList<WorkingSliceRevision>
            revisions)
    {
        WorkingSliceRevisionPanel.Items.Clear();

        if (revisions.Count == 0)
        {
            WorkingSliceRevisionPanel.Items.Add(
                new TextBlock
                {
                    Text =
                        "No earlier saved Working Slice revisions yet.",
                    Foreground =
                        Brush("MutedTextBrush"),
                    FontStyle =
                        Windows.UI.Text.FontStyle.Italic,
                    FontSize = 12,
                    TextWrapping =
                        TextWrapping.Wrap
                });

            return;
        }

        foreach (WorkingSliceRevision revision
                 in revisions)
        {
            var stack =
                new StackPanel
                {
                    Spacing = 6
                };

            stack.Children.Add(
                new TextBlock
                {
                    Text =
                        $"Title: {revision.PriorTitle}",
                    Foreground =
                        Brush("TextBrush"),
                    FontWeight =
                        FontWeights.SemiBold,
                    TextWrapping =
                        TextWrapping.Wrap
                });

            if (!string.IsNullOrWhiteSpace(
                    revision.PriorResearchNotes))
            {
                stack.Children.Add(
                    CreateSelectableText(
                        revision.PriorResearchNotes,
                        13,
                        20));
            }

            if (!string.IsNullOrWhiteSpace(
                    revision.PriorConclusion))
            {
                stack.Children.Add(
                    new TextBlock
                    {
                        Text =
                            "Conclusion",
                        Foreground =
                            Brush("AccentBrush"),
                        FontWeight =
                            FontWeights.SemiBold,
                        Margin =
                            new Thickness(
                                0,
                                5,
                                0,
                                0)
                    });

                stack.Children.Add(
                    CreateSelectableText(
                        revision.PriorConclusion,
                        13,
                        20));
            }

            WorkingSliceRevisionPanel.Items.Add(
                new Expander
                {
                    Header =
                        $"Revision {revision.Id} · {revision.ChangeSummary}",
                    IsExpanded = false,
                    Margin =
                        new Thickness(
                            0,
                            2,
                            0,
                            2),
                    Content =
                        new Border
                        {
                            Background =
                                Brush("PanelBrush"),
                            BorderBrush =
                                Brush("BorderBrush"),
                            BorderThickness =
                                new Thickness(1),
                            CornerRadius =
                                new CornerRadius(5),
                            Padding =
                                new Thickness(10),
                            Child = stack
                        }
                });
        }
    }

    private void FillWorkingSliceLinkedNotes(
        WorkingSlice slice)
    {
        WorkingSliceLinkedNotesPanel.Children.Clear();

        ResearchNote? contextNote =
            _notes.GetContextNote(
                slice.ContextBlockId);

        if (contextNote is not null)
        {
            WorkingSliceLinkedNotesPanel.Children.Add(
                new TextBlock
                {
                    Text =
                        "Context note",
                    Foreground =
                        Brush("AccentBrush"),
                    FontWeight =
                        FontWeights.SemiBold
                });

            WorkingSliceLinkedNotesPanel.Children.Add(
                CreateSelectableText(
                    contextNote.Body,
                    13.5,
                    21));
        }

        int ayahNotes = 0;

        for (int ayah =
                 slice.StartAyah;
             ayah <= slice.EndAyah;
             ayah++)
        {
            ResearchNote? note =
                _notes.GetAyahNote(
                    slice.SurahNumber,
                    ayah);

            if (note is null)
            {
                continue;
            }

            ayahNotes++;

            WorkingSliceLinkedNotesPanel.Children.Add(
                new TextBlock
                {
                    Text =
                        $"Ayah {slice.SurahNumber}:{ayah}",
                    Foreground =
                        Brush("AccentBrush"),
                    FontWeight =
                        FontWeights.SemiBold,
                    Margin =
                        new Thickness(
                            0,
                            7,
                            0,
                            0)
                });

            WorkingSliceLinkedNotesPanel.Children.Add(
                CreateSelectableText(
                    note.Body,
                    13.5,
                    21));
        }

        if (contextNote is null &&
            ayahNotes == 0)
        {
            WorkingSliceLinkedNotesPanel.Children.Add(
                new TextBlock
                {
                    Text =
                        "No linked Ayah or Context notes in this slice yet.",
                    Foreground =
                        Brush("MutedTextBrush"),
                    FontStyle =
                        Windows.UI.Text.FontStyle.Italic,
                    TextWrapping =
                        TextWrapping.Wrap
                });
        }
    }

    private void RefreshWorkingSliceEvidence(
        bool preserveOffset)
    {
        if (_refreshingWorkingSliceEvidence ||
            WorkingSliceWorkspaceGrid.Visibility !=
                Visibility.Visible ||
            _selectedWorkingSliceId
                is not long id)
        {
            return;
        }

        _refreshingWorkingSliceEvidence = true;

        double priorOffset =
            WorkingSliceEvidenceScrollViewer
                .VerticalOffset;

        int previouslyRendered =
            preserveOffset
                ? _workingSliceRenderedVerseCount
                : 0;

        try
        {
            WorkingSlice slice =
                _workingSlices.Get(id);

            IReadOnlyList<VerseBundle> chapter =
                GetChapterCached(
                    slice.SurahNumber);

            List<VerseBundle> verses =
                chapter
                    .Where(
                        verse =>
                            verse.VerseNumber >=
                                slice.StartAyah &&
                            verse.VerseNumber <=
                                slice.EndAyah)
                    .ToList();

            ClearWorkingSliceZoomTargets();

            _workingSliceEvidenceDefinition =
                slice;
            _workingSliceEvidenceVerses =
                verses;
            _workingSliceRenderedVerseCount =
                0;

            WorkingSliceEvidencePanel.Children.Clear();

            var heading =
                new TextBlock
                {
                    Text =
                        $"Evidence · {_chapters[slice.SurahNumber - 1].DisplayName} · {slice.RangeLabel} · {GetContextJuzLabel(slice.SurahNumber, slice.StartAyah, slice.EndAyah)}",
                    Foreground =
                        Brush("TextBrush"),
                    FontSize =
                        Z(22),
                    FontWeight =
                        FontWeights.SemiBold,
                    TextWrapping =
                        TextWrapping.Wrap,
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            12)
                };

            WorkingSliceEvidencePanel.Children.Add(
                heading);

            CaptureWorkingSliceZoomTargets(
                heading);

            int initialCount =
                _selectedTafsirIds.Count == 0 &&
                verses.Count <= 30
                    ? verses.Count
                    : Math.Min(
                        CurrentInitialVerseBatch,
                        verses.Count);

            if (preserveOffset)
            {
                initialCount =
                    Math.Max(
                        initialCount,
                        Math.Min(
                            previouslyRendered,
                            verses.Count));
            }

            AppendWorkingSliceEvidenceBatch(
                initialCount);

            if (preserveOffset)
            {
                RootGrid.DispatcherQueue.TryEnqueue(
                    () =>
                    {
                        WorkingSliceEvidenceScrollViewer
                            .ChangeView(
                                null,
                                Math.Min(
                                    priorOffset,
                                    WorkingSliceEvidenceScrollViewer
                                        .ScrollableHeight),
                                null,
                                disableAnimation: true);
                    });
            }
            else
            {
                WorkingSliceEvidenceScrollViewer
                    .ChangeView(
                        0,
                        0,
                        null,
                        disableAnimation: true);
            }
        }
        catch (Exception ex)
        {
            ClearWorkingSliceZoomTargets();
            _workingSliceEvidenceVerses =
                Array.Empty<VerseBundle>();
            _workingSliceEvidenceDefinition =
                null;
            _workingSliceRenderedVerseCount =
                0;
            WorkingSliceEvidencePanel.Children.Clear();

            WorkingSliceEvidencePanel.Children.Add(
                CreateErrorCard(
                    ex.Message));
        }
        finally
        {
            _refreshingWorkingSliceEvidence = false;
        }
    }

    private void AppendWorkingSliceEvidenceBatch(
        int? requestedCount = null)
    {
        if (_appendingWorkingSliceEvidence ||
            _workingSliceEvidenceDefinition
                is not WorkingSlice slice ||
            _workingSliceRenderedVerseCount >=
                _workingSliceEvidenceVerses.Count)
        {
            return;
        }

        _appendingWorkingSliceEvidence = true;

        try
        {
            int batch =
                requestedCount ??
                CurrentNextVerseBatch;

            int end =
                Math.Min(
                    _workingSliceEvidenceVerses.Count,
                    _workingSliceRenderedVerseCount +
                        Math.Max(1, batch));

            for (int i =
                     _workingSliceRenderedVerseCount;
                 i < end;
                 i++)
            {
                Border card =
                    CreateWorkingSliceEvidenceCard(
                        slice,
                        _workingSliceEvidenceVerses[i]);

                WorkingSliceEvidencePanel.Children.Add(
                    card);

                CaptureWorkingSliceZoomTargets(
                    card);
            }

            _workingSliceRenderedVerseCount =
                end;
        }
        finally
        {
            _appendingWorkingSliceEvidence = false;
        }
    }

    private void WorkingSliceEvidenceScrollViewer_ViewChanged(
        object sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (_refreshingWorkingSliceEvidence ||
            _appendingWorkingSliceEvidence ||
            WorkingSliceWorkspaceGrid.Visibility !=
                Visibility.Visible ||
            _workingSliceRenderedVerseCount >=
                _workingSliceEvidenceVerses.Count)
        {
            return;
        }

        double remaining =
            WorkingSliceEvidenceScrollViewer
                .ScrollableHeight -
            WorkingSliceEvidenceScrollViewer
                .VerticalOffset;

        double threshold =
            Math.Max(
                900,
                WorkingSliceEvidenceScrollViewer
                    .ViewportHeight * 1.5);

        if (remaining <= threshold)
        {
            AppendWorkingSliceEvidenceBatch();
        }
    }

    private Border CreateWorkingSliceEvidenceCard(
        WorkingSlice slice,
        VerseBundle verse)
    {
        var content =
            new StackPanel
            {
                Spacing = 8
            };

        if (_juz.IsJuzStart(
                slice.SurahNumber,
                verse.VerseNumber,
                out int juzNumber))
        {
            content.Children.Add(
                CreateWorkingSliceJuzBoundaryMarker(
                    juzNumber));
        }

        var heading =
            new Grid();

        heading.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        heading.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        heading.Children.Add(
            new TextBlock
            {
                Text =
                    $"Ayah / আয়াত · {verse.VerseKey}",
                Foreground =
                    Brush("AccentBrush"),
                FontWeight =
                    FontWeights.SemiBold,
                FontSize =
                    Z(16),
                VerticalAlignment =
                    VerticalAlignment.Center
            });

        var wbwButton =
            new Button
            {
                Content =
                    "Word-by-word",
                Tag = verse,
                Padding =
                    new Thickness(
                        10,
                        4,
                        10,
                        4)
            };

        wbwButton.Click +=
            WorkingSliceWordByWord_Click;

        Grid.SetColumn(
            wbwButton,
            1);

        heading.Children.Add(
            wbwButton);

        content.Children.Add(
            heading);

        AddWorkingSliceArabic(
            content,
            slice,
            verse);

        if (_selectedTranslationIds.Count > 0)
        {
            content.Children.Add(
                CreateWorkingSliceSectionHeading(
                    "Translations / অনুবাদ"));

            content.Children.Add(
                CreateWorkingSliceSourceArea(
                    slice,
                    verse,
                    verse.Translations));
        }

        if (_selectedTafsirIds.Count > 0)
        {
            content.Children.Add(
                CreateWorkingSliceSectionHeading(
                    "Tafsir / তাফসীর"));

            content.Children.Add(
                CreateWorkingSliceSourceArea(
                    slice,
                    verse,
                    verse.Tafsirs));
        }

        var card =
            new Border
            {
                Background =
                    Brush("CardBrush"),
                BorderBrush =
                    Brush("BorderBrush"),
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(8),
                Padding =
                    new Thickness(18),
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        14),
                Child = content
            };

        card.ContextFlyout =
            BuildAyahContextMenu(
                slice,
                verse,
                card);

        return card;
    }

    private void AddWorkingSliceArabic(
        StackPanel content,
        WorkingSlice slice,
        VerseBundle verse)
    {
        var scripts =
            new List<(string Label, string Text, string FontRole)>();

        if (UthmaniToggle.IsChecked == true)
        {
            scripts.Add(
                ("Uthmani", verse.Uthmani, "Uthmani"));
        }

        if (IndoPakToggle.IsChecked == true)
        {
            scripts.Add(
                ("IndoPak", verse.IndoPak, "IndoPak"));
        }

        if (NastaleeqToggle.IsChecked == true)
        {
            scripts.Add(
                ("IndoPak Nastaleeq", verse.IndoPakNastaleeq, "IndoPak"));
        }

        foreach (var script in scripts)
        {
            var arabic =
                new TextBlock
                {
                    Text = script.Text,
                    Foreground =
                        Brush("TextBrush"),
                    FontSize =
                        Z(script.FontRole == "IndoPak" ? 37 : 38),
                    LineHeight =
                        Z(script.FontRole == "IndoPak" ? 82 : 68),
                    TextWrapping =
                        TextWrapping.Wrap,
                    TextAlignment =
                        TextAlignment.Center,
                    FlowDirection =
                        FlowDirection.RightToLeft,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,
                    Margin =
                        new Thickness(
                            0,
                            7,
                            0,
                            7),
                    IsTextSelectionEnabled =
                        true,
                    SelectionHighlightColor =
                        Brush("AccentBrush")
                };

            if (script.FontRole == "IndoPak" &&
                File.Exists(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Fonts",
                        "indopak-nastaleeq-waqf-lazim-v4.2.1.ttf")))
            {
                arabic.FontFamily =
                    new FontFamily(
                        "Fonts/indopak-nastaleeq-waqf-lazim-v4.2.1.ttf#AlQuran IndoPak by QuranWBW");
            }
            else if (script.FontRole == "Uthmani" &&
                File.Exists(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Fonts",
                        "UthmanicHafs1Ver18.ttf")))
            {
                arabic.FontFamily =
                    new FontFamily(
                        "Fonts/UthmanicHafs1Ver18.ttf#KFGQPC HAFS Uthmanic Script");
            }

            arabic.ContextFlyout =
                BuildAyahContextMenu(
                    slice,
                    verse,
                    arabic,
                    arabic,
                    arabic);

            content.Children.Add(
                new Border
                {
                    Background =
                        Brush("PanelAltBrush"),
                    BorderBrush =
                        Brush("BorderBrush"),
                    BorderThickness =
                        new Thickness(1),
                    CornerRadius =
                        new CornerRadius(6),
                    Padding =
                        new Thickness(14),
                    Child =
                        new StackPanel
                        {
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = script.Label,
                                    Foreground =
                                        Brush("MutedTextBrush"),
                                    FontWeight =
                                        FontWeights.SemiBold,
                                    FontSize =
                                        Z(13)
                                },
                                arabic
                            }
                        }
                });
        }
    }

    private TextBlock CreateWorkingSliceSectionHeading(
        string text) =>
        new()
        {
            Text = text,
            Foreground =
                Brush("TextBrush"),
            FontWeight =
                FontWeights.SemiBold,
            FontSize =
                Z(16),
            Margin =
                new Thickness(
                    0,
                    12,
                    0,
                    4)
        };

    private UIElement CreateWorkingSliceSourceArea(
        WorkingSlice slice,
        VerseBundle verse,
        IReadOnlyList<SourceText> sources)
    {
        var grid =
            new Grid
            {
                ColumnSpacing = 12
            };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        StackPanel en =
            CreateWorkingSliceSourceColumn(
                slice,
                verse,
                "English",
                sources.Where(
                    x => !x.IsBengali));

        StackPanel bn =
            CreateWorkingSliceSourceColumn(
                slice,
                verse,
                "বাংলা · Bengali",
                sources.Where(
                    x => x.IsBengali));

        Grid.SetColumn(
            bn,
            1);

        grid.Children.Add(
            en);
        grid.Children.Add(
            bn);

        return grid;
    }

    private StackPanel CreateWorkingSliceSourceColumn(
        WorkingSlice slice,
        VerseBundle verse,
        string heading,
        IEnumerable<SourceText> sources)
    {
        var stack =
            new StackPanel
            {
                Spacing = 5
            };

        stack.Children.Add(
            new TextBlock
            {
                Text = heading,
                Foreground =
                    Brush("MutedTextBrush"),
                FontWeight =
                    FontWeights.SemiBold,
                FontSize =
                    Z(13)
            });

        List<SourceText> items =
            sources.ToList();

        if (items.Count == 0)
        {
            stack.Children.Add(
                new TextBlock
                {
                    Text =
                        "No selected source for this language.",
                    Foreground =
                        Brush("MutedTextBrush"),
                    FontStyle =
                        Windows.UI.Text.FontStyle.Italic,
                    TextWrapping =
                        TextWrapping.Wrap,
                    FontSize =
                        Z(13)
                });

            return stack;
        }

        foreach (SourceText item in items)
        {
            string label =
                item.DisplayType == "Translation"
                    ? item.ResourceName
                    : $"{item.ResourceName} · {item.DisplayType}";

            if (!string.IsNullOrWhiteSpace(
                    item.ScopeLabel))
            {
                label +=
                    $" · scope {item.ScopeLabel}";
            }

            stack.Children.Add(
                new TextBlock
                {
                    Text = label,
                    Foreground =
                        Brush("AccentBrush"),
                    FontWeight =
                        FontWeights.SemiBold,
                    FontSize =
                        Z(13),
                    TextWrapping =
                        TextWrapping.Wrap,
                    Margin =
                        new Thickness(
                            0,
                            5,
                            0,
                            0)
                });

            var text =
                new TextBlock
                {
                    Text = item.Text,
                    Foreground =
                        Brush("TextBrush"),
                    FontSize =
                        Z(item.IsBengali ? 17 : 16),
                    LineHeight =
                        Z(item.IsBengali ? 28 : 26),
                    TextWrapping =
                        TextWrapping.Wrap,
                    IsTextSelectionEnabled =
                        true,
                    SelectionHighlightColor =
                        Brush("AccentBrush")
                };

            if (item.IsBengali)
            {
                text.FontFamily =
                    new FontFamily(
                        "Nirmala UI");
            }

            text.ContextFlyout =
                BuildAyahContextMenu(
                    slice,
                    verse,
                    text,
                    text);

            stack.Children.Add(
                text);

            if (item.Footnotes.Count > 0)
            {
                var notes =
                    new StackPanel
                    {
                        Spacing = 6
                    };

                foreach (SourceFootnote note
                         in item.Footnotes)
                {
                    var noteText =
                        new TextBlock
                        {
                            Text =
                                $"[{note.Marker}] {note.Text}",
                            Foreground =
                                Brush("MutedTextBrush"),
                            FontSize =
                                Z(13),
                            LineHeight =
                                Z(21),
                            TextWrapping =
                                TextWrapping.Wrap,
                            IsTextSelectionEnabled =
                                true,
                            SelectionHighlightColor =
                                Brush("AccentBrush")
                        };

                    noteText.ContextFlyout =
                        BuildAyahContextMenu(
                            slice,
                            verse,
                            noteText,
                            noteText);

                    notes.Children.Add(
                        noteText);
                }

                stack.Children.Add(
                    new Expander
                    {
                        Header =
                            $"Footnotes / পাদটীকা · {item.Footnotes.Count}",
                        Content =
                            notes,
                        IsExpanded =
                            false,
                        Margin =
                            new Thickness(
                                0,
                                4,
                                0,
                                2)
                    });
            }
        }

        return stack;
    }

    private MenuFlyout BuildAyahContextMenu(
        WorkingSlice slice,
        VerseBundle verse,
        FrameworkElement anchor,
        TextBlock? sourceText = null,
        TextBlock? wordSelectionSource = null)
    {
        var menu =
            new MenuFlyout();

        MenuFlyoutItem? copySelection =
            null;

        if (sourceText is not null)
        {
            copySelection =
                new MenuFlyoutItem
                {
                    Text = "Copy selection"
                };

            copySelection.Click +=
                (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(
                            sourceText.SelectedText))
                    {
                        return;
                    }

                    CopyTextToClipboard(
                        sourceText.SelectedText);
                };

            menu.Items.Add(
                copySelection);

            menu.Items.Add(
                new MenuFlyoutSeparator());
        }

        var copyRef =
            new MenuFlyoutItem
            {
                Text =
                    "Copy ayah reference"
            };

        copyRef.Click +=
            (_, _) =>
                CopyTextToClipboard(
                    $"Qur'an {verse.VerseKey}");

        menu.Items.Add(
            copyRef);

        var openResearch =
            new MenuFlyoutItem
            {
                Text =
                    "Open ayah in Research"
            };

        openResearch.Click +=
            (_, _) =>
                NavigateToResearchTarget(
                    slice.SurahNumber,
                    verse.VerseNumber,
                    slice.ContextBlockId);

        menu.Items.Add(
            openResearch);

        var note =
            new MenuFlyoutItem
            {
                Text =
                    "Ayah Note"
            };

        note.Click +=
            (_, _) =>
            {
                NavigateToResearchTarget(
                    slice.SurahNumber,
                    verse.VerseNumber,
                    slice.ContextBlockId);

                SelectAyahNoteTarget(
                    slice.SurahNumber,
                    verse.VerseNumber);

                AyahNoteExpander.IsExpanded =
                    true;
            };

        menu.Items.Add(
            note);

        var bookmark =
            new MenuFlyoutItem
            {
                Text =
                    "Bookmark ayah"
            };

        bookmark.Click +=
            (_, _) =>
            {
                BookmarkEntry? existing =
                    _bookmarks.Get(
                        slice.SurahNumber,
                        verse.VerseNumber);

                _bookmarks.Save(
                    slice.SurahNumber,
                    verse.VerseNumber,
                    existing?.Title ??
                        $"{_chapters[slice.SurahNumber - 1].NameSimple} {verse.VerseKey}",
                    existing?.Note ??
                        string.Empty);

                RefreshResearchHistory(
                    force: true);

                StatusText.Text =
                    $"Bookmark saved for {verse.VerseKey}.";
            };

        menu.Items.Add(
            bookmark);

        menu.Items.Add(
            new MenuFlyoutSeparator());

        MenuFlyoutSubItem wordByWordSubmenu =
            BuildWordByWordSubmenu(
                verse.VerseKey,
                anchor,
                wordSelectionSource);

        menu.Items.Add(
            wordByWordSubmenu);

        menu.Opening +=
            (_, _) =>
            {
                if (copySelection is not null)
                {
                    copySelection.IsEnabled =
                        sourceText is not null &&
                        !string.IsNullOrWhiteSpace(
                            sourceText.SelectedText);
                }

                RefreshSelectedWordOnlyAvailability(
                    wordByWordSubmenu,
                    wordSelectionSource);
            };

        return menu;
    }

    private void WorkingSliceWordByWord_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is VerseBundle verse)
        {
            ShowVerseWordByWordFlyout(
                verse.VerseKey,
                button);
        }
    }

    private void CopyTextToClipboard(
        string text)
    {
        var package =
            new DataPackage();

        package.SetText(
            text);

        Clipboard.SetContent(
            package);

        Clipboard.Flush();

        StatusText.Text =
            "Copied to clipboard.";
    }

    private async Task ShowWorkingSliceMessage(
        string title,
        string message)
    {
        var dialog =
            new ContentDialog
            {
                XamlRoot =
                    RootGrid.XamlRoot,
                Title = title,
                Content =
                    new TextBlock
                    {
                        Text =
                            message,
                        Foreground =
                            Brush("TextBrush"),
                        TextWrapping =
                            TextWrapping.Wrap,
                        MinWidth = 420
                    },
                CloseButtonText =
                    "OK"
            };

        await dialog.ShowAsync();
    }
}
