using System;
using System.Text;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static partial class BmsChartParser
{
    private static readonly Encoding shift_jis_encoding =
        CodePagesEncodingProvider.Instance.GetEncoding(932)
        ?? throw new InvalidOperationException("Shift-JIS encoding is not available.");

    public static string[] ReadAllLines(byte[] bytes) => decodeText(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Compute the common beatmap-set title from the raw <c>#TITLE</c> values of
    /// every chart in the set. Uses the longest common prefix, strips any trailing
    /// opening bracket/paren/hyphen that would be part of a per-difficulty suffix,
    /// and trims the result.
    /// </summary>
    /// <example>
    /// "Title [NORMAL]" / "Title [HYPER]" → LCP "Title [" → strip "[" → "Title"
    /// "Title -NORMAL-" / "Title -HYPER-" → LCP "Title -" → strip "-" → "Title"
    /// </example>
    public static string InferCommonSetTitle(string[] rawTitles)
    {
        if (rawTitles.Length == 0) return string.Empty;
        if (rawTitles.Length == 1) return rawTitles[0].Trim();

        // Longest common prefix.
        var lcp = rawTitles[0];
        for (var i = 1; i < rawTitles.Length; i++)
        {
            var other = rawTitles[i];
            var len = 0;
            while (len < lcp.Length && len < other.Length && lcp[len] == other[len])
                len++;
            lcp = lcp[..len];
            if (lcp.Length == 0) break;
        }

        // Trim back to the last suffix boundary — the point where a
        // per-difficulty suffix starts (space + opener). Only trim if every
        // title has a matching closer after that position (real suffix).
        for (var i = lcp.Length - 1; i >= 0; i--)
        {
            var c = lcp[i];
            var closer = c switch
            {
                '[' => ']',
                '(' => ')',
                '（' => '）',
                '-' => '-',
                '~' => '~',
                '<' => '>',
                '"' => '"',
                _ => '\0',
            };

            if (closer != '\0' && (i == 0 || lcp[i - 1] == ' '))
            {
                // Only trim if the suffix content DIFFERS across titles
                // and every title has the matching closer after this position.
                var allHaveCloser = true;
                var suffixesDiffer = false;
                string? firstSuffix = null;

                foreach (var t in rawTitles)
                {
                    if (t.Length <= i || t.IndexOf(closer, i) < 0)
                    {
                        allHaveCloser = false;
                        break;
                    }

                    var suffix = t[i..];
                    firstSuffix ??= suffix;
                    if (suffix != firstSuffix)
                        suffixesDiffer = true;
                }

                if (allHaveCloser && suffixesDiffer)
                {
                    lcp = lcp[..(i == 0 ? 0 : i)];
                    break;
                }
            }
        }

        // Strip trailing opening chars only when every title has a matching
        // closer after the LCP (real suffix wrapper, not part of base title).
        while (lcp.Length > 0)
        {
            var last = lcp[^1];
            var closer = last switch
            {
                '[' => ']',
                '(' => ')',
                '（' => '）',
                '-' => '-',
                '~' => '~',
                _ => '\0',
            };

            if (closer == '\0')
                break;

            var allHaveCloser = true;
            var lcpLen = lcp.Length;
            foreach (var t in rawTitles)
            {
                if (t.Length <= lcpLen || t.IndexOf(closer, lcpLen) < 0)
                {
                    allHaveCloser = false;
                    break;
                }
            }

            if (!allHaveCloser)
                break;

            lcp = lcp[..^1];
        }

        var result = lcp.Trim();
        return result.Length > 0 ? result : rawTitles[0].Trim();
    }

    public static string InferTitle(string title)
    {
        char[] p = ['[', '(', '-'];
        char[] q = [']', ')', '-'];

        for (var i = 0; i < p.Length; i++)
        {
            var start = title.LastIndexOf(p[i]);
            if (title[^1] == q[i])
            {
                return title[..start].TrimEnd();
            }
        }

        return title;
    }

    private static string decodeText(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xef && content[1] == 0xbb && content[2] == 0xbf)
            return Encoding.UTF8.GetString(content);

        var utf8 = new UTF8Encoding(false, true);

        try
        {
            return utf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return shift_jis_encoding.GetString(content);
        }
    }
}
