using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using QuranReconciliation.Models;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private MenuFlyout BuildResearchAyahContextMenu(
        VerseBundle verse,
        FrameworkElement anchor,
        TextBlock? sourceText = null,
        TextBlock? wordSelectionSource = null)
    {
        var menu =
            new MenuFlyout();

        MenuFlyoutItem? copySelection =
            null;

        if (sourceText is not null)
        {
            copySelection =
                new MenuFlyoutItem
                {
                    Text = "Copy selection"
                };

            copySelection.Click +=
                (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(
                            sourceText.SelectedText))
                    {
                        return;
                    }

                    CopyTextToClipboard(
                        sourceText.SelectedText);
                };

            menu.Items.Add(
                copySelection);

            menu.Items.Add(
                new MenuFlyoutSeparator());
        }

        var copyReference =
            new MenuFlyoutItem
            {
                Text = "Copy ayah reference"
            };

        copyReference.Click +=
            (_, _) =>
                CopyTextToClipboard(
                    $"Qur'an {verse.VerseKey}");

        menu.Items.Add(
            copyReference);

        var note =
            new MenuFlyoutItem
            {
                Text = "Ayah Note"
            };

        note.Click +=
            (_, _) =>
            {
                SelectAyahNoteTarget(
                    _currentSurah,
                    verse.VerseNumber);

                AyahNoteExpander.IsExpanded =
                    true;

                StatusText.Text =
                    $"Ayah note opened for {verse.VerseKey}.";
            };

        menu.Items.Add(
            note);

        var bookmark =
            new MenuFlyoutItem
            {
                Text = "Bookmark ayah"
            };

        bookmark.Click +=
            (_, _) =>
            {
                BookmarkEntry? existing =
                    _bookmarks.Get(
                        _currentSurah,
                        verse.VerseNumber);

                _bookmarks.Save(
                    _currentSurah,
                    verse.VerseNumber,
                    existing?.Title ??
                        $"{_chapters[_currentSurah - 1].NameSimple} {verse.VerseKey}",
                    existing?.Note ??
                        string.Empty);

                RefreshResearchHistory(
                    force: true);

                StatusText.Text =
                    $"Bookmark saved for {verse.VerseKey}.";
            };

        menu.Items.Add(
            bookmark);

        menu.Items.Add(
            new MenuFlyoutSeparator());

        MenuFlyoutSubItem wordByWordSubmenu =
            BuildWordByWordSubmenu(
                verse.VerseKey,
                anchor,
                wordSelectionSource);

        menu.Items.Add(
            wordByWordSubmenu);

        menu.Opening +=
            (_, _) =>
            {
                if (copySelection is not null)
                {
                    copySelection.IsEnabled =
                        sourceText is not null &&
                        !string.IsNullOrWhiteSpace(
                            sourceText.SelectedText);
                }

                RefreshSelectedWordOnlyAvailability(
                    wordByWordSubmenu,
                    wordSelectionSource);
            };

        return menu;
    }

    private MenuFlyoutSubItem BuildWordByWordSubmenu(
        string verseKey,
        FrameworkElement anchor,
        TextBlock? selectionSource = null)
    {
        var word =
            new MenuFlyoutSubItem
            {
                Text = "Word-by-word",
                IsEnabled =
                    _wordByWord.IsAvailable
            };

        if (selectionSource is not null)
        {
            var selectedOnly =
                new MenuFlyoutItem
                {
                    Text = "Open selected Arabic word only…",
                    IsEnabled =
                        !string.IsNullOrWhiteSpace(
                            selectionSource.SelectedText)
                };

            selectedOnly.Click +=
                (_, _) =>
                    ShowSelectedWordByWord(
                        verseKey,
                        selectionSource,
                        anchor);

            word.Items.Add(
                selectedOnly);

            word.Items.Add(
                new MenuFlyoutSeparator());
        }

        var open =
            new MenuFlyoutItem
            {
                Text = "Open full ayah beside this text…"
            };

        open.Click +=
            (_, _) =>
                QueueVerseWordByWordFlyout(
                    verseKey,
                    anchor);

        var copy =
            new MenuFlyoutItem
            {
                Text = "Copy word-by-word glosses"
            };

        copy.Click +=
            (_, _) =>
                CopyVerseWordByWord(
                    verseKey);

        word.Items.Add(open);
        word.Items.Add(copy);

        return word;
    }

    private static void RefreshSelectedWordOnlyAvailability(
        MenuFlyoutSubItem wordByWordSubmenu,
        TextBlock? selectionSource)
    {
        if (selectionSource is null ||
            wordByWordSubmenu.Items.Count == 0 ||
            wordByWordSubmenu.Items[0]
                is not MenuFlyoutItem selectedOnly ||
            !string.Equals(
                selectedOnly.Text,
                "Open selected Arabic word only…",
                StringComparison.Ordinal))
        {
            return;
        }

        selectedOnly.IsEnabled =
            !string.IsNullOrWhiteSpace(
                selectionSource.SelectedText);
    }

    private void ShowSelectedWordByWord(
        string verseKey,
        TextBlock source,
        FrameworkElement anchor)
    {
        string selected =
            source.SelectedText?.Trim() ??
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                selected))
        {
            StatusText.Text =
                "Select one Arabic word first, then use Word-by-word → Open selected Arabic word only.";
            return;
        }

        string normalized =
            NormalizeArabicToken(
                selected);

        if (string.IsNullOrWhiteSpace(
                normalized))
        {
            StatusText.Text =
                "The current selection is not a usable Arabic word selection.";
            return;
        }

        IReadOnlyList<WordByWordEntry> words =
            _wordByWord.GetVerse(
                verseKey);

        if (words.Count == 0)
        {
            StatusText.Text =
                $"Word-by-word data unavailable for {verseKey}.";
            return;
        }

        List<string> sourceWords =
            source.Text
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(
                    NormalizeArabicToken)
                .Where(
                    token =>
                        !string.IsNullOrWhiteSpace(
                            token))
                .ToList();

        List<int> sourceMatches =
            sourceWords
                .Select(
                    (token, index) =>
                        (token, index))
                .Where(
                    item =>
                        string.Equals(
                            item.token,
                            normalized,
                            StringComparison.Ordinal))
                .Select(
                    item =>
                        item.index)
                .ToList();

        if (sourceMatches.Count > 1)
        {
            StatusText.Text =
                "That Arabic token occurs more than once in this displayed ayah, so text selection alone cannot identify its exact position safely. Open the full ayah word-by-word view instead.";
            return;
        }

        if (sourceMatches.Count == 1 &&
            sourceWords.Count == words.Count)
        {
            int expectedPosition =
                sourceMatches[0] + 1;

            List<WordByWordEntry> positional =
                words
                    .Where(
                        candidate =>
                            candidate.Position ==
                                expectedPosition)
                    .ToList();

            if (positional.Count == 1)
            {
                ShowWordMeaningFlyout(
                    positional[0],
                    anchor);
                return;
            }
        }

        List<WordByWordEntry> directMatches =
            words
                .Where(
                    candidate =>
                        string.Equals(
                            NormalizeArabicToken(
                                candidate.Arabic),
                            normalized,
                            StringComparison.Ordinal))
                .ToList();

        if (directMatches.Count == 1)
        {
            ShowWordMeaningFlyout(
                directMatches[0],
                anchor);
            return;
        }

        StatusText.Text =
            directMatches.Count == 0
                ? "That selection cannot be mapped safely to one Quran Foundation word position; open the full ayah word-by-word view instead."
                : "That Arabic token maps to more than one Quran Foundation word in this ayah, so the workstation will not guess the position.";
    }

    private static string NormalizeArabicToken(
        string value)
    {
        string decomposed =
            value.Normalize(
                NormalizationForm.FormKD);

        var letters =
            new StringBuilder(
                decomposed.Length);

        foreach (char c in decomposed)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            letters.Append(
                NormalizeArabicLetter(
                    c));
        }

        return letters
            .ToString()
            .Normalize(
                NormalizationForm.FormC);
    }

    private static char NormalizeArabicLetter(
        char value) =>
        value switch
        {
            'آ' or 'أ' or 'إ' or 'ٱ' =>
                'ا',
            'ؤ' =>
                'و',
            'ئ' or 'ى' or 'ی' or 'ے' =>
                'ي',
            'ک' =>
                'ك',
            'ہ' or 'ھ' =>
                'ه',
            _ =>
                value
        };

    private void QueueVerseWordByWordFlyout(
        string verseKey,
        FrameworkElement anchor)
    {
        bool queued =
            RootGrid.DispatcherQueue.TryEnqueue(
                () =>
                {
                    if (anchor.XamlRoot is null)
                    {
                        StatusText.Text =
                            "The word-by-word view could not open because its source text is no longer visible.";
                        return;
                    }

                    ShowVerseWordByWordFlyout(
                        verseKey,
                        anchor);
                });

        if (!queued)
        {
            StatusText.Text =
                "The word-by-word view could not be queued for display.";
        }
    }

    private void CopyVerseWordByWord(
        string verseKey)
    {
        IReadOnlyList<WordByWordEntry> words =
            _wordByWord.GetVerse(
                verseKey);

        if (words.Count == 0)
        {
            StatusText.Text =
                $"Word-by-word data unavailable for {verseKey}.";
            return;
        }

        string text =
            string.Join(
                Environment.NewLine,
                words.Select(
                    word =>
                        $"{word.Arabic} · {word.Transliteration} · {word.EnglishMeaning}"));

        CopyTextToClipboard(
            text);
    }

    private void ShowVerseWordByWordFlyout(
        string verseKey,
        FrameworkElement anchor)
    {
        IReadOnlyList<WordByWordEntry> words =
            _wordByWord.GetVerse(
                verseKey);

        if (words.Count == 0)
        {
            StatusText.Text =
                $"Word-by-word data unavailable for {verseKey}.";
            return;
        }

        double flyoutWidth =
            Math.Clamp(
                AppWindow.Size.Width * 0.42,
                720,
                900);

        var rows =
            new StackPanel
            {
                Spacing = 5,
                FlowDirection =
                    FlowDirection.LeftToRight
            };

        foreach (WordByWordEntry word
                 in words)
        {
            var grid =
                new Grid
                {
                    ColumnSpacing = 14,
                    Padding =
                        new Thickness(
                            8,
                            5,
                            8,
                            5),
                    FlowDirection =
                        FlowDirection.LeftToRight
                };

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(205)
                });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(225)
                });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width =
                        new GridLength(
                            1,
                            GridUnitType.Star)
                });

            var arabic =
                new Button
                {
                    Content =
                        word.Arabic,
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,
                    FontSize = 23,
                    FlowDirection =
                        FlowDirection.RightToLeft,
                    Padding =
                        new Thickness(
                            10,
                            5,
                            10,
                            5)
                };

            arabic.Click +=
                (_, _) =>
                    ShowWordMeaningFlyout(
                        word,
                        arabic);

            arabic.ContextFlyout =
                BuildWordFlyoutContextMenu(
                    word,
                    arabic);

            var transliteration =
                new TextBlock
                {
                    Text =
                        word.Transliteration,
                    Foreground =
                        Brush("MutedTextBrush"),
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    TextWrapping =
                        TextWrapping.Wrap
                };

            Grid.SetColumn(
                transliteration,
                1);

            var meaning =
                new TextBlock
                {
                    Text =
                        word.EnglishMeaning,
                    Foreground =
                        Brush("TextBrush"),
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    TextWrapping =
                        TextWrapping.Wrap
                };

            Grid.SetColumn(
                meaning,
                2);

            grid.Children.Add(
                arabic);
            grid.Children.Add(
                transliteration);
            grid.Children.Add(
                meaning);

            rows.Children.Add(
                grid);
        }

        var header =
            new StackPanel
            {
                Spacing = 4,
                FlowDirection =
                    FlowDirection.LeftToRight
            };

        header.Children.Add(
            new TextBlock
            {
                Text =
                    $"Word-by-word · {verseKey}",
                Foreground =
                    Brush("TextBrush"),
                FontSize = 18,
                FontWeight =
                    FontWeights.SemiBold
            });

        header.Children.Add(
            new TextBlock
            {
                Text =
                    "Quran Foundation word-level glosses and transliteration. Click or right-click an Arabic word for its focused meaning.",
                Foreground =
                    Brush("MutedTextBrush"),
                FontSize = 12,
                TextWrapping =
                    TextWrapping.Wrap
            });

        var content =
            new StackPanel
            {
                Spacing = 10,
                Width = flyoutWidth,
                FlowDirection =
                    FlowDirection.LeftToRight
            };

        content.Children.Add(
            header);

        content.Children.Add(
            new ScrollViewer
            {
                MaxHeight = 620,
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                VerticalScrollMode =
                    ScrollMode.Enabled,
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                HorizontalScrollMode =
                    ScrollMode.Disabled,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Hidden,
                Content =
                    rows
            });

        var presenterStyle =
            new Style(
                typeof(FlyoutPresenter));

        presenterStyle.Setters.Add(
            new Setter
            {
                Property =
                    FrameworkElement.MaxWidthProperty,
                Value =
                    flyoutWidth + 48
            });

        var flyout =
            new Flyout
            {
                Placement =
                    FlyoutPlacementMode.Left,
                FlyoutPresenterStyle =
                    presenterStyle,
                Content =
                    content
            };

        try
        {
            flyout.ShowAt(
                anchor);
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Word-by-word view could not open: {ex.Message}";
        }
    }

    private MenuFlyout BuildWordFlyoutContextMenu(
        WordByWordEntry word,
        FrameworkElement anchor)
    {
        var menu =
            new MenuFlyout();

        var meaning =
            new MenuFlyoutItem
            {
                Text = "Word meaning…"
            };

        meaning.Click +=
            (_, _) =>
                ShowWordMeaningFlyout(
                    word,
                    anchor);

        menu.Items.Add(
            meaning);

        var copyArabic =
            new MenuFlyoutItem
            {
                Text = "Copy Arabic word"
            };

        copyArabic.Click +=
            (_, _) =>
                CopyTextToClipboard(
                    word.Arabic);

        menu.Items.Add(
            copyArabic);

        var copyTransliteration =
            new MenuFlyoutItem
            {
                Text = "Copy transliteration"
            };

        copyTransliteration.Click +=
            (_, _) =>
                CopyTextToClipboard(
                    word.Transliteration);

        menu.Items.Add(
            copyTransliteration);

        var copyMeaning =
            new MenuFlyoutItem
            {
                Text = "Copy English meaning"
            };

        copyMeaning.Click +=
            (_, _) =>
                CopyTextToClipboard(
                    word.EnglishMeaning);

        menu.Items.Add(
            copyMeaning);

        return menu;
    }

    private void ShowWordMeaningFlyout(
        WordByWordEntry word,
        FrameworkElement anchor)
    {
        var panel =
            new StackPanel
            {
                Spacing = 8,
                Width = 380
            };

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    word.Arabic,
                Foreground =
                    Brush("TextBrush"),
                FontSize = 34,
                TextAlignment =
                    TextAlignment.Center,
                FlowDirection =
                    FlowDirection.RightToLeft
            });

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    $"Transliteration: {word.Transliteration}",
                Foreground =
                    Brush("MutedTextBrush"),
                TextWrapping =
                    TextWrapping.Wrap
            });

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    $"English word-by-word gloss: {word.EnglishMeaning}",
                Foreground =
                    Brush("TextBrush"),
                FontWeight =
                    FontWeights.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            });

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Research aid only; this is not claimed to be exact character-level alignment to a selected full-ayah translation.",
                Foreground =
                    Brush("MutedTextBrush"),
                FontSize = 12,
                TextWrapping =
                    TextWrapping.Wrap
            });

        var flyout =
            new Flyout
            {
                Placement =
                    FlyoutPlacementMode.Left,
                Content =
                    panel
            };

        flyout.ShowAt(
            anchor);
    }
}
