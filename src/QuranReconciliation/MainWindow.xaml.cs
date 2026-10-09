using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;
using System.Globalization;
using Windows.Graphics;
using Windows.UI;

namespace QuranReconciliation;

public sealed partial class MainWindow : Window
{
    private const float DefaultZoom = 1.00f;
    private const float MinZoom = 0.80f;
    private const float MaxZoom = 1.60f;
    private const float ZoomStep = 0.10f;

    private static readonly int[] DefaultTranslationIds =
    [
        85, 19, 20,       // Abdel Haleem, Pickthall, Saheeh International
        161, 163, 213     // Taisirul, Mujibur Rahman, Abu Bakr Zakaria
    ];

    private float _zoom = DefaultZoom;
    private bool _ready;
    private bool _loadingSurah;

    private readonly AppSettings _settings;
    private readonly CorpusRepository _corpus;
    private readonly IReadOnlyList<ChapterSummary> _chapters;
    private readonly IReadOnlyList<ResourceSummary> _resources;
    private readonly HashSet<int> _selectedTranslationIds;
    private readonly HashSet<int> _selectedTafsirIds;

    private const int ChapterCacheLimit = 6;
    private readonly Dictionary<string, IReadOnlyList<VerseBundle>> _chapterCache = [];
    private readonly LinkedList<string> _chapterCacheOrder = [];

    private int _currentSurah = 1;

    // Research zoom is layout-native: text metrics scale and WinUI reflows
    // them inside the unchanged visible center viewport.
    private double Z(double value) => value * _zoom;

    private static readonly IReadOnlyDictionary<string, ThemeSpec> Themes =
        new Dictionary<string, ThemeSpec>(StringComparer.Ordinal)
        {
            ["Daylight"] = new(
                "#FFF6FAFF", "#FFFFFFFF", "#FFEEF5FB", "#FFFFFFFF",
                "#FFCAD5E1", "#FF15202B", "#FF607080", "#FF1677C8",
                false, "#FFF3F8FC", "#FF16212B", "#FFDDEAF4", "#FFC9DCEB"),

            ["Paper Sage"] = new(
                "#FFF5F8F2", "#FFFBFDF9", "#FFEDF3E9", "#FFFFFFFF",
                "#FFC9D4C5", "#FF1E2B20", "#FF657064", "#FF4C8B5A",
                false, "#FFF0F5ED", "#FF1D2A20", "#FFDDE9D8", "#FFC8DDC1"),

            ["Warm Sand"] = new(
                "#FFFBF6EC", "#FFFFFCF6", "#FFF3EBDD", "#FFFFFEFA",
                "#FFD9CCB9", "#FF2D251B", "#FF756A5A", "#FFB56A2A",
                false, "#FFF7F0E4", "#FF2B241B", "#FFE9DDCC", "#FFDCC9B0"),

            ["Midnight Blue"] = new(
                "#FF101827", "#FF162235", "#FF121D2E", "#FF19283D",
                "#FF31435A", "#FFEAF2FA", "#FFA9B9C9", "#FF58A6E7",
                true, "#FF101827", "#FFF1F7FC", "#FF213653", "#FF2E4B70"),

            ["Twilight Plum"] = new(
                "#FF1A1421", "#FF241B2C", "#FF201827", "#FF2A2033",
                "#FF4B3A58", "#FFF4ECF7", "#FFC5B5CA", "#FFC78AF0",
                true, "#FF1A1421", "#FFF7F0FA", "#FF362844", "#FF4A345D"),

            ["Forest Night"] = new(
                "#FF101A17", "#FF16241F", "#FF13201B", "#FF1A2A24",
                "#FF304C40", "#FFEAF5F0", "#FFA7BCB2", "#FF62C08A",
                true, "#FF101A17", "#FFF0F8F4", "#FF20382F", "#FF2A4B3E"),

            ["OLED Pure"] = new(
                "#FF000000", "#FF050505", "#FF000000", "#FF080808",
                "#FF2A2A2A", "#FFF5F5F5", "#FFA8A8A8", "#FFFFFFFF",
                true, "#FF000000", "#FFFFFFFF", "#FF202020", "#FF333333"),

            ["OLED Cyan"] = new(
                "#FF000000", "#FF031011", "#FF000000", "#FF061719",
                "#FF174447", "#FFE8FFFF", "#FF91BFC2", "#FF47E5E8",
                true, "#FF000000", "#FFEFFFFF", "#FF0C292B", "#FF124043"),

            ["OLED Amber"] = new(
                "#FF000000", "#FF110B02", "#FF000000", "#FF1A1003",
                "#FF4C3310", "#FFFFF7E8", "#FFC5AF87", "#FFFFB347",
                true, "#FF000000", "#FFFFF8E9", "#FF2B1B05", "#FF49300B"),
        };

