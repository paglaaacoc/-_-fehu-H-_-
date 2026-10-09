using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private readonly WordByWordRepository _atlasWordEvidence = new();
    private bool _atlasEvidenceOpening;

    private async void AtlasEvidence_Click(object sender, RoutedEventArgs e)
    {
        if (_atlasEvidenceOpening ||
            sender is not Button button ||
            button.Tag is not ContextAtlasRangeTarget target ||
            target.StartAyah != target.EndAyah)
            return;

        _atlasEvidenceOpening = true;
        try
        {
            string verseKey = $"{target.SurahNumber}:{target.StartAyah}";

            // Both readers are SELECT-only and use existing local databases.
            // No new research storage, queries of neighboring Surahs, or writes.
            var result = await Task.Run(() =>
            {
                IReadOnlyList<WordByWordEntry> words = _atlasWordEvidence.GetVerse(verseKey);
                ContextAtlasResearchSnapshot research = _atlasResearchSnapshot.ReadRange(
                    target.SurahNumber, target.StartAyah, target.EndAyah);
                return (words, research);
            });

            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock
            {
                Text = $"Local evidence for Ayah {verseKey} · read-only",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("AccentBrush"),
                TextWrapping = TextWrapping.Wrap
            });

            ContextAtlasResearchSnapshot rs = result.research;
            content.Children.Add(new TextBlock
            {
                Text = $"{rs.Contexts.Count} matching/overlapping live research Contexts · " +
                       $"{rs.WorkingSlices.Count} Working Slices · {rs.AyahNoteCount} Ayah notes · " +
                       $"{rs.AyahNoteRevisionCount} Ayah note revisions · " +
                       $"{rs.ContextNoteCount} Context notes in overlapping research Contexts.",
                Foreground = Brush("TextBrush"),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            });

            foreach (ContextAtlasLiveContext item in rs.Contexts.Take(8))
            {
                content.Children.Add(new TextBlock
                {
                    Text = $"Research Context {item.StartAyah}-{item.EndAyah} · {item.Status}",
                    Foreground = Brush("MutedTextBrush"),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            foreach (ContextAtlasWorkingSliceSummary slice in rs.WorkingSlices.Take(8))
            {
                content.Children.Add(new TextBlock
                {
                    Text = $"Working Slice · {slice.Title} · {slice.StartAyah}-{slice.EndAyah} · {slice.Status}",
                    Foreground = Brush("MutedTextBrush"),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            content.Children.Add(new TextBlock
            {
                Text = $"Existing word-by-word evidence · {result.words.Count} word positions",
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("AccentBrush"),
                Margin = new Thickness(0, 8, 0, 0)
            });

            // These are source words already bundled with the accepted workstation.
            // Virtualized native ListView retains good behavior for long Ayat.
            var wordsList = new ListView
            {
                Height = 340,
                SelectionMode = ListViewSelectionMode.None,
                ItemsSource = result.words.Select(word =>
                    $"{word.Position}.  {word.Arabic}   ·   {word.Transliteration}   ·   {word.EnglishMeaning}")
                    .ToList()
            };
            content.Children.Add(wordsList);
            content.Children.Add(new TextBlock
            {
                Text = "Evidence is derived solely from local word-by-word data and a read-only summary " +
                       "of your existing research. No proposal is applied and no note is changed.",
                Foreground = Brush("MutedTextBrush"),
                TextWrapping = TextWrapping.Wrap
            });

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = $"Ayah Evidence · {verseKey}",
                Content = new ScrollViewer
                {
                    Content = content,
                    MaxHeight = 660,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // A missing/unavailable evidence database is a harmless closed state.
            await ShowAtlasMessageAsync("Ayah Evidence unavailable", ex.Message);
        }
        finally
        {
            _atlasEvidenceOpening = false;
        }
    }
}
