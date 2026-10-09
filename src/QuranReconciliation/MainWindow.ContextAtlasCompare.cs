using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    // Session-only, display-only navigation state. No owner research writes.
    private int _atlasCompareDifferenceIndex;

    private void RenderAtlasVisualCompare()
    {
        AddAtlasHeading("Compare Contexts · Ayah-aligned", 26);
        if (_atlasCorpora.Count < 2)
        {
            AddAtlasMuted("Import a second valid Proposal Corpus to inspect two independent Context maps.");
            return;
        }

        if (AtlasCompareASelector.SelectedItem is not ComboBoxItem ai ||
            ai.Tag is not ProposalCorpusPackage a ||
            AtlasCompareBSelector.SelectedItem is not ComboBoxItem bi ||
            bi.Tag is not ProposalCorpusPackage b)
            return;

        if (string.Equals(a.Corpus.CorpusId, b.Corpus.CorpusId, StringComparison.Ordinal))
        {
            AddAtlasMuted("Choose two different Proposal Corpora in the A and B selectors.");
            return;
        }

        ProposalCorpusComparison summary = ProposalCorpusRepository.Compare(a.Corpus, b.Corpus);
        AddAtlasMuted($"{a.Corpus.DisplayName}  ↔  {b.Corpus.DisplayName}");
        AddAtlasMuted($"All Surahs · Shared boundaries: {summary.SharedBoundaries} · A-only: {summary.LeftOnlyBoundaries} · B-only: {summary.RightOnlyBoundaries} · Exactly matching fine ranges: {summary.ExactBlocks}.");
        AddAtlasMuted("Use the Surah list at left. Each comparison examines the same Ayah transition, never mismatched Context ordinal numbers.");

        ProposalSurah left = a.Corpus.Surahs.Single(x => x.SurahNumber == _atlasSurah);
        ProposalSurah right = b.Corpus.Surahs.Single(x => x.SurahNumber == _atlasSurah);

        var leftEdges = left.Boundaries.Select(x => x.AfterAyah).ToHashSet();
        var rightEdges = right.Boundaries.Select(x => x.AfterAyah).ToHashSet();
        List<int> differences = leftEdges.SymmetricDifferenceOrdered(rightEdges);
        bool macroSame = left.MacroGroups
            .Select(x => (x.StartAyah, x.EndAyah, x.Label))
            .SequenceEqual(right.MacroGroups.Select(x => (x.StartAyah, x.EndAyah, x.Label)));

        AddAtlasSearchText(
            $"Surah {_atlasSurah} · {left.SurahName}",
            $"A blocks: {left.ContextBlocks.Count} · B blocks: {right.ContextBlocks.Count} · " +
            $"A-only boundaries: {leftEdges.Except(rightEdges).Count()} · " +
            $"B-only boundaries: {rightEdges.Except(leftEdges).Count()} · " +
            $"Macro Groups: {(macroSame ? "matching spans/headings" : "different spans or headings")}.");

        if (differences.Count == 0)
        {
            AddAtlasMuted("Both proposals agree on every fine Context boundary in this Surah. " +
                (macroSame ? "Their Macro Groups also match." :
                 "Their Macro Groups still differ; review them in the corpus Browse view."));
            return;
        }

        _atlasCompareDifferenceIndex = Math.Clamp(_atlasCompareDifferenceIndex, 0, differences.Count - 1);
        int afterAyah = differences[_atlasCompareDifferenceIndex];

        // Only the current disagreement is materialized as native controls.
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Margin = new Thickness(0, 8, 0, 8) };
        var previous = new Button { Content = "← Previous difference",
            IsEnabled = _atlasCompareDifferenceIndex > 0, Padding = new Thickness(12, 5, 12, 5) };
        previous.Click += (_, _) => MoveAtlasCompareDifference(-1);
        nav.Children.Add(previous);
        var next = new Button { Content = "Next difference →",
            IsEnabled = _atlasCompareDifferenceIndex + 1 < differences.Count,
            Padding = new Thickness(12, 5, 12, 5) };
        next.Click += (_, _) => MoveAtlasCompareDifference(1);
        nav.Children.Add(next);
        var paired = new Button
        {
            Content = "Read Ayat side by side",
            Padding = new Thickness(12, 5, 12, 5)
        };
        paired.Click += AtlasCompareReadingOpen_Click;
        nav.Children.Add(paired);
        AtlasContentPanel.Children.Add(nav);

        bool aSplits = leftEdges.Contains(afterAyah);
        bool bSplits = rightEdges.Contains(afterAyah);
        AddAtlasSearchText(
            $"Disagreement {_atlasCompareDifferenceIndex + 1} of {differences.Count} · after Ayah {_atlasSurah}:{afterAyah}",
            aSplits ? $"A splits after {_atlasSurah}:{afterAyah}; B continues across it." :
                      $"B splits after {_atlasSurah}:{afterAyah}; A continues across it.");

        var columns = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        StackPanel leftPanel = CreateAtlasCompareColumn(a.Corpus.DisplayName, left, afterAyah, aSplits);
        StackPanel rightPanel = CreateAtlasCompareColumn(b.Corpus.DisplayName, right, afterAyah, bSplits);

        // Keep a readable alternative on narrower windows. Re-evaluated on
        // Compare refresh, not coupled to the frozen right sidebar.
        if (AtlasContentScrollViewer.ActualWidth < 760)
        {
            var stacked = new StackPanel { Spacing = 14 };
            stacked.Children.Add(leftPanel);
            stacked.Children.Add(rightPanel);
            AtlasContentPanel.Children.Add(stacked);
        }
        else
        {
            Grid.SetColumn(leftPanel, 0);
            Grid.SetColumn(rightPanel, 1);
            columns.Children.Add(leftPanel);
            columns.Children.Add(rightPanel);
            AtlasContentPanel.Children.Add(columns);
        }
        AddAtlasMuted("Compare is read-only. Reading a passage or navigating here never applies a proposal to your research.");
    }

    private void MoveAtlasCompareDifference(int delta)
    {
        _atlasCompareDifferenceIndex += delta;
        RefreshContextAtlas();
    }

    private StackPanel CreateAtlasCompareColumn(
        string displayName, ProposalSurah surah, int afterAyah, bool splits)
    {
        var column = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        var title = new TextBlock
        {
            Text = displayName,
            Foreground = Brush("AccentBrush"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        column.Children.Add(title);
        column.Children.Add(new TextBlock
        {
            Text = splits ? $"Boundary after Ayah {afterAyah}" : $"No boundary after Ayah {afterAyah}",
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        var evidenceActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (int ayah in new[] { afterAyah, afterAyah + 1 })
        {
            var inspect = new Button
            {
                Content = $"Evidence {surah.SurahNumber}:{ayah}",
                Tag = new ContextAtlasRangeTarget(surah.SurahNumber, ayah, ayah, $"Ayah {surah.SurahNumber}:{ayah}"),
                Padding = new Thickness(8, 4, 8, 4)
            };
            inspect.Click += AtlasEvidence_Click;
            evidenceActions.Children.Add(inspect);
        }
        column.Children.Add(evidenceActions);


        ProposalContextBlock before = surah.ContextBlocks.Single(x =>
            x.StartAyah <= afterAyah && x.EndAyah >= afterAyah);
        ProposalContextBlock after = surah.ContextBlocks.Single(x =>
            x.StartAyah <= afterAyah + 1 && x.EndAyah >= afterAyah + 1);

        AddAtlasCompareBlock(column, surah, before, "Context containing previous Ayah");
        if (!string.Equals(before.ContextBlockId, after.ContextBlockId, StringComparison.Ordinal))
            AddAtlasCompareBlock(column, surah, after, "Context containing next Ayah");

        ProposalBoundary? boundary = surah.Boundaries
            .FirstOrDefault(x => x.AfterAyah == afterAyah);
        Border rationale = NewAtlasCard();
        rationale.Background = Brush("PanelAltBrush");
        rationale.Child = new TextBlock
        {
            Text = boundary is null
                ? $"This proposal keeps Ayat {afterAyah} and {afterAyah + 1} together in a single Context."
                : $"Boundary reason · {boundary.Reason}\nConfidence: {boundary.Confidence}",
            Foreground = Brush("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        column.Children.Add(rationale);

        ProposalMacroGroup? macro = surah.MacroGroups
            .FirstOrDefault(x => x.StartAyah <= afterAyah && x.EndAyah >= afterAyah);
        if (macro is not null)
        {
            column.Children.Add(new TextBlock
            {
                Text = $"Macro Group · {macro.StartAyah}-{macro.EndAyah} · {macro.Label}",
                Foreground = Brush("MutedTextBrush"),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            });
        }
        return column;
    }

    private void AddAtlasCompareBlock(
        StackPanel column, ProposalSurah surah, ProposalContextBlock block, string role)
    {
        Border card = NewAtlasCard();
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = $"{role} · {surah.SurahNumber}:{block.DisplayRange}",
            Foreground = Brush("AccentBrush"),
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = block.CoherenceNote,
            Foreground = Brush("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        });

        var read = new Button
        {
            Content = "Read A/B Ayat",
            Tag = new ContextAtlasRangeTarget(
                surah.SurahNumber, block.StartAyah, block.EndAyah,
                $"Compare · {surah.SurahName} · {block.DisplayRange}"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 4, 10, 4)
        };
        read.Click += AtlasCompareReadingOpen_Click;
        content.Children.Add(read);
        var pin = new Button
        {
            Content = "Pin Context",
            Tag = new ContextAtlasRangeTarget(surah.SurahNumber, block.StartAyah, block.EndAyah,
                $"{surah.SurahName} {block.DisplayRange}"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 4, 10, 4)
        };
        pin.Click += AtlasPin_Click;
        content.Children.Add(pin);
        card.Child = content;
        column.Children.Add(card);
    }
}

internal static class AtlasCompareSetExtensions
{
    // Difference location is the Ayah number AFTER which one proposal splits
    // and the other does not; symmetric difference is stable and repeatable.
    internal static List<int> SymmetricDifferenceOrdered(this HashSet<int> left, HashSet<int> right) =>
        left.Except(right).Concat(right.Except(left)).Distinct().OrderBy(x => x).ToList();
}
