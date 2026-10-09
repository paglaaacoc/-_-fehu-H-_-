using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReconciliation.Infrastructure;
using System.Text;

namespace QuranReconciliation;

public partial class App : Application
{
    private Window? _window;
    private PortableInstanceGuard? _instanceGuard;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        WriteDiagnostic("Application constructed.");
    }

    protected override void OnLaunched(
        LaunchActivatedEventArgs args)
    {
        try
        {
            WriteDiagnostic("OnLaunched entered.");

            _instanceGuard =
                PortableInstanceGuard.Acquire();

            var ownerState =
                new OwnerDataBackupService(
                    BuildIdentity.Label);

            OwnerStateRecoveryResult recovery =
                ownerState.RecoverInterruptedOperation();

            var mainWindow =
                new MainWindow();

            _window = mainWindow;

            WriteDiagnostic("MainWindow constructed.");

            mainWindow.Activate();

            WriteDiagnostic("MainWindow activated.");

            if (recovery.Recovered)
            {
                mainWindow.SetStartupSafetyNotice(
                    recovery.Message);
            }
        }
        catch (Exception ex)
        {
            WriteDiagnostic(
                "Fatal launch exception:",
                ex);

            ShowStartupFailure(
                ex);
        }
    }

    private void ShowStartupFailure(
        Exception exception)
    {
        var title =
            new TextBlock
            {
                Text =
                    $"{BuildIdentity.WorkstationName} could not safely start.",
                FontSize = 22,
                FontWeight =
                    Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping =
                    TextWrapping.Wrap
            };

        var body =
            new TextBlock
            {
                Text =
                    exception.Message +
                    "\n\nNo replacement research database was initialized after this failure. " +
                    "See Diagnostics/startup.log and, when present, Diagnostics/owner-state-recovery.log.",
                TextWrapping =
                    TextWrapping.Wrap,
                IsTextSelectionEnabled =
                    true
            };

        var panel =
            new StackPanel
            {
                Spacing = 16,
                Padding =
                    new Thickness(24)
            };

        panel.Children.Add(title);
        panel.Children.Add(body);

        var window =
            new Window
            {
                Title =
                    $"{BuildIdentity.WorkstationName} — Recovery Required",
                Content =
                    new ScrollViewer
                    {
                        Content = panel
                    }
            };

        _window = window;
        window.Activate();
    }

    private void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteDiagnostic(
            "Unhandled UI exception:",
            e.Exception);
    }

    private static void WriteDiagnostic(
        string message,
        Exception? exception = null)
    {
        try
        {
            var directory =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Diagnostics");

            Directory.CreateDirectory(
                directory);

            var path =
                Path.Combine(
                    directory,
                    "startup.log");

            var builder =
                new StringBuilder();

            builder.Append(
                DateTimeOffset.Now.ToString("O"));

            builder.Append("  ");
            builder.AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(
                    exception.ToString());
            }

            File.AppendAllText(
                path,
                builder.ToString());
        }
        catch
        {
            // Diagnostics must never become a launch dependency.
        }
    }
}
