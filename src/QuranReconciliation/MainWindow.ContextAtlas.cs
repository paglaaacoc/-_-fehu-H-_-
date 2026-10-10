using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;
using Windows.Storage.Pickers;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private ProposalCorpusRepository? _atlasCorpusRepository;
    private ProposalCorpusImportService? _atlasImportService;
    private ProposalHandoffExportService? _atlasExportService;
    private JuzRepository? _atlasJuz;
    private readonly ContextAtlasResearchSnapshotRepository _atlasResearchSnapshot =
        new();

    private IReadOnlyList<ProposalCorpusPackage> _atlasCorpora =
        Array.Empty<ProposalCorpusPackage>();

    private ProposalCorpusPackage? _atlasCurrentCorpus;
    private ContextAtlasRangeTarget? _atlasSelectedRange;
    private string _atlasMode = "Browse";
    private int _atlasSurah = 1;
    private bool _atlasUiLoading;
    // Lightweight review index; visible native cards are appended by scroll demand.
    private readonly List<(string Header, string Body, ContextAtlasRangeTarget Target)> _atlasReviewEntries = [];
    private int _atlasReviewRendered;
    private bool _atlasReviewAppending;

    private async void OpenContextAtlas_Click(
        object sender,
        RoutedEventArgs e)
    {
        WorkspaceGrid.Visibility =
            Visibility.Collapsed;

        WorkingSliceWorkspaceGrid.Visibility =
            Visibility.Collapsed;

        HistoryWorkspaceGrid.Visibility =
            Visibility.Collapsed;

        CorpusSearchWorkspaceGrid.Visibility =
            Visibility.Collapsed;

        ContextAtlasWorkspaceGrid.Visibility =
            Visibility.Visible;

        try
        {
            // Keep the native Atlas tree alive across same-launch workspace
            // switches: corpus choices, tab, Surah, disagreement, scroll
            // positions, and any open paired/full reader stay as the user left them.
            // Import/Remove explicitly invoke ReloadAtlasCorpora instead.
            if (_atlasCorpora.Count == 0 ||
                AtlasCorpusSelector.Items.Count == 0)
            {
                EnsureContextAtlasLoaded();
                RefreshContextAtlas();
            }
            else
            {
                // Owner research can change while Atlas is hidden. Refresh
                // only its read-only sidebar, not the current research view.
                RefreshAtlasLiveResearchPane();
            }

            UpdateWorkspaceNavigationState();

            int rejectedCount =
                _atlasCorpusRepository?.RejectedImportedPackages.Count ?? 0;
            StatusText.Text =
                rejectedCount > 0
                    ? $"Context Atlas loaded; {rejectedCount} invalid imported ZIP(s) skipped and preserved in ContextAtlas/Imported."
                    : "Context Atlas · read-only Proposal Corpus workspace.";
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Context Atlas failed closed: {ex.Message}";

            await ShowAtlasMessageAsync(
                "Context Atlas unavailable",
                ex.Message);
        }
    }

    private void CloseContextAtlas_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowResearchWorkspace();

        StatusText.Text =
            $"{_chapters[_currentSurah - 1].DisplayName} · research workspace";
    }

    private void EnsureContextAtlasLoaded()
    {
        if (_atlasCorpusRepository is null)
        {
            _atlasCorpusRepository =
                new ProposalCorpusRepository(
                    _chapters);

            _atlasImportService =
                new ProposalCorpusImportService(
                    _chapters);

            _atlasExportService =
                new ProposalHandoffExportService(
                    _chapters);

            _atlasJuz =
                new JuzRepository(
                    _chapters);
        }

        ReloadAtlasCorpora(
            preserveCurrentId: true);

        if (AtlasSurahList.Items.Count == 0)
        {
            _atlasUiLoading = true;

            try
            {
                foreach (ChapterSummary chapter
                         in _chapters)
                {
                    AtlasSurahList.Items.Add(
                        new ListViewItem
                        {
                            Content = new TextBlock
                            {
                                Text = chapter.DisplayName,
                                TextWrapping = TextWrapping.Wrap,
                                TextTrimming = TextTrimming.None,
                                MaxWidth = Math.Max(110, AtlasSurahList.ActualWidth - 38),
                                Foreground = Brush("TextBrush")
                            },
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            Tag = chapter.Number
                        });
                }

                AtlasSurahList.SelectedIndex =
                    _atlasSurah - 1;
            }
            finally
            {
                _atlasUiLoading = false;
            }
        }
    }

    private void ReloadAtlasCorpora(
        bool preserveCurrentId)
    {
        if (_atlasCorpusRepository is null)
        {
            return;
        }

        string? currentId =
            preserveCurrentId
                ? _atlasCurrentCorpus
                    ?.Corpus.CorpusId
                : null;

        // Preserve A/B identities, not ComboBox positions: imported corpora
        // can reorder or disappear. Guarded loading suppresses change events.
        string? leftId =
            AtlasCompareASelector.SelectedItem is ComboBoxItem selectedA &&
            selectedA.Tag is ProposalCorpusPackage left
                ? left.Corpus.CorpusId
                : null;
        string? rightId =
            AtlasCompareBSelector.SelectedItem is ComboBoxItem selectedB &&
            selectedB.Tag is ProposalCorpusPackage right
                ? right.Corpus.CorpusId
                : null;

        _atlasCorpora =
            _atlasCorpusRepository.LoadAll();

        if (_atlasCorpora.Count == 0)
        {
            throw new InvalidDataException(
                "Context Atlas has no valid Proposal Corpora.");
        }

        _atlasCurrentCorpus =
            currentId is not null
                ? _atlasCorpora.FirstOrDefault(
                    x =>
                        string.Equals(
                            x.Corpus.CorpusId,
                            currentId,
                            StringComparison.Ordinal))
                : null;

        _atlasCurrentCorpus ??=
            _atlasCorpora[0];

        var restored = AtlasSessionSelection.Restore(
            _atlasCorpora.Select(x => x.Corpus.CorpusId).ToList(),
            _atlasCurrentCorpus.Corpus.CorpusId, leftId, rightId);

        // Only an actually removed A/B corpus invalidates the comparison.
        // Routine reentry and unrelated imports retain disagreement position.
        if (restored.RemovedSelectedCorpus)
            _atlasCompareDifferenceIndex = 0;

        _atlasUiLoading = true;

        try
        {
            AtlasCorpusSelector.Items.Clear();
            AtlasCompareASelector.Items.Clear();
            AtlasCompareBSelector.Items.Clear();

            for (int index = 0;
                 index < _atlasCorpora.Count;
                 index++)
            {
                ProposalCorpusPackage package =
                    _atlasCorpora[index];

                string label =
                    package.DisplayLabel;

                AtlasCorpusSelector.Items.Add(
                    new ComboBoxItem
                    {
                        Content = label,
                        Tag = package
                    });

                AtlasCompareASelector.Items.Add(
                    new ComboBoxItem
                    {
                        Content = label,
                        Tag = package
                    });

                AtlasCompareBSelector.Items.Add(
                    new ComboBoxItem
                    {
                        Content = label,
                        Tag = package
                    });

                if (string.Equals(
                        package.Corpus.CorpusId,
                        _atlasCurrentCorpus
                            .Corpus.CorpusId,
                        StringComparison.Ordinal))
                {
                    AtlasCorpusSelector.SelectedIndex =
                        index;
                }
            }

            AtlasCompareASelector.SelectedIndex = restored.Left;
            AtlasCompareBSelector.SelectedIndex = restored.Right;
        }
        finally
        {
            _atlasUiLoading = false;
        }

        RefreshAtlasCorpusMetadata();
        RefreshAtlasToolbarState();
    }

    private void AtlasCorpusSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_atlasUiLoading ||
            AtlasCorpusSelector.SelectedItem
                is not ComboBoxItem item ||
            item.Tag
                is not ProposalCorpusPackage package)
        {
            return;
        }

        _atlasCurrentCorpus =
            package;

        _atlasSelectedRange =
            null;

        RefreshAtlasCorpusMetadata();
        RefreshAtlasToolbarState();
        RefreshContextAtlas();
    }

    private void AtlasSurahList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double textWidth = Math.Max(90, AtlasSurahList.ActualWidth - 40);
        foreach (ListViewItem item in AtlasSurahList.Items.OfType<ListViewItem>())
            if (item.Content is TextBlock text)
                text.MaxWidth = textWidth;
    }

    private void AtlasContentScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // StackPanel inside ScrollViewer otherwise measures unconstrained width.
        double width = Math.Max(100, AtlasContentScrollViewer.ActualWidth - 18);
        if (double.IsNaN(AtlasContentPanel.Width) ||
            Math.Abs(AtlasContentPanel.Width - width) > 0.5)
            AtlasContentPanel.Width = width;
        RefreshAtlasPinnedPairLayout();
    }

    private void AtlasSurahList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_atlasUiLoading ||
            AtlasSurahList.SelectedItem
                is not ListViewItem item ||
            item.Tag is not int surah)
        {
            return;
        }

        _atlasSurah =
            surah;

        _atlasSelectedRange =
            null;

        if (_atlasMode is "Browse" or "Compare")
        {
            _atlasCompareDifferenceIndex = 0;
            RefreshContextAtlas();
        }
    }

    private void AtlasSearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_atlasMode ==
            "Browse")
        {
            RefreshContextAtlas();
        }
    }

    private void AtlasTabButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag?.ToString()
                is not string mode)
        {
            return;
        }

        _atlasMode =
            mode;

        RefreshContextAtlas();
    }

    private void AtlasCompareSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_atlasUiLoading ||
            _atlasMode !=
                "Compare")
        {
            return;
        }

        _atlasCompareDifferenceIndex = 0;
        RefreshContextAtlas();
    }

    private void RefreshContextAtlas()
    {
        if (_atlasCurrentCorpus is null)
        {
            return;
        }

        // Switching Atlas modes/corpora leaves no transient reading state behind.
        CloseAtlasReadingPreview();
        CloseAtlasCompareReader();
        ApplyAtlasTabState();

        AtlasCompareControls.Visibility =
            _atlasMode ==
                "Compare"
                ? Visibility.Visible
                : Visibility.Collapsed;

        AtlasContentPanel.Children.Clear();
        _atlasReviewEntries.Clear();
        _atlasReviewRendered = 0;
        if (_atlasMode != "Pins")
        {
            _atlasResponsivePinsGrid = null;
            _atlasResponsivePinsA = null;
            _atlasResponsivePinsB = null;
        }

        switch (_atlasMode)
        {
            case "Browse":
                RenderAtlasBrowse();
                break;

            case "Related Worksets":
                RenderAtlasWorksets();
                break;

            case "Review":
                RenderAtlasReview();
                break;

            case "Pins":
                RenderAtlasPins();
                break;

            case "Compare":
                RenderAtlasCompare();
                break;

            case "Provenance":
                RenderAtlasProvenance();
                break;
        }

        if (_atlasSelectedRange is null &&
            _atlasMode ==
                "Browse")
        {
            ProposalSurah surah =
                CurrentAtlasSurah();

            ProposalContextBlock? first =
                surah.ContextBlocks
                    .FirstOrDefault();

            if (first is not null)
            {
                _atlasSelectedRange =
                    new ContextAtlasRangeTarget(
                        surah.SurahNumber,
                        first.StartAyah,
                        first.EndAyah,
                        $"{surah.SurahName} {first.DisplayRange}");
            }
        }

        RefreshAtlasLiveResearchPane();
    }

    private void RenderAtlasBrowse()
    {
        ProposalCorpusPackage package =
            _atlasCurrentCorpus!;

        string query =
            AtlasSearchTextBox.Text
                .Trim();

        if (!string.IsNullOrWhiteSpace(
                query))
        {
            RenderAtlasSearch(
                package,
                query);

            return;
        }

        ProposalSurah surah =
            CurrentAtlasSurah();

        AddAtlasHeading(
            $"{surah.SurahNumber}. {surah.SurahName}",
            26);

        AddAtlasMuted(
            $"{surah.VersesCount} Ayat · {surah.ContextBlocks.Count} fine Context Blocks · {surah.MacroGroups.Count} Macro Groups");

        foreach (ProposalMacroGroup macro
                 in surah.MacroGroups)
        {
            AddAtlasHeading(
                $"{macro.VerseSpan} · {macro.Label}",
                19,
                topMargin: 18);

            AddAtlasMuted(
                $"{macro.ContextBlockCount} Context Block{(macro.ContextBlockCount == 1 ? string.Empty : "s")} · {macro.MacroGroupId}");

            foreach (string blockId
                     in macro.ContextBlockIds)
            {
                ProposalContextBlock block =
                    surah.ContextBlocks.Single(
                        x =>
                            string.Equals(
                                x.ContextBlockId,
                                blockId,
                                StringComparison.Ordinal));

                Border card =
                    NewAtlasCard();

                var stack =
                    new StackPanel
                    {
                        Spacing = 7
                    };

                var rangeButton =
                    new Button
                    {
                        Content =
                            $"Ayat {block.DisplayRange} · {block.Confidence}",
                        Tag =
                            new ContextAtlasRangeTarget(
                                surah.SurahNumber,
                                block.StartAyah,
                                block.EndAyah,
                                $"{surah.SurahName} {block.DisplayRange}"),
                        HorizontalAlignment =
                            HorizontalAlignment.Left,
                        Padding =
                            new Thickness(
                                10,
                                4,
                                10,
                                4)
                    };

                rangeButton.Click +=
                    AtlasRange_Click;

                var readButton = new Button
                {
                    Content = "Read Ayat",
                    Tag = rangeButton.Tag,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 4, 10, 4)
                };
                readButton.Click += AtlasReadingOpen_Click;
                var rangeActions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8
                };
                rangeActions.Children.Add(rangeButton);
                rangeActions.Children.Add(readButton);
                stack.Children.Add(rangeActions);

                stack.Children.Add(
                    new TextBlock
                    {
                        Text =
                            block.CoherenceNote,
                        Foreground =
                            Brush("TextBrush"),
                        TextWrapping =
                            TextWrapping.Wrap,
                        IsTextSelectionEnabled =
                            true
                    });

                stack.Children.Add(
                    new TextBlock
                    {
                        Text =
                            block.JuzStart ==
                                block.JuzEnd
                                ? $"Juz {block.JuzStart} · {block.ContextBlockId}"
                                : $"Juz {block.JuzStart} → {block.JuzEnd} · {block.ContextBlockId}",
                        Foreground =
                            Brush("MutedTextBrush"),
                        FontSize = 12
                    });

                ProposalBoundary? boundary =
                    surah.Boundaries
                        .FirstOrDefault(
                            x =>
                                x.AfterAyah ==
                                block.EndAyah);

                if (boundary is not null)
                {
                    stack.Children.Add(
                        new TextBlock
                        {
                            Text =
                                $"Boundary after Ayah {boundary.AfterAyah} → {boundary.NextAyah}",
                            Foreground =
                                Brush("AccentBrush"),
                            FontWeight =
                                FontWeights.SemiBold,
                            Margin =
                                new Thickness(
                                    0,
                                    5,
                                    0,
                                    0)
                        });

                    stack.Children.Add(
                        new TextBlock
                        {
                            Text =
                                boundary.Reason,
                            Foreground =
                                Brush("MutedTextBrush"),
                            TextWrapping =
                                TextWrapping.Wrap,
                            IsTextSelectionEnabled =
                                true
                        });
                }

                card.Child =
                    stack;

                AtlasContentPanel.Children.Add(
                    card);
            }
        }
    }

    private void RenderAtlasSearch(
        ProposalCorpusPackage package,
        string query)
    {
        string normalized =
            query.Trim();

        AddAtlasHeading(
            $"Search · {normalized}",
            24);

        int count = 0;

        bool corpusMatch =
            Contains(
                package.Corpus.DisplayName,
                normalized) ||
            Contains(
                package.Corpus.CorpusId,
                normalized) ||
            package.Corpus.Contributors.Any(
                x =>
                    Contains(
                        x.ModelName,
                        normalized) ||
                    Contains(
                        x.Provider,
                        normalized));

        if (corpusMatch)
        {
            AddAtlasSearchText(
                "Corpus",
                package.Corpus.DisplayName +
                " · " +
                package.Corpus.CorpusId);

            count++;
        }

        foreach (ProposalSurah surah
                 in package.Corpus.Surahs)
        {
            bool surahMatch =
                Contains(
                    surah.SurahName,
                    normalized) ||
                Contains(
                    surah.SurahNumber
                        .ToString(),
                    normalized);

            if (surahMatch)
            {
                AddAtlasSearchRange(
                    $"Surah {surah.SurahNumber}",
                    surah.SurahName,
                    new ContextAtlasRangeTarget(
                        surah.SurahNumber,
                        1,
                        surah.VersesCount,
                        surah.SurahName));

                count++;
            }

            foreach (ProposalMacroGroup macro
                     in surah.MacroGroups)
            {
                if (!Contains(
                        macro.Label,
                        normalized))
                {
                    continue;
                }

                AddAtlasSearchRange(
                    $"Macro Group · {surah.SurahNumber}:{macro.VerseSpan}",
                    macro.Label,
                    new ContextAtlasRangeTarget(
                        surah.SurahNumber,
                        macro.StartAyah,
                        macro.EndAyah,
                        macro.Label));

                count++;

                if (count >= 250)
                {
                    break;
                }
            }

            foreach (ProposalContextBlock block
                     in surah.ContextBlocks)
            {
                if (!Contains(
                        block.CoherenceNote,
                        normalized))
                {
                    continue;
                }

                AddAtlasSearchRange(
                    $"Context · {surah.SurahNumber}:{block.DisplayRange}",
                    block.CoherenceNote,
                    new ContextAtlasRangeTarget(
                        surah.SurahNumber,
                        block.StartAyah,
                        block.EndAyah,
                        block.CoherenceNote));

                count++;

                if (count >= 250)
                {
                    break;
                }
            }

            foreach (ProposalBoundary boundary
                     in surah.Boundaries)
            {
                if (!Contains(
                        boundary.Reason,
                        normalized))
                {
                    continue;
                }

                AddAtlasSearchRange(
                    $"Boundary · {surah.SurahNumber}:{boundary.AfterAyah}→{boundary.NextAyah}",
                    boundary.Reason,
                    new ContextAtlasRangeTarget(
                        surah.SurahNumber,
                        boundary.AfterAyah,
                        boundary.NextAyah,
                        boundary.Reason));

                count++;

                if (count >= 250)
                {
                    break;
                }
            }

            if (count >= 250)
            {
                break;
            }
        }

        if (count < 250)
        {
            foreach (ProposalWorkset workset
                     in package.Corpus
                         .CrossSurahWorksets)
            {
                if (!Contains(
                        workset.Label,
                        normalized) &&
                    !Contains(
                        workset.Note,
                        normalized) &&
                    !workset.Subsets.Any(
                        subset =>
                            Contains(
                                subset.Label,
                                normalized)))
                {
                    continue;
                }

                AddAtlasSearchText(
                    $"Related Workset · {workset.Id}",
                    workset.Label +
                    " · " +
                    workset.Note);

                count++;
            }
        }

        if (count == 0)
        {
            AddAtlasMuted(
                "No Proposal Corpus records matched this search.");
        }
        else if (count >= 250)
        {
            AddAtlasMuted(
                "Showing the first 250 matches.");
        }
    }

    private void RenderAtlasWorksets()
    {
        ProposalCorpusDocument corpus =
            _atlasCurrentCorpus!
                .Corpus;

        AddAtlasHeading(
            "Related Worksets",
            26);

        AddAtlasMuted(
            "Cross-Surah comparative overlay. Members remain independent Surah-local Context ranges.");

        foreach (ProposalWorkset workset
                 in corpus.CrossSurahWorksets)
        {
            Border card =
                NewAtlasCard();

            var stack =
                new StackPanel
                {
                    Spacing = 7
                };

            stack.Children.Add(
                new TextBlock
                {
                    Text =
                        $"{workset.Id} · {workset.Label}",
                    Foreground =
                        Brush("TextBrush"),
                    FontSize = 17,
                    FontWeight =
                        FontWeights.SemiBold,
                    TextWrapping =
                        TextWrapping.Wrap
                });

            stack.Children.Add(
                new TextBlock
                {
                    Text =
                        workset.Note,
                    Foreground =
                        Brush("MutedTextBrush"),
                    TextWrapping =
                        TextWrapping.Wrap,
                    IsTextSelectionEnabled =
                        true
                });

            foreach (ProposalWorksetMember member
                     in workset.Members)
            {
                AddAtlasWorksetMemberButton(
                    stack,
                    member);
            }

            foreach (ProposalWorksetSubset subset
                     in workset.Subsets)
            {
                stack.Children.Add(
                    new TextBlock
                    {
                        Text =
                            subset.Label,
                        Foreground =
                            Brush("TextBrush"),
                        FontWeight =
                            FontWeights.SemiBold,
                        Margin =
                            new Thickness(
                                0,
                                5,
                                0,
                                0),
                        TextWrapping =
                            TextWrapping.Wrap
                    });

                foreach (ProposalWorksetMember member
                         in subset.Members)
                {
                    AddAtlasWorksetMemberButton(
                        stack,
                        member);
                }
            }

            card.Child =
                stack;

            AtlasContentPanel.Children.Add(
                card);
        }
    }

    private void AddAtlasWorksetMemberButton(
        StackPanel stack,
        ProposalWorksetMember member)
    {
        var button =
            new Button
            {
                Content =
                    $"{member.SurahNumber}. {member.SurahName} · {member.VerseSpan}",
                Tag =
                    new ContextAtlasRangeTarget(
                        member.SurahNumber,
                        member.StartAyah,
                        member.EndAyah,
                        $"{member.SurahName} {member.VerseSpan}"),
                HorizontalAlignment =
                    HorizontalAlignment.Left,
                Padding =
                    new Thickness(
                        10,
                        4,
                        10,
                        4)
            };

        button.Click +=
            AtlasRange_Click;

        stack.Children.Add(
            button);
    }

    private void RenderAtlasReview()
    {
        ProposalCorpusDocument corpus =
            _atlasCurrentCorpus!
                .Corpus;

        AddAtlasHeading(
            "Review index",
            26);

        AddAtlasMuted(
            "Medium/low confidence, single-Ayah Contexts, Juz crossings/coincident boundaries, English-clarified boundaries, and corpus review flags.");

        int count = 0;

        foreach (ProposalSurah surah
                 in corpus.Surahs)
        {
            foreach (string flag
                     in surah.SurahFlags)
            {
                AddAtlasReviewEntry(
                    $"Surah flag · {surah.SurahNumber}",
                    flag,
                    new ContextAtlasRangeTarget(
                        surah.SurahNumber,
                        1,
                        surah.VersesCount,
                        flag));

                count++;
            }

            foreach (ProposalContextBlock block
                     in surah.ContextBlocks)
            {
                var reasons =
                    new List<string>();

                if (!string.Equals(
                        block.Confidence,
                        "high",
                        StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add(
                        $"{block.Confidence} confidence");
                }

                if (block.StartAyah ==
                    block.EndAyah)
                {
                    reasons.Add(
                        "single-Ayah Context");
                }

                if (block.JuzStart !=
                    block.JuzEnd)
                {
                    reasons.Add(
                        $"crosses Juz {block.JuzStart}→{block.JuzEnd}");
                }

                if (reasons.Count == 0)
                {
                    continue;
                }

                AddAtlasReviewEntry(
                    $"Context · {surah.SurahNumber}:{block.DisplayRange}",
                    string.Join(
                        " · ",
                        reasons) +
                    "\n" +
                    block.CoherenceNote,
                    new ContextAtlasRangeTarget(
                        surah.SurahNumber,
                        block.StartAyah,
                        block.EndAyah,
                        block.CoherenceNote));

                count++;
            }

            foreach (ProposalBoundary boundary
                     in surah.Boundaries)
            {
                var reasons =
                    new List<string>();

                if (!string.Equals(
                        boundary.Confidence,
                        "high",
                        StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add(
                        $"{boundary.Confidence} confidence");
                }

                if (boundary.EnglishUsed)
                {
                    reasons.Add(
                        "English clarification used");
                }

                if (_atlasJuz is not null &&
                    _atlasJuz.IsJuzStart(
                        surah.SurahNumber,
                        boundary.NextAyah,
                        out int juz))
                {
                    reasons.Add(
                        $"coincides with Juz {juz} start");
                }

                if (reasons.Count == 0)
                {
                    continue;
                }

                AddAtlasReviewEntry(
                    $"Boundary · {surah.SurahNumber}:{boundary.AfterAyah}→{boundary.NextAyah}",
                    string.Join(
                        " · ",
                        reasons) +
                    "\n" +
                    boundary.Reason,
                    new ContextAtlasRangeTarget(
                        surah.SurahNumber,
                        boundary.AfterAyah,
                        boundary.NextAyah,
                        boundary.Reason));

                count++;
            }
        }

        if (count == 0)
        {
            AddAtlasMuted(
                "This corpus exposes no records requiring the review index.");
        }
        else
        {
            AppendAtlasReviewCards(18);
        }
    }

    private void AddAtlasReviewEntry(string header, string body, ContextAtlasRangeTarget target) =>
        _atlasReviewEntries.Add((header, body, target));

    private void AppendAtlasReviewCards(int batch)
    {
        if (_atlasReviewAppending || _atlasMode != "Review")
            return;
        _atlasReviewAppending = true;
        try
        {
            int end = Math.Min(_atlasReviewEntries.Count, _atlasReviewRendered + batch);
            for (int i = _atlasReviewRendered; i < end; i++)
            {
                var entry = _atlasReviewEntries[i];
                AddAtlasSearchRange(entry.Header, entry.Body, entry.Target);
            }
            _atlasReviewRendered = end;
        }
        finally
        {
            _atlasReviewAppending = false;
        }
    }

    private void AtlasContentScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_atlasMode != "Review" || _atlasReviewAppending ||
            _atlasReviewRendered >= _atlasReviewEntries.Count ||
            AtlasReadingPreviewGrid.Visibility == Visibility.Visible)
            return;
        double remaining = AtlasContentScrollViewer.ScrollableHeight -
                           AtlasContentScrollViewer.VerticalOffset;
        if (remaining < Math.Max(450, AtlasContentScrollViewer.ViewportHeight))
            AppendAtlasReviewCards(15);
    }

    // Build 1.5: Ayah-aligned, on-demand A/B Context inspection is isolated
    // in MainWindow.ContextAtlasCompare.cs. Existing read-only Atlas architecture remains.
    private void RenderAtlasCompare() =>
        RenderAtlasVisualCompare();

    private void RenderAtlasProvenance()
    {
        ProposalCorpusPackage package =
            _atlasCurrentCorpus!;

        ProposalCorpusDocument corpus =
            package.Corpus;

        AddAtlasHeading(
            "Proposal Corpus provenance",
            26);

        AddAtlasSearchText(
            "Identity",
            $"{corpus.DisplayName}\n{corpus.CorpusId}\nEdition {corpus.Edition} · {corpus.PublishedDate} · {corpus.Status}");

        AddAtlasSearchText(
            "Package / payload hashes",
            $"Package SHA-256: {package.PackageSha256}\nPayload SHA-256: {package.PayloadSha256}");

        AddAtlasSearchText(
            "Contributors",
            CorpusContributorSummary(
                package));

        AddAtlasSearchText(
            "Lineage",
            corpus.Lineage.Kind +
            (string.IsNullOrWhiteSpace(
                 corpus.Lineage.ParentCorpusId)
                ? string.Empty
                : $"\nParent: {corpus.Lineage.ParentCorpusId}\nParent payload: {corpus.Lineage.ParentPayloadSha256}"));

        AddAtlasSearchText(
            "Evidence policy",
            PrettyJson(
                corpus.EvidencePolicy));

        AddAtlasSearchText(
            "Source identity",
            PrettyJson(
                corpus.SourceIdentity));

        AddAtlasSearchText(
            "Statistics",
            $"114 Surahs · {corpus.Statistics.AyatCovered:N0} Ayat · {corpus.Statistics.TotalContextBlocks:N0} Context Blocks · {corpus.Statistics.TotalInternalBoundaries:N0} boundaries · {corpus.Statistics.MacroGroups:N0} Macro Groups · {corpus.Statistics.CrossSurahWorksets} Related Worksets");
    }

    private void AtlasRange_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag
                is not ContextAtlasRangeTarget target)
        {
            return;
        }

        _atlasSelectedRange =
            target;

        _atlasSurah =
            target.SurahNumber;

        _atlasUiLoading = true;

        try
        {
            AtlasSurahList.SelectedIndex =
                target.SurahNumber - 1;
        }
        finally
        {
            _atlasUiLoading = false;
        }

        RefreshAtlasLiveResearchPane();
    }

    private void RefreshAtlasLiveResearchPane()
    {
        AtlasLiveResearchPanel.Children.Clear();

        if (_atlasSelectedRange is null)
        {
            AtlasSelectedPassageText.Text =
                "Select a proposed passage to inspect live owner research state.";

            return;
        }

        ContextAtlasRangeTarget target =
            _atlasSelectedRange;

        AtlasSelectedPassageText.Text =
            $"{target.SurahNumber}:{target.StartAyah}" +
            (target.StartAyah ==
                target.EndAyah
                ? string.Empty
                : $"–{target.EndAyah}") +
            $" · {target.Label}";

        try
        {
            ContextAtlasResearchSnapshot snapshot =
                _atlasResearchSnapshot.ReadRange(
                    target.SurahNumber,
                    target.StartAyah,
                    target.EndAyah);

            AddLiveLine(
                snapshot.HasExactContextMatch
                    ? "Proposal vs owner: exact live Context match"
                    : snapshot.Contexts.Count == 0
                        ? "Proposal vs owner: no overlapping live Context"
                        : "Proposal vs owner: differs from live Context structure",
                emphasized: true);

            if (snapshot.Contexts.Count > 0)
            {
                AddLiveLine(
                    "Live Contexts");

                foreach (ContextAtlasLiveContext context
                         in snapshot.Contexts)
                {
                    AddLiveLine(
                        $"{context.StartAyah}–{context.EndAyah} · {context.Status}");
                }
            }

            AddLiveLine(
                $"Working Slices: {snapshot.WorkingSlices.Count}");

            foreach (ContextAtlasWorkingSliceSummary slice
                     in snapshot.WorkingSlices)
            {
                AddLiveLine(
                    $"{slice.Title} · {slice.Status} · {slice.StartAyah}–{slice.EndAyah} · {slice.RevisionCount} revisions");
            }

            AddLiveLine(
                $"Ayah notes: {snapshot.AyahNoteCount} current · {snapshot.AyahNoteRevisionCount} revisions");

            AddLiveLine(
                $"Context notes: {snapshot.ContextNoteCount} current · {snapshot.ContextNoteRevisionCount} revisions");

            AddLiveLine(
                snapshot.LastUpdatedUtc is DateTimeOffset updated
                    ? $"Last owner-state update in passage: {updated.LocalDateTime:g}"
                    : "No owner-state timestamp in this passage.");
        }
        catch (Exception ex)
        {
            AddLiveLine(
                "Live research snapshot unavailable",
                emphasized: true);

            AddLiveLine(
                ex.Message);
        }
    }

    private async void ImportAtlasCorpus_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_atlasImportService is null)
        {
            EnsureContextAtlasLoaded();
        }

        var picker =
            new FileOpenPicker
            {
                SuggestedStartLocation =
                    PickerLocationId.Downloads
            };

        picker.FileTypeFilter.Add(
            ".zip");

        nint hwnd =
            WinRT.Interop.WindowNative
                .GetWindowHandle(
                    this);

        WinRT.Interop.InitializeWithWindow
            .Initialize(
                picker,
                hwnd);

        Windows.Storage.StorageFile? file =
            await picker.PickSingleFileAsync();

        if (file is null)
        {
            return;
        }

        try
        {
            ProposalCorpusImportResult result =
                await Task.Run(
                    () =>
                        _atlasImportService!
                            .Import(
                                file.Path));

            ReloadAtlasCorpora(
                preserveCurrentId: false);

            _atlasCurrentCorpus =
                result.Package;

            SelectAtlasCorpus(
                result.Package.Corpus.CorpusId);

            RefreshContextAtlas();

            StatusText.Text =
                result.WasAlreadyPresent
                    ? "Proposal Corpus already present · no-op."
                    : "Proposal Corpus validated and copied byte-for-byte into the isolated Atlas library.";

            await ShowAtlasMessageAsync(
                result.WasAlreadyPresent
                    ? "Already imported"
                    : "Import complete",
                $"{result.Package.Corpus.DisplayName}\n\nPayload SHA-256:\n{result.Package.PayloadSha256}");
        }
        catch (Exception ex)
        {
            StatusText.Text =
                $"Proposal Corpus import refused: {ex.Message}";

            await ShowAtlasMessageAsync(
                "Import refused",
                ex.Message);
        }
    }

    private async void RemoveAtlasCorpus_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_atlasCurrentCorpus is null ||
            _atlasCurrentCorpus.IsBuiltIn ||
            _atlasImportService is null)
        {
            return;
        }

        ProposalCorpusPackage removing =
            _atlasCurrentCorpus;

        var dialog =
            new ContentDialog
            {
                XamlRoot =
                    RootGrid.XamlRoot,
                Title =
                    "Remove imported Proposal Corpus?",
                Content =
                    new TextBlock
                    {
                        Text =
                            $"{removing.Corpus.DisplayName}\n\nThis deletes only the imported ZIP from ContextAtlas/Imported. Owner research state is not touched.",
                        TextWrapping =
                            TextWrapping.Wrap
                    },
                PrimaryButtonText =
                    "Remove imported corpus",
                CloseButtonText =
                    "Cancel",
                DefaultButton =
                    ContentDialogButton.Close
            };

        if (await dialog.ShowAsync() !=
            ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            _atlasImportService.RemoveImported(
                removing);

            _atlasCurrentCorpus =
                null;

            ReloadAtlasCorpora(
                preserveCurrentId: false);

            RefreshContextAtlas();

            StatusText.Text =
                "Imported Proposal Corpus removed from the isolated Atlas library.";
        }
        catch (Exception ex)
        {
            await ShowAtlasMessageAsync(
                "Removal failed",
                ex.Message);
        }
    }

    private async void ExportNewAtlasHandoff_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_atlasExportService is null)
        {
            EnsureContextAtlasLoaded();
        }

        try
        {
            StatusText.Text =
                "Generating Arabic-first New Proposal Handoff…";

            string path =
                await Task.Run(
                    () =>
                        _atlasExportService!
                            .ExportNewProposalHandoff());

            StatusText.Text =
                $"New Proposal Handoff exported · {Path.GetFileName(path)}";

            await ShowAtlasMessageAsync(
                "New Proposal Handoff exported",
                path);
        }
        catch (Exception ex)
        {
            await ShowAtlasMessageAsync(
                "Export failed",
                ex.Message);
        }
    }

    private async void ExportImproveAtlasHandoff_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_atlasCurrentCorpus is null)
        {
            return;
        }

        if (_atlasExportService is null)
        {
            EnsureContextAtlasLoaded();
        }

        try
        {
            ProposalCorpusPackage parent =
                _atlasCurrentCorpus;

            StatusText.Text =
                "Generating Improve Existing Proposal Handoff…";

            string path =
                await Task.Run(
                    () =>
                        _atlasExportService!
                            .ExportImproveExistingHandoff(
                                parent));

            StatusText.Text =
                $"Improve Existing Proposal Handoff exported · {Path.GetFileName(path)}";

            await ShowAtlasMessageAsync(
                "Improve Existing Proposal Handoff exported",
                path);
        }
        catch (Exception ex)
        {
            await ShowAtlasMessageAsync(
                "Export failed",
                ex.Message);
        }
    }

    private void SelectAtlasCorpus(
        string corpusId)
    {
        _atlasUiLoading = true;

        try
        {
            for (int index = 0;
                 index < AtlasCorpusSelector.Items.Count;
                 index++)
            {
                if (AtlasCorpusSelector.Items[index]
                        is ComboBoxItem item &&
                    item.Tag
                        is ProposalCorpusPackage package &&
                    string.Equals(
                        package.Corpus.CorpusId,
                        corpusId,
                        StringComparison.Ordinal))
                {
                    AtlasCorpusSelector.SelectedIndex =
                        index;

                    _atlasCurrentCorpus =
                        package;

                    break;
                }
            }
        }
        finally
        {
            _atlasUiLoading = false;
        }

        RefreshAtlasCorpusMetadata();
        RefreshAtlasToolbarState();
    }

    private void RefreshAtlasCorpusMetadata()
    {
        if (_atlasCurrentCorpus is null)
        {
            AtlasCorpusMetadataText.Text =
                string.Empty;

            return;
        }

        ProposalCorpusDocument corpus =
            _atlasCurrentCorpus.Corpus;

        AtlasCorpusMetadataText.Text =
            $"{CorpusContributorSummary(_atlasCurrentCorpus)} · {corpus.PublishedDate}" +
            (string.IsNullOrWhiteSpace(
                 corpus.Lineage.ParentCorpusId)
                ? string.Empty
                : $" · revision of {corpus.Lineage.ParentCorpusId}");
    }

    private void RefreshAtlasToolbarState()
    {
        AtlasRemoveCorpusButton.IsEnabled =
            _atlasCurrentCorpus is not null &&
            !_atlasCurrentCorpus.IsBuiltIn;
    }

    private ProposalSurah CurrentAtlasSurah() =>
        _atlasCurrentCorpus!
            .Corpus.Surahs.Single(
                x =>
                    x.SurahNumber ==
                    _atlasSurah);

    private void ApplyAtlasTabState()
    {
        foreach (Button button
                 in new[]
                 {
                     AtlasBrowseTabButton,
                     AtlasWorksetsTabButton,
                     AtlasReviewTabButton,
                     AtlasPinsTabButton,
                     AtlasCompareTabButton,
                     AtlasProvenanceTabButton
                 })
        {
            bool active =
                string.Equals(
                    button.Tag?.ToString(),
                    _atlasMode,
                    StringComparison.Ordinal);

            ApplyWorkspaceNavState(
                button,
                active);
        }
    }

    private static bool Contains(
        string? value,
        string query) =>
        !string.IsNullOrWhiteSpace(
            value) &&
        value.Contains(
            query,
            StringComparison.OrdinalIgnoreCase);

    private void AddAtlasHeading(
        string text,
        double size,
        double topMargin = 0)
    {
        AtlasContentPanel.Children.Add(
            new TextBlock
            {
                Text = text,
                Foreground =
                    Brush("TextBrush"),
                FontSize = size,
                FontWeight =
                    FontWeights.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        0,
                        topMargin,
                        0,
                        4)
            });
    }

    private void AddAtlasMuted(
        string text)
    {
        AtlasContentPanel.Children.Add(
            new TextBlock
            {
                Text = text,
                Foreground =
                    Brush("MutedTextBrush"),
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        8)
            });
    }

    private Border NewAtlasCard() =>
        new()
        {
            Background =
                Brush("CardBrush"),
            BorderBrush =
                Brush("BorderBrush"),
            BorderThickness =
                new Thickness(1),
            CornerRadius =
                new CornerRadius(7),
            Padding =
                new Thickness(12),
            Margin =
                new Thickness(
                    0,
                    5,
                    0,
                    7)
        };

    private void AddAtlasSearchRange(
        string header,
        string body,
        ContextAtlasRangeTarget target)
    {
        Border card =
            NewAtlasCard();

        var panel =
            new StackPanel
            {
                Spacing = 6
            };

        var button =
            new Button
            {
                Content = header,
                Tag = target,
                HorizontalAlignment =
                    HorizontalAlignment.Left,
                Padding =
                    new Thickness(
                        10,
                        4,
                        10,
                        4)
            };

        button.Click +=
            AtlasRange_Click;

        var readButton = new Button
        {
            Content = "Read Ayat",
            Tag = target,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 4, 10, 4)
        };
        readButton.Click += AtlasReadingOpen_Click;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        actions.Children.Add(button);
        actions.Children.Add(readButton);
        var pinButton = new Button
        {
            Content = "Pin",
            Tag = target,
            Padding = new Thickness(10, 4, 10, 4)
        };
        pinButton.Click += AtlasPin_Click;
        actions.Children.Add(pinButton);
        panel.Children.Add(actions);

        panel.Children.Add(
            new TextBlock
            {
                Text = body,
                Foreground =
                    Brush("TextBrush"),
                TextWrapping =
                    TextWrapping.Wrap,
                IsTextSelectionEnabled =
                    true
            });

        card.Child =
            panel;

        AtlasContentPanel.Children.Add(
            card);
    }

    private void AddAtlasSearchText(
        string header,
        string body)
    {
        Border card =
            NewAtlasCard();

        card.Child =
            new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = header,
                        Foreground =
                            Brush("AccentBrush"),
                        FontWeight =
                            FontWeights.SemiBold,
                        TextWrapping =
                            TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = body,
                        Foreground =
                            Brush("TextBrush"),
                        TextWrapping =
                            TextWrapping.Wrap,
                        IsTextSelectionEnabled =
                            true
                    }
                }
            };

        AtlasContentPanel.Children.Add(
            card);
    }

    private void AddLiveLine(
        string text,
        bool emphasized = false)
    {
        AtlasLiveResearchPanel.Children.Add(
            new TextBlock
            {
                Text = text,
                Foreground =
                    emphasized
                        ? Brush("AccentBrush")
                        : Brush("TextBrush"),
                FontWeight =
                    emphasized
                        ? FontWeights.SemiBold
                        : FontWeights.Normal,
                FontSize =
                    emphasized
                        ? 13.5
                        : 12.5,
                TextWrapping =
                    TextWrapping.Wrap,
                IsTextSelectionEnabled =
                    true
            });
    }

    private static string CorpusContributorSummary(
        ProposalCorpusPackage package) =>
        string.Join(
            " + ",
            package.Corpus.Contributors.Select(
                x =>
                    string.IsNullOrWhiteSpace(
                        x.ReasoningMode)
                        ? x.ModelName
                        : $"{x.ModelName} / {x.ReasoningMode}"));

    private static string PrettyJson(
        System.Text.Json.JsonElement element)
    {
        try
        {
            using var document =
                System.Text.Json.JsonDocument.Parse(
                    element.GetRawText());

            return System.Text.Json.JsonSerializer.Serialize(
                document.RootElement,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
        }
        catch
        {
            return element.ToString();
        }
    }

    private async Task ShowAtlasMessageAsync(
        string title,
        string message)
    {
        var dialog =
            new ContentDialog
            {
                XamlRoot =
                    RootGrid.XamlRoot,
                Title = title,
                Content =
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping =
                            TextWrapping.Wrap,
                        IsTextSelectionEnabled =
                            true
                    },
                CloseButtonText =
                    "OK"
            };

        await dialog.ShowAsync();
    }
}
