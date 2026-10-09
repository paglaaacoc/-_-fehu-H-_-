using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuranReconciliation.Models;
using QuranReconciliation.Infrastructure;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    // Transient read-only paired view. No notes, Context writes or new DB state.
    private int _atlasPairedReadRequest;
    private int _atlasPairedReadBoundary;
    private bool _atlasPairedReadLoadingSources;
    private bool _atlasPairedLeftAppending;
    private bool _atlasPairedRightAppending;
    private int _atlasPairedLeftRendered;
    private int _atlasPairedRightRendered;
    private double _atlasCompareBackgroundScrollOffset;
    private ProposalSurah? _atlasPairedLeftSurah;
    private ProposalSurah? _atlasPairedRightSurah;
    private IReadOnlyList<ContextAtlasPreviewVerse> _atlasPairedLeftVerses = [];
    private IReadOnlyList<ContextAtlasPreviewVerse> _atlasPairedRightVerses = [];

    private readonly List<TextBlock> _atlasPairedLeftText = [];
    private readonly List<TextBlock> _atlasPairedRightText = [];
    private readonly List<(TextBlock Control, double Size, double? LineHeight)> _atlasPairedZoom = [];

    private async void AtlasCompareReadingOpen_Click(object sender, RoutedEventArgs e) =>
        await OpenAtlasCompareReadingAsync();

    private void InitializeAtlasCompareReadingSources()
    {
        if (AtlasCompareReadingEnglishSelector.Items.Count != 0)
            return;

        _atlasPairedReadLoadingSources = true;
        try
        {
            AtlasCompareReadingEnglishSelector.Items.Add(new ComboBoxItem
                { Content = "None", Tag = -1 });
            AtlasCompareReadingBengaliSelector.Items.Add(new ComboBoxItem
                { Content = "None", Tag = -1 });

            foreach (ResourceSummary resource in _resources.Where(x => x.Kind == "translation"))
            {
                var item = new ComboBoxItem { Content = resource.Name, Tag = resource.Id };
                if (resource.IsBengali)
                    AtlasCompareReadingBengaliSelector.Items.Add(item);
                else if (resource.LanguageName.Equals("english", StringComparison.OrdinalIgnoreCase))
                    AtlasCompareReadingEnglishSelector.Items.Add(item);
            }

            SelectPreviewSource(AtlasCompareReadingEnglishSelector, 20);
            SelectPreviewSource(AtlasCompareReadingBengaliSelector, 161);
        }
        finally
        {
            _atlasPairedReadLoadingSources = false;
        }
    }

    private async Task OpenAtlasCompareReadingAsync()
    {
        if (_atlasMode != "Compare" ||
            AtlasCompareASelector.SelectedItem is not ComboBoxItem ai ||
            ai.Tag is not ProposalCorpusPackage a ||
            AtlasCompareBSelector.SelectedItem is not ComboBoxItem bi ||
            bi.Tag is not ProposalCorpusPackage b ||
            string.Equals(a.Corpus.CorpusId, b.Corpus.CorpusId, StringComparison.Ordinal))
            return;

        ProposalSurah left = a.Corpus.Surahs.Single(s => s.SurahNumber == _atlasSurah);
        ProposalSurah right = b.Corpus.Surahs.Single(s => s.SurahNumber == _atlasSurah);
        var leftEdges = left.Boundaries.Select(x => x.AfterAyah).ToHashSet();
        var rightEdges = right.Boundaries.Select(x => x.AfterAyah).ToHashSet();
        List<int> disagreements = leftEdges.SymmetricDifferenceOrdered(rightEdges);
        if (disagreements.Count == 0)
            return;

        _atlasCompareDifferenceIndex = Math.Clamp(_atlasCompareDifferenceIndex, 0, disagreements.Count - 1);
        int boundary = disagreements[_atlasCompareDifferenceIndex];

        ProposalContextBlock lBefore = left.ContextBlocks.Single(x => x.StartAyah <= boundary && x.EndAyah >= boundary);
        ProposalContextBlock lAfter = left.ContextBlocks.Single(x => x.StartAyah <= boundary + 1 && x.EndAyah >= boundary + 1);
        ProposalContextBlock rBefore = right.ContextBlocks.Single(x => x.StartAyah <= boundary && x.EndAyah >= boundary);
        ProposalContextBlock rAfter = right.ContextBlocks.Single(x => x.StartAyah <= boundary + 1 && x.EndAyah >= boundary + 1);

        InitializeAtlasCompareReadingSources();
        _atlasPairedReadBoundary = boundary;
        _atlasPairedLeftSurah = left;
        _atlasPairedRightSurah = right;
        _atlasCompareBackgroundScrollOffset = AtlasContentScrollViewer.VerticalOffset;
        AtlasContentScrollViewer.Visibility = Visibility.Collapsed;
        AtlasReadingPreviewGrid.Visibility = Visibility.Collapsed;
        AtlasCompareReadingGrid.Visibility = Visibility.Visible;

        AtlasCompareReadingHeadline.Text = $"Surah {_atlasSurah} · Ayah {_atlasSurah}:{boundary} → {_atlasSurah}:{boundary + 1} · compare actual passages";
        AtlasCompareReadingLeftTitle.Text = $"Corpus A · {a.Corpus.DisplayName} · {_atlasSurah}:{lBefore.StartAyah}–{lAfter.EndAyah}" +
            (leftEdges.Contains(boundary) ? $" · SPLITS after {boundary}" : " · joins both Ayat");
        AtlasCompareReadingRightTitle.Text = $"Corpus B · {b.Corpus.DisplayName} · {_atlasSurah}:{rBefore.StartAyah}–{rAfter.EndAyah}" +
            (rightEdges.Contains(boundary) ? $" · SPLITS after {boundary}" : " · joins both Ayat");

        int request = ++_atlasPairedReadRequest;
        int en = PreviewSelectedId(AtlasCompareReadingEnglishSelector);
        int bn = PreviewSelectedId(AtlasCompareReadingBengaliSelector);
        ResetAtlasPairedReaderCards();
        AtlasCompareReadingLeftPanel.Children.Add(new TextBlock
            { Text = "Loading local canonical Ayat…", Foreground = Brush("MutedTextBrush") });
        AtlasCompareReadingRightPanel.Children.Add(new TextBlock
            { Text = "Loading local canonical Ayat…", Foreground = Brush("MutedTextBrush") });

        UpdateAtlasPairedReadingLayout();

        try
        {
            // Load immutable canonical evidence once per corpus-specific range on a
            // background thread; build only a small initial native text batch.
            var loaded = await Task.Run(() =>
            {
                var first = _atlasPreviewReader.ReadRange(
                    left.SurahNumber, lBefore.StartAyah, lAfter.EndAyah, en, bn);
                var second = _atlasPreviewReader.ReadRange(
                    right.SurahNumber, rBefore.StartAyah, rAfter.EndAyah, en, bn);
                return (first, second);
            });

            if (request != _atlasPairedReadRequest ||
                AtlasCompareReadingGrid.Visibility != Visibility.Visible)
                return;

            _atlasPairedLeftVerses = loaded.first;
            _atlasPairedRightVerses = loaded.second;
            AtlasCompareReadingLeftPanel.Children.Clear();
            AtlasCompareReadingRightPanel.Children.Clear();
            AppendAtlasPairedReaderCards(true, 6);
            AppendAtlasPairedReaderCards(false, 6);
            AtlasCompareReadingLeftScroll.ChangeView(null, 0, null, disableAnimation: true);
            AtlasCompareReadingRightScroll.ChangeView(null, 0, null, disableAnimation: true);
        }
        catch (Exception ex)
        {
            if (request != _atlasPairedReadRequest)
                return;
            ResetAtlasPairedReaderCards();
            var error = $"Paired Ayah reading unavailable: {ex.Message}";
            AtlasCompareReadingLeftPanel.Children.Add(new TextBlock
                { Text = error, Foreground = Brush("TextBrush"), TextWrapping = TextWrapping.Wrap });
            AtlasCompareReadingRightPanel.Children.Add(new TextBlock
                { Text = error, Foreground = Brush("TextBrush"), TextWrapping = TextWrapping.Wrap });
        }
    }

    private void ResetAtlasPairedReaderCards()
    {
        _atlasPairedLeftVerses = [];
        _atlasPairedRightVerses = [];
        _atlasPairedLeftRendered = 0;
        _atlasPairedRightRendered = 0;
        _atlasPairedLeftText.Clear();
        _atlasPairedRightText.Clear();
        _atlasPairedZoom.Clear();
        AtlasCompareReadingLeftPanel.Children.Clear();
        AtlasCompareReadingRightPanel.Children.Clear();
    }

    private async void AtlasCompareReadingSource_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (_atlasPairedReadLoadingSources ||
            AtlasCompareReadingGrid.Visibility != Visibility.Visible)
            return;
        await OpenAtlasCompareReadingAsync();
    }

    private void AtlasCompareReadingClose_Click(object sender, RoutedEventArgs e) =>
        CloseAtlasCompareReader();

    private void CloseAtlasCompareReader()
    {
        ++_atlasPairedReadRequest; // Ignore late asynchronous reads.
        if (AtlasCompareReadingGrid is null)
            return;
        bool wasOpen = AtlasCompareReadingGrid.Visibility == Visibility.Visible;
        AtlasCompareReadingGrid.Visibility = Visibility.Collapsed;
        ResetAtlasPairedReaderCards();
        _atlasPairedLeftSurah = null;
        _atlasPairedRightSurah = null;
        if (wasOpen && AtlasReadingPreviewGrid.Visibility != Visibility.Visible)
        {
            AtlasContentScrollViewer.Visibility = Visibility.Visible;
            double offset = _atlasCompareBackgroundScrollOffset;
            RootGrid.DispatcherQueue.TryEnqueue(() =>
                AtlasContentScrollViewer.ChangeView(null,
                    Math.Min(offset, AtlasContentScrollViewer.ScrollableHeight),
                    null, disableAnimation: true));
        }
    }

    private void AtlasCompareReadingLeftScroll_ViewChanged(
        object sender, ScrollViewerViewChangedEventArgs e) =>
        AppendAtlasPairedReaderIfNearEnd(true);

    private void AtlasCompareReadingRightScroll_ViewChanged(
        object sender, ScrollViewerViewChangedEventArgs e) =>
        AppendAtlasPairedReaderIfNearEnd(false);

    private void AppendAtlasPairedReaderIfNearEnd(bool left)
    {
        if (AtlasCompareReadingGrid.Visibility != Visibility.Visible)
            return;
        var scroll = left ? AtlasCompareReadingLeftScroll : AtlasCompareReadingRightScroll;
        if (scroll.ScrollableHeight - scroll.VerticalOffset <
            Math.Max(360, scroll.ViewportHeight))
            AppendAtlasPairedReaderCards(left, 5);
    }

    private void AppendAtlasPairedReaderCards(bool left, int count)
    {
        if (AtlasCompareReadingGrid.Visibility != Visibility.Visible ||
            (left ? _atlasPairedLeftAppending : _atlasPairedRightAppending))
            return;
        var verses = left ? _atlasPairedLeftVerses : _atlasPairedRightVerses;
        var chapter = left ? _atlasPairedLeftSurah : _atlasPairedRightSurah;
        if (chapter is null)
            return;

        if (left) _atlasPairedLeftAppending = true;
        else _atlasPairedRightAppending = true;
        try
        {
            int begin = left ? _atlasPairedLeftRendered : _atlasPairedRightRendered;
            int end = Math.Min(verses.Count, begin + count);
            var panel = left ? AtlasCompareReadingLeftPanel : AtlasCompareReadingRightPanel;
            for (int i = begin; i < end; i++)
                panel.Children.Add(CreateAtlasPairedVerseCard(verses[i], chapter, left));
            if (left) _atlasPairedLeftRendered = end;
            else _atlasPairedRightRendered = end;
            RefreshAtlasPairedReadingWidths();
        }
        finally
        {
            if (left) _atlasPairedLeftAppending = false;
            else _atlasPairedRightAppending = false;
        }
    }

    private Border CreateAtlasPairedVerseCard(
        ContextAtlasPreviewVerse verse, ProposalSurah chapter, bool left)
    {
        var stack = new StackPanel { Spacing = 7 };
        ProposalContextBlock block = chapter.ContextBlocks.Single(b =>
            b.StartAyah <= verse.VerseNumber && verse.VerseNumber <= b.EndAyah);
        bool atStart = verse.VerseNumber == block.StartAyah;
        if (atStart)
        {
            AddAtlasPairedText(stack,
                $"Context {chapter.SurahNumber}:{block.DisplayRange} · {block.CoherenceNote}",
                13, left, accent: true);
        }
        if (verse.VerseNumber == _atlasPairedReadBoundary ||
            verse.VerseNumber == _atlasPairedReadBoundary + 1)
        {
            AddAtlasPairedText(stack,
                verse.VerseNumber == _atlasPairedReadBoundary
                ? $"Boundary inspection · Ayah {verse.VerseKey}"
                : $"After disputed transition · Ayah {verse.VerseKey}",
                15, left, accent: true);
        }
        else
            AddAtlasPairedText(stack, $"Ayah {verse.VerseKey}", 14, left);

        if (UthmaniToggle.IsChecked == true)
            AddAtlasPairedArabic(stack, "Uthmani", verse.Uthmani, false, left);
        if (IndoPakToggle.IsChecked == true)
            AddAtlasPairedArabic(stack, "IndoPak", verse.IndoPak, true, left);
        if (NastaleeqToggle.IsChecked == true)
            AddAtlasPairedArabic(stack, "IndoPak Nastaleeq", verse.IndoPakNastaleeq, true, left);

        if (verse.English is { Length: > 0 })
        {
            AddAtlasPairedText(stack, "English", 12, left);
            AddAtlasPairedText(stack, verse.English, 16, left,
                centered: true, lineHeight: 25);
        }
        if (verse.Bengali is { Length: > 0 })
        {
            AddAtlasPairedText(stack, "বাংলা · Bengali", 12, left);
            var bn = AddAtlasPairedText(stack, verse.Bengali, 17, left,
                centered: true, lineHeight: 29);
            bn.FontFamily = new FontFamily("Nirmala UI");
        }

        if (verse.VerseNumber == _atlasPairedReadBoundary ||
            verse.VerseNumber == _atlasPairedReadBoundary + 1)
        {
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var target = new ContextAtlasRangeTarget(
                chapter.SurahNumber, verse.VerseNumber, verse.VerseNumber,
                $"Ayah {verse.VerseKey}");
            var evidence = new Button { Content = "Ayah Evidence", Tag = target,
                Padding = new Thickness(8, 4, 8, 4) };
            evidence.Click += AtlasEvidence_Click;
            tools.Children.Add(evidence);
            var pin = new Button { Content = "Pin Ayah", Tag = target,
                Padding = new Thickness(8, 4, 8, 4) };
            pin.Click += AtlasPin_Click;
            tools.Children.Add(pin);
            stack.Children.Add(tools);
        }

        return new Border
        {
            Background = Brush(
                verse.VerseNumber == _atlasPairedReadBoundary + 1 ? "PanelAltBrush" : "CardBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = stack
        };
    }

    private TextBlock AddAtlasPairedText(
        StackPanel panel, string text, double size, bool left,
        bool accent = false, bool centered = false, double? lineHeight = null)
    {
        var scroll = left ? AtlasCompareReadingLeftScroll : AtlasCompareReadingRightScroll;
        var control = new TextBlock
        {
            Text = text,
            FontSize = Z(size),
            Foreground = Brush(accent ? "AccentBrush" : "TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
            IsTextSelectionEnabled = true,
            MaxWidth = Math.Max(90, scroll.ActualWidth - 52),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left
        };
        if (lineHeight.HasValue)
            control.LineHeight = Z(lineHeight.Value);
        (left ? _atlasPairedLeftText : _atlasPairedRightText).Add(control);
        _atlasPairedZoom.Add((control, size, lineHeight));
        panel.Children.Add(control);
        return control;
    }

    private void AddAtlasPairedArabic(
        StackPanel panel, string label, string text, bool indoPak, bool left)
    {
        AddAtlasPairedText(panel, label, 12, left);
        var arabic = AddAtlasPairedText(panel, text, indoPak ? 35 : 36,
            left, centered: true, lineHeight: indoPak ? 78 : 66);
        arabic.FlowDirection = FlowDirection.RightToLeft;
        arabic.SelectionHighlightColor = Brush("AccentBrush");
        string fontFile = indoPak
            ? "indopak-nastaleeq-waqf-lazim-v4.2.1.ttf"
            : "UthmanicHafs1Ver18.ttf";
        string family = indoPak
            ? "AlQuran IndoPak by QuranWBW"
            : "KFGQPC HAFS Uthmanic Script";
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Fonts", fontFile)))
            arabic.FontFamily = new FontFamily($"Fonts/{fontFile}#{family}");
    }

    private void AtlasCompareReadingGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateAtlasPairedReadingLayout();

    private void UpdateAtlasPairedReadingLayout()
    {
        if (AtlasCompareReadingGrid is null ||
            AtlasCompareReadingColumns is null)
            return;

        bool stacked = AtlasCompareReadingGrid.ActualWidth < 840;
        AtlasCompareReadingColumns.ColumnDefinitions[0].Width =
            new GridLength(1, GridUnitType.Star);
        AtlasCompareReadingColumns.ColumnDefinitions[1].Width =
            stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        AtlasCompareReadingColumns.RowDefinitions[0].Height =
            new GridLength(1, GridUnitType.Star);
        AtlasCompareReadingColumns.RowDefinitions[1].Height =
            stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(AtlasCompareReadingLeftBorder, 0);
        Grid.SetRow(AtlasCompareReadingLeftBorder, 0);
        Grid.SetColumn(AtlasCompareReadingRightBorder, stacked ? 0 : 1);
        Grid.SetRow(AtlasCompareReadingRightBorder, stacked ? 1 : 0);
        RefreshAtlasPairedReadingWidths();
    }

    private void AtlasCompareReadingScroll_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshAtlasPairedReadingWidths();

    private void RefreshAtlasPairedReadingWidths()
    {
        RefreshSide(true);
        RefreshSide(false);

        void RefreshSide(bool left)
        {
            var scroll = left ? AtlasCompareReadingLeftScroll : AtlasCompareReadingRightScroll;
            var panel = left ? AtlasCompareReadingLeftPanel : AtlasCompareReadingRightPanel;
            double width = Math.Max(100, scroll.ActualWidth - 16);
            if (double.IsNaN(panel.Width) || Math.Abs(panel.Width - width) > 0.5)
                panel.Width = width;
            double wrapWidth = Math.Max(85, scroll.ActualWidth - 48);
            foreach (var control in left ? _atlasPairedLeftText : _atlasPairedRightText)
                control.MaxWidth = wrapWidth;
        }
    }

    private void RefreshAtlasPairedReadingScripts()
    {
        if (AtlasCompareReadingGrid.Visibility != Visibility.Visible)
            return;

        double leftOffset = AtlasCompareReadingLeftScroll.VerticalOffset;
        double rightOffset = AtlasCompareReadingRightScroll.VerticalOffset;
        int leftCount = Math.Max(6, _atlasPairedLeftRendered);
        int rightCount = Math.Max(6, _atlasPairedRightRendered);

        _atlasPairedLeftRendered = 0;
        _atlasPairedRightRendered = 0;
        _atlasPairedLeftText.Clear();
        _atlasPairedRightText.Clear();
        _atlasPairedZoom.Clear();
        AtlasCompareReadingLeftPanel.Children.Clear();
        AtlasCompareReadingRightPanel.Children.Clear();

        AppendAtlasPairedReaderCards(true, leftCount);
        AppendAtlasPairedReaderCards(false, rightCount);

        RootGrid.DispatcherQueue.TryEnqueue(() =>
        {
            AtlasCompareReadingLeftScroll.ChangeView(null,
                Math.Min(leftOffset, AtlasCompareReadingLeftScroll.ScrollableHeight),
                null, disableAnimation: true);
            AtlasCompareReadingRightScroll.ChangeView(null,
                Math.Min(rightOffset, AtlasCompareReadingRightScroll.ScrollableHeight),
                null, disableAnimation: true);
        });
    }

    private void ApplyAtlasCompareReadingZoomMetrics()
    {
        if (AtlasCompareReadingGrid.Visibility != Visibility.Visible)
            return;
        double lOffset = AtlasCompareReadingLeftScroll.VerticalOffset;
        double rOffset = AtlasCompareReadingRightScroll.VerticalOffset;
        foreach (var (control, size, lineHeight) in _atlasPairedZoom)
        {
            control.FontSize = Z(size);
            if (lineHeight.HasValue)
                control.LineHeight = Z(lineHeight.Value);
        }
        RefreshAtlasPairedReadingWidths();
        RootGrid.DispatcherQueue.TryEnqueue(() =>
        {
            AtlasCompareReadingLeftScroll.ChangeView(null,
                Math.Min(lOffset, AtlasCompareReadingLeftScroll.ScrollableHeight),
                null, disableAnimation: true);
            AtlasCompareReadingRightScroll.ChangeView(null,
                Math.Min(rOffset, AtlasCompareReadingRightScroll.ScrollableHeight),
                null, disableAnimation: true);
        });
    }
}
