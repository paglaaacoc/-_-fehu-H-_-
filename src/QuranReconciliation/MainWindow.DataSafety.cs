using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuranReconciliation.Infrastructure;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private readonly OwnerDataBackupService _ownerDataSafety =
        new(BuildIdentity.Label);

    private bool _suppressSettingsSaveOnClose;
    private bool _ownerStateOperationActive;

    internal void SetStartupSafetyNotice(
        string message)
    {
        StatusText.Text =
            "Owner-state recovery completed · " +
            message;
    }

    private bool HasUnsavedResearchDrafts =>
        HasUnsavedNoteDrafts ||
        HasUnsavedWorkingSliceDraft;

    private async Task<bool>
        RequireSavedDraftsForBackupAsync()
    {
        if (!HasUnsavedResearchDrafts)
        {
            return true;
        }

        await ShowDataSafetyMessageAsync(
            "Save research edits first",
            "A note or Working Slice contains unsaved text. Verified backups contain committed owner state only. Save or deliberately discard those edits before creating the backup.");

        return false;
    }

    private async Task<bool>
        ResolveDirtyDraftsBeforeDestructiveOperationAsync(
            string operationName)
    {
        if (!HasUnsavedResearchDrafts)
        {
            return true;
        }

        var explanation =
            new TextBlock
            {
                Text =
                    $"Unsaved note or Working Slice edits are not yet in SQLite and therefore are not protected by verified backups.\n\n" +
                    $"Choose “Discard drafts & continue” only if you intentionally want those unsaved edits excluded from this {operationName.ToLowerInvariant()}. Otherwise cancel, save them, and run {operationName.ToLowerInvariant()} again.",
                TextWrapping =
                    TextWrapping.Wrap
            };

        var dialog =
            new ContentDialog
            {
                XamlRoot =
                    RootGrid.XamlRoot,
                Title =
                    "Unsaved research changes",
                Content =
                    explanation,
                PrimaryButtonText =
                    "Discard drafts & continue",
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
            StatusText.Text =
                $"{operationName} cancelled so unsaved research edits can be saved.";
            return false;
        }

        DiscardNoteDraftsForSafety();
        DiscardWorkingSliceDraftForSafety();

        StatusText.Text =
            "Unsaved drafts deliberately excluded from the destructive owner-state operation.";

        return true;
    }

    private void SetOwnerStateOperationBusy(
        bool busy,
        string message)
    {
        _ownerStateOperationActive = busy;

        if (RootGrid is not null)
        {
            RootGrid.IsHitTestVisible =
                !busy;
        }

        StatusText.Text =
            message;
    }

    private async void CreateVerifiedBackup_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ownerStateOperationActive)
        {
            return;
        }

        if (!await RequireSavedDraftsForBackupAsync())
        {
            return;
        }

        try
        {
            SaveCurrentSettings(
                throwOnFailure: true);

            SetOwnerStateOperationBusy(
                true,
                "Creating and independently verifying owner-data backup…");

            VerifiedOwnerBackup backup;

            try
            {
                backup =
                    await Task.Run(() =>
                        _ownerDataSafety.CreateVerifiedBackup(
                            OwnerDataBackupService.ManualReason));
            }
            finally
            {
                SetOwnerStateOperationBusy(
                    false,
                    "Verified backup operation finished.");
            }

            StatusText.Text =
                $"Verified backup created · {Path.GetFileName(backup.Path)}";

            await ShowDataSafetyMessageAsync(
                "Verified backup created",
                $"Backup: {Path.GetFileName(backup.Path)}\n\n" +
                $"Build: {backup.Manifest.AppBuild}\n" +
                $"Source: {backup.Manifest.SourceRevision ?? "not recorded"}\n\n" +
                "It contains a SQLite-consistent research snapshot, current settings, file digests and a manifest. The finished archive was reopened and independently verified before this message was shown.");
        }
        catch (Exception ex)
        {
            SetOwnerStateOperationBusy(
                false,
                $"Backup safely failed: {ex.Message}");

            await ShowDataSafetyMessageAsync(
                "Backup failed",
                "No destructive owner-state operation was performed.\n\n" +
                ex.Message);
        }
    }

    private async void RestoreVerifiedBackup_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ownerStateOperationActive)
        {
            return;
        }

        try
        {
            IReadOnlyList<BackupSummary> backups =
                await Task.Run(() =>
                    _ownerDataSafety.ListBackups());

            if (backups.Count == 0)
            {
                await ShowDataSafetyMessageAsync(
                    "No backups found",
                    "No readable backup archives are available in the portable Backups folder.");
                return;
            }

            var selector =
                new ComboBox
                {
                    Header = "Backup",
                    HorizontalAlignment =
                        HorizontalAlignment.Stretch
                };

            foreach (BackupSummary backup
                     in backups)
            {
                selector.Items.Add(
                    new ComboBoxItem
                    {
                        Content =
                            backup.DisplayLabel,
                        Tag =
                            backup.Path
                    });
            }

            selector.SelectedIndex = 0;

            var explanation =
                new TextBlock
                {
                    Text =
                        "The selected archive will be fully verified before restore. A verified PreRestore backup of the current committed owner state is created first. During replacement, research writes are disabled and a durable recovery record is maintained. The app closes after success.",
                    TextWrapping =
                        TextWrapping.Wrap
                };

            var panel =
                new StackPanel
                {
                    Spacing = 12
                };

            panel.Children.Add(explanation);
            panel.Children.Add(selector);

            var dialog =
                new ContentDialog
                {
                    XamlRoot =
                        RootGrid.XamlRoot,
                    Title =
                        "Restore verified backup",
                    Content =
                        panel,
                    PrimaryButtonText =
                        "Verify & restore",
                    CloseButtonText =
                        "Cancel",
                    DefaultButton =
                        ContentDialogButton.Close
                };

            ContentDialogResult result =
                await dialog.ShowAsync();

            if (result !=
                    ContentDialogResult.Primary ||
                selector.SelectedItem
                    is not ComboBoxItem selected ||
                selected.Tag?.ToString()
                    is not string backupPath)
            {
                return;
            }

            if (!await ResolveDirtyDraftsBeforeDestructiveOperationAsync(
                    "Restore"))
            {
                return;
            }

            SaveCurrentSettings(
                throwOnFailure: true);

            _suppressSettingsSaveOnClose =
                true;

            SetOwnerStateOperationBusy(
                true,
                "Restore in progress · verifying target, creating PreRestore, and protecting the live owner state…");

            RestoreOwnerStateResult restored;

            try
            {
                restored =
                    await Task.Run(() =>
                        _ownerDataSafety.RestoreOwnerState(
                            backupPath));
            }
            finally
            {
                SetOwnerStateOperationBusy(
                    false,
                    "Restore operation finished.");
            }

            await ShowDataSafetyMessageAsync(
                "Restore verified complete",
                $"Restored: {Path.GetFileName(restored.RestoredBackup.Path)}\n\n" +
                $"Automatic rollback backup: {Path.GetFileName(restored.PreRestoreBackup.Path)}\n\n" +
                "The installed owner state was independently verified and the durable operation record was cleared. The app will now close for a clean relaunch.");

            Close();
        }
        catch (OwnerStateRecoveryRequiredException ex)
        {
            _suppressSettingsSaveOnClose =
                true;

            SetOwnerStateOperationBusy(
                false,
                "Recovery required · normal research use is blocked.");

            await ShowDataSafetyMessageAsync(
                "Recovery required",
                ex.Message +
                "\n\nThe app will close. On the next launch R11 will attempt recovery before normal database initialization.");

            Close();
        }
        catch (Exception ex)
        {
            _suppressSettingsSaveOnClose =
                false;

            SetOwnerStateOperationBusy(
                false,
                $"Restore safely failed: {ex.Message}");

            await ShowDataSafetyMessageAsync(
                "Restore safely failed",
                "The requested restore did not complete. The exact verified pre-operation owner state was retained or restored before this failure was returned.\n\n" +
                ex.Message);
        }
    }

    private async void ResetResearchData_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_ownerStateOperationActive)
        {
            return;
        }

        if (!await ResolveDirtyDraftsBeforeDestructiveOperationAsync(
                "Reset"))
        {
            return;
        }

        VerifiedOwnerBackup safetyBackup;

        try
        {
            SaveCurrentSettings(
                throwOnFailure: true);

            SetOwnerStateOperationBusy(
                true,
                "Creating the mandatory verified PreReset backup…");

            try
            {
                safetyBackup =
                    await Task.Run(() =>
                        _ownerDataSafety.CreateVerifiedBackup(
                            OwnerDataBackupService.PreResetReason));
            }
            finally
            {
                SetOwnerStateOperationBusy(
                    false,
                    "PreReset safety backup finished.");
            }
        }
        catch (Exception ex)
        {
            SetOwnerStateOperationBusy(
                false,
                $"Reset blocked because its safety backup failed: {ex.Message}");

            await ShowDataSafetyMessageAsync(
                "Reset blocked",
                "No research data was changed because a fresh verified backup could not be created.\n\n" +
                ex.Message);
            return;
        }

        var confirmation =
            new TextBox
            {
                Header =
                    "Type RESET to authorize destructive research-data reset",
                PlaceholderText =
                    "RESET"
            };

        var warning =
            new TextBlock
            {
                Text =
                    "This deletes committed mutable research data: Context Maps, notes/revisions, bookmarks, Working Slices and their History. App preferences and immutable corpora are preserved.\n\n" +
                    $"Fresh verified backup: {Path.GetFileName(safetyBackup.Path)}\n\n" +
                    "Immediately before reset, the app verifies that archive again and proves it still matches the live research database and settings. During replacement, writes are disabled and a durable recovery record protects interruption recovery.",
                TextWrapping =
                    TextWrapping.Wrap
            };

        var panel =
            new StackPanel
                {
                    Spacing = 12
                };

        panel.Children.Add(warning);
        panel.Children.Add(confirmation);

        var dialog =
            new ContentDialog
            {
                XamlRoot =
                    RootGrid.XamlRoot,
                Title =
                    "Reset research data",
                Content =
                    panel,
                PrimaryButtonText =
                    "Reset research data",
                CloseButtonText =
                    "Cancel",
                DefaultButton =
                    ContentDialogButton.Close,
                IsPrimaryButtonEnabled =
                    false
            };

        confirmation.TextChanged +=
            (_, _) =>
            {
                dialog.IsPrimaryButtonEnabled =
                    string.Equals(
                        confirmation.Text.Trim(),
                        "RESET",
                        StringComparison.Ordinal);
            };

        ContentDialogResult result =
            await dialog.ShowAsync();

        if (result !=
            ContentDialogResult.Primary)
        {
            StatusText.Text =
                "Reset cancelled. The verified safety backup was retained.";
            return;
        }

        try
        {
            _suppressSettingsSaveOnClose =
                true;

            SetOwnerStateOperationBusy(
                true,
                "Reset in progress · re-verifying the exact PreReset backup and live owner state…");

            try
            {
                await Task.Run(() =>
                    _ownerDataSafety.ResetResearchData(
                        safetyBackup));
            }
            finally
            {
                SetOwnerStateOperationBusy(
                    false,
                    "Reset operation finished.");
            }

            await ShowDataSafetyMessageAsync(
                "Research data reset verified complete",
                $"Reset completed only after re-verifying: {Path.GetFileName(safetyBackup.Path)}\n\n" +
                "Preferences and immutable corpora were preserved. The fresh research database was verified, the recovery record was cleared, and the app will now close.");

            Close();
        }
        catch (OwnerStateRecoveryRequiredException ex)
        {
            _suppressSettingsSaveOnClose =
                true;

            SetOwnerStateOperationBusy(
                false,
                "Recovery required · normal research use is blocked.");

            await ShowDataSafetyMessageAsync(
                "Recovery required",
                ex.Message +
                "\n\nThe app will close. On the next launch R11 will attempt recovery before normal database initialization.");

            Close();
        }
        catch (Exception ex)
        {
            _suppressSettingsSaveOnClose =
                false;

            SetOwnerStateOperationBusy(
                false,
                $"Reset safely failed: {ex.Message}");

            await ShowDataSafetyMessageAsync(
                "Reset safely failed",
                "The destructive reset did not complete. The exact verified pre-reset owner state was retained or restored before this failure was returned.\n\n" +
                ex.Message);
        }
    }

    private async Task ShowDataSafetyMessageAsync(
        string title,
        string message)
    {
        var dialog =
            new ContentDialog
            {
                XamlRoot =
                    RootGrid.XamlRoot,
                Title =
                    title,
                Content =
                    new TextBlock
                    {
                        Text =
                            message,
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
