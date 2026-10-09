using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReconciliation.Infrastructure;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private JuzRepository _juz = null!;
    private bool _updatingJuzUi;

    private void InitializeJuzNavigation()
    {
        _juz =
            new JuzRepository(
                _chapters);

        JuzSelector.Items.Clear();

        foreach (JuzBoundary juz
                 in _juz.GetAll())
        {
            JuzSelector.Items.Add(
                new ComboBoxItem
                {
                    Content =
                        juz.DisplayLabel,
                    Tag =
                        juz
                });
        }
    }

    private void JuzSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_ready ||
            _updatingJuzUi ||
            JuzSelector.SelectedItem
                is not ComboBoxItem item ||
            item.Tag
                is not JuzBoundary juz)
        {
            return;
        }

        NavigateToJuzStart(
            juz);
    }

    private void NavigateToJuzStart(
        JuzBoundary juz)
    {
        ShowResearchWorkspace();

        if (_currentSurah !=
            juz.StartSurah)
        {
            SurahList.SelectedIndex =
                juz.StartSurah - 1;
        }

        if (_currentSurah !=
            juz.StartSurah)
        {
            _currentSurah =
                juz.StartSurah;

            LoadCurrentSurah(
                resetScroll: true);

            LoadContextMapForCurrentSurah();
        }

        ResetVerseRenderingAtAyah(
            juz.StartAyah);

        UpdateJuzUi(
            juz.StartSurah,
            juz.StartAyah);

        RootGrid.DispatcherQueue.TryEnqueue(
            () =>
            {
                FindRenderedVerseElement(
                    juz.StartAyah)
                    ?.StartBringIntoView(
                        new BringIntoViewOptions
                        {
                            AnimationDesired =
                                false,
                            VerticalAlignmentRatio =
                                0
                        });
            });

        StatusText.Text =
            $"Opened Juz {juz.Number} at {juz.StartVerseKey} · responsive lazy window.";
    }

    private void UpdateJuzUi(
        int surah,
        int ayah)
    {
        int juzNumber =
            _juz.GetJuzNumber(
                surah,
                ayah);

        JuzBoundary juz =
            _juz.Get(
                juzNumber);

        _updatingJuzUi = true;

        try
        {
            if (JuzSelector.SelectedIndex !=
                juzNumber - 1)
            {
                JuzSelector.SelectedIndex =
                    juzNumber - 1;
            }

            CurrentJuzText.Text =
                $"Research position: Juz {juzNumber} · {juz.StartVerseKey}–{juz.EndVerseKey}";
        }
        finally
        {
            _updatingJuzUi = false;
        }
    }

    private void UpdateJuzFromResearchViewport()
    {
        if (!_ready ||
            WorkspaceGrid.Visibility !=
                Visibility.Visible)
        {
            return;
        }

        foreach (UIElement child
                 in VersePanel.Children)
        {
            if (child is not FrameworkElement element ||
                element.Tag is not int ayah ||
                element.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var transform =
                    element.TransformToVisual(
                        WorkspaceScrollViewer);

                Windows.Foundation.Point top =
                    transform.TransformPoint(
                        new Windows.Foundation.Point(
                            0,
                            0));

                if (top.Y + element.ActualHeight >= 0)
                {
                    UpdateJuzUi(
                        _currentSurah,
                        ayah);
                    return;
                }
            }
            catch
            {
                // Viewport awareness is advisory navigation metadata only.
                // It must never interfere with research rendering.
            }
        }
    }

    private string GetSurahJuzSummary(
        int surah,
        int verseCount) =>
        _juz.GetRangeLabel(
            surah,
            1,
            verseCount);

    private string GetContextJuzLabel(
        int surah,
        int startAyah,
        int endAyah) =>
        _juz.GetRangeLabel(
            surah,
            startAyah,
            endAyah);

    private string GetJuzPromptMetadata(
        int surah,
        int verseCount)
    {
        string span =
            GetSurahJuzSummary(
                surah,
                verseCount);

        IReadOnlyList<JuzBoundary> internalStarts =
            _juz.GetStartsInsideRange(
                surah,
                1,
                verseCount);

        if (internalStarts.Count == 0)
        {
            return
                $"Traditional reading-division metadata: this Surah is within {span}. " +
                "Juz boundaries are metadata only; do not force a Context Block boundary because of a Juz boundary.";
        }

        string boundaries =
            string.Join(
                "; ",
                internalStarts.Select(
                    juz =>
                        $"Juz {juz.Number} begins at {juz.StartVerseKey}"));

        return
            $"Traditional reading-division metadata: this Surah spans {span}; {boundaries}. " +
            "Treat Juz boundaries as metadata only. Do not force a Context Block boundary there unless the discourse itself supports that boundary.";
    }

    private Border CreateResearchJuzBoundaryMarker(
        int juzNumber)
    {
        TextBlock text =
            RegisterZoomText(
                new TextBlock
                {
                    Text =
                        $"Juz {juzNumber} begins here · পারা {juzNumber}",
                    Foreground =
                        Brush("AccentBrush"),
                    FontWeight =
                        FontWeights.SemiBold,
                    TextWrapping =
                        TextWrapping.Wrap
                },
                14);

        return new Border
        {
            Background =
                Brush("PanelAltBrush"),
            BorderBrush =
                Brush("AccentBrush"),
            BorderThickness =
                new Thickness(
                    0,
                    0,
                    0,
                    1),
            Padding =
                new Thickness(
                    10,
                    7,
                    10,
                    7),
            Margin =
                new Thickness(
                    0,
                    0,
                    0,
                    14),
            Child =
                text
        };
    }

    private Border CreateWorkingSliceJuzBoundaryMarker(
        int juzNumber)
    {
        return new Border
        {
            Background =
                Brush("PanelAltBrush"),
            BorderBrush =
                Brush("AccentBrush"),
            BorderThickness =
                new Thickness(
                    0,
                    0,
                    0,
                    1),
            Padding =
                new Thickness(
                    10,
                    7,
                    10,
                    7),
            Margin =
                new Thickness(
                    0,
                    0,
                    0,
                    10),
            Child =
                new TextBlock
                {
                    Text =
                        $"Juz {juzNumber} begins here · পারা {juzNumber}",
                    Foreground =
                        Brush("AccentBrush"),
                    FontWeight =
                        FontWeights.SemiBold,
                    FontSize =
                        Z(14),
                    TextWrapping =
                        TextWrapping.Wrap
                }
        };
    }
}
