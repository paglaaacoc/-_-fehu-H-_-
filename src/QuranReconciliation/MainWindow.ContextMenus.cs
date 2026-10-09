using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace QuranReconciliation;

public sealed partial class MainWindow
{
    private bool _persistentTextMenusAttached;

    private void AttachPersistentTextBoxContextMenus()
    {
        if (_persistentTextMenusAttached)
        {
            return;
        }

        _persistentTextMenusAttached = true;

        TextBox[] boxes =
        [
            SplitAfterTextBox,
            AyahNoteTextBox,
            ContextNoteTextBox,
            HistoryWorkspaceSearchTextBox,
            WorkingSliceTitleTextBox,
            WorkingSliceResearchTextBox,
            WorkingSliceConclusionTextBox
        ];

        foreach (TextBox box in boxes)
        {
            AttachTextBoxContextMenu(box);
        }
    }

    private void AttachTextBoxContextMenu(
        TextBox textBox)
    {
        textBox.ContextFlyout =
            BuildTextBoxEditMenu(textBox);
    }

    private MenuFlyout BuildTextBoxEditMenu(
        TextBox textBox)
    {
        var menu = new MenuFlyout();

        var cut =
            new MenuFlyoutItem
            {
                Text = "Cut"
            };

        var copy =
            new MenuFlyoutItem
            {
                Text = "Copy"
            };

        var paste =
            new MenuFlyoutItem
            {
                Text = "Paste"
            };

        var selectAll =
            new MenuFlyoutItem
            {
                Text = "Select all"
            };

        cut.Click +=
            (_, _) =>
            {
                if (textBox.IsReadOnly ||
                    textBox.SelectionLength <= 0)
                {
                    return;
                }

                CopyTextToClipboard(
                    textBox.SelectedText);

                textBox.SelectedText =
                    string.Empty;
            };

        copy.Click +=
            (_, _) =>
            {
                if (textBox.SelectionLength <= 0)
                {
                    return;
                }

                CopyTextToClipboard(
                    textBox.SelectedText);
            };

        paste.Click +=
            async (_, _) =>
            {
                if (textBox.IsReadOnly)
                {
                    return;
                }

                DataPackageView content =
                    Clipboard.GetContent();

                if (!content.Contains(
                        StandardDataFormats.Text))
                {
                    return;
                }

                string value =
                    await content.GetTextAsync();

                textBox.SelectedText =
                    value;
            };

        selectAll.Click +=
            (_, _) =>
                textBox.SelectAll();

        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(
            new MenuFlyoutSeparator());
        menu.Items.Add(selectAll);

        menu.Opening +=
            (_, _) =>
            {
                bool hasSelection =
                    textBox.SelectionLength > 0;

                cut.IsEnabled =
                    !textBox.IsReadOnly &&
                    hasSelection;

                copy.IsEnabled =
                    hasSelection;

                paste.IsEnabled =
                    !textBox.IsReadOnly;

                selectAll.IsEnabled =
                    !string.IsNullOrEmpty(
                        textBox.Text);
            };

        return menu;
    }

    private MenuFlyout BuildCopyTextContextMenu(
        TextBlock textBlock)
    {
        var menu = new MenuFlyout();

        var copy =
            new MenuFlyoutItem
            {
                Text = "Copy selection"
            };

        copy.Click +=
            (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(
                        textBlock.SelectedText))
                {
                    return;
                }

                CopyTextToClipboard(
                    textBlock.SelectedText);
            };

        menu.Items.Add(copy);

        menu.Opening +=
            (_, _) =>
                copy.IsEnabled =
                    !string.IsNullOrWhiteSpace(
                        textBlock.SelectedText);

        return menu;
    }
}
