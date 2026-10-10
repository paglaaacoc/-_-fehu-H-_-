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
            // Keep the two historically accepted defaults. Selection changes
            // only these display labels/texts, via the SAME SELECT-only reader.
            // No source selection is persisted, and matching counts are separate.
            const int defaultEnglishId = 20;
            const int defaultBengaliId = 161;
            var englishSources = _resources
                .Where(x => x.Kind == "translation" &&
                    x.LanguageName.Equals("english", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Name).ToArray();
            var bengaliSources = _resources
                .Where(x => x.Kind == "translation" && x.IsBengali)
                .OrderBy(x => x.Name).ToArray();

            string SourceName(IReadOnlyList<ResourceSummary> options, int id) =>
                options.FirstOrDefault(x => x.Id == id)?.Name ?? $"Source {id}";

            var englishHeading = SearchPreviewLabel(
                $"English translation · {SourceName(englishSources, defaultEnglishId)}", 14);
            var englishText = SearchPreviewLabel(
                evidence.English ?? "Translation unavailable for this Ayah.", 17);
            panel.Children.Add(englishHeading);
            panel.Children.Add(englishText);

            var bengaliHeading = SearchPreviewLabel(
                $"বাংলা translation · {SourceName(bengaliSources, defaultBengaliId)}", 14);
            var bengaliText = SearchPreviewLabel(
                evidence.Bengali ?? "এই আয়াতের অনুবাদ পাওয়া যায়নি।", 18);
            bengaliText.FontFamily = new FontFamily("Nirmala UI");
            panel.Children.Add(bengaliHeading);
            panel.Children.Add(bengaliText);

            // Zero extra controls in the main Search workspace; both selectors
            // stay collapsed inside this individual read-only preview.
            var sourceChoices = new StackPanel { Spacing = 8 };
            var englishSelector = new ComboBox
            {
                Header = "English translation source",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            foreach (var option in englishSources)
                englishSelector.Items.Add(new ComboBoxItem
                {
                    Content = option.Name,
                    Tag = option.Id
                });
            englishSelector.SelectedIndex = Array.FindIndex(
                englishSources, x => x.Id == defaultEnglishId);

            var bengaliSelector = new ComboBox
            {
                Header = "বাংলা translation source",
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            foreach (var option in bengaliSources)
                bengaliSelector.Items.Add(new ComboBoxItem
                {
                    Content = option.Name,
                    Tag = option.Id
                });
            bengaliSelector.SelectedIndex = Array.FindIndex(
                bengaliSources, x => x.Id == defaultBengaliId);

            if (englishSources.Length > 0) sourceChoices.Children.Add(englishSelector);
            if (bengaliSources.Length > 0) sourceChoices.Children.Add(bengaliSelector);
            if (sourceChoices.Children.Count > 0)
                panel.Children.Add(new Expander
                {
                    Header = "Change translation sources (optional)",
                    IsExpanded = false,
                    Content = sourceChoices
                });

            bool previewActive = true;
            int selectionGeneration = 0;
            async Task RefreshTranslationsAsync()
            {
                int generation = ++selectionGeneration;
                int en = (englishSelector.SelectedItem as ComboBoxItem)?.Tag is int eid
                    ? eid : defaultEnglishId;
                int bn = (bengaliSelector.SelectedItem as ComboBoxItem)?.Tag is int bid
                    ? bid : defaultBengaliId;
                try
                {
                    var changed = await Task.Run(() =>
                        _searchVersePreview.ReadRange(surah, ayah, ayah, en, bn));
                    if (!previewActive || generation != selectionGeneration ||
                        _ownerStateOperationActive || changed.Count != 1)
                        return;
                    englishHeading.Text =
                        $"English translation · {SourceName(englishSources, en)}";
                    englishText.Text =
                        changed[0].English ?? "Translation unavailable for this Ayah.";
                    bengaliHeading.Text =
                        $"বাংলা translation · {SourceName(bengaliSources, bn)}";
                    bengaliText.Text =
                        changed[0].Bengali ?? "এই আয়াতের অনুবাদ পাওয়া যায়নি।";
                }
                catch (Exception ex)
                {
                    if (previewActive && generation == selectionGeneration)
                        StatusText.Text = "Translation preview unavailable: " + ex.Message;
                }
            }

            englishSelector.SelectionChanged += async (_, _) =>
                await RefreshTranslationsAsync();
            bengaliSelector.SelectionChanged += async (_, _) =>
                await RefreshTranslationsAsync();

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
            previewActive = false;
            ++selectionGeneration;
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
