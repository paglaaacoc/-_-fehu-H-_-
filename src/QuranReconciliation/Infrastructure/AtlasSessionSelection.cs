namespace QuranReconciliation.Infrastructure;

/// <summary>
/// Session-only selector restoration for read-only Context Atlas corpora.
/// Return indices in the NEW list, not the positions of obsolete ComboBox items.
/// Pure logic; usable from the native UI and from the Windows regression probe.
/// </summary>
internal static class AtlasSessionSelection
{
    internal static (int Left, int Right, bool RemovedSelectedCorpus) Restore(
        IReadOnlyList<string> availableIds, string currentId,
        string? previousLeftId, string? previousRightId)
    {
        if (availableIds.Count == 0)
            throw new ArgumentException("No available corpora.", nameof(availableIds));

        int Find(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return -1;
            for (int i = 0; i < availableIds.Count; i++)
                if (string.Equals(availableIds[i], id, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        int left = Find(previousLeftId);
        if (left < 0)
            left = Math.Max(0, Find(currentId));

        int right = Find(previousRightId);
        if (right < 0)
            right = Enumerable.Range(0, availableIds.Count)
                .FirstOrDefault(i => i != left, left);

        bool missing =
            (previousLeftId is not null && Find(previousLeftId) < 0) ||
            (previousRightId is not null && Find(previousRightId) < 0);

        // An intentional A==B selection is retained when both IDs still exist;
        // the Compare view itself displays its existing same-corpus message.
        return (left, right, missing);
    }
}