    public MainWindow()
    {
        InitializeComponent();
        InitializeWorkspaceShortcuts();
        AttachResearchDirtyTracking();
        InitializeScrollOwnershipRouting();
        RootGrid.Background = Brush("AppBackgroundBrush");
        Title = $"{BuildIdentity.WorkstationName} — {BuildIdentity.Version}";

        TryApplyApplicationIcon();

        ResearchDatabase.Initialize();
        _settings = SettingsStore.Load();
        ConfirmExitMenuItem.IsChecked = _settings.ConfirmBeforeExit;

        _corpus = new CorpusRepository();
        _chapters = _corpus.GetChapters();
        _resources = _corpus.GetResources();

        InitializeJuzNavigation();

        _selectedTranslationIds = _settings.SelectedTranslationIds
            .Where(id => _resources.Any(r => r.Kind == "translation" && r.Id == id))
            .ToHashSet();

        _selectedTafsirIds = _settings.SelectedTafsirIds
            .Where(id => _resources.Any(r => r.Kind == "tafsir" && r.Id == id))
            .ToHashSet();

        if (!_settings.SourceSelectionInitialized)
        {
            foreach (int id in DefaultTranslationIds)
            {
                if (_resources.Any(r => r.Kind == "translation" && r.Id == id))
                {
                    _selectedTranslationIds.Add(id);
                }
            }

            _settings.SourceSelectionInitialized = true;
        }

        _zoom = Math.Clamp(_settings.Zoom, MinZoom, MaxZoom);
        ThemeSelector.SelectedIndex = FindThemeIndex(_settings.Theme);
        UthmaniToggle.IsChecked = _settings.ShowUthmani;
        IndoPakToggle.IsChecked = _settings.ShowIndoPak;
        NastaleeqToggle.IsChecked = _settings.ShowIndoPakNastaleeq;

        ApplyLayoutSettings();

        BuildSurahList();
        BuildSourceList();
        ApplyWindowPlacement();

        _currentSurah = Math.Clamp(_settings.LastSurahNumber, 1, 114);
        SurahList.SelectedIndex = _currentSurah - 1;

        Closed += MainWindow_Closed;
        AppWindow.Closing += AppWindow_Closing;
        _ready = true;

        ApplyZoom(rebuildContent: false);
        ApplySelectedTheme();
        // Preserve the long-standing last-opened-Surah behavior, but always
        // start that Surah at ayah 1. Precise return points belong to Bookmarks.
        LoadCurrentSurah(resetScroll: true);
        LoadContextMapForCurrentSurah();
        RefreshResearchHistory();
        UpdateWorkspaceNavigationState();
        SaveCurrentSettings();
    }

