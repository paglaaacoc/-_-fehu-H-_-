using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace QuranReconciliation.Infrastructure;

/// <summary>
/// Deterministic matching-only text copies for the disposable search index.
/// NEVER use normalized text to render or rewrite the canonical Qur'an.
/// </summary>
internal static class CorpusSearchTextNormalizer
{
    internal const string Version = "1";

    internal static string PlainTranslationText(string raw) =>
        WebUtility.HtmlDecode(
            Regex.Replace(raw ?? string.Empty, @"<[^>]*>", " ",
                RegexOptions.Singleline | RegexOptions.CultureInvariant));

    internal static string Normalize(string? input, string language)
    {
        bool arabic = string.Equals(language, "arabic",
            StringComparison.OrdinalIgnoreCase);
        string text = (input ?? string.Empty).Normalize(
            arabic ? NormalizationForm.FormKD : NormalizationForm.FormC);
        var result = new StringBuilder(text.Length);
        bool separated = true;
        foreach (char original in text)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(original);
            if (arabic &&
                (category is UnicodeCategory.NonSpacingMark or
                    UnicodeCategory.SpacingCombiningMark or
                    UnicodeCategory.EnclosingMark ||
                 original == '\u0640' || original == '\u0670' ||
                 (original >= '\u06D6' && original <= '\u06ED')))
            {
                continue;
            }

            char c = arabic
                ? original switch
                {
                    'ٱ' or 'أ' or 'إ' or 'آ' => 'ا',
                    'ى' => 'ي',
                    // Preserve hamza and taa marbuta; unlike speech-oriented
                    // matching they are significant to exact research search.
                    _ => original
                }
                : char.ToLowerInvariant(original);

            bool searchable = char.IsLetterOrDigit(c) ||
                (!arabic && category is UnicodeCategory.NonSpacingMark or
                    UnicodeCategory.SpacingCombiningMark or
                    UnicodeCategory.EnclosingMark);
            if (searchable)
            {
                result.Append(c);
                separated = false;
            }
            else if (!separated)
            {
                result.Append(' ');
                separated = true;
            }
        }

        return result.ToString().Trim();
    }
}
