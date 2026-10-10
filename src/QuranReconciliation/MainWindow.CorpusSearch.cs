using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReconciliation.Infrastructure;

namespace QuranReconciliation;

/// <summary>
/// Display-only wrapper; the canonical Arabic / translation text is never altered
/// or written to research state by corpus search.
/// </summary>
public sealed class CorpusSearchDisplayRow
{
    internal CorpusSearchHit Hit { get; }
    public string Location { get; }
    public string Source { get; }
    public string MatchLabel { get; }
    public string Text { get; }

    internal CorpusSearchDisplayRow(string verseKey, string source,
        string explanation, string displayedText)
    {
        string[] parts = verseKey.Split(':');
        int surah = int.Parse(parts[0]);
        int ayah = int.Parse(parts[1]);
        Hit = new CorpusSearchHit(verseKey, surah, ayah, "arabic", null,
            source, "arabic", "uthmani", displayedText,
            CorpusSearchMatch.Exact);
        Location = $"Ayah {verseKey}";
        Source = source;
        MatchLabel = explanation;
        Text = displayedText;
    }

    internal CorpusSearchDisplayRow(CorpusSearchHit hit)
    {
        Hit = hit;
        Location = $"{hit.ChapterNumber}:{hit.VerseNumber} · {hit.VerseKey}";
        Source = hit.SourceKind == "arabic"
            ? $"Arabic · {hit.SourceName}"
            : $"{(hit.Language == "bangla" ? "বাংলা" : "English")} · {hit.SourceName}";
        MatchLabel = hit.Match == CorpusSearchMatch.Exact
            ? "Exact original-text match"
            : "Normalized spelling / diacritic match (not character-for-character)";
        Text = hit.DisplayText;
    }
}

public sealed partial class MainWindow
{
    private const int CorpusSearchPageSize = 50;
    private readonly List<CorpusSearchDisplayRow> _corpusSearchRows = [];
    private CorpusSearchRepository? _corpusSearchRepository;
    private Task<CorpusSearchRepository>? _corpusSearchInitialization;
    private DispatcherQueueTimer? _corpusSearchTimer;
    private bool _corpusSearchSourcesInitialized;
    private int _corpusSearchGeneration;
    private bool _corpusSearchBusy;
    private bool _corpusSearchHasMore;

    private void OpenCorpusSearch_Click(object sender, RoutedEventArgs e) =>
        OpenCorpusSearchWorkspace();

    private void CloseCorpusSearch_Click(object sender, RoutedEventArgs e)
    {
        ShowResearchWorkspace();
        StatusText.Text = $"{_chapters[_currentSurah - 1].DisplayName} · research workspace";
    }

    private void OpenCorpusSearchWorkspace()
    {
        if (_ownerStateOperationActive)
        {
            StatusText.Text = "Corpus search cannot open during an owner-data safety operation.";
            return;
        }

        EnsureCorpusSearchSourceChoices();
        // Also synchronize after cold startup if XAML fired its initial
        // SelectionChanged before all named controls were constructed.
        SetCorpusSearchScriptChoices(SelectedCorpusSearchMode());
        WorkspaceGrid.Visibility = Visibility.Collapsed;
        WorkingSliceWorkspaceGrid.Visibility = Visibility.Collapsed;
        HistoryWorkspaceGrid.Visibility = Visibility.Collapsed;
        ContextAtlasWorkspaceGrid.Visibility = Visibility.Collapsed;
        CorpusSearchWorkspaceGrid.Visibility = Visibility.Visible;
        UpdateWorkspaceNavigationState();
        StatusText.Text = "Corpus Search · separate verified offline index · no tafsir.";
        if (CorpusSearchTextBox.Text.Length == 0)
            CorpusSearchTextBox.Focus(FocusState.Programmatic);
    }

    private void EnsureCorpusSearchSourceChoices()
    {
        if (_corpusSearchSourcesInitialized) return;

        foreach (var resource in _resources.Where(x => x.Kind == "translation")
                 .OrderBy(x => x.IsBengali ? 1 : 0).ThenBy(x => x.Name))
        {
            CorpusSearchTranslationSelector.Items.Add(new ComboBoxItem
            {
                Content = $"{(resource.IsBengali ? "বাংলা" : "English")} · {resource.Name}",
                Tag = resource.Id
            });
        }

        _corpusSearchSourcesInitialized = true;
    }

