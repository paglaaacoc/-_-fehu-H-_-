using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private readonly ContextAtlasVersePreviewReader _atlasPreviewReader = new();
    private IReadOnlyList<ContextAtlasPreviewVerse> _atlasPreviewVerses = [];
    private readonly List<(TextBlock Control, double Size, double? LineHeight)> _atlasPreviewZoomTargets = [];
    private readonly List<TextBlock> _atlasPreviewWrapTargets = [];
    private ContextAtlasRangeTarget? _atlasPreviewTarget;
    private int _atlasPreviewRendered;
    private int _atlasPreviewRequest;
    private bool _atlasPreviewAppending;
    private bool _atlasPreviewInitializingSources;

    // The chosen Context remains authoritative; neighbors are transient evidence.
    private const int AtlasSurroundingRadius = 3;

    private void AtlasLiveResearchScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // WinUI ScrollViewer can leave an inner StackPanel with unbounded desired width.
        // Fit its children to the actual sidebar viewport, including after zoom/resize.
        double width = Math.Max(110, AtlasLiveResearchScrollViewer.ActualWidth - 18);
        if (double.IsNaN(AtlasLiveResearchPanel.Width) || Math.Abs(AtlasLiveResearchPanel.Width - width) > 0.5)
            AtlasLiveResearchPanel.Width = width;
    }

    private void AtlasReadingScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshAtlasReadingCardWidths();

    private void RefreshAtlasReadingCardWidths()
    {
        // An Auto StackPanel width is NaN. A numeric difference cannot initialize it.
        // Bound the card column and selectable verse text to the actual viewport
        // so long Arabic, English and Bangla passages wrap rather than clip.
        double panelWidth = Math.Max(140, AtlasReadingScrollViewer.ActualWidth - 18);
        if (double.IsNaN(AtlasReadingCardsPanel.Width) ||
            Math.Abs(AtlasReadingCardsPanel.Width - panelWidth) > 0.5)
            AtlasReadingCardsPanel.Width = panelWidth;

        double textWidth = Math.Max(96, panelWidth - 46); // card padding and border
        foreach (TextBlock text in _atlasPreviewWrapTargets)
            text.MaxWidth = textWidth;
    }

    private void InitializeAtlasReadingSources()
    {
        if (AtlasReadingEnglishSelector.Items.Count != 0)
            return;

        _atlasPreviewInitializingSources = true;
        try
        {
            AtlasReadingEnglishSelector.Items.Add(new ComboBoxItem { Content = "None", Tag = -1 });
            AtlasReadingBengaliSelector.Items.Add(new ComboBoxItem { Content = "None", Tag = -1 });

            foreach (ResourceSummary source in _resources.Where(x => x.Kind == "translation"))
            {
                var item = new ComboBoxItem { Content = source.Name, Tag = source.Id };
                if (source.IsBengali)
                    AtlasReadingBengaliSelector.Items.Add(item);
                else if (source.LanguageName.Equals("english", StringComparison.OrdinalIgnoreCase))
                    AtlasReadingEnglishSelector.Items.Add(item);
            }

            SelectPreviewSource(AtlasReadingEnglishSelector, 20);
            SelectPreviewSource(AtlasReadingBengaliSelector, 161);
        }
        finally
        {
            _atlasPreviewInitializingSources = false;
        }
    }

    private static void SelectPreviewSource(ComboBox selector, int id)
    {
        for (int index = 0; index < selector.Items.Count; index++)
        {
            if (selector.Items[index] is ComboBoxItem item &&
                item.Tag is int candidate && candidate == id)
            {
                selector.SelectedIndex = index;
                return;
            }
        }
        selector.SelectedIndex = 0;
    }

    private static int PreviewSelectedId(ComboBox selector) =>
        selector.SelectedItem is ComboBoxItem item && item.Tag is int id ? id : -1;

    private async void AtlasReadingOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ContextAtlasRangeTarget target)
            await OpenAtlasReadingAsync(target);
    }

    private async Task OpenAtlasReadingAsync(ContextAtlasRangeTarget target)
    {
        if (_atlasCurrentCorpus is null ||
            ContextAtlasWorkspaceGrid.Visibility != Visibility.Visible)
            return;

        InitializeAtlasReadingSources();
        _atlasPreviewTarget = target;
        _atlasSelectedRange = target;
        _atlasSurah = target.SurahNumber;
        AtlasReadingRangeLabel.Text = $"{target.SurahNumber}:{target.StartAyah}–{target.EndAyah} · {target.Label}";
        AtlasContentScrollViewer.Visibility = Visibility.Collapsed;
        AtlasReadingPreviewGrid.Visibility = Visibility.Visible;
        AtlasReadingCardsPanel.Children.Clear();
        _atlasPreviewZoomTargets.Clear();
        _atlasPreviewWrapTargets.Clear();
        _atlasPreviewVerses = [];
        _atlasPreviewRendered = 0;
        RefreshAtlasLiveResearchPane();
        UpdateAtlasReadingNeighbourButtons();

        int request = ++_atlasPreviewRequest;
        int english = PreviewSelectedId(AtlasReadingEnglishSelector);
        int bengali = PreviewSelectedId(AtlasReadingBengaliSelector);

        AtlasReadingCardsPanel.Children.Add(new TextBlock
        {
            Text = "Loading local Quran evidence…",
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap
        });

        try
        {
            // Bounded, SELECT-only: selected passage plus up to three
            // preceding and three following Ayat, asynchronously.
            int evidenceStart = target.StartAyah;
            int evidenceEnd = target.EndAyah;
            if (AtlasReadingSurroundingToggle.IsChecked == true)
            {
                evidenceStart = Math.Max(1, target.StartAyah - AtlasSurroundingRadius);
                evidenceEnd = Math.Min(_chapters[target.SurahNumber - 1].VersesCount,
                    target.EndAyah + AtlasSurroundingRadius);
            }
            IReadOnlyList<ContextAtlasPreviewVerse> loaded = await Task.Run(() =>
                _atlasPreviewReader.ReadRange(target.SurahNumber, evidenceStart,
                    evidenceEnd, english, bengali));

            if (request != _atlasPreviewRequest ||
                AtlasReadingPreviewGrid.Visibility != Visibility.Visible)
                return;

            _atlasPreviewVerses = loaded;
            AtlasReadingCardsPanel.Children.Clear();
            _atlasPreviewZoomTargets.Clear();
        _atlasPreviewWrapTargets.Clear();
            AppendAtlasReadingCards(6);
            AtlasReadingScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
        }
        catch (Exception ex)
        {
            if (request != _atlasPreviewRequest)
                return;
            AtlasReadingCardsPanel.Children.Clear();
            AtlasReadingCardsPanel.Children.Add(new TextBlock
            {
                Text = "Reading preview unavailable: " + ex.Message,
                Foreground = Brush("TextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private void UpdateAtlasReadingNeighbourButtons()
    {
        var blocks = GetAtlasPreviewBlocks();
        int index = AtlasPreviewBlockIndex(blocks);
        AtlasReadingPreviousButton.IsEnabled = index > 0;
        AtlasReadingNextButton.IsEnabled = index >= 0 && index + 1 < blocks.Count;
    }

    private IReadOnlyList<ProposalContextBlock> GetAtlasPreviewBlocks() =>
        _atlasCurrentCorpus?.Corpus.Surahs
            .FirstOrDefault(x => x.SurahNumber == _atlasPreviewTarget?.SurahNumber)
            ?.ContextBlocks.OrderBy(x => x.StartAyah).ToList()
            ?? [];

    private int AtlasPreviewBlockIndex(IReadOnlyList<ProposalContextBlock> blocks)
    {
        if (_atlasPreviewTarget is null)
            return -1;
        for (int i = 0; i < blocks.Count; i++)
            if (blocks[i].StartAyah == _atlasPreviewTarget.StartAyah &&
                blocks[i].EndAyah == _atlasPreviewTarget.EndAyah)
                return i;

        // Search/workset selections may cover more than one fine Context Block.
        for (int i = 0; i < blocks.Count; i++)
            if (blocks[i].EndAyah >= _atlasPreviewTarget.StartAyah &&
                blocks[i].StartAyah <= _atlasPreviewTarget.EndAyah)
                return i;
        return -1;
    }

    private async Task MoveAtlasPreviewAsync(int step)
    {
        var blocks = GetAtlasPreviewBlocks();
        int next = AtlasPreviewBlockIndex(blocks) + step;
        if (next < 0 || next >= blocks.Count || _atlasPreviewTarget is null)
            return;
        ProposalContextBlock block = blocks[next];
        await OpenAtlasReadingAsync(new ContextAtlasRangeTarget(
            _atlasPreviewTarget.SurahNumber, block.StartAyah, block.EndAyah,
            $"Context {block.ContextBlockId} · {block.DisplayRange}"));
    }

    private async void AtlasReadingPrevious_Click(object sender, RoutedEventArgs e) =>
        await MoveAtlasPreviewAsync(-1);

    private async void AtlasReadingNext_Click(object sender, RoutedEventArgs e) =>
        await MoveAtlasPreviewAsync(1);

    private void AtlasReadingClose_Click(object sender, RoutedEventArgs e) =>
        CloseAtlasReadingPreview();

    private void CloseAtlasReadingPreview()
    {
        ++_atlasPreviewRequest; // Ignore any late background read.
        if (AtlasReadingPreviewGrid is null)
            return;
        AtlasReadingPreviewGrid.Visibility = Visibility.Collapsed;
        AtlasContentScrollViewer.Visibility = Visibility.Visible;
        AtlasReadingCardsPanel.Children.Clear();
        _atlasPreviewZoomTargets.Clear();
        _atlasPreviewWrapTargets.Clear();
        _atlasPreviewVerses = [];
        _atlasPreviewTarget = null;
        _atlasPreviewRendered = 0;
    }

    private async void AtlasReadingTranslation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_atlasPreviewInitializingSources || _atlasPreviewTarget is null ||
            AtlasReadingPreviewGrid.Visibility != Visibility.Visible)
            return;
        await OpenAtlasReadingAsync(_atlasPreviewTarget);
    }

    private async void AtlasReadingSurrounding_Changed(object sender, RoutedEventArgs e)
    {
        if (_atlasPreviewTarget is null ||
            AtlasReadingPreviewGrid.Visibility != Visibility.Visible)
            return;
        await OpenAtlasReadingAsync(_atlasPreviewTarget);
    }

    private void AtlasReadingScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_atlasPreviewAppending || _atlasPreviewRendered >= _atlasPreviewVerses.Count ||
            AtlasReadingPreviewGrid.Visibility != Visibility.Visible)
            return;
        double remaining = AtlasReadingScrollViewer.ScrollableHeight -
                           AtlasReadingScrollViewer.VerticalOffset;
        if (remaining < Math.Max(500, AtlasReadingScrollViewer.ViewportHeight))
            AppendAtlasReadingCards(5);
    }

    private void AppendAtlasReadingCards(int batch)
    {
        if (_atlasPreviewAppending || AtlasReadingPreviewGrid.Visibility != Visibility.Visible)
            return;
        _atlasPreviewAppending = true;
        try
        {
            int end = Math.Min(_atlasPreviewVerses.Count, _atlasPreviewRendered + batch);
            for (int i = _atlasPreviewRendered; i < end; i++)
                AtlasReadingCardsPanel.Children.Add(CreateAtlasReadingCard(_atlasPreviewVerses[i]));
            _atlasPreviewRendered = end;
            RefreshAtlasReadingCardWidths();
        }
        finally
        {
            _atlasPreviewAppending = false;
        }
    }

    private TextBlock AtlasReadingText(string value, double size, double? lineHeight = null)
    {
        var control = new TextBlock
        {
            Text = value,
            FontSize = Z(size),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
            IsTextSelectionEnabled = true,
            MaxWidth = Math.Max(96, AtlasReadingScrollViewer.ActualWidth - 64),
            Foreground = Brush("TextBrush"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (lineHeight is double lh)
            control.LineHeight = Z(lh);
        _atlasPreviewZoomTargets.Add((control, size, lineHeight));
        _atlasPreviewWrapTargets.Add(control);
        return control;
    }

    private Border CreateAtlasReadingCard(ContextAtlasPreviewVerse verse)
    {
        var panel = new StackPanel { Spacing = 9 };
        bool outside = _atlasPreviewTarget is not null &&
            (verse.VerseNumber < _atlasPreviewTarget.StartAyah ||
             verse.VerseNumber > _atlasPreviewTarget.EndAyah);
        var title = AtlasReadingText(
            outside ? $"Outside selected Context · Ayah {verse.VerseKey}"
                    : $"Ayah {verse.VerseKey}", 15);
        title.Foreground = outside ? Brush("MutedTextBrush") : Brush("AccentBrush");
        title.FontWeight = FontWeights.SemiBold;
        panel.Children.Add(title);

        // Build 1.5 session-only Ayah pin; does not persist research data.
        if (_atlasPreviewTarget is not null)
        {
            var pinAyah = new Button
            {
                Content = "Pin this Ayah",
                Tag = new ContextAtlasRangeTarget(_atlasPreviewTarget.SurahNumber,
                    verse.VerseNumber, verse.VerseNumber, $"Ayah {verse.VerseKey}"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 4, 10, 4)
            };
            pinAyah.Click += AtlasPin_Click;
            panel.Children.Add(pinAyah);
            var evidence = new Button
            {
                Content = "Ayah Evidence",
                Tag = new ContextAtlasRangeTarget(_atlasPreviewTarget.SurahNumber,
                    verse.VerseNumber, verse.VerseNumber, $"Ayah {verse.VerseKey}"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 4, 10, 4)
            };
            evidence.Click += AtlasEvidence_Click;
            panel.Children.Add(evidence);
        }

        if (UthmaniToggle.IsChecked == true)
            AddAtlasArabic(panel, "Uthmani", verse.Uthmani, "Uthmani");
        if (IndoPakToggle.IsChecked == true)
            AddAtlasArabic(panel, "IndoPak", verse.IndoPak, "IndoPak");
        if (NastaleeqToggle.IsChecked == true)
            AddAtlasArabic(panel, "IndoPak Nastaleeq", verse.IndoPakNastaleeq, "IndoPak");

        if (verse.English is { Length: > 0 } english)
        {
            var heading = AtlasReadingText("English", 13);
            heading.Foreground = Brush("MutedTextBrush");
            panel.Children.Add(heading);
            var en = AtlasReadingText(english, 17, 28);
            en.TextAlignment = TextAlignment.Center;
            panel.Children.Add(en);
        }
        if (verse.Bengali is { Length: > 0 } bengali)
        {
            var heading = AtlasReadingText("বাংলা · Bengali", 13);
            heading.Foreground = Brush("MutedTextBrush");
            panel.Children.Add(heading);
            var bn = AtlasReadingText(bengali, 18, 30);
            bn.FontFamily = new FontFamily("Nirmala UI");
            bn.TextAlignment = TextAlignment.Center;
            panel.Children.Add(bn);
        }

        return new Border
        {
            Background = Brush(outside ? "PanelAltBrush" : "CardBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel
        };
    }

    private void AddAtlasArabic(StackPanel panel, string label, string text, string fontRole)
    {
        var heading = AtlasReadingText(label, 13);
        heading.Foreground = Brush("MutedTextBrush");
        panel.Children.Add(heading);
        var arabic = AtlasReadingText(text, fontRole == "IndoPak" ? 37 : 38,
            fontRole == "IndoPak" ? 82 : 68);
        arabic.FlowDirection = FlowDirection.RightToLeft;
        arabic.TextAlignment = TextAlignment.Center;
        arabic.SelectionHighlightColor = Brush("AccentBrush");
        string file = fontRole == "IndoPak"
            ? "indopak-nastaleeq-waqf-lazim-v4.2.1.ttf"
            : "UthmanicHafs1Ver18.ttf";
        string family = fontRole == "IndoPak"
            ? "AlQuran IndoPak by QuranWBW"
            : "KFGQPC HAFS Uthmanic Script";
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Fonts", file)))
            arabic.FontFamily = new FontFamily($"Fonts/{file}#{family}");
        panel.Children.Add(arabic);
    }

    private void ApplyAtlasReadingZoomMetrics()
    {
        if (AtlasReadingPreviewGrid.Visibility != Visibility.Visible)
            return;
        double offset = AtlasReadingScrollViewer.VerticalOffset;
        foreach (var (text, size, lineHeight) in _atlasPreviewZoomTargets)
        {
            text.FontSize = Z(size);
            if (lineHeight is double lh)
                text.LineHeight = Z(lh);
        }
        // No corpus requery or control recreation on zoom.
        RootGrid.DispatcherQueue.TryEnqueue(() =>
            AtlasReadingScrollViewer.ChangeView(null,
                Math.Min(offset, AtlasReadingScrollViewer.ScrollableHeight), null,
                disableAnimation: true));
    }
}
