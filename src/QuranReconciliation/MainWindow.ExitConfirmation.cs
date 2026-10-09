using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private bool _exitConfirmationOpen;
    private bool _exitAlreadyApproved;

    private void ConfirmExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _ownerStateOperationActive)
            return;

        _settings.ConfirmBeforeExit = ConfirmExitMenuItem.IsChecked;
        SaveCurrentSettings();
        StatusText.Text = _settings.ConfirmBeforeExit
            ? "Exit confirmation enabled. A normal close will ask first."
            : "Exit confirmation disabled. A normal close will exit without an extra question.";
    }

    private async Task ConfirmNormalExitAsync()
    {
        if (_exitConfirmationOpen || _exitAlreadyApproved)
            return;

        _exitConfirmationOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Close The Holy Quran TRP?",
                Content = new TextBlock
                {
                    Text = HasUnsavedResearchDrafts
                        ? "You have unsaved research notes or Working Slice edits. Closing will discard those unsaved edits. Choose Keep working to save them first."
                        : "Exit the application? Your session-only Atlas comparison, Pins and workspace positions will end when the application closes.",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Exit application",
                CloseButtonText = "Keep working",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                StatusText.Text = "Exit cancelled. Your working session is unchanged.";
                return;
            }

            if (_ownerStateOperationActive)
            {
                StatusText.Text = "Exit blocked during owner-state safety operation.";
                return;
            }

            // Close() raises AppWindow.Closing again. This narrowly scoped
            // bypass prevents a confirmation loop and keeps Closed/settings
            // persistence and the existing protected owner-state gate intact.
            _exitAlreadyApproved = true;
            Close();
        }
        catch (Exception ex)
        {
            // Failure to display a safety prompt must fail closed, never
            // silently discard unsaved research.
            StatusText.Text = $"Close confirmation unavailable; no exit performed: {ex.Message}";
        }
        finally
        {
            _exitConfirmationOpen = false;
        }
    }
}
