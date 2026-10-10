using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReconciliation.Infrastructure;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private CorpusOccurrenceRepository? _corpusOccurrenceRepository;
    private Task<CorpusOccurrenceRepository>? _corpusOccurrenceInitialization;
    private bool _corpusUpdatingCandidates;
    private string _corpusLastCandidateInput = "";

    private string SelectedCorpusSearchMode() =>
        (CorpusSearchModeSelector?.SelectedItem as ComboBoxItem)
            ?.Tag?.ToString() ?? "text";

    private async Task<CorpusOccurrenceRepository> GetCorpusOccurrenceRepositoryAsync()
    {
        if (_corpusOccurrenceRepository is not null) return _corpusOccurrenceRepository;
        // Hash three immutable source files away from the WinUI thread.
        _corpusOccurrenceInitialization ??= Task.Run(() =>
            new CorpusOccurrenceRepository(
                AppPaths.CorpusDatabase, AppPaths.WordByWordDatabase,
                AppPaths.CorpusLemmaDatabase));
        _corpusOccurrenceRepository = await _corpusOccurrenceInitialization;
        return _corpusOccurrenceRepository;
    }

    private void CorpusSearchMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CorpusSearchModeSelector is null || CorpusSearchTextBox is null ||
            CorpusSearchLanguageSelector is null ||
            CorpusSearchTranslationSelector is null ||
            CorpusSearchScriptSelector is null ||
            CorpusSearchNormalizedToggle is null ||
            CorpusSearchLemmaCandidateSelector is null)
            return;

        string mode = SelectedCorpusSearchMode();
        bool text = mode == "text";
        CorpusSearchLanguageSelector.IsEnabled = text;
        CorpusSearchTranslationSelector.IsEnabled =
            text && SelectedCorpusSearchLanguage() != CorpusSearchLanguage.Arabic;
        CorpusSearchTranslationSelector.Header =
            text ? "Translation source" : "Translation source (Text mode only)";
        CorpusSearchNormalizedToggle.IsEnabled = text;
        // In full-Ayah comparison the old "All scripts" choice has always
        // compared Uthmani alone; make that existing truth visible.
        if (CorpusSearchScriptSelector.Items[0] is ComboBoxItem defaultScript)
        {
            defaultScript.Content = mode == "ayah" ? "Uthmani (default)" : "All Arabic scripts";
            defaultScript.Tag = mode == "ayah" ? "uthmani" : "all";
        }
        CorpusSearchScriptSelector.IsEnabled =
            mode == "ayah" ||
            (text && SelectedCorpusSearchLanguage() == CorpusSearchLanguage.Arabic);
        switch (mode)
        {
            case "lemma":
                CorpusSearchTextBox.Header = "Arabic word — verified grammatical lemma";
                CorpusSearchTextBox.PlaceholderText = "ٱللَّهِ · رَحْمَة";
                CorpusSearchModeDescription.Text =
                    "Exact annotated Arabic lemma and part of speech, one Ayah card per source verse. Totals count individual word positions.";
                break;
            case "phrase":
                CorpusSearchTextBox.Header = "Exact vocalized Uthmani word sequence (1–8 words)";
                CorpusSearchTextBox.PlaceholderText = "بِسْمِ ٱللَّهِ";
                CorpusSearchModeDescription.Text =
                    "Matches only identical adjacent canonical Uthmani word tokens, not similar meanings or lemma expansions.";
                break;
            case "ayah":
                CorpusSearchTextBox.Header = "Canonical Ayah reference";
                CorpusSearchTextBox.PlaceholderText = "1:1";
                CorpusSearchModeDescription.Text =
                    "Find complete, exactly identical Ayat in the chosen script (Uthmani by default). No approximate matching.";
                break;
            default:
                CorpusSearchTextBox.Header = "Arabic, English or বাংলা word / phrase";
                CorpusSearchTextBox.PlaceholderText = "الله · mercy · আল্লাহ";
                CorpusSearchModeDescription.Text =
                    "Text search keeps original literal and optional normalized matching across selected languages and sources.";
                break;
        }
        if (mode != "lemma")
            ClearCorpusLemmaChoices();
        // Apply the mode-aware filter state (and clear a script-specific Text
        // filter if Language is not Arabic) through its single authority.
        if (_ready)
            CorpusSearchFilter_Changed(sender, e);
    }

    private void CorpusSearchLemmaCandidate_Changed(
        object sender, SelectionChangedEventArgs e)
    {
        if (_corpusUpdatingCandidates || !_ready ||
            CorpusSearchWorkspaceGrid.Visibility != Visibility.Visible)
            return;
        ScheduleCorpusSearch();
    }

    private void ClearCorpusLemmaChoices()
    {
        if (CorpusSearchLemmaCandidateSelector is null) return;
        _corpusUpdatingCandidates = true;
        try
        {
            CorpusSearchLemmaCandidateSelector.Items.Clear();
            CorpusSearchLemmaCandidateSelector.Visibility = Visibility.Collapsed;
            _corpusLastCandidateInput = "";
        }
        finally { _corpusUpdatingCandidates = false; }
    }

    private async Task<CorpusLemmaCandidate?> ResolveLemmaAsync(
        CorpusOccurrenceRepository repository, string input, int generation)
    {
        if (!string.Equals(_corpusLastCandidateInput, input, StringComparison.Ordinal))
        {
            IReadOnlyList<CorpusLemmaCandidate> candidates =
                await Task.Run(() => repository.SuggestLemmas(input));
            if (generation != _corpusSearchGeneration)
                return null;

            _corpusUpdatingCandidates = true;
            try
            {
                CorpusSearchLemmaCandidateSelector.Items.Clear();
                foreach (var candidate in candidates)
                {
                    CorpusSearchLemmaCandidateSelector.Items.Add(new ComboBoxItem
                    {
                        Content = $"{candidate.Lemma} · POS {candidate.Pos} · " +
                            $"{candidate.Occurrences:N0} words / {candidate.DistinctAyat:N0} Ayat",
                        Tag = candidate
                    });
                }
                CorpusSearchLemmaCandidateSelector.Visibility =
                    candidates.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                if (candidates.Count == 1)
                    CorpusSearchLemmaCandidateSelector.SelectedIndex = 0;
                _corpusLastCandidateInput = input;
            }
            finally { _corpusUpdatingCandidates = false; }
        }
        if (CorpusSearchLemmaCandidateSelector.SelectedItem is ComboBoxItem item &&
            item.Tag is CorpusLemmaCandidate candidateResult)
            return candidateResult;

        if (CorpusSearchLemmaCandidateSelector.Items.Count > 1)
            CorpusSearchSummaryText.Text =
                "Multiple grammatical lemmas share this spelling. Choose an annotated lemma / part of speech; no guess is made.";
        else
            CorpusSearchSummaryText.Text =
                "No verified lemma for this whole Arabic word. Use Text search for substring matches or try a different vocalized word.";
        return null;
    }

    private static (int Surah, int Ayah) ParseCanonicalAyah(string key)
    {
        var parts = key.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out int s) || s is < 1 or > 114 ||
            !int.TryParse(parts[1], out int a) || a < 1)
            throw new ArgumentException("Enter a canonical Ayah reference such as 2:255.");
        return (s, a);
    }

    private int? _corpusChapterPreviewSurah;

    /// <summary>
    /// Only bare chapter numbers and unfinished "chapter:" inputs are chapter
    /// navigation hints. Completed "chapter:ayah" inputs ALWAYS execute the
    /// existing exact-complete-Ayah comparison, never prefix matching.
    /// </summary>
    private bool TryShowCorpusChapterReference(string reference)
    {
        bool chapterOnly = reference.Length > 0 &&
            reference.All(c => c is >= '0' and <= '9');
        bool waitingForAyah = reference.EndsWith(':') &&
            reference.Length > 1 &&
            reference[..^1].All(c => c is >= '0' and <= '9');
        if (!chapterOnly && !waitingForAyah) return false;

        string numeric = waitingForAyah ? reference[..^1] : reference;
        if (!int.TryParse(numeric, out int surah) ||
            surah is < 1 or > 114)
        {
            _corpusChapterPreviewSurah = null;
            CorpusSearchSummaryText.Text = "Enter a valid Surah number from 1 through 114.";
            CorpusSearchLoadMoreButton.Visibility = Visibility.Collapsed;
            return true;
        }

        var chapter = _chapters[surah - 1];
        _corpusChapterPreviewSurah = surah;
        CorpusSearchChapterTitle.Text =
            $"Surah {chapter.Number} · {chapter.DisplayName}";
        CorpusSearchChapterDetails.Text =
            $"{chapter.VersesCount} canonical Ayat · read-only chapter metadata.";
        CorpusSearchChapterSummaryPanel.Visibility = Visibility.Visible;
        CorpusSearchResultsList.Visibility = Visibility.Collapsed;
        CorpusSearchLoadMoreButton.Visibility = Visibility.Collapsed;
        _corpusSearchHasMore = false;
        CorpusSearchSummaryText.Text = waitingForAyah
            ? $"Surah {surah} selected. Enter an Ayah number after the colon."
            : $"Surah {surah} summary. Enter {surah}:Ayah for exact complete-text matching.";
        StatusText.Text = $"Corpus Search · Surah {surah} (read-only reference).";
        return true;
    }

    private void CorpusSearchViewFirstAyah_Click(object sender, RoutedEventArgs e)
    {
        if (_corpusChapterPreviewSurah is not int surah ||
            _ownerStateOperationActive ||
            CorpusSearchChapterSummaryPanel.Visibility != Visibility.Visible)
            return;
        _ = ShowCorpusSearchVersePreviewAsync(surah, 1);
    }

    private async Task RunCorpusOccurrenceSearchAsync(bool append,
        int generation, int offset)
    {
        string mode = SelectedCorpusSearchMode();
        string text = CorpusSearchTextBox.Text.Trim();
        if (mode == "ayah" && TryShowCorpusChapterReference(text))
            return;
        var engine = await GetCorpusOccurrenceRepositoryAsync();
        if (generation != _corpusSearchGeneration) return;

        if (mode == "lemma")
        {
            var choice = await ResolveLemmaAsync(engine, text, generation);
            if (choice is null || generation != _corpusSearchGeneration) return;
            CorpusAyahOccurrencePage page = await Task.Run(() =>
                engine.SearchLemmaAyat(choice.Lemma, choice.Pos,
                    limit: CorpusSearchPageSize, offset: offset));
            if (generation != _corpusSearchGeneration) return;
            foreach (var hit in page.Hits)
            {
                string positions = string.Join(", ", hit.WordPositions);
                var row = new CorpusSearchDisplayRow(hit.VerseKey,
                    $"Verified Arabic lemma · {choice.Lemma} (POS {choice.Pos})",
                    $"Word position(s): {positions} · {hit.WordPositions.Count} occurrence(s) in this Ayah",
                    $"Annotated whole word (canonical Uthmani): {hit.UthmaniExample}");
                _corpusSearchRows.Add(row);
                CorpusSearchResultsList.Items.Add(row);
            }
            _corpusSearchHasMore = page.HasMore;
            CorpusSearchSummaryText.Text =
                $"Verified lemma: {page.WordOccurrences:N0} word occurrences across " +
                $"{page.DistinctAyat:N0} distinct Ayat. " +
                $"Showing {_corpusSearchRows.Count:N0} Ayat. Source: Quranic Arabic Corpus v0.4 (word positions).";
        }
        else if (mode == "phrase")
        {
            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length is < 1 or > 8)
                throw new ArgumentException("Exact phrase requires 1–8 space-separated canonical Uthmani words.");
            CorpusOccurrencePage page = await Task.Run(() =>
                engine.SearchExactWords(words, limit: CorpusSearchPageSize, offset: offset));
            if (generation != _corpusSearchGeneration) return;
            foreach (var hit in page.Hits)
            {
                var row = new CorpusSearchDisplayRow(hit.VerseKey,
                    "Uthmani exact adjacent word sequence",
                    $"Begins at word position {hit.Position}; no lemma/orthographic expansion",
                    text);
                _corpusSearchRows.Add(row);
                CorpusSearchResultsList.Items.Add(row);
            }
            _corpusSearchHasMore = page.HasMore;
            CorpusSearchSummaryText.Text =
                $"Exact contiguous phrase: {page.WordOccurrences:N0} occurrences across " +
                $"{page.DistinctAyat:N0} distinct Ayat. " +
                $"Showing {_corpusSearchRows.Count:N0} occurrences.";
        }
        else if (mode == "ayah")
        {
            _ = ParseCanonicalAyah(text);
            var script = (CorpusSearchScriptSelector.SelectedItem as ComboBoxItem)
                ?.Tag?.ToString() ?? "uthmani";
            if (script == "all") script = "uthmani";
            IReadOnlyList<CorpusRepeatedAyah> matches = await Task.Run(() =>
                engine.FindIdenticalAyat(text, script));
            if (generation != _corpusSearchGeneration) return;
            foreach (var hit in matches.Skip(offset).Take(CorpusSearchPageSize))
            {
                var row = new CorpusSearchDisplayRow(hit.VerseKey,
                    $"Complete Ayah — exact {script} text",
                    "Identical entire canonical Ayah, not a semantic similarity",
                    $"Same full text as Ayah {text}.");
                _corpusSearchRows.Add(row);
                CorpusSearchResultsList.Items.Add(row);
            }
            _corpusSearchHasMore = matches.Count > offset + CorpusSearchPageSize;
            CorpusSearchSummaryText.Text =
                $"Exactly identical complete Ayat ({script}): {matches.Count:N0}. " +
                $"Showing {_corpusSearchRows.Count:N0}.";
        }
        else throw new InvalidOperationException("Unknown verified research count method.");

        CorpusSearchLoadMoreButton.Visibility =
            _corpusSearchHasMore ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = $"Corpus Search · {_corpusSearchRows.Count:N0} evidence cards shown.";
    }
}
