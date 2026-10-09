namespace QuranReconciliation.Infrastructure;

/// <summary>
/// Pure same-launch re-entry policy. Ordinary workspace navigation must not
/// reload an active Working Slice, which could discard in-memory unsaved edits.
/// A first open or deliberate target navigation may refresh from the repository.
/// </summary>
internal static class WorkingSliceNavigation
{
    internal static bool ShouldRefresh(bool initialized, long? preferredId) =>
        !initialized || preferredId.HasValue;
}
