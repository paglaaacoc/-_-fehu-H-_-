using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private void InitializeWorkspaceShortcuts()
    {
        var research = new KeyboardAccelerator
        {
            Key = VirtualKey.Number1,
            Modifiers = VirtualKeyModifiers.Control
        };
        research.Invoked +=
            (_, args) =>
            {
                ShowResearchWorkspace();
                StatusText.Text =
                    $"{_chapters[_currentSurah - 1].DisplayName} · research workspace";
                args.Handled = true;
            };

        var slices = new KeyboardAccelerator
        {
            Key = VirtualKey.Number2,
            Modifiers = VirtualKeyModifiers.Control
        };
        slices.Invoked +=
            (_, args) =>
            {
                OpenWorkingSlices();
                args.Handled = true;
            };

        var history = new KeyboardAccelerator
        {
            Key = VirtualKey.Number3,
            Modifiers = VirtualKeyModifiers.Control
        };
        history.Invoked +=
            (_, args) =>
            {
                OpenHistory_Click(
                    HistoryNavButton,
                    new RoutedEventArgs());
                args.Handled = true;
            };

        var find = new KeyboardAccelerator
        {
            Key = VirtualKey.F,
            Modifiers = VirtualKeyModifiers.Control
        };
        find.Invoked +=
            (_, args) =>
            {
                if (HistoryWorkspaceGrid.Visibility ==
                    Visibility.Visible)
                {
                    HistoryWorkspaceSearchTextBox.Focus(
                        FocusState.Programmatic);
                }
                else if (WorkingSliceWorkspaceGrid.Visibility ==
                         Visibility.Visible)
                {
                    WorkingSliceSearchTextBox.Focus(
                        FocusState.Programmatic);
                }
                else
                {
                    StatusText.Text =
                        "Ctrl+F is available in History and Working Slices.";
                }

                args.Handled = true;
            };

        var save = new KeyboardAccelerator
        {
            Key = VirtualKey.S,
            Modifiers = VirtualKeyModifiers.Control
        };
        save.Invoked +=
            (_, args) =>
            {
                if (!_ownerStateOperationActive &&
                    WorkingSliceWorkspaceGrid.Visibility ==
                        Visibility.Visible &&
                    _workingSliceDirty)
                {
                    SaveActiveWorkingSlice();
                }

                args.Handled = true;
            };

        RootGrid.KeyboardAccelerators.Add(research);
        RootGrid.KeyboardAccelerators.Add(slices);
        RootGrid.KeyboardAccelerators.Add(history);
        RootGrid.KeyboardAccelerators.Add(find);
        RootGrid.KeyboardAccelerators.Add(save);
    }

    private void UpdateWorkspaceNavigationState()
    {
        if (ResearchNavButton is null ||
            WorkingSlicesNavButton is null ||
            HistoryNavButton is null ||
            ContextAtlasNavButton is null)
        {
            return;
        }

        bool researchActive =
            WorkspaceGrid.Visibility ==
                Visibility.Visible;

        bool slicesActive =
            WorkingSliceWorkspaceGrid.Visibility ==
                Visibility.Visible;

        bool historyActive =
            HistoryWorkspaceGrid.Visibility ==
                Visibility.Visible;

        bool atlasActive =
            ContextAtlasWorkspaceGrid.Visibility ==
                Visibility.Visible;

        ApplyWorkspaceNavState(
            ResearchNavButton,
            researchActive);

        ApplyWorkspaceNavState(
            WorkingSlicesNavButton,
            slicesActive);

        ApplyWorkspaceNavState(
            HistoryNavButton,
            historyActive);

        ApplyWorkspaceNavState(
            ContextAtlasNavButton,
            atlasActive);
    }

    private void ApplyWorkspaceNavState(
        Microsoft.UI.Xaml.Controls.Button button,
        bool active)
    {
        button.FontWeight =
            active
                ? FontWeights.SemiBold
                : FontWeights.Normal;

        button.Opacity =
            active
                ? 1.0
                : 0.68;

        button.BorderThickness =
            active
                ? new Thickness(1)
                : new Thickness(0);

        button.BorderBrush =
            active
                ? Brush("AccentBrush")
                : Brush("BorderBrush");
    }
}
