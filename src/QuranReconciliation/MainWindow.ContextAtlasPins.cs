using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuranReconciliation.Models;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private const int AtlasPinLimit = 40;
    private Grid? _atlasResponsivePinsGrid;
    private Border? _atlasResponsivePinsA;
    private Border? _atlasResponsivePinsB;
    private readonly List<ContextAtlasRangeTarget> _atlasPinnedRanges = [];
    private ContextAtlasRangeTarget? _atlasPinA;
    private ContextAtlasRangeTarget? _atlasPinB;

    private static bool SameAtlasRange(ContextAtlasRangeTarget a, ContextAtlasRangeTarget b) =>
        a.SurahNumber == b.SurahNumber && a.StartAyah == b.StartAyah && a.EndAyah == b.EndAyah;

    private void AtlasPin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ContextAtlasRangeTarget target)
            return;
        if (_atlasPinnedRanges.Any(x => SameAtlasRange(x, target)))
        {
            StatusText.Text = $"Already pinned · {target.SurahNumber}:{target.StartAyah}-{target.EndAyah}";
            return;
        }
        if (_atlasPinnedRanges.Count >= AtlasPinLimit)
        {
            StatusText.Text = "Session pin tray is full (40). Remove a pin before adding another.";
            return;
        }
        _atlasPinnedRanges.Add(target);
        _atlasPinA ??= target;
        if (_atlasPinB is null && !SameAtlasRange(_atlasPinA, target))
            _atlasPinB = target;
        AtlasPinsTabButton.Content = $"Pins ({_atlasPinnedRanges.Count})";
        StatusText.Text = $"Pinned for this session · {target.SurahNumber}:{target.StartAyah}-{target.EndAyah}";
        // Do not disturb the current reading location or compare tab.
        if (_atlasMode == "Pins")
            RefreshContextAtlas();
    }

    private void RenderAtlasPins()
    {
        AddAtlasHeading("Pin & Compare · this session only", 26);
        AddAtlasMuted("Pin Ayat or complete Context Blocks from Browse, Compare, or Read Ayat. " +
            "Nothing is written to research storage or carried into the next launch.");

        if (_atlasPinnedRanges.Count == 0)
        {
            AddAtlasMuted("No pins yet. Open a Context or Read Ayat and choose Pin.");
            return;
        }

        if (_atlasPinA is not null && _atlasPinB is not null)
            RenderAtlasPinnedPair(_atlasPinA, _atlasPinB);
        else
            AddAtlasMuted("Select a second pin as B to see two passages side by side.");

        AddAtlasHeading($"Pinned passages ({_atlasPinnedRanges.Count} / {AtlasPinLimit})", 19, 16);
        var clear = new Button { Content = "Clear session pins", Margin = new Thickness(0, 0, 0, 8) };
        clear.Click += (_, _) =>
        {
            _atlasPinnedRanges.Clear();
            _atlasPinA = null;
            _atlasPinB = null;
            AtlasPinsTabButton.Content = "Pins";
            RefreshContextAtlas();
        };
        AtlasContentPanel.Children.Add(clear);

        foreach (ContextAtlasRangeTarget pin in _atlasPinnedRanges.ToList())
        {
            Border card = NewAtlasCard();
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock
            {
                Text = $"{pin.SurahNumber}:{pin.StartAyah}-{pin.EndAyah} · {pin.Label}",
                Foreground = Brush("TextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var useA = new Button
            {
                Content = _atlasPinA is not null && SameAtlasRange(_atlasPinA, pin) ? "A ✓" : "Set A",
                Tag = pin, Padding = new Thickness(8, 4, 8, 4)
            };
            useA.Click += (_, _) => { _atlasPinA = pin; RefreshContextAtlas(); };
            actions.Children.Add(useA);
            var useB = new Button
            {
                Content = _atlasPinB is not null && SameAtlasRange(_atlasPinB, pin) ? "B ✓" : "Set B",
                Tag = pin, Padding = new Thickness(8, 4, 8, 4)
            };
            useB.Click += (_, _) => { _atlasPinB = pin; RefreshContextAtlas(); };
            actions.Children.Add(useB);
            var read = new Button { Content = "Read Ayat", Tag = pin,
                Padding = new Thickness(8, 4, 8, 4) };
            read.Click += AtlasReadingOpen_Click;
            actions.Children.Add(read);
            var remove = new Button { Content = "Remove", Tag = pin,
                Padding = new Thickness(8, 4, 8, 4) };
            remove.Click += (_, _) =>
            {
                _atlasPinnedRanges.RemoveAll(x => SameAtlasRange(x, pin));
                if (_atlasPinA is not null && SameAtlasRange(_atlasPinA, pin)) _atlasPinA = null;
                if (_atlasPinB is not null && SameAtlasRange(_atlasPinB, pin)) _atlasPinB = null;
                AtlasPinsTabButton.Content = _atlasPinnedRanges.Count == 0
                    ? "Pins" : $"Pins ({_atlasPinnedRanges.Count})";
                RefreshContextAtlas();
            };
            actions.Children.Add(remove);
            group.Children.Add(actions);
            card.Child = group;
            AtlasContentPanel.Children.Add(card);
        }
    }

    private void RenderAtlasPinnedPair(ContextAtlasRangeTarget left, ContextAtlasRangeTarget right)
    {
        AddAtlasHeading("Pinned passages · paired evidence", 19);
        var first = CreateAtlasPinnedEvidence(left, "A");
        var second = CreateAtlasPinnedEvidence(right, "B");

        // A single responsive Grid avoids a clipped right Arabic column when
        // users resize or zoom while the session pin tray is visible.
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        grid.Children.Add(first);
        grid.Children.Add(second);

        _atlasResponsivePinsGrid = grid;
        _atlasResponsivePinsA = first;
        _atlasResponsivePinsB = second;
        AtlasContentPanel.Children.Add(grid);
        RefreshAtlasPinnedPairLayout();
    }

    // Called by the existing Atlas viewport SizeChanged handler.
    // No accumulating event closures across pin additions/removals.
    private void RefreshAtlasPinnedPairLayout()
    {
        if (_atlasMode != "Pins" ||
            _atlasResponsivePinsGrid is null ||
            _atlasResponsivePinsA is null ||
            _atlasResponsivePinsB is null ||
            _atlasResponsivePinsGrid.Parent is null)
            return;

        Grid grid = _atlasResponsivePinsGrid;
        Border first = _atlasResponsivePinsA;
        Border second = _atlasResponsivePinsB;
        double width = Math.Max(140, AtlasContentScrollViewer.ActualWidth - 18);
        bool stacked = width < 760;
        grid.Width = width;
        grid.ColumnDefinitions[1].Width =
            stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        grid.RowDefinitions[1].Height =
            stacked ? GridLength.Auto : new GridLength(0);
        double columnWidth = stacked ? width : (width - 12) / 2;
        first.Width = columnWidth;
        second.Width = columnWidth;
        Grid.SetColumn(first, 0);
        Grid.SetRow(first, 0);
        Grid.SetColumn(second, stacked ? 0 : 1);
        Grid.SetRow(second, stacked ? 1 : 0);
    }

    private Border CreateAtlasPinnedEvidence(ContextAtlasRangeTarget pin, string side)
    {
        Border card = NewAtlasCard();
        double viewport = Math.Max(180, AtlasContentScrollViewer.ActualWidth - 18);
        double cardWidth = viewport < 760 ? viewport : (viewport - 12) / 2;
        double textWidth = Math.Max(110, cardWidth - 34);
        var stack = new StackPanel { Spacing = 10, Width = textWidth };
        var wrappedText = new List<TextBlock>();
        stack.Children.Add(new TextBlock
        {
            Text = $"Pin {side} · {pin.SurahNumber}:{pin.StartAyah}-{pin.EndAyah} · {pin.Label}",
            Foreground = Brush("AccentBrush"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = textWidth
        });
        // Keep every Arabic and explanatory TextBlock within the measured card.
        // The initial width is only a fallback before WinUI assigns the real size.
        card.SizeChanged += (_, _) =>
        {
            double measured = Math.Max(90, card.ActualWidth - 34);
            stack.Width = measured;
            foreach (TextBlock target in wrappedText)
                target.MaxWidth = measured;
        };
        wrappedText.Add((TextBlock)stack.Children[0]);
        try
        {
            int windowEnd = Math.Min(pin.EndAyah, pin.StartAyah + 2);
            var evidence = _atlasPreviewReader.ReadRange(
                pin.SurahNumber, pin.StartAyah, windowEnd, -1, -1);
            foreach (var ayah in evidence)
            {
                var ayahLabel = new TextBlock
                {
                    Text = $"Ayah {ayah.VerseKey}",
                    Foreground = Brush("MutedTextBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = textWidth
                };
                stack.Children.Add(ayahLabel);
                wrappedText.Add(ayahLabel);
                var arabicText = new TextBlock
                {
                    Text = ayah.Uthmani,
                    FontFamily = new FontFamily(
                        "Fonts/UthmanicHafs1Ver18.ttf#KFGQPC HAFS Uthmanic Script"),
                    FontSize = Z(24),
                    FlowDirection = FlowDirection.RightToLeft,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.None,
                    MaxWidth = textWidth,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Foreground = Brush("TextBrush"),
                    IsTextSelectionEnabled = true
                };
                stack.Children.Add(arabicText);
                wrappedText.Add(arabicText);
            }
            if (windowEnd < pin.EndAyah)
                stack.Children.Add(new TextBlock
                {
                    Text = $"Showing first {evidence.Count} Ayat only. Use Read Ayat for the whole Context.",
                    Foreground = Brush("MutedTextBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = textWidth
                });
        }
        catch (Exception ex)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Pinned evidence unavailable: " + ex.Message,
                Foreground = Brush("MutedTextBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        var read = new Button { Content = "Read full passage", Tag = pin,
            HorizontalAlignment = HorizontalAlignment.Left };
        read.Click += AtlasReadingOpen_Click;
        stack.Children.Add(read);
        card.Child = stack;
        return card;
    }
}