    private void TryApplyApplicationIcon()
    {
        try
        {
            string icon =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "QuranReconciliation.ico");

            if (File.Exists(icon))
            {
                AppWindow.SetIcon(icon);
            }
        }
        catch
        {
            // Shell-icon failure must never interfere with research startup.
        }
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        ApplyZoom(rebuildContent: false);
        ApplySelectedTheme();
        AttachPersistentTextBoxContextMenus();
    }

    private void BuildSurahList()
    {
        SurahList.Items.Clear();

        foreach (ChapterSummary chapter in _chapters)
        {
            SurahList.Items.Add(new ListViewItem
            {
                Content = new TextBlock
                {
                    Text = chapter.DisplayName,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("TextBrush"),
                    FontSize = 14
                },
                Tag = chapter,
                Padding = new Thickness(8, 7, 8, 7),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            });
        }
    }

    private void BuildSourceList()
    {
        SourceListPanel.Children.Clear();

        AddSourceGroup(
            "English translations",
            _resources.Where(r => r.Kind == "translation" && !r.IsBengali));

        AddSourceGroup(
            "বাংলা · Bengali translations",
            _resources.Where(r => r.Kind == "translation" && r.IsBengali));

        AddSourceGroup(
            "English tafsir / commentary",
            _resources.Where(r => r.Kind == "tafsir" && !r.IsBengali));

        AddSourceGroup(
            "বাংলা তাফসীর · Bengali tafsir",
            _resources.Where(r => r.Kind == "tafsir" && r.IsBengali));
    }

    private void AddSourceGroup(
        string title,
        IEnumerable<ResourceSummary> resources)
    {
        List<ResourceSummary> group = resources.ToList();
        if (group.Count == 0)
        {
            return;
        }

        SourceListPanel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brush("TextBrush"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 14, 4, 8)
        });

        foreach (ResourceSummary resource in group)
        {
            bool selected = resource.Kind == "translation"
                ? _selectedTranslationIds.Contains(resource.Id)
                : _selectedTafsirIds.Contains(resource.Id);

            var label = new TextBlock
            {
                Text = resource.DisplayLabel,
                Foreground = Brush("TextBrush"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14
            };

            var checkBox = new CheckBox
            {
                Content = label,
                Tag = resource,
                IsChecked = selected,
                Margin = new Thickness(3, 2, 2, 5),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            if (!string.IsNullOrWhiteSpace(resource.AuthorName))
            {
                ToolTipService.SetToolTip(
                    checkBox,
                    $"{resource.Name}\n{resource.AuthorName}\n{resource.DisplayType}");
            }
            else
            {
                ToolTipService.SetToolTip(
                    checkBox,
                    $"{resource.Name}\n{resource.DisplayType}");
            }

            checkBox.Checked += SourceCheckBox_Changed;
            checkBox.Unchecked += SourceCheckBox_Changed;
            SourceListPanel.Children.Add(checkBox);
        }
    }

    private void SourceCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready ||
            sender is not CheckBox checkBox ||
            checkBox.Tag is not ResourceSummary resource)
        {
            return;
        }

        HashSet<int> selected = resource.Kind == "translation"
            ? _selectedTranslationIds
            : _selectedTafsirIds;

        if (checkBox.IsChecked == true)
        {
            selected.Add(resource.Id);
        }
        else
        {
            selected.Remove(resource.Id);
        }

        SaveCurrentSettings();
        LoadCurrentSurah(resetScroll: false);

        if (WorkingSliceWorkspaceGrid.Visibility ==
            Visibility.Visible)
        {
            RefreshWorkingSliceEvidence(
                preserveOffset: true);
        }

        if (ContextAtlasWorkspaceGrid.Visibility == Visibility.Visible &&
            AtlasCompareReadingGrid.Visibility == Visibility.Visible)
        {
            // Re-render only the existing lazy A/B cards on script selection;
            // no corpus query or research data mutation.
            RefreshAtlasPairedReadingScripts();
        }
    }

    private void SurahList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready ||
            SurahList.SelectedItem is not ListViewItem item ||
            item.Tag is not ChapterSummary chapter)
        {
            return;
        }

        _currentSurah = chapter.Number;
        SaveCurrentSettings();
        LoadCurrentSurah(resetScroll: true);
        LoadContextMapForCurrentSurah();
    }

    private void ArabicVisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        SaveCurrentSettings();
        LoadCurrentSurah(resetScroll: false);

        if (WorkingSliceWorkspaceGrid.Visibility ==
            Visibility.Visible)
        {
            RefreshWorkingSliceEvidence(
                preserveOffset: true);
        }
    }

    private void LoadCurrentSurah(bool resetScroll)
    {
        if (_loadingSurah)
        {
            return;
        }

        _loadingSurah = true;

        double previousOffset =
            resetScroll
                ? 0
                : WorkspaceScrollViewer.VerticalOffset;

        int previousStartAyah =
            resetScroll
                ? 1
                : _renderedVerseStartIndex + 1;

        try
        {
            ChapterSummary chapter = _chapters[_currentSurah - 1];

            SurahTitleText.Text = chapter.DisplayName;
            string juzSummary =
                GetSurahJuzSummary(
                    chapter.Number,
                    chapter.VersesCount);

            SurahMetaText.Text =
                $"{chapter.VersesCount} ayat · {juzSummary} · {_selectedTranslationIds.Count} translation sources · {_selectedTafsirIds.Count} tafsir sources";

            IReadOnlyList<VerseBundle> verses =
                GetChapterCached(chapter.Number);

            ResetVerseRendering(
                verses,
                previousStartAyah);

            UpdateJuzUi(
                chapter.Number,
                Math.Min(
                    previousStartAyah,
                    verses.Count));

            if (resetScroll)
            {
                WorkspaceScrollViewer.ChangeView(
                    0,
                    0,
                    null,
                    disableAnimation: true);
            }
            else
            {
                ScheduleWorkspaceOffsetRestore(previousOffset);
            }

            StatusText.Text =
                $"{chapter.DisplayName} · {verses.Count} ayat · verified local corpus · offline";

            EnsureAyahNoteTargetForSurah(chapter.Number);
        }
        catch (Exception ex)
        {
            VersePanel.Children.Clear();
            VersePanel.Children.Add(CreateErrorCard(ex.Message));
            StatusText.Text = "Could not load the selected Surah.";
        }
        finally
        {
            _loadingSurah = false;
        }
    }

    private IReadOnlyList<VerseBundle> GetChapterCached(
        int chapterNumber)
    {
        string key =
            chapterNumber + "|tr:" +
            string.Join(",", _selectedTranslationIds.OrderBy(x => x)) +
            "|tf:" +
            string.Join(",", _selectedTafsirIds.OrderBy(x => x));

        if (_chapterCache.TryGetValue(key, out var cached))
        {
            _chapterCacheOrder.Remove(key);
            _chapterCacheOrder.AddLast(key);
            return cached;
        }

        IReadOnlyList<VerseBundle> loaded = _corpus.GetChapter(
            chapterNumber,
            _selectedTranslationIds,
            _selectedTafsirIds);

        _chapterCache[key] = loaded;
        _chapterCacheOrder.AddLast(key);

        while (_chapterCacheOrder.Count > ChapterCacheLimit)
        {
            string oldest = _chapterCacheOrder.First!.Value;
            _chapterCacheOrder.RemoveFirst();
            _chapterCache.Remove(oldest);
        }

        return loaded;
    }

    private Border CreateVerseCard(VerseBundle verse)
    {
        var content = new StackPanel();

        if (_juz.IsJuzStart(
                _currentSurah,
                verse.VerseNumber,
                out int juzNumber))
        {
            content.Children.Add(
                CreateResearchJuzBoundaryMarker(
                    juzNumber));
        }

        var ayahHeader = new Grid
        {
            Margin = new Thickness(0, 0, 0, 14)
        };
        ayahHeader.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        ayahHeader.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });

        ayahHeader.Children.Add(RegisterZoomText(
            new TextBlock
            {
                Text = $"Ayah / আয়াত · {verse.VerseKey}",
                Foreground = Brush("AccentBrush"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            },
            17));

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var noteButton = new Button
        {
            Content = "Note",
            Tag = verse.VerseNumber,
            Padding = new Thickness(12, 5, 12, 5)
        };
        noteButton.Click += SelectAyahForNote_Click;

        var bookmarkButton = new Button
        {
            Content = "Bookmark",
            Tag = verse.VerseNumber,
            Padding = new Thickness(12, 5, 12, 5)
        };
        bookmarkButton.Click += BookmarkAyah_Click;

        actionPanel.Children.Add(noteButton);
        actionPanel.Children.Add(bookmarkButton);

        Grid.SetColumn(actionPanel, 1);
        ayahHeader.Children.Add(actionPanel);

        content.Children.Add(ayahHeader);

        UIElement arabicArea = CreateArabicArea(verse);
        content.Children.Add(arabicArea);

        if (_selectedTranslationIds.Count > 0)
        {
            content.Children.Add(CreateSectionHeading(
                "Translations / অনুবাদ",
                new Thickness(0, 18, 0, 10)));

            content.Children.Add(CreateBilingualSourceArea(
                verse,
                verse.Translations));
        }

        if (_selectedTafsirIds.Count > 0)
        {
            content.Children.Add(CreateSectionHeading(
                "Tafsir / তাফসীর",
                new Thickness(0, 20, 0, 10)));

            content.Children.Add(CreateBilingualSourceArea(
                verse,
                verse.Tafsirs));
        }

        var card =
            new Border
            {
                Background = Brush("CardBrush"),
                BorderBrush = Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 16),
                Child = content,
                Tag = verse.VerseNumber
            };

        card.ContextFlyout =
            BuildResearchAyahContextMenu(
                verse,
                card);

        return card;
    }

    private UIElement CreateArabicArea(VerseBundle verse)
    {
        var scripts = new List<(string Label, string Text, string FontRole)>();

        if (UthmaniToggle.IsChecked == true)
        {
            scripts.Add(("Uthmani", verse.Uthmani, "Uthmani"));
        }

        if (IndoPakToggle.IsChecked == true)
        {
            scripts.Add(("IndoPak", verse.IndoPak, "IndoPak"));
        }

        if (NastaleeqToggle.IsChecked == true)
        {
            scripts.Add(("IndoPak Nastaleeq", verse.IndoPakNastaleeq, "IndoPak"));
        }

        if (scripts.Count == 0)
        {
            return RegisterZoomText(
                new TextBlock
                {
                    Text = "Arabic script display is hidden.",
                    Foreground = Brush("MutedTextBrush"),
                    Margin = new Thickness(0, 2, 0, 2)
                },
                14);
        }

        var grid = new Grid
        {
            ColumnSpacing = 14
        };

        for (int i = 0; i < scripts.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });

            FrameworkElement block = CreateArabicBlock(
                scripts[i].Label,
                scripts[i].Text,
                scripts[i].FontRole,
                verse);

            Grid.SetColumn(block, i);
            grid.Children.Add(block);
        }

        return grid;
    }

    private FrameworkElement CreateArabicBlock(
        string label,
        string text,
        string fontRole,
        VerseBundle verse)
    {
        bool useIndoPakFont =
            string.Equals(
                fontRole,
                "IndoPak",
                StringComparison.Ordinal);

        bool useUthmaniFont =
            string.Equals(
                fontRole,
                "Uthmani",
                StringComparison.Ordinal);

        var textBlock = CreateZoomSelectableText(
            text,
            baseFontSize: useIndoPakFont ? 38 : 39,
            baseLineHeight: useIndoPakFont ? 84 : 70);

        textBlock.FlowDirection = FlowDirection.RightToLeft;
        textBlock.TextAlignment = TextAlignment.Center;
        textBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
        textBlock.VerticalAlignment = VerticalAlignment.Center;
        textBlock.Margin = new Thickness(0, 10, 0, 10);

        if (useIndoPakFont)
        {
            string bundledFont =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fonts",
                    "indopak-nastaleeq-waqf-lazim-v4.2.1.ttf");

            if (File.Exists(bundledFont))
            {
                // Unpackaged WinUI resolves relative font-file references
                // from the portable app folder.
                textBlock.FontFamily = new FontFamily(
                    "Fonts/indopak-nastaleeq-waqf-lazim-v4.2.1.ttf#AlQuran IndoPak by QuranWBW");
            }
        }
        else if (useUthmaniFont)
        {
            string bundledFont =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Fonts",
                    "UthmanicHafs1Ver18.ttf");

            if (File.Exists(bundledFont))
            {
                textBlock.FontFamily = new FontFamily(
                    "Fonts/UthmanicHafs1Ver18.ttf#KFGQPC HAFS Uthmanic Script");
            }
        }

        textBlock.ContextFlyout =
            BuildResearchAyahContextMenu(
                verse,
                textBlock,
                textBlock,
                textBlock);

        var cardGrid = RegisterZoomLayout(
            new Grid(),
            baseMinHeight: useIndoPakFont ? 126 : 116);

        var labelBlock = RegisterZoomText(
            new TextBlock
            {
                Text = label,
                Foreground = Brush("MutedTextBrush"),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            },
            14);

        textBlock.VerticalAlignment = VerticalAlignment.Center;
        textBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
        RegisterZoomLayout(
            textBlock,
            baseMargin: new Thickness(0, 20, 0, 6));

        cardGrid.Children.Add(textBlock);
        cardGrid.Children.Add(labelBlock);

        return new Border
        {
            Background = Brush("PanelAltBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            Child = cardGrid
        };
    }

    private UIElement CreateBilingualSourceArea(
        VerseBundle verse,
        IReadOnlyList<SourceText> sourceTexts)
    {
        var grid = new Grid
        {
            ColumnSpacing = 14
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star)
        });

        StackPanel english = CreateSourceColumn(
            verse,
            "English",
            sourceTexts.Where(x => !x.IsBengali));

        StackPanel bengali = CreateSourceColumn(
            verse,
            "বাংলা · Bengali",
            sourceTexts.Where(x => x.IsBengali));

        Grid.SetColumn(english, 0);
        Grid.SetColumn(bengali, 1);

        grid.Children.Add(english);
        grid.Children.Add(bengali);

        return grid;
    }

    private StackPanel CreateSourceColumn(
        VerseBundle verse,
        string heading,
        IEnumerable<SourceText> sourceTexts)
    {
        var stack = new StackPanel();

        stack.Children.Add(RegisterZoomText(
            new TextBlock
            {
                Text = heading,
                Foreground = Brush("MutedTextBrush"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 7)
            },
            14));

        List<SourceText> items = sourceTexts.ToList();

        if (items.Count == 0)
        {
            stack.Children.Add(RegisterZoomText(
                new TextBlock
                {
                    Text = "No selected source for this language.",
                    Foreground = Brush("MutedTextBrush"),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap
                },
                14));

            return stack;
        }

        foreach (SourceText item in items)
        {
            string label = item.DisplayType == "Translation"
                ? item.ResourceName
                : $"{item.ResourceName} · {item.DisplayType}";

            if (!string.IsNullOrWhiteSpace(item.ScopeLabel))
            {
                label += $" · scope {item.ScopeLabel}";
            }

            stack.Children.Add(RegisterZoomText(
                new TextBlock
                {
                    Text = label,
                    Foreground = Brush("AccentBrush"),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 6, 0, 5),
                    TextWrapping = TextWrapping.Wrap
                },
                14));

            TextBlock text = CreateZoomSelectableText(
                item.Text,
                baseFontSize: item.IsBengali ? 18 : 17,
                baseLineHeight: item.IsBengali ? 30 : 28);

            if (item.IsBengali)
            {
                text.FontFamily = new FontFamily("Nirmala UI");
            }

            text.ContextFlyout =
                BuildResearchAyahContextMenu(
                    verse,
                    text,
                    text);

            stack.Children.Add(text);

            if (item.Footnotes.Count > 0)
            {
                var footnoteStack = new StackPanel
                {
                    Spacing = 7,
                    Margin = new Thickness(2, 6, 2, 4)
                };

                foreach (SourceFootnote footnote in item.Footnotes)
                {
                    TextBlock footnoteText = CreateZoomSelectableText(
                        $"[{footnote.Marker}] {footnote.Text}",
                        baseFontSize: item.IsBengali ? 15 : 14,
                        baseLineHeight: item.IsBengali ? 25 : 23);

                    if (item.IsBengali)
                    {
                        footnoteText.FontFamily =
                            new FontFamily("Nirmala UI");
                    }

                    footnoteText.Foreground =
                        Brush("MutedTextBrush");

                    footnoteText.ContextFlyout =
                        BuildResearchAyahContextMenu(
                            verse,
                            footnoteText,
                            footnoteText);

                    footnoteStack.Children.Add(
                        footnoteText);
                }

                stack.Children.Add(
                    new Expander
                    {
                        Header = $"Footnotes / পাদটীকা · {item.Footnotes.Count}",
                        Content = footnoteStack,
                        Margin = new Thickness(0, 7, 0, 2),
                        IsExpanded = false
                    });
            }
        }

        return stack;
    }

    private TextBlock CreateSectionHeading(
        string text,
        Thickness margin)
    {
        return RegisterZoomText(
            new TextBlock
            {
                Text = text,
                Foreground = Brush("TextBrush"),
                FontWeight = FontWeights.SemiBold,
                Margin = margin
            },
            17);
    }

    private TextBlock CreateSelectableText(
        string text,
        double fontSize,
        double lineHeight)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = Brush("TextBrush"),
            SelectionHighlightColor = Brush("AccentBrush"),
            FontSize = fontSize,
            LineHeight = lineHeight,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            SelectionFlyout = null
        };

        block.ContextFlyout =
            BuildCopyTextContextMenu(block);

        return block;
    }

    private Border CreateErrorCard(string message)
    {
        return new Border
        {
            Background = Brush("CardBrush"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Child = RegisterZoomText(
                new TextBlock
                {
                    Text = message,
                    Foreground = Brush("TextBrush"),
                    TextWrapping = TextWrapping.Wrap
                },
                16)
        };
    }

    private void ThemeSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        ApplySelectedTheme();
        SaveCurrentSettings();
    }

    private void ApplySelectedTheme()
    {
        if (ThemeSelector.SelectedItem is not ComboBoxItem item ||
            item.Tag?.ToString() is not string key ||
            !Themes.TryGetValue(key, out ThemeSpec? theme))
        {
            return;
        }

        SetBrush("AppBackgroundBrush", theme.Background);
        SetBrush("PanelBrush", theme.Panel);
        SetBrush("PanelAltBrush", theme.PanelAlt);
        SetBrush("CardBrush", theme.Card);
        SetBrush("BorderBrush", theme.Border);
        SetBrush("TextBrush", theme.Text);
        SetBrush("MutedTextBrush", theme.Muted);
        SetBrush("AccentBrush", theme.Accent);

        RootGrid.RequestedTheme =
            theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;

        ApplyTitleBarTheme(theme);
    }

    private void ApplyTitleBarTheme(ThemeSpec theme)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        AppWindowTitleBar titleBar = AppWindow.TitleBar;
        titleBar.BackgroundColor = ParseColor(theme.TitleBarBackground);
        titleBar.ForegroundColor = ParseColor(theme.TitleBarForeground);
        titleBar.InactiveBackgroundColor = ParseColor(theme.TitleBarBackground);
        titleBar.InactiveForegroundColor = ParseColor(theme.Muted);
        titleBar.ButtonBackgroundColor = ParseColor(theme.TitleBarBackground);
        titleBar.ButtonForegroundColor = ParseColor(theme.TitleBarForeground);
        titleBar.ButtonInactiveBackgroundColor = ParseColor(theme.TitleBarBackground);
        titleBar.ButtonInactiveForegroundColor = ParseColor(theme.Muted);
        titleBar.ButtonHoverBackgroundColor = ParseColor(theme.TitleBarHover);
        titleBar.ButtonHoverForegroundColor = ParseColor(theme.TitleBarForeground);
        titleBar.ButtonPressedBackgroundColor = ParseColor(theme.TitleBarPressed);
        titleBar.ButtonPressedForegroundColor = ParseColor(theme.TitleBarForeground);
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        SetZoom(_zoom - ZoomStep);
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        SetZoom(DefaultZoom);
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        SetZoom(_zoom + ZoomStep);
    }

    private void SetZoom(float value)
    {
        _zoom = Math.Clamp(value, MinZoom, MaxZoom);
        ApplyZoom();
        SaveCurrentSettings();
    }

    private void ApplyZoom(bool rebuildContent = true)
    {
        if (WorkspaceScrollViewer is null || ZoomResetButton is null)
        {
            return;
        }

        double previousOffset = WorkspaceScrollViewer.VerticalOffset;

        UpdateWorkspaceContentWidth();

        SurahTitleText.FontSize = Z(26);
        SurahMetaText.FontSize = Z(15);
        ZoomResetButton.Content = $"{Math.Round(_zoom * 100)}%";

        if (rebuildContent && _ready && !_loadingSurah)
        {
            // Preserve the owner-approved R4 layout-native reflow model.
            // Rescale already-rendered native controls in place: do not requery
            // the corpus and do not destroy/recreate verse/source control trees.
            ApplyRenderedVerseZoomMetrics();
            ScheduleWorkspaceOffsetRestore(previousOffset);

            if (WorkingSliceWorkspaceGrid.Visibility ==
                Visibility.Visible)
            {
                double workingSliceOffset =
                    WorkingSliceEvidenceScrollViewer
                        .VerticalOffset;

                // Match the accepted Research strategy: update the existing
                // native text tree in place instead of rebuilding every
                // ayah/source merely because zoom changed.
                ApplyWorkingSliceZoomMetrics();
                ScheduleWorkingSliceOffsetRestore(
                    workingSliceOffset);
            }

            if (ContextAtlasWorkspaceGrid.Visibility ==
                Visibility.Visible &&
                AtlasReadingPreviewGrid.Visibility ==
                Visibility.Visible)
            {
                // Reflow already-rendered preview text only.
                // No SQL, no re-render and no new controls on zoom.
                ApplyAtlasReadingZoomMetrics();
            }

            if (ContextAtlasWorkspaceGrid.Visibility == Visibility.Visible &&
                AtlasCompareReadingGrid.Visibility == Visibility.Visible)
            {
                // Keep both read-only Arabic columns anchored on zoom,
                // reflowing current cards without SQL or recreating controls.
                ApplyAtlasCompareReadingZoomMetrics();
            }
        }
    }

    private void SelectableText_RightTapped(
        object sender,
        RightTappedRoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not TextBlock textBlock ||
            string.IsNullOrWhiteSpace(textBlock.SelectedText))
        {
            StatusText.Text =
                "Right-click is safely suppressed unless text is selected.";
            return;
        }

        textBlock.CopySelectionToClipboard();
        StatusText.Text = "Selected text copied.";
    }

    private int FindThemeIndex(string themeName)
    {
        for (int i = 0; i < ThemeSelector.Items.Count; i++)
        {
            if (ThemeSelector.Items[i] is ComboBoxItem item &&
                string.Equals(
                    item.Tag?.ToString(),
                    themeName,
                    StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }

    private void ApplyWindowPlacement()
    {
        if (_settings.WindowWidth is not int requestedWidth ||
            _settings.WindowHeight is not int requestedHeight)
        {
            return;
        }

        try
        {
            DisplayArea display = DisplayArea.GetFromWindowId(
                AppWindow.Id,
                DisplayAreaFallback.Primary);

            RectInt32 work = display.WorkArea;

            int width = Math.Clamp(requestedWidth, 1000, work.Width);
            int height = Math.Clamp(requestedHeight, 700, work.Height);

            AppWindow.Resize(new SizeInt32(width, height));

            if (_settings.WindowX is int requestedX &&
                _settings.WindowY is int requestedY)
            {
                int maxX = Math.Max(
                    work.X,
                    work.X + work.Width - width);

                int maxY = Math.Max(
                    work.Y,
                    work.Y + work.Height - height);

                int x = Math.Clamp(requestedX, work.X, maxX);
                int y = Math.Clamp(requestedY, work.Y, maxY);

                AppWindow.Move(new PointInt32(x, y));
            }
        }
        catch
        {
            // Portable settings must never prevent startup on another display.
        }
    }

    private void AppWindow_Closing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        if (_ownerStateOperationActive)
        {
            args.Cancel = true;
            StatusText.Text =
                "Owner-state safety operation in progress. Close is temporarily disabled until verification or recovery handoff completes.";
            return;
        }

        // A normal window close (X, Alt+F4, taskbar Close) is canceled first
        // while the asynchronous native dialog asks the owner's permission.
        // Destructive owner-state handoffs have their existing guarded path.
        if (!_suppressSettingsSaveOnClose && _ready &&
            _settings.ConfirmBeforeExit && !_exitAlreadyApproved)
        {
            args.Cancel = true;
            if (!_exitConfirmationOpen)
                _ = ConfirmNormalExitAsync();
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_suppressSettingsSaveOnClose ||
            _ownerStateOperationActive)
        {
            return;
        }

        SaveCurrentSettings();
    }

    private void SaveCurrentSettings(
        bool throwOnFailure = false)
    {
        if (!_ready)
        {
            return;
        }

        try
        {
            _settings.Theme =
                (ThemeSelector.SelectedItem as ComboBoxItem)?
                    .Tag?
                    .ToString() ?? "Daylight";

            _settings.Zoom = _zoom;
            _settings.ConfirmBeforeExit = ConfirmExitMenuItem.IsChecked;
            _settings.ShowUthmani = UthmaniToggle.IsChecked == true;
            _settings.ShowIndoPak = IndoPakToggle.IsChecked == true;
            _settings.ShowIndoPakNastaleeq =
                NastaleeqToggle.IsChecked == true;

            _settings.LastSurahNumber = _currentSurah;
            _settings.SourceSelectionInitialized = true;
            _settings.SelectedTranslationIds =
                _selectedTranslationIds.OrderBy(x => x).ToList();
            _settings.SelectedTafsirIds =
                _selectedTafsirIds.OrderBy(x => x).ToList();

            _settings.ShowLeftSidebar = LeftSidebarMenuItem.IsChecked;
            _settings.ShowRightSidebar = RightSidebarMenuItem.IsChecked;
            _settings.LeftSidebarWidth = _leftSidebarWidth;
            _settings.RightSidebarWidth = _rightSidebarWidth;
            _settings.TopBarMode = _topBarMode;

            _settings.WindowX = AppWindow.Position.X;
            _settings.WindowY = AppWindow.Position.Y;
            _settings.WindowWidth = AppWindow.Size.Width;
            _settings.WindowHeight = AppWindow.Size.Height;

            SettingsStore.Save(_settings);
        }
        catch
        {
            if (throwOnFailure)
            {
                throw;
            }

            // Preference persistence should not interrupt research use.
        }
    }

    private SolidColorBrush Brush(string key)
    {
        return (SolidColorBrush)RootGrid.Resources[key];
    }

    private void SetBrush(string key, string hex)
    {
        Brush(key).Color = ParseColor(hex);
    }

    private static Color ParseColor(string hex)
    {
        string value = hex.TrimStart('#');

        if (value.Length == 6)
        {
            value = "FF" + value;
        }

        if (value.Length != 8)
        {
            throw new FormatException($"Invalid ARGB color: {hex}");
        }

        byte a = byte.Parse(
            value.AsSpan(0, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);

        byte r = byte.Parse(
            value.AsSpan(2, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);

        byte g = byte.Parse(
            value.AsSpan(4, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);

        byte b = byte.Parse(
            value.AsSpan(6, 2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);

        return Color.FromArgb(a, r, g, b);
    }

    private sealed record ThemeSpec(
        string Background,
        string Panel,
        string PanelAlt,
        string Card,
        string Border,
        string Text,
        string Muted,
        string Accent,
        bool IsDark,
        string TitleBarBackground,
        string TitleBarForeground,
        string TitleBarHover,
        string TitleBarPressed);
}
