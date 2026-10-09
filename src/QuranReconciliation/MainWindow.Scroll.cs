using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private ScrollViewer? _surahListScrollViewer;

    private void InitializeScrollOwnershipRouting()
    {
        LeftSidebarBorder.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(LeftRegion_PointerWheelChanged),
            handledEventsToo: true);

        CenterWorkspaceBorder.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(CenterRegion_PointerWheelChanged),
            handledEventsToo: true);

        RightSidebarBorder.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(RightRegion_PointerWheelChanged),
            handledEventsToo: true);

        WorkingSliceEvidenceBorder.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(WorkingSliceEvidenceRegion_PointerWheelChanged),
            handledEventsToo: true);

        WorkingSliceRecordBorder.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(WorkingSliceRecordRegion_PointerWheelChanged),
            handledEventsToo: true);
    }

    private void LeftRegion_PointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e)
    {
        _surahListScrollViewer ??=
            FindDescendantScrollViewer(SurahList);

        RouteWheelToOwner(
            LeftSidebarBorder,
            _surahListScrollViewer,
            e);
    }

    private void CenterRegion_PointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e) =>
        RouteWheelToOwner(
            CenterWorkspaceBorder,
            WorkspaceScrollViewer,
            e);

    private void RightRegion_PointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e) =>
        RouteWheelToOwner(
            RightSidebarBorder,
            RightSidebarScrollViewer,
            e);

    private void WorkingSliceEvidenceRegion_PointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e) =>
        RouteWheelToOwner(
            WorkingSliceEvidenceBorder,
            WorkingSliceEvidenceScrollViewer,
            e);

    private void WorkingSliceRecordRegion_PointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e) =>
        RouteWheelToOwner(
            WorkingSliceRecordBorder,
            WorkingSliceRecordScrollViewer,
            e);

    private void ClipRegionToBounds_SizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            e.NewSize.Width <= 0 ||
            e.NewSize.Height <= 0)
        {
            return;
        }

        element.Clip =
            new RectangleGeometry
            {
                Rect =
                    new Windows.Foundation.Rect(
                        0,
                        0,
                        e.NewSize.Width,
                        e.NewSize.Height)
            };
    }

    private static void RouteWheelToOwner(
        UIElement visibleRegion,
        ScrollViewer? owner,
        PointerRoutedEventArgs e)
    {
        if (e.Handled)
        {
            // The correct native owner already consumed this wheel event.
            // The panel-root handler is only a fallback, never a second scroll.
            return;
        }

        if (owner is null ||
            owner.ScrollableHeight <= 0)
        {
            return;
        }

        int delta =
            e.GetCurrentPoint(visibleRegion)
                .Properties
                .MouseWheelDelta;

        if (delta == 0)
        {
            return;
        }

        double wheelNotches = delta / 120.0;
        double step = Math.Clamp(
            owner.ViewportHeight * 0.10,
            56,
            112);

        double target = Math.Clamp(
            owner.VerticalOffset -
            (wheelNotches * step),
            0,
            owner.ScrollableHeight);

        owner.ChangeView(
            null,
            target,
            null,
            disableAnimation: true);

        e.Handled = true;
    }

    private static ScrollViewer? FindDescendantScrollViewer(
        DependencyObject root)
    {
        int count =
            VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child =
                VisualTreeHelper.GetChild(root, i);

            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            ScrollViewer? nested =
                FindDescendantScrollViewer(child);

            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
