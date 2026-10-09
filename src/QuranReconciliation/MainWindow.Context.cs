using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReconciliation.Infrastructure;
using QuranReconciliation.Models;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private readonly ContextRepository _contexts = new();
    private bool _loadingContextMap;
    private long? _selectedContextId;

    private void LoadContextMapForCurrentSurah(
        long? preferredId = null,
        int? preferredAyah = null)
    {
        if (!_ready)
        {
            return;
        }

        _loadingContextMap = true;

        try
        {
            int verseCount = _chapters[_currentSurah - 1].VersesCount;

            IReadOnlyList<ContextBlock> blocks =
                _contexts.EnsureSeeded(_currentSurah, verseCount);

            _contexts.ValidateMap(_currentSurah, verseCount);

            ContextBlock? preferredBlock =
                preferredId is long requestedId
                    ? blocks.FirstOrDefault(
                        block =>
                            block.Id == requestedId)
                    : null;

            ContextBlock? ayahBlock =
                preferredAyah is int targetAyah
                    ? blocks.FirstOrDefault(
                        block =>
                            targetAyah >= block.StartAyah &&
                            targetAyah <= block.EndAyah)
                    : null;

            bool preferredCoversAyah =
                preferredBlock is not null &&
                (!preferredAyah.HasValue ||
                 (preferredAyah.Value >= preferredBlock.StartAyah &&
                  preferredAyah.Value <= preferredBlock.EndAyah));

            ContextBlock? existingBlock =
                _selectedContextId is long existingId
                    ? blocks.FirstOrDefault(
                        block =>
                            block.Id == existingId)
                    : null;

            long? target =
                (preferredCoversAyah
                    ? preferredBlock
                    : ayahBlock)?
                    .Id ??
                existingBlock?.Id ??
                blocks.FirstOrDefault()?.Id;

            ContextList.Items.Clear();
            ListViewItem? targetItem = null;

            foreach (ContextBlock block in blocks)
            {
                var label = new StackPanel();

                label.Children.Add(new TextBlock
                {
                    Text = block.RangeLabel,
                    Foreground = Brush("TextBrush"),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold
                });

                label.Children.Add(new TextBlock
                {
                    Text =
                        $"{block.Status} · {GetContextJuzLabel(block.SurahNumber, block.StartAyah, block.EndAyah)} · {block.Origin}",
                    Foreground = Brush("MutedTextBrush"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });

                var card =
                    new Border
                    {
                        BorderBrush =
                            Brush("BorderBrush"),
                        BorderThickness =
                            new Thickness(1),
                        CornerRadius =
                            new CornerRadius(6),
                        Padding =
                            new Thickness(6, 5, 6, 5),
                        HorizontalAlignment =
                            HorizontalAlignment.Stretch,
                        Child =
                            label
                    };

                var item = new ListViewItem
                {
                    Content = card,
                    Tag = block,
                    Padding = new Thickness(0),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };

                ContextList.Items.Add(item);

                if (block.Id == target)
                {
                    targetItem = item;
                }
            }

            if (targetItem is null &&
                ContextList.Items.Count > 0)
            {
                targetItem =
                    ContextList.Items[0]
                        as ListViewItem;
            }

            ContextList.SelectedItem =
                targetItem;

            _selectedContextId =
                targetItem?.Tag
                    is ContextBlock selectedBlock
                        ? selectedBlock.Id
                        : null;
        }
        catch (Exception ex)
        {
            ContextList.Items.Clear();
            _selectedContextId = null;
            ContextSelectedText.Text = "Context map unavailable.";
            ContextHistoryPanel.Items.Clear();
            StatusText.Text = $"Context map error: {ex.Message}";
        }
        finally
        {
            _loadingContextMap = false;
        }

        RefreshSelectedContextUi();
        RefreshProposalDiscardUi();
    }

    private void CopyContextProposalPrompt_Click(
        object sender,
        RoutedEventArgs e)
    {
        ChapterSummary chapter =
            _chapters[_currentSurah - 1];

        string juzMetadata =
            GetJuzPromptMetadata(
                chapter.Number,
                chapter.VersesCount);

        string prompt =
            $"Analyze Surah {chapter.Number} ({chapter.NameSimple}) of the Qur'an and propose a Context Map based on coherent thematic/discourse units. " +
            "A Context Block is the smallest consecutive ayah range that should normally be read together for coherent understanding; do not impose an arbitrary maximum length. " +
            $"{juzMetadata} " +
            $"Cover ayah 1 through {chapter.VersesCount} exactly once, in ascending contiguous order, with no gaps and no overlaps. " +
            "Return ONLY one ayah range per line, with no headings, commentary, bullets, explanations, or extra text. " +
            "Use formats like 1-5 for a range or 6 for a single ayah.";

        CopyTextToClipboard(
            prompt);

        StatusText.Text =
            $"AI Context Map prompt copied for Surah {chapter.Number} · {chapter.NameSimple}.";
    }

    private void RefreshProposalDiscardUi()
    {
        if (DiscardContextProposalButton is null)
        {
            return;
        }

        ContextProposalDiscardState? state =
            _contexts.GetDiscardableProposal(
                _currentSurah);

        if (state is null)
        {
            DiscardContextProposalButton.Visibility =
                Visibility.Collapsed;
            DiscardContextProposalButton.Tag =
                null;
            ToolTipService.SetToolTip(
                DiscardContextProposalButton,
                null);
            return;
        }

        DiscardContextProposalButton.Visibility =
            Visibility.Visible;

        DiscardContextProposalButton.Tag =
            state;

        DiscardContextProposalButton.IsEnabled =
            state.CanDiscard;

        ToolTipService.SetToolTip(
            DiscardContextProposalButton,
            state.CanDiscard
                ? $"Discard proposal #{state.ImportId} and restore the previous Context Map."
                : state.Reason);
    }

    private async void DiscardContextProposal_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.Tag is not ContextProposalDiscardState state ||
            !state.CanDiscard)
        {
            return;
        }

        if (!RequireSavedContextNotesBeforeStructuralEdit())
            return;

        var content =
            new StackPanel
            {
                Spacing = 10,
                MinWidth = 500
            };

        content.Children.Add(
            new TextBlock
            {
                Text =
                    $"Proposal #{state.ImportId} · {state.BlockCount} block(s)",
                Foreground =
                    Brush("TextBrush"),
                FontWeight =
                    FontWeights.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            });

        content.Children.Add(
            new TextBlock
            {
                Text =
                    $"Ordered ranges: {state.NormalizedRanges}",
                Foreground =
                    Brush("MutedTextBrush"),
                TextWrapping =
                    TextWrapping.Wrap
            });

        content.Children.Add(
            new TextBlock
            {
                Text =
                    "This proposal is still pristine: no Context boundary/status work, Context notes, or Working Slices depend on it. Discarding it will restore the exact previous Context Map. The proposal itself remains recorded in History as discarded.",
                Foreground =
                    Brush("TextBrush"),
                TextWrapping =
                    TextWrapping.Wrap
            });

        var dialog =
            new ContentDialog
            {
                XamlRoot =
                    RootGrid.XamlRoot,
                Title =
                    "Discard unworked Context proposal?",
                Content =
                    content,
                PrimaryButtonText =
                    "Discard and restore previous map",
                CloseButtonText =
                    "Cancel",
                DefaultButton =
                    ContentDialogButton.Close
            };

        ContentDialogResult result =
            await dialog.ShowAsync();

        if (result !=
            ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            _contexts.DiscardUnworkedProposal(
                state.ImportId,
                _currentSurah);

            LoadContextMapForCurrentSurah();
            RefreshResearchHistory(
                force: true);

            StatusText.Text =
                $"Proposal #{state.ImportId} discarded; previous Context Map restored.";
        }
        catch (Exception ex)
        {
            await ShowContextProposalMessage(
                "Proposal not discarded",
                ex.Message);

            LoadContextMapForCurrentSurah();
        }
    }

    private async void ImportContextProposal_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!RequireSavedContextNotesBeforeStructuralEdit())
            return;

        int verseCount =
            _chapters[_currentSurah - 1].VersesCount;

        IReadOnlyList<ContextBlock> current =
            _contexts.GetBlocks(
                _currentSurah);

        if (current.Any(
            block =>
                string.Equals(
                    block.Status,
                    "Accepted",
                    StringComparison.Ordinal)))
        {
            await ShowContextProposalMessage(
                "Proposal import blocked",
                "This Surah contains at least one Accepted / Locked context block. Proposal import will never overwrite an accepted owner decision. Change the accepted status deliberately first if you truly want to replace this map.");
            return;
        }

        var input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 230,
            MaxHeight = 420,
            PlaceholderText =
                "1–5\n6–10\n11\n12–20"
        };

        AttachTextBoxContextMenu(input);

        var content = new StackPanel
        {
            Spacing = 10,
            MinWidth = 520
        };

        content.Children.Add(
            new TextBlock
            {
                Text =
                    "Paste one contiguous ayah range per line. Use forms such as 1–5, 6-10, or a single ayah such as 11. Bullets and simple numbered-list prefixes are accepted. The proposal must cover this entire Surah with no gaps or overlaps.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("TextBrush")
            });

        content.Children.Add(
            new TextBlock
            {
                Text =
                    "Imported blocks start as Proposed. Existing editable blocks are retained as inactive history rather than deleted.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("MutedTextBrush"),
                FontSize = 12
            });

        content.Children.Add(input);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title =
                $"Import Context Map proposal · {_chapters[_currentSurah - 1].NameSimple}",
            Content = content,
            PrimaryButtonText = "Preview",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult inputResult =
            await dialog.ShowAsync();

        if (inputResult != ContentDialogResult.Primary)
        {
            return;
        }

        IReadOnlyList<ContextProposalRange> ranges;

        try
        {
            ranges =
                ContextProposalParser.Parse(
                    input.Text,
                    verseCount);
        }
        catch (Exception ex)
        {
            await ShowContextProposalMessage(
                "Proposal not imported",
                ex.Message);
            return;
        }

        string normalized =
            ContextProposalParser.Normalize(
                ranges);

        var preview = new StackPanel
        {
            Spacing = 10,
            MinWidth = 520
        };

        preview.Children.Add(
            new TextBlock
            {
                Text =
                    $"{ranges.Count} Proposed context block(s) · complete coverage 1–{verseCount}",
                Foreground = Brush("TextBrush"),
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });

        preview.Children.Add(
            new Border
            {
                Background = Brush("PanelAltBrush"),
                BorderBrush = Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Child = new ScrollViewer
                {
                    MaxHeight = 290,
                    VerticalScrollBarVisibility =
                        ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        Text =
                            string.Join(
                                Environment.NewLine,
                                ranges.Select(
                                    range =>
                                        $"Ayat {range.RangeLabel}")),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush("TextBrush")
                    }
                }
            });

        preview.Children.Add(
            new TextBlock
            {
                Text =
                    current.Count == 1 &&
                    string.Equals(
                        current[0].Origin,
                        "Unsegmented seed",
                        StringComparison.Ordinal)
                        ? "This will replace the neutral full-Surah seed."
                        : $"This will replace the current {current.Count}-block editable map. Existing context notes and boundary history remain preserved with the prior inactive blocks.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("MutedTextBrush"),
                FontSize = 12
            });

        int activeDependentSlices =
            _workingSlices.CountActiveForSurah(_currentSurah);
        if (activeDependentSlices > 0)
        {
            preview.Children.Add(
                new TextBlock
                {
                    Text =
                        $"Research provenance notice: this Surah has {activeDependentSlices} active Working Slice(s). Those originally linked to replaced Context blocks retain their historical parent IDs and saved research. They will NOT be silently reparented to this proposal. Opening an archived parent will offer an explicit current-Context navigation choice.",
                    Foreground = Brush("TextBrush"),
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
        }

        var confirm = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Preview proposed Context Map",
            Content = preview,
            PrimaryButtonText = "Import proposal",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult confirmResult =
            await confirm.ShowAsync();

        if (confirmResult != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            ContextProposalImportResult imported =
                _contexts.ImportProposal(
                    _currentSurah,
                    verseCount,
                    input.Text,
                    ranges);

            LoadContextMapForCurrentSurah(
                imported.FirstContextBlockId);

            RefreshResearchHistory(
                force: true);

            StatusText.Text =
                $"Context proposal #{imported.ImportId} imported · {imported.BlockCount} Proposed blocks · {normalized}.";
        }
        catch (Exception ex)
        {
            await ShowContextProposalMessage(
                "Proposal not imported",
                ex.Message);

            LoadContextMapForCurrentSurah();
        }
    }

    private async Task ShowContextProposalMessage(
        string title,
        string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("TextBrush"),
                MinWidth = 420
            },
            CloseButtonText = "OK"
        };

        await dialog.ShowAsync();
    }

    private void ContextList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_loadingContextMap)
        {
            return;
        }

        if (ContextList.SelectedItem is ListViewItem item &&
            item.Tag is ContextBlock block)
        {
            _selectedContextId = block.Id;
        }
        else
        {
            _selectedContextId = null;
        }

        RefreshSelectedContextUi();
    }

    private void RefreshSelectedContextUi()
    {
        if (_selectedContextId is not long id)
        {
            ContextSelectedText.Text = "Select a context block.";
            ContextStatusSelector.SelectedIndex = -1;
            ContextHistoryPanel.Items.Clear();
            LoadContextNoteTarget(null);
            RefreshContextActionState(null);
            return;
        }

        try
        {
            ContextBlock block = _contexts.GetBlock(id);

            ContextSelectedText.Text =
                $"{block.RangeLabel} · {GetContextJuzLabel(block.SurahNumber, block.StartAyah, block.EndAyah)} · {block.Origin}" +
                (block.Status == "Accepted"
                    ? " · LOCKED"
                    : string.Empty);

            int statusIndex = block.Status switch
            {
                "Proposed" => 0,
                "Owner Reviewed" => 1,
                "Accepted" => 2,
                _ => -1
            };

            bool previousLoading = _loadingContextMap;
            _loadingContextMap = true;
            ContextStatusSelector.SelectedIndex = statusIndex;
            _loadingContextMap = previousLoading;

            SplitAfterTextBox.Text =
                block.StartAyah < block.EndAyah
                    ? ((block.StartAyah + block.EndAyah) / 2).ToString()
                    : string.Empty;

            RefreshContextHistory(id);
            LoadContextNoteTarget(id);
            RefreshContextActionState(block);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Context selection error: {ex.Message}";
        }
    }

    private void RefreshContextHistory(long blockId)
    {
        ContextHistoryPanel.Items.Clear();

        IReadOnlyList<ResearchHistoryEntry> activity =
            _history.SearchTimeline(
                    null,
                    "Context",
                    _currentSurah,
                    250)
                .Where(
                    item =>
                        item.ContextBlockId == blockId)
                .Take(10)
                .ToList();

        if (activity.Count == 0)
        {
            ContextHistoryPanel.Items.Add(
                new TextBlock
                {
                    Text = "No Context activity yet.",
                    Foreground = Brush("MutedTextBrush"),
                    FontSize = 12,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap
                });

            return;
        }

        foreach (ResearchHistoryEntry item in activity)
        {
            ContextHistoryPanel.Items.Add(
                new TextBlock
                {
                    Text =
                        $"{item.ChangedUtc.LocalDateTime:g} · {item.Kind} · {item.Body}",
                    Foreground =
                        Brush("MutedTextBrush"),
                    FontSize = 11.5,
                    Margin =
                        new Thickness(
                            0,
                            2,
                            0,
                            2),
                    TextWrapping =
                        TextWrapping.Wrap
                });
        }
    }

    private void RefreshContextActionState(
        ContextBlock? block)
    {
        bool hasBlock =
            block is not null;

        ContextCreateWorkingSliceButton.IsEnabled =
            hasBlock;

        bool editable =
            block is not null &&
            !string.Equals(
                block.Status,
                "Accepted",
                StringComparison.Ordinal);

        IReadOnlyList<ContextBlock> blocks =
            hasBlock
                ? _contexts.GetBlocks(
                    block!.SurahNumber)
                : Array.Empty<ContextBlock>();

        int index = -1;

        if (block is not null)
        {
            for (int i = 0;
                 i < blocks.Count;
                 i++)
            {
                if (blocks[i].Id ==
                    block.Id)
                {
                    index = i;
                    break;
                }
            }
        }

        ContextBlock? previous =
            index > 0
                ? blocks[index - 1]
                : null;

        ContextBlock? next =
            index >= 0 &&
            index < blocks.Count - 1
                ? blocks[index + 1]
                : null;

        bool previousEditable =
            previous is not null &&
            !string.Equals(
                previous.Status,
                "Accepted",
                StringComparison.Ordinal);

        bool nextEditable =
            next is not null &&
            !string.Equals(
                next.Status,
                "Accepted",
                StringComparison.Ordinal);

        ContextExtendStartButton.IsEnabled =
            editable &&
            previousEditable &&
            previous!.StartAyah <
                previous.EndAyah;

        ContextShrinkStartButton.IsEnabled =
            editable &&
            previousEditable &&
            block!.StartAyah <
                block.EndAyah;

        ContextShrinkEndButton.IsEnabled =
            editable &&
            nextEditable &&
            block!.StartAyah <
                block.EndAyah;

        ContextExtendEndButton.IsEnabled =
            editable &&
            nextEditable &&
            next!.StartAyah <
                next.EndAyah;

        ContextMergePreviousButton.IsEnabled =
            editable &&
            previousEditable;

        ContextMergeNextButton.IsEnabled =
            editable &&
            nextEditable;

        bool validSplit =
            editable &&
            block!.StartAyah <
                block.EndAyah &&
            int.TryParse(
                SplitAfterTextBox.Text,
                out int splitAyah) &&
            splitAyah >=
                block.StartAyah &&
            splitAyah <
                block.EndAyah;

        ContextSplitButton.IsEnabled =
            validSplit;

        SplitAfterTextBox.IsEnabled =
            editable &&
            block is not null &&
            block.StartAyah <
                block.EndAyah;

        ContextBoundaryStateText.Text =
            block is null
                ? "Select a Context Block to inspect available boundary actions."
                : !editable
                    ? "Accepted / Locked: boundary editing is disabled. Change status deliberately to reopen editing."
                    : $"Editable range {block.RangeLabel}. Unavailable edge operations are disabled.";
    }

    private void ContextSplitAfter_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_loadingContextMap)
        {
            return;
        }

        if (_selectedContextId is long id)
        {
            try
            {
                RefreshContextActionState(
                    _contexts.GetBlock(id));
            }
            catch
            {
                RefreshContextActionState(null);
            }
        }
        else
        {
            RefreshContextActionState(null);
        }
    }

    private void ContextStatusSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_ready ||
            _loadingContextMap ||
            _selectedContextId is not long id ||
            ContextStatusSelector.SelectedItem is not ComboBoxItem item ||
            item.Tag?.ToString() is not string status)
        {
            return;
        }

        try
        {
            _contexts.SetStatus(id, status);
            LoadContextMapForCurrentSurah(id);

            StatusText.Text = status == "Accepted"
                ? "Context block accepted and boundary-locked."
                : $"Context status changed to {status}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not change context status: {ex.Message}";
            LoadContextMapForCurrentSurah(id);
        }
    }

    private void ContextExtendStart_Click(object sender, RoutedEventArgs e) =>
        RunContextEdit(
            id =>
            {
                _contexts.ExtendStart(id);
                return id;
            },
            "Context start extended upward.");

    private void ContextShrinkStart_Click(object sender, RoutedEventArgs e) =>
        RunContextEdit(
            id =>
            {
                _contexts.ShrinkStart(id);
                return id;
            },
            "Context start moved downward.");

    private void ContextShrinkEnd_Click(object sender, RoutedEventArgs e) =>
        RunContextEdit(
            id =>
            {
                _contexts.ShrinkEnd(id);
                return id;
            },
            "Context end moved upward.");

    private void ContextExtendEnd_Click(object sender, RoutedEventArgs e) =>
        RunContextEdit(
            id =>
            {
                _contexts.ExtendEnd(id);
                return id;
            },
            "Context end extended downward.");

    private void ContextMergePrevious_Click(object sender, RoutedEventArgs e) =>
        RunContextEdit(
            id => _contexts.MergeWithPrevious(id),
            "Merged with previous context block.");

    private void ContextMergeNext_Click(object sender, RoutedEventArgs e) =>
        RunContextEdit(
            id => _contexts.MergeWithNext(id),
            "Merged with next context block.");

    private void ContextSplit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedContextId is not long id)
        {
            StatusText.Text = "Select a context block before splitting.";
            return;
        }

        if (!int.TryParse(SplitAfterTextBox.Text, out int ayah))
        {
            StatusText.Text = "Enter a valid ayah number for the split.";
            return;
        }

        RunContextEdit(
            selectedId =>
            {
                _contexts.SplitAfter(selectedId, ayah);
                return selectedId;
            },
            $"Context split after ayah {ayah}.");
    }

    private void RunContextEdit(
        Func<long, long> operation,
        string successMessage)
    {
        if (!RequireSavedContextNotesBeforeStructuralEdit())
            return;

        if (_selectedContextId is not long id)
        {
            StatusText.Text = "Select a context block first.";
            return;
        }

        try
        {
            long selectedAfter = operation(id);

            _contexts.ValidateMap(
                _currentSurah,
                _chapters[_currentSurah - 1].VersesCount);

            LoadContextMapForCurrentSurah(selectedAfter);
            RefreshResearchHistory();
            StatusText.Text = successMessage;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            LoadContextMapForCurrentSurah(id);
        }
    }
    private bool RequireSavedContextNotesBeforeStructuralEdit()
    {
        // Context replacement, merge and proposal discard can archive a
        // Context ID. Preserve the note's original research identity: don't
        // allow a structural change to strand its uncommitted draft.
        var pending = new HashSet<long>(_contextNoteDrafts.Keys);
        if (_contextNoteDirty && _noteContextId is long currentId)
            pending.Add(currentId);

        foreach (long id in pending)
        {
            try
            {
                if (_contexts.GetHistoricalBlock(id).SurahNumber != _currentSurah)
                    continue;
            }
            catch
            {
                // Unknown original authority must be resolved before editing
                // Context structure; fail closed without discarding notes.
            }

            StatusText.Text =
                "Context structure unchanged. Save the original Context-note " +
                "draft(s) for this Surah before importing, discarding, " +
                "splitting or merging Contexts. Their original IDs are preserved.";
            return false;
        }

        return true;
    }


}
