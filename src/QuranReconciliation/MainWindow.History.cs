using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private const int HistoryPageSize = 120;
    private readonly HistoryRepository _history = new();
    private readonly BookmarkRepository _bookmarks = new();
    private int _historyVisibleLimit = HistoryPageSize;
    private bool _historyWorkspaceInitialized;
    private string _historyFilter = "All";
    private string _historyMode = "Organized";
    private int _historySurahFilter;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer?
        _historySearchDebounceTimer;

    private void OpenHistory_Click(
        object sender,
        RoutedEventArgs e)
    {
        WorkspaceGrid.Visibility = Visibility.Collapsed;
        WorkingSliceWorkspaceGrid.Visibility = Visibility.Collapsed;
        ContextAtlasWorkspaceGrid.Visibility = Visibility.Collapsed;
        HistoryWorkspaceGrid.Visibility = Visibility.Visible;

        EnsureHistorySurahFilterBuilt();
        EnsureHistorySearchDebounce();
        // Keep paging/filter state across workspace navigation. History
        // still refreshes to show newly committed research from other tabs.
        RefreshHistoryWorkspace(preserveOffset: _historyWorkspaceInitialized);
        UpdateWorkspaceNavigationState();

        if (!_historyWorkspaceInitialized)
        {
            _historyWorkspaceInitialized = true;
            HistoryWorkspaceSearchTextBox.Focus(FocusState.Programmatic);
        }

        StatusText.Text =
            "Research History & Navigation · notes, context decisions, Working Slices and bookmarks.";
    }

    private void CloseHistory_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowResearchWorkspace();

        StatusText.Text =
            $"{_chapters[_currentSurah - 1].DisplayName} · research workspace";
    }

    private void ShowResearch_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowResearchWorkspace();

        StatusText.Text =
            $"{_chapters[_currentSurah - 1].DisplayName} · research workspace";
    }

    private void ShowResearchWorkspace()
    {
        HistoryWorkspaceGrid.Visibility = Visibility.Collapsed;
        WorkingSliceWorkspaceGrid.Visibility = Visibility.Collapsed;
        ContextAtlasWorkspaceGrid.Visibility = Visibility.Collapsed;
        WorkspaceGrid.Visibility = Visibility.Visible;
        UpdateWorkspaceContentWidth();
        UpdateWorkspaceNavigationState();
    }

    private void EnsureHistorySurahFilterBuilt()
    {
        if (HistorySurahFilter.Items.Count > 1)
        {
            return;
        }

        foreach (ChapterSummary chapter in _chapters)
        {
            HistorySurahFilter.Items.Add(
                new ComboBoxItem
                {
                    Content = chapter.DisplayName,
                    Tag = chapter.Number
                });
        }

        HistorySurahFilter.SelectedIndex = 0;
    }

    private void HistoryFilter_Checked(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is RadioButton radio &&
            radio.Tag?.ToString() is string filter)
        {
            _historyFilter = filter;
        }

        if (_ready &&
            HistoryWorkspaceGrid.Visibility == Visibility.Visible)
        {
            _historyVisibleLimit = HistoryPageSize;
            RefreshHistoryWorkspace();
        }
    }

    private void HistorySurahFilter_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (HistorySurahFilter.SelectedItem is ComboBoxItem item &&
            int.TryParse(
                item.Tag?.ToString(),
                out int surah))
        {
            _historySurahFilter = surah;
        }
        else
        {
            _historySurahFilter = 0;
        }

        if (_ready &&
            HistoryWorkspaceGrid.Visibility == Visibility.Visible)
        {
            _historyVisibleLimit = HistoryPageSize;
            RefreshHistoryWorkspace();
        }
    }

    private void HistoryWorkspaceSearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (!_ready ||
            HistoryWorkspaceGrid.Visibility != Visibility.Visible)
        {
            return;
        }

        EnsureHistorySearchDebounce();

        _historySearchDebounceTimer!.Stop();
        _historySearchDebounceTimer.Start();
    }

    private void EnsureHistorySearchDebounce()
    {
        if (_historySearchDebounceTimer is not null)
        {
            return;
        }

        _historySearchDebounceTimer =
            RootGrid.DispatcherQueue.CreateTimer();

        _historySearchDebounceTimer.Interval =
            TimeSpan.FromMilliseconds(250);

        _historySearchDebounceTimer.IsRepeating =
            false;

        _historySearchDebounceTimer.Tick +=
            (_, _) =>
            {
                _historyVisibleLimit = HistoryPageSize;

                if (HistoryWorkspaceGrid.Visibility ==
                    Visibility.Visible)
                {
                    RefreshHistoryWorkspace();
                }
            };
    }

    private void HistoryModeSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (HistoryModeSelector.SelectedItem
                is ComboBoxItem item &&
            item.Tag?.ToString() is string mode)
        {
            _historyMode =
                mode == "Timeline"
                    ? "Timeline"
                    : "Organized";
        }

        if (_ready &&
            HistoryWorkspaceGrid.Visibility ==
                Visibility.Visible)
        {
            _historyVisibleLimit = HistoryPageSize;
            RefreshHistoryWorkspace();
        }
    }

    private void LoadMoreHistory_Click(
        object sender,
        RoutedEventArgs e)
    {
        _historyVisibleLimit = Math.Min(
            _historyVisibleLimit + HistoryPageSize,
            _historyMode == "Timeline"
                ? 10000
                : 2000);

        RefreshHistoryWorkspace(preserveOffset: true);
    }

    private void RefreshResearchHistory(bool force = false)
    {
        if (HistoryWorkspaceGrid is null)
        {
            return;
        }

        if (force ||
            HistoryWorkspaceGrid.Visibility == Visibility.Visible)
        {
            RefreshHistoryWorkspace(preserveOffset: true);
        }
    }

    private void RefreshHistoryWorkspace(bool preserveOffset = false)
    {
        if (HistoryResultsList is null ||
            HistoryWorkspaceSummaryText is null ||
            HistoryWorkspaceSearchTextBox is null)
        {
            return;
        }

        ScrollViewer? scroll =
            preserveOffset ? FindHistoryScrollViewer(HistoryResultsList) : null;
        double? originalOffset = scroll?.VerticalOffset;

        try
        {
            string query =
                HistoryWorkspaceSearchTextBox.Text ?? string.Empty;

            bool organized =
                string.Equals(
                    _historyMode,
                    "Organized",
                    StringComparison.Ordinal);

            IReadOnlyList<ResearchHistoryEntry> entries =
                organized
                    ? _history.SearchOrganized(
                        query,
                        _historyFilter,
                        _historySurahFilter,
                        _historyVisibleLimit)
                    : _history.SearchTimeline(
                        query,
                        _historyFilter,
                        _historySurahFilter,
                        _historyVisibleLimit);

            var rows =
                new List<HistoryDisplayRow>(
                    entries.Count);

            int lastSurah = -1;
            long? lastContext = long.MinValue;
            DateOnly? lastDay = null;

            foreach (ResearchHistoryEntry entry in entries)
            {
                string groupHeader = string.Empty;
                string subgroupHeader = string.Empty;

                if (organized)
                {
                    if (entry.SurahNumber != lastSurah)
                    {
                        groupHeader =
                            _chapters[entry.SurahNumber - 1]
                                .DisplayName;

                        lastSurah =
                            entry.SurahNumber;

                        lastContext =
                            long.MinValue;
                    }

                    long? contextKey =
                        entry.ContextBlockId;

                    if (contextKey != lastContext)
                    {
                        subgroupHeader =
                            contextKey is long contextId
                                ? GetHistoryContextGroupLabel(
                                    contextId,
                                    entry)
                                : "Ayah research";

                        lastContext =
                            contextKey;
                    }
                }
                else
                {
                    DateOnly day =
                        DateOnly.FromDateTime(
                            entry.ChangedUtc.LocalDateTime);

                    if (lastDay != day)
                    {
                        groupHeader =
                            entry.ChangedUtc.LocalDateTime
                                .ToString("dddd, MMMM d, yyyy");

                        lastDay = day;
                    }
                }

                rows.Add(
                    new HistoryDisplayRow(
                        entry,
                        groupHeader,
                        subgroupHeader));
            }

            HistoryResultsList.ItemsSource = rows;

            if (originalOffset is double offset)
            {
                // Refresh to include new research while keeping the owner's
                // logical viewing position on ordinary workspace re-entry.
                HistoryResultsList.DispatcherQueue.TryEnqueue(() =>
                {
                    HistoryResultsList.UpdateLayout();
                    FindHistoryScrollViewer(HistoryResultsList)?.ChangeView(
                        null, offset, null, disableAnimation: true);
                });
            }

            string trimmed = query.Trim();
            string filterLabel =
                _historyFilter switch
                {
                    "AyahNotes" => "Ayah notes",
                    "ContextNotes" => "Context notes",
                    "Context" => "Context activity",
                    "Bookmarks" => "Bookmarks",
                    "WorkingSlices" => "Working Slices",
                    _ => "All research"
                };

            string modeLabel =
                organized
                    ? "Organized"
                    : "Timeline";

            HistoryWorkspaceSummaryText.Text =
                rows.Count == 0
                    ? string.IsNullOrWhiteSpace(trimmed)
                        ? $"No {filterLabel.ToLowerInvariant()} yet"
                        : $"No matches for “{trimmed}”"
                    : $"{modeLabel} · {filterLabel} · {rows.Count} item(s)";

            int max =
                organized
                    ? 2000
                    : 10000;

            HistoryLoadMoreButton.Visibility =
                entries.Count >= _historyVisibleLimit &&
                _historyVisibleLimit < max
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            HistoryResultsList.ItemsSource =
                Array.Empty<HistoryDisplayRow>();

            HistoryWorkspaceSummaryText.Text =
                "History search unavailable.";

            HistoryLoadMoreButton.Visibility =
                Visibility.Collapsed;

            StatusText.Text =
                $"History search unavailable: {ex.Message}";
        }
    }

    private static ScrollViewer? FindHistoryScrollViewer(
        DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        int children = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < children; i++)
        {
            ScrollViewer? found =
                FindHistoryScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }

        return null;
    }

    private void HistoryRowExpander_Expanding(
        Expander expander,
        ExpanderExpandingEventArgs args)
    {
        if (expander.DataContext is not HistoryDisplayRow row ||
            row.RevisionsLoaded)
        {
            return;
        }

        try
        {
            row.SetLazyRevisions(
                _history.LoadRevisionsOnDemand(row.Entry));
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Could not load History revisions: {ex.Message}";
        }
    }

    private string GetHistoryContextGroupLabel(
        long contextId,
        ResearchHistoryEntry entry)
    {
        try
        {
            ContextBlock block =
                _contexts.GetBlock(contextId);

            return
                $"Context · {block.RangeLabel} · {block.Status}";
        }
        catch
        {
            if (entry.StartAyah is int start)
            {
                string range =
                    entry.EndAyah is int end &&
                    end != start
                        ? $"Ayat {start}–{end}"
                        : $"Ayah {start}";

                return $"Context · {range}";
            }

            return "Context activity";
        }
    }

    private void HistoryJumpToTarget_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not HistoryDisplayRow row ||
            !row.Entry.CanJump)
        {
            return;
        }

        if (row.Entry.WorkingSliceId is long sliceId)
        {
            OpenWorkingSlices(
                sliceId);
            return;
        }

        if (row.Entry.JumpAyah is not int ayah)
        {
            return;
        }

        NavigateToResearchTarget(
            row.Entry.SurahNumber,
            ayah,
            row.Entry.ContextBlockId);
    }

    private void NavigateToResearchTarget(
        int surahNumber,
        int ayahNumber,
        long? contextBlockId = null)
    {
        ShowResearchWorkspace();

        if (_currentSurah != surahNumber)
        {
            SurahList.SelectedIndex =
                surahNumber - 1;
        }

        if (_currentSurah != surahNumber)
        {
            _currentSurah = surahNumber;
            LoadCurrentSurah(resetScroll: true);
            LoadContextMapForCurrentSurah();
        }

        if (contextBlockId is long contextId)
        {
            LoadContextMapForCurrentSurah(
                contextId,
                ayahNumber);
        }

        EnsureVerseRenderedThrough(ayahNumber);

        UpdateJuzUi(
            surahNumber,
            ayahNumber);

        RootGrid.DispatcherQueue.TryEnqueue(() =>
        {
            FindRenderedVerseElement(
                ayahNumber)
                ?.StartBringIntoView(
                    new BringIntoViewOptions
                    {
                        AnimationDesired = false,
                        VerticalAlignmentRatio = 0
                    });
        });

        StatusText.Text =
            $"Opened {_chapters[surahNumber - 1].NameSimple} {surahNumber}:{ayahNumber}.";
    }

    private async void BookmarkAyah_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not int ayah)
        {
            return;
        }

        int surah = _currentSurah;

        try
        {
            BookmarkEntry? existing =
                _bookmarks.Get(surah, ayah);

            var titleBox = new TextBox
            {
                Header = "Title (optional)",
                Text = existing?.Title ?? string.Empty,
                PlaceholderText =
                    $"{_chapters[surah - 1].NameSimple} {surah}:{ayah}"
            };

            var noteBox = new TextBox
            {
                Header = "Bookmark note (optional)",
                Text = existing?.Note ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 110
            };

            AttachTextBoxContextMenu(titleBox);
            AttachTextBoxContextMenu(noteBox);

            var content = new StackPanel
            {
                Spacing = 10
            };
            content.Children.Add(titleBox);
            content.Children.Add(noteBox);

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = existing is null
                    ? $"Bookmark {surah}:{ayah}"
                    : $"Edit bookmark {surah}:{ayah}",
                Content = content,
                PrimaryButtonText = existing is null
                    ? "Save bookmark"
                    : "Save changes",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            ContentDialogResult result =
                await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            string title =
                string.IsNullOrWhiteSpace(titleBox.Text)
                    ? $"{_chapters[surah - 1].NameSimple} {surah}:{ayah}"
                    : titleBox.Text.Trim();

            BookmarkEntry saved =
                _bookmarks.Save(
                    surah,
                    ayah,
                    title,
                    noteBox.Text);

            RefreshResearchHistory(force: true);

            StatusText.Text =
                $"Bookmark saved for {saved.SurahNumber}:{saved.AyahNumber}.";
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Could not save bookmark for {surah}:{ayah}: {ex.Message}";
        }
    }
}
