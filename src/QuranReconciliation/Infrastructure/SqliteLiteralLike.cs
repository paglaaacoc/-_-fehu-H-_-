namespace QuranReconciliation.Infrastructure;

// SQLite LIKE treats percent, underscore and escape as operators unless escaped.
// User-supplied research searches are literal substring searches.
internal static class SqliteLiteralLike
{
    internal static string Pattern(string needle) =>
        "%" + needle
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_") + "%";
}
