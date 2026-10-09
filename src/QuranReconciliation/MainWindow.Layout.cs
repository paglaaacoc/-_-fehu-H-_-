using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private const double DefaultLeftSidebarWidth = 300;
    private const double DefaultRightSidebarWidth = 390;
    private const double MinLeftSidebarWidth = 250;
    private const double MinRightSidebarWidth = 360;
    private const double SplitterWidth = 6;

    private double _leftSidebarWidth = DefaultLeftSidebarWidth;
    private double _rightSidebarWidth = DefaultRightSidebarWidth;
    private string _topBarMode = "Full";

    private void ApplyLayoutSettings()
    {
        _leftSidebarWidth = Math.Clamp(
            _settings.LeftSidebarWidth,
            MinLeftSidebarWidth,
            700);

        _rightSidebarWidth = Math.Clamp(
            _settings.RightSidebarWidth,
            MinRightSidebarWidth,
            800);

        _topBarMode = _settings.TopBarMode is "Compact" or "Hidden"
            ? _settings.TopBarMode
            : "Full";

        LeftSidebarMenuItem.IsChecked = _settings.ShowLeftSidebar;
        RightSidebarMenuItem.IsChecked = _settings.ShowRightSidebar;

        ApplySidebarVisibility();
        ApplyTopBarMode();
    }

    private void ApplySidebarVisibility()
    {
        bool showLeft = LeftSidebarMenuItem.IsChecked;
        bool showRight = RightSidebarMenuItem.IsChecked;

        LeftSidebarBorder.Visibility =
            showLeft ? Visibility.Visible : Visibility.Collapsed;
        LeftSidebarSplitter.Visibility =
            showLeft ? Visibility.Visible : Visibility.Collapsed;
        LeftSidebarColumn.Width =
            new GridLength(showLeft ? _leftSidebarWidth : 0);
        LeftSplitterColumn.Width =
            new GridLength(showLeft ? SplitterWidth : 0);

        RightSidebarBorder.Visibility =
            showRight ? Visibility.Visible : Visibility.Collapsed;
        RightSidebarSplitter.Visibility =
            showRight ? Visibility.Visible : Visibility.Collapsed;
        RightSidebarColumn.Width =
            new GridLength(showRight ? _rightSidebarWidth : 0);
        RightSplitterColumn.Width =
            new GridLength(showRight ? SplitterWidth : 0);

        UpdateWorkspaceContentWidth();
    }

    private void LeftSidebarMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        ApplySidebarVisibility();
        SaveCurrentSettings();
    }

    private void RightSidebarMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        ApplySidebarVisibility();
        SaveCurrentSettings();
    }

    private void LeftSidebarSplitter_DragDelta(
        object sender,
        DragDeltaEventArgs e)
    {
        if (!LeftSidebarMenuItem.IsChecked)
        {
            return;
        }

        double max = Math.Max(
            MinLeftSidebarWidth,
            WorkspaceGrid.ActualWidth -
            (RightSidebarMenuItem.IsChecked ? _rightSidebarWidth : 0) -
            720 -
            (LeftSplitterColumn.Width.Value + RightSplitterColumn.Width.Value));

        _leftSidebarWidth = Math.Clamp(
            _leftSidebarWidth + e.HorizontalChange,
            MinLeftSidebarWidth,
            Math.Min(700, max));

        LeftSidebarColumn.Width =
            new GridLength(_leftSidebarWidth);

        UpdateWorkspaceContentWidth();
        SaveCurrentSettings();
    }

    private void RightSidebarSplitter_DragDelta(
        object sender,
        DragDeltaEventArgs e)
    {
        if (!RightSidebarMenuItem.IsChecked)
        {
            return;
        }

        double max = Math.Max(
            MinRightSidebarWidth,
            WorkspaceGrid.ActualWidth -
            (LeftSidebarMenuItem.IsChecked ? _leftSidebarWidth : 0) -
            720 -
            (LeftSplitterColumn.Width.Value + RightSplitterColumn.Width.Value));

        _rightSidebarWidth = Math.Clamp(
            _rightSidebarWidth - e.HorizontalChange,
            MinRightSidebarWidth,
            Math.Min(800, max));

        RightSidebarColumn.Width =
            new GridLength(_rightSidebarWidth);

        UpdateWorkspaceContentWidth();
        SaveCurrentSettings();
    }

    private void ResetPanelWidths_Click(
        object sender,
        RoutedEventArgs e)
    {
        _leftSidebarWidth = DefaultLeftSidebarWidth;
        _rightSidebarWidth = DefaultRightSidebarWidth;
        ApplySidebarVisibility();
        SaveCurrentSettings();
        StatusText.Text = "Sidebar widths reset.";
    }

    private void TopBarFull_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetTopBarMode("Full");
    }

    private void TopBarCompact_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetTopBarMode("Compact");
    }

    private void TopBarHidden_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetTopBarMode("Hidden");
    }

    private void SetTopBarMode(string mode)
    {
        _topBarMode = mode;
        ApplyTopBarMode();
        SaveCurrentSettings();
    }

    private void ApplyTopBarMode()
    {
        switch (_topBarMode)
        {
            case "Hidden":
                TopBarBorder.Visibility = Visibility.Collapsed;
                break;

            case "Compact":
                TopBarBorder.Visibility = Visibility.Visible;
                TopBarBorder.Padding = new Thickness(16, 6, 16, 6);
                TopBarTitleText.FontSize = 20;
                TopBarSubtitleText.Visibility = Visibility.Collapsed;
                break;

            default:
                _topBarMode = "Full";
                TopBarBorder.Visibility = Visibility.Visible;
                TopBarBorder.Padding = new Thickness(24, 15, 24, 15);
                TopBarTitleText.FontSize = 29;
                TopBarSubtitleText.Visibility = Visibility.Visible;
                break;
        }

        UpdateWorkspaceContentWidth();
    }

}
