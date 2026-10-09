using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReconciliation.Models;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private const int InitialVerseBatch = 14;
    private const int NextVerseBatch = 14;
    private const int TafsirInitialVerseBatch = 6;
    private const int TafsirNextVerseBatch = 6;

    private IReadOnlyList<VerseBundle> _loadedVerses =
        Array.Empty<VerseBundle>();

    private int _renderedVerseStartIndex;
    private int _renderedVerseCount;
    private bool _appendingVerseBatch;
    private int _zoomRestoreGeneration;
    private int _workingSliceZoomRestoreGeneration;

    private int CurrentInitialVerseBatch =>
        _selectedTafsirIds.Count > 0
            ? TafsirInitialVerseBatch
            : InitialVerseBatch;

    private int CurrentNextVerseBatch =>
        _selectedTafsirIds.Count > 0
            ? TafsirNextVerseBatch
            : NextVerseBatch;

    private readonly List<ZoomTextTarget> _zoomTextTargets = [];
    private readonly List<ZoomLayoutTarget> _zoomLayoutTargets = [];
    private readonly List<ZoomTextTarget> _workingSliceZoomTextTargets = [];

    private sealed record ZoomTextTarget(
        TextBlock Block,
        double BaseFontSize,
        double? BaseLineHeight);

    private sealed record ZoomLayoutTarget(
        FrameworkElement Element,
        double? BaseMinHeight,
        Thickness? BaseMargin);

    private TextBlock RegisterZoomText(
        TextBlock block,
        double baseFontSize,
        double? baseLineHeight = null)
    {
        block.FontSize = Z(baseFontSize);
        if (baseLineHeight is double lineHeight)
        {
            block.LineHeight = Z(lineHeight);
        }

        _zoomTextTargets.Add(new ZoomTextTarget(
            block,
            baseFontSize,
            baseLineHeight));
        return block;
    }

    private T RegisterZoomLayout<T>(
        T element,
        double? baseMinHeight = null,
        Thickness? baseMargin = null)
        where T : FrameworkElement
    {
        if (baseMinHeight is double minHeight)
        {
            element.MinHeight = Z(minHeight);
        }

        if (baseMargin is Thickness margin)
        {
            element.Margin = ScaleThickness(margin);
        }

        _zoomLayoutTargets.Add(new ZoomLayoutTarget(
            element,
            baseMinHeight,
            baseMargin));
        return element;
    }

    private Thickness ScaleThickness(Thickness value) =>
        new(
            Z(value.Left),
            Z(value.Top),
            Z(value.Right),
            Z(value.Bottom));

    private TextBlock CreateZoomSelectableText(
        string text,
        double baseFontSize,
        double baseLineHeight)
    {
        TextBlock block = CreateSelectableText(
            text,
            Z(baseFontSize),
            Z(baseLineHeight));

        _zoomTextTargets.Add(new ZoomTextTarget(
            block,
            baseFontSize,
            baseLineHeight));
        return block;
    }

    private void ClearVerseZoomTargets()
    {
        _zoomTextTargets.Clear();
        _zoomLayoutTargets.Clear();
    }

    private void ClearWorkingSliceZoomTargets()
    {
        _workingSliceZoomTextTargets.Clear();
    }

    private void CaptureWorkingSliceZoomTargets(
        DependencyObject root)
    {
        if (root is TextBlock block)
        {
            double divisor =
                Math.Max(_zoom, 0.01f);

            double baseFontSize =
                block.FontSize / divisor;

            double? baseLineHeight =
                block.LineHeight > 0
                    ? block.LineHeight / divisor
                    : null;

            _workingSliceZoomTextTargets.Add(
                new ZoomTextTarget(
                    block,
                    baseFontSize,
                    baseLineHeight));

            return;
        }

        switch (root)
        {
            case Panel panel:
                foreach (UIElement child
                         in panel.Children)
                {
                    CaptureWorkingSliceZoomTargets(
                        child);
                }

                break;

            case Border border
                when border.Child
                    is DependencyObject child:
                CaptureWorkingSliceZoomTargets(
                    child);
                break;

            case ContentControl contentControl
                when contentControl.Content
                    is DependencyObject child:
                CaptureWorkingSliceZoomTargets(
                    child);
                break;
        }
    }

    private void ApplyWorkingSliceZoomMetrics()
    {
        foreach (ZoomTextTarget target
                 in _workingSliceZoomTextTargets)
        {
            target.Block.FontSize =
                Z(target.BaseFontSize);

            if (target.BaseLineHeight
                    is double lineHeight)
            {
                target.Block.LineHeight =
                    Z(lineHeight);
            }
        }
    }

    private void ScheduleWorkingSliceOffsetRestore(
        double previousOffset)
    {
        int generation =
            ++_workingSliceZoomRestoreGeneration;

        RootGrid.DispatcherQueue.TryEnqueue(
            () =>
            {
                if (generation !=
                    _workingSliceZoomRestoreGeneration)
                {
                    return;
                }

                WorkingSliceEvidenceScrollViewer
                    .ChangeView(
                        null,
                        Math.Min(
                            previousOffset,
                            WorkingSliceEvidenceScrollViewer
                                .ScrollableHeight),
                        null,
                        disableAnimation: true);
            });
    }

    private void ApplyRenderedVerseZoomMetrics()
    {
        foreach (ZoomTextTarget target in _zoomTextTargets)
        {
            target.Block.FontSize = Z(target.BaseFontSize);
            if (target.BaseLineHeight is double lineHeight)
            {
                target.Block.LineHeight = Z(lineHeight);
            }
        }

        foreach (ZoomLayoutTarget target in _zoomLayoutTargets)
        {
            if (target.BaseMinHeight is double minHeight)
            {
                target.Element.MinHeight = Z(minHeight);
            }

            if (target.BaseMargin is Thickness margin)
            {
                target.Element.Margin = ScaleThickness(margin);
            }
        }
    }

    private void ScheduleWorkspaceOffsetRestore(
        double previousOffset)
    {
        int generation = ++_zoomRestoreGeneration;

        RootGrid.DispatcherQueue.TryEnqueue(() =>
        {
            if (generation != _zoomRestoreGeneration)
            {
                return;
            }

            WorkspaceScrollViewer.ChangeView(
                null,
                Math.Min(
                    previousOffset,
                    WorkspaceScrollViewer.ScrollableHeight),
                null,
                disableAnimation: true);
        });
    }

    private void ResetVerseRendering(
        IReadOnlyList<VerseBundle> verses,
        int startAyah = 1)
    {
        _loadedVerses = verses;

        int requestedStart =
            Math.Max(
                0,
                Math.Min(
                    verses.Count,
                    startAyah - 1));

        _renderedVerseStartIndex =
            requestedStart;

        _renderedVerseCount = 0;

        ClearVerseZoomTargets();
        VersePanel.Children.Clear();

        int remaining =
            Math.Max(
                0,
                verses.Count -
                _renderedVerseStartIndex);

        int initialCount =
            _selectedTafsirIds.Count == 0 &&
            remaining <= 30
                ? remaining
                : Math.Min(
                    CurrentInitialVerseBatch,
                    remaining);

        AppendVerseBatch(initialCount);
    }

    private void ResetVerseRenderingAtAyah(
        int ayahNumber)
    {
        ResetVerseRendering(
            _loadedVerses,
            ayahNumber);

        WorkspaceScrollViewer.ChangeView(
            0,
            0,
            null,
            disableAnimation: true);
    }

    private void AppendVerseBatch(int? requestedCount = null)
    {
        int renderedEnd =
            _renderedVerseStartIndex +
            _renderedVerseCount;

        if (_appendingVerseBatch ||
            renderedEnd >=
                _loadedVerses.Count)
        {
            return;
        }

        _appendingVerseBatch = true;
        try
        {
            int batch =
                requestedCount ??
                CurrentNextVerseBatch;

            int end = Math.Min(
                _loadedVerses.Count,
                renderedEnd + batch);

            for (int i = renderedEnd;
                 i < end;
                 i++)
            {
                VersePanel.Children.Add(
                    CreateVerseCard(
                        _loadedVerses[i]));
            }

            _renderedVerseCount =
                end -
                _renderedVerseStartIndex;
        }
        finally
        {
            _appendingVerseBatch = false;
        }
    }

    private void WorkspaceScrollViewer_ViewChanged(
        object sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (_loadingSurah)
        {
            return;
        }

        if (_renderedVerseStartIndex +
            _renderedVerseCount <
            _loadedVerses.Count)
        {
            double remaining =
                WorkspaceScrollViewer.ScrollableHeight -
                WorkspaceScrollViewer.VerticalOffset;

            double threshold = Math.Max(
                900,
                WorkspaceScrollViewer.ViewportHeight * 1.5);

            if (remaining <= threshold)
            {
                AppendVerseBatch();
            }
        }

        if (!e.IsIntermediate)
        {
            UpdateJuzFromResearchViewport();
        }
    }

    private void EnsureVerseRenderedThrough(
        int ayahNumber)
    {
        int targetIndex =
            Math.Max(
                0,
                Math.Min(
                    _loadedVerses.Count - 1,
                    ayahNumber - 1));

        int renderedEnd =
            _renderedVerseStartIndex +
            _renderedVerseCount;

        if (targetIndex >=
                _renderedVerseStartIndex &&
            targetIndex <
                renderedEnd)
        {
            return;
        }

        if (targetIndex <
            _renderedVerseStartIndex)
        {
            ResetVerseRendering(
                _loadedVerses,
                1);

            renderedEnd =
                _renderedVerseStartIndex +
                _renderedVerseCount;
        }

        int requested =
            targetIndex + 1 -
            renderedEnd;

        if (requested > 0)
        {
            AppendVerseBatch(
                requested);
        }
    }

    private FrameworkElement?
        FindRenderedVerseElement(
            int ayahNumber)
    {
        foreach (UIElement child
                 in VersePanel.Children)
        {
            if (child is FrameworkElement element &&
                element.Tag is int ayah &&
                ayah == ayahNumber)
            {
                return element;
            }
        }

        return null;
    }

    private void WorkspaceScrollViewer_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        UpdateWorkspaceContentWidth();
    }

    private void UpdateWorkspaceContentWidth()
    {
        if (WorkspaceScrollViewer is null ||
            WorkspaceContentPanel is null)
        {
            return;
        }

        double viewport =
            WorkspaceScrollViewer.ViewportWidth > 0
                ? WorkspaceScrollViewer.ViewportWidth
                : WorkspaceScrollViewer.ActualWidth;

        if (viewport <= 0)
        {
            return;
        }

        double logicalViewport = viewport - 48;

        WorkspaceContentPanel.Width =
            Math.Max(640, logicalViewport);
    }
}
