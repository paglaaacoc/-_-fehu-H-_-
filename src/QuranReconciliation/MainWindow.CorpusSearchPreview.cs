using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private readonly ContextAtlasVersePreviewReader _searchVersePreview = new();
    private bool _searchPreviewOpen;

    /// <summary>
    /// Pure canonical read-only preview: never touches Research state, Atlas
    /// selection, active Surah, unsaved draft or working slice. Closing returns
    /// to the exact Corpus Search query, loaded page and scroll position.
    /// </summary>
    private async Task ShowCorpusSearchVersePreviewAsync(int surah, int ayah)
    {
        if (_ownerStateOperationActive || _searchPreviewOpen ||
            CorpusSearchWorkspaceGrid.Visibility != Visibility.Visible)
            return;
        _searchPreviewOpen = true;
        try
        {
            // Reuse accepted Atlas SELECT-only canonical verse/translation reader.
            IReadOnlyList<ContextAtlasPreviewVerse> result = await Task.Run(() =>
                _searchVersePreview.ReadRange(surah, ayah, ayah, 20, 161));
            if (_ownerStateOperationActive ||
                CorpusSearchWorkspaceGrid.Visibility != Visibility.Visible ||
                result.Count != 1)
                return;

            var evidence = result[0];
            var panel = new StackPanel { Spacing = 10, Padding = new Thickness(12, 8, 12, 12) };
            panel.Children.Add(SearchPreviewLabel(
                $"Canonical Ayah · {evidence.VerseKey} · read-only", 18));
            AddSearchPreviewArabic(panel, "Uthmani", evidence.Uthmani,
                "UthmanicHafs1Ver18.ttf", "KFGQPC HAFS Uthmanic Script");
            AddSearchPreviewArabic(panel, "IndoPak", evidence.IndoPak,
                "indopak-nastaleeq-waqf-lazim-v4.2.1.ttf", "AlQuran IndoPak by QuranWBW");
            AddSearchPreviewArabic(panel, "IndoPak Nastaleeq", evidence.IndoPakNastaleeq,
                "indopak-nastaleeq-waqf-lazim-v4.2.1.ttf", "AlQuran IndoPak by QuranWBW");
            if (!string.IsNullOrWhiteSpace(evidence.English))
            {
                panel.Children.Add(SearchPreviewLabel("English translation", 14));
                panel.Children.Add(SearchPreviewLabel(evidence.English, 17));
            }
            if (!string.IsNullOrWhiteSpace(evidence.Bengali))
            {
                panel.Children.Add(SearchPreviewLabel("বাংলা translation", 14));
                var bangla = SearchPreviewLabel(evidence.Bengali, 18);
                bangla.FontFamily = new FontFamily("Nirmala UI");
                panel.Children.Add(bangla);
            }

            var dialog = new ContentDialog
            {
                XamlRoot = ((FrameworkElement)Content).XamlRoot,
                Title = $"View Ayah {evidence.VerseKey}",
                Content = new ScrollViewer
                {
                    Content = panel,
                    MaxHeight = 640,
                    HorizontalScrollMode = ScrollMode.Disabled,
                    VerticalScrollMode = ScrollMode.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                PrimaryButtonText = "Open in Research",
                CloseButtonText = "Close preview",
                DefaultButton = ContentDialogButton.Close
            };
            var action = await dialog.ShowAsync();
            if (action == ContentDialogResult.Primary && !_ownerStateOperationActive)
                NavigateToResearchTarget(surah, ayah);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Read-only Ayah preview unavailable: " + ex.Message;
        }
        finally
        {
            _searchPreviewOpen = false;
        }
    }

    private static TextBlock SearchPreviewLabel(string value, double size)
    {
        return new TextBlock
        {
            Text = value,
            FontSize = size,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, 4, 0, 4),
            TextAlignment = TextAlignment.Center
        };
    }

    private static void AddSearchPreviewArabic(StackPanel target,
        string label, string text, string fontFile, string fontName)
    {
        target.Children.Add(SearchPreviewLabel(label, 13));
        var verse = SearchPreviewLabel(text, 32);
        verse.FlowDirection = FlowDirection.RightToLeft;
        string path = Path.Combine(AppContext.BaseDirectory, "Fonts", fontFile);
        if (File.Exists(path))
            verse.FontFamily = new FontFamily($"Fonts/{fontFile}#{fontName}");
        target.Children.Add(verse);
    }
}
