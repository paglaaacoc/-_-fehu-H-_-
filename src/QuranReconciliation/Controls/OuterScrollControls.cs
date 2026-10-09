using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace QuranReconciliation.Controls;

/// <summary>
/// Controls nested inside a panel-owned ScrollViewer/ListView. Their native
/// pointer-wheel class handler is deliberately suppressed so the routed wheel
/// event reaches the visible panel root, which owns scrolling.
/// </summary>
public sealed class OuterScrollTextBox : TextBox
{
    protected override void OnPointerWheelChanged(
        PointerRoutedEventArgs e)
    {
        // Do not call base: the research sidebar owns mouse-wheel scrolling.
    }
}

public sealed class OuterScrollListView : ListView
{
    protected override void OnPointerWheelChanged(
        PointerRoutedEventArgs e)
    {
        // Do not call base: the visible panel root owns mouse-wheel scrolling.
    }
}

public sealed class OuterScrollComboBox : ComboBox
{
    protected override void OnPointerWheelChanged(
        PointerRoutedEventArgs e)
    {
        if (IsDropDownOpen)
        {
            base.OnPointerWheelChanged(e);
            return;
        }

        // A closed selector must not steal the research-sidebar wheel.
    }
}