    private void CorpusSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready || CorpusSearchWorkspaceGrid.Visibility != Visibility.Visible) return;
        ScheduleCorpusSearch();
    }

    private void CorpusSearchFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        string mode = SelectedCorpusSearchMode();
        bool textMode = mode == "text";
        var language = SelectedCorpusSearchLanguage();
        bool isArabic = language == CorpusSearchLanguage.Arabic;
        CorpusSearchScriptSelector.IsEnabled =
            mode == "ayah" || (textMode && isArabic);
        if (textMode && !isArabic && CorpusSearchScriptSelector.SelectedIndex > 0)
        {
            // An explicit script constrains Arabic only in Text mode.
            CorpusSearchScriptSelector.SelectedIndex = 0;
        }

        CorpusSearchTranslationSelector.IsEnabled = textMode && !isArabic;
        if (textMode && isArabic && CorpusSearchTranslationSelector.SelectedIndex > 0)
            CorpusSearchTranslationSelector.SelectedIndex = 0;

        if (CorpusSearchWorkspaceGrid.Visibility == Visibility.Visible)
            ScheduleCorpusSearch();
    }

    private void EnsureCorpusSearchDebounce()
    {
        if (_corpusSearchTimer is not null) return;
        _corpusSearchTimer = DispatcherQueue.CreateTimer();
        _corpusSearchTimer.Interval = TimeSpan.FromMilliseconds(480);
        _corpusSearchTimer.IsRepeating = false;
        _corpusSearchTimer.Tick += (_, _) =>
        {
            _corpusSearchTimer.Stop();
            _ = RunCorpusSearchAsync(append: false);
        };
    }

    private void ScheduleCorpusSearch()
    {
        _corpusSearchGeneration++;
        EnsureCorpusSearchDebounce();
        _corpusSearchTimer!.Stop();
        if (string.IsNullOrWhiteSpace(CorpusSearchTextBox.Text))
        {
            ClearCorpusSearchResults();
            CorpusSearchSummaryText.Text = "Enter a word or phrase to search all selected sources.";
            return;
        }
        CorpusSearchSummaryText.Text = "Search pending…";
        _corpusSearchTimer.Start();
    }

    private void ClearCorpusSearchResults()
    {
        _corpusSearchRows.Clear();
        CorpusSearchResultsList.Items.Clear();
        CorpusSearchChapterSummaryPanel.Visibility = Visibility.Collapsed;
        CorpusSearchResultsList.Visibility = Visibility.Visible;
        CorpusSearchLoadMoreButton.Visibility = Visibility.Collapsed;
        _corpusSearchHasMore = false;
    }

    private void CorpusSearchSubmit_Click(object sender, RoutedEventArgs e)
    {
        _corpusSearchTimer?.Stop();
        _ = RunCorpusSearchAsync(append: false);
    }

    private void CorpusSearchLoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (_corpusSearchBusy || !_corpusSearchHasMore) return;
        _ = RunCorpusSearchAsync(append: true);
    }

    private CorpusSearchLanguage SelectedCorpusSearchLanguage()
    {
        string? tag = (CorpusSearchLanguageSelector.SelectedItem as ComboBoxItem)
            ?.Tag?.ToString();
        return tag switch
        {
            "arabic" => CorpusSearchLanguage.Arabic,
            "english" => CorpusSearchLanguage.English,
            "bangla" => CorpusSearchLanguage.Bangla,
            _ => CorpusSearchLanguage.All
        };
    }

    private CorpusSearchQuery BuildCorpusSearchQuery(int offset)
    {
        CorpusSearchLanguage language = SelectedCorpusSearchLanguage();
        string script = language == CorpusSearchLanguage.Arabic
            ? (CorpusSearchScriptSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all"
            : "all";
        int? source = null;
        if (language != CorpusSearchLanguage.Arabic &&
            CorpusSearchTranslationSelector.SelectedItem is ComboBoxItem choice &&
            int.TryParse(choice.Tag?.ToString(), out int sourceId) &&
            sourceId >= 0)
        {
            source = sourceId;
        }

        return new CorpusSearchQuery(
            CorpusSearchTextBox.Text, language, script,
            CorpusSearchNormalizedToggle.IsChecked == true,
            Limit: CorpusSearchPageSize, Offset: offset,
            TranslationSourceId: source);
    }

    private async Task<CorpusSearchRepository> GetCorpusSearchRepositoryAsync()
    {
        if (_corpusSearchRepository is not null) return _corpusSearchRepository;

        // Canonical corpus and search-index hashing can read ~200 MB from disk.
        // First-open integrity validation MUST NOT block the WinUI thread.
        _corpusSearchInitialization ??= Task.Run(() =>
            new CorpusSearchRepository(
                AppPaths.CorpusDatabase, AppPaths.CorpusSearchDatabase));
        _corpusSearchRepository = await _corpusSearchInitialization;
        return _corpusSearchRepository;
    }

    private async Task RunCorpusSearchAsync(bool append)
    {
        if (!_ready || _ownerStateOperationActive) return;
        int generation = ++_corpusSearchGeneration;
        int offset = append ? _corpusSearchRows.Count : 0;

        if (!append) ClearCorpusSearchResults();
        if (string.IsNullOrWhiteSpace(CorpusSearchTextBox.Text))
        {
            CorpusSearchSummaryText.Text = "Enter a word or phrase to begin.";
            return;
        }

        _corpusSearchBusy = true;
        CorpusSearchSubmitButton.IsEnabled = false;
        CorpusSearchLoadMoreButton.Visibility = Visibility.Collapsed;
        CorpusSearchSummaryText.Text = "Searching the verified offline corpus…";

        try
        {
            if (SelectedCorpusSearchMode() != "text")
            {
                await RunCorpusOccurrenceSearchAsync(append, generation, offset);
                return;
            }
            CorpusSearchQuery query = BuildCorpusSearchQuery(offset);
            var service = await GetCorpusSearchRepositoryAsync();
            CorpusSearchPage page = await Task.Run(() => service.Search(query));
            if (generation != _corpusSearchGeneration) return;

            foreach (CorpusSearchHit hit in page.Hits)
            {
                var row = new CorpusSearchDisplayRow(hit);
                _corpusSearchRows.Add(row);
                CorpusSearchResultsList.Items.Add(row);
            }

            _corpusSearchHasMore = page.HasMore;
            CorpusSearchSummaryText.Text = _corpusSearchRows.Count == 0
                ? "No matching Ayat or translations found for these filters."
                : $"Showing {_corpusSearchRows.Count} source occurrences · " +
                  (_corpusSearchHasMore ? "More available" : "All matches shown") +
                  " · Exact hits are listed before normalized hits.";
            CorpusSearchLoadMoreButton.Visibility =
                _corpusSearchHasMore ? Visibility.Visible : Visibility.Collapsed;

            StatusText.Text = $"Corpus Search · {_corpusSearchRows.Count} source occurrences shown.";
        }
        catch (Exception ex)
        {
            if (generation != _corpusSearchGeneration) return;
            _corpusSearchHasMore = false;
            CorpusSearchLoadMoreButton.Visibility = Visibility.Collapsed;
            CorpusSearchSummaryText.Text =
                $"Search unavailable: {ex.Message} — verified index required; " +
                "no owner research was changed.";
            StatusText.Text = "Corpus Search failed closed.";
        }
        finally
        {
            if (generation == _corpusSearchGeneration)
            {
                _corpusSearchBusy = false;
                CorpusSearchSubmitButton.IsEnabled = true;
            }
        }
    }

    private void CorpusSearchReadAyah_Click(object sender, RoutedEventArgs e)
    {
        if (_ownerStateOperationActive ||
            sender is not Button button ||
            button.Tag is not CorpusSearchDisplayRow selected)
            return;

        _ = ShowCorpusSearchVersePreviewAsync(
            selected.Hit.ChapterNumber, selected.Hit.VerseNumber);
    }

    private void CorpusSearchOpenResearch_Click(object sender, RoutedEventArgs e)
    {
        if (_ownerStateOperationActive ||
            sender is not Button button ||
            button.Tag is not CorpusSearchDisplayRow selected)
            return;

        // Deliberate action, not the default result click; this retains the
        // existing R5 dirty-draft/reader navigation authority.
        NavigateToResearchTarget(selected.Hit.ChapterNumber, selected.Hit.VerseNumber);
    }
}
