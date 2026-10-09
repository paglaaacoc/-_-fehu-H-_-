using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private async void OpenUserGuide_Click(object sender, RoutedEventArgs e)
    {
        string guidePath = Path.Combine(
            AppContext.BaseDirectory, "Guides", "THTRP-User-Guide.html");

        try
        {
            if (!File.Exists(guidePath))
                throw new FileNotFoundException(
                    "The offline guide was not included with this build.", guidePath);

            // Uses the local packaged HTML; never depends on an internet service.
            // Opening Help does not leave or reset the active research cockpit.
            var guide = await Windows.Storage.StorageFile.GetFileFromPathAsync(guidePath);
            bool opened = await Windows.System.Launcher.LaunchFileAsync(guide);
            if (!opened)
                throw new InvalidOperationException(
                    "Windows could not open the local guide. Check your default .html app.");

            StatusText.Text = "Complete offline User Guide opened; workstation state preserved.";
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "User Guide unavailable",
                Content = new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
    }
}
