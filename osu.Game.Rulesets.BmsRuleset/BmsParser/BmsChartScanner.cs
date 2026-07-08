using System;
using System.Collections.Generic;
using System.Linq;
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
    /// every chart in the set. Takes the longest prefix shared by a majority of
    /// the titles (so a single outlier — a typo like "congolict" among "conflict"
    /// charts, or an unrelated song dumped in the same folder — can't collapse the
    /// result), strips any trailing opening bracket/paren/hyphen that would be
    /// part of a per-difficulty suffix, and trims the result.
    /// </summary>
    /// <example>
    /// "Title [NORMAL]" / "Title [HYPER]" → LCP "Title [" → strip "[" → "Title"
    /// "Title -NORMAL-" / "Title -HYPER-" → LCP "Title -" → strip "-" → "Title"
    /// </example>
    public static string InferCommonSetTitle(string[] rawTitles)
    {
        // Skip empty/whitespace titles — a chart with no #TITLE shouldn't poison the
        // common-prefix inference (an empty title inflates the quorum and collapses
        // the LCP to "").
        var titles = rawTitles
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToArray();

        if (titles.Length == 0) return string.Empty;
        if (titles.Length == 1) return titles[0];

        // A single outlier in the folder collapses a pure longest-common-prefix to
        // a useless stub, so restrict the inference to the majority-shared prefix.
        var core = computeCoreTitles(titles);
        var quorum = Math.Max(2, core.Length / 2 + 1);

        // Longest common prefix over the core.
        var lcp = core[0];
        for (var i = 1; i < core.Length; i++)
        {
            var other = core[i];
            var len = 0;
            while (len < lcp.Length && len < other.Length && lcp[len] == other[len])
                len++;
            lcp = lcp[..len];
            if (lcp.Length == 0) break;
        }

        // Trim back to the last suffix boundary — the point where a per-difficulty
        // suffix starts. A suffix opener may be glued directly to the base title
        // (e.g. "コモリヌ[SP ANOTHER]", "secret:mirage(SP NORMAL)"), so bracket pairs
        // don't require a space before the opener. Symmetric delimiters (- ~ ") still
        // do, otherwise a base title like "Aleph-0" would have its inner '-' stripped.
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

            if (closer == '\0')
                continue;

            var symmetric = c == closer;
            if (symmetric && i != 0 && lcp[i - 1] != ' ')
                continue;

            // Imported or externally normalized titles can lack the closer when a
            // suffix was truncated mid-content. Such opener-plus-content still counts
            // as suffix evidence; only a bare dangling opener with empty content does
            // not. Require a quorum so a single outlier can't manufacture a suffix
            // boundary on its own.
            var withCloser = 0;
            var suffixesDiffer = false;
            string? firstSuffix = null;

            foreach (var t in core)
            {
                if (t.Length <= i)
                    continue;

                var hasCloser = t.IndexOf(closer, i) >= 0;
                var truncatedSuffix = !symmetric && !hasCloser && t.Length > i + 1;
                if (!hasCloser && !truncatedSuffix)
                    continue;

                withCloser++;
                var suffix = t[i..];
                firstSuffix ??= suffix;
                if (suffix != firstSuffix)
                    suffixesDiffer = true;
            }

            if (withCloser >= quorum && suffixesDiffer)
            {
                lcp = lcp[..(i == 0 ? 0 : i)];
                break;
            }
        }

        // Strip trailing opening chars only when a quorum of titles has a
        // matching closer after the LCP (real suffix wrapper, not part of the
        // base title). See the truncation note on the boundary trim above.
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

            var lcpLen = lcp.Length;
            var withCloser = 0;
            foreach (var t in core)
            {
                if (t.Length > lcpLen && t.IndexOf(closer, lcpLen) >= 0)
                    withCloser++;
            }

            if (withCloser < quorum)
                break;

            lcp = lcp[..^1];
        }

        var result = lcp.Trim();
        return result.Length > 0 ? result : titles[0];
    }

    /// <summary>
    /// Returns the subset of <paramref name="rawTitles"/> that share the longest
    /// prefix held by a majority of the titles. This is the set of "real" charts,
    /// excluding outliers (typos, unrelated songs) that would otherwise poison a
    /// longest-common-prefix. Falls back to all titles when no prefix is shared by
    /// a quorum.
    /// </summary>
    private static string[] computeCoreTitles(string[] rawTitles)
    {
        var n = rawTitles.Length;
        // Simple majority, but never less than 2 (so the single-chart and pair cases
        // still require full agreement, preserving the "keep uniform suffix" behaviour).
        var quorum = Math.Max(2, n / 2 + 1);

        var bestPrefix = string.Empty;
        foreach (var title in rawTitles)
        {
            for (var len = title.Length; len > bestPrefix.Length; len--)
            {
                var candidate = title[..len];
                var count = 0;
                foreach (var x in rawTitles)
                {
                    if (x.StartsWith(candidate, StringComparison.Ordinal))
                        count++;
                }

                if (count >= quorum)
                {
                    bestPrefix = candidate;
                    break;
                }
            }
        }

        // No majority-shared prefix → every title is essentially distinct; defer to
        // the caller's LCP/fallback over the full set.
        if (bestPrefix.Length == 0)
            return rawTitles;

        var coreCount = 0;
        foreach (var x in rawTitles)
        {
            if (x.StartsWith(bestPrefix, StringComparison.Ordinal))
                coreCount++;
        }

        if (coreCount < 2)
            return rawTitles;

        var core = new string[coreCount];
        var j = 0;
        foreach (var x in rawTitles)
        {
            if (x.StartsWith(bestPrefix, StringComparison.Ordinal))
                core[j++] = x;
        }

        return core;
    }

    // (opener, closer, symmetric). Symmetric delimiters (- and ~) use the same char
    // to open and close, so LastIndexOf would land on the trailing closer; those
    // skip the final char to find the real opener.
    private static readonly (char opener, char closer, bool symmetric)[] title_suffix_pairs =
    [
        ('[', ']', false),
        ('(', ')', false),
        ('-', '-', true),
        ('~', '~', true),
    ];

    public static string InferTitle(string title)
    {
        if (title.Length == 0) return title;

        var last = title[^1];

        foreach (var (opener, closer, symmetric) in title_suffix_pairs)
        {
            if (last != closer)
                continue;

            var start = symmetric
                ? title.LastIndexOf(opener, title.Length - 2)
                : title.LastIndexOf(opener);

            if (start >= 0)
                return title[..start].TrimEnd();
        }

        return title;
    }

    /// <summary>
    /// Trims whitespace and wrapping difficulty-suffix delimiters — the half-width
    /// set mirrored by <see cref="title_suffix_pairs"/> — from a fragment. Used when
    /// extracting <c>DifficultyName</c> from a #SUBTITLE or from a title suffix.
    /// </summary>
    public static string StripDifficultyDelimiters(string value)
        => value.Trim().Trim('[', ']', '-', '(', ')', '~');

    /// <summary>
    /// Infers a per-chart <c>DifficultyName</c> by splitting <paramref name="rawTitle"/>
    /// relative to the set's common base title (<paramref name="setTitle"/>).
    /// <para>
    /// An outlier whose raw title doesn't share the set base (a typo like
    /// "congolict" among "conflict" charts, or an unrelated song dumped in the
    /// folder) has no suffix to split — the raw title itself is returned so the
    /// chart is still identifiable. A single-chart set has <paramref name="setTitle"/>
    /// equal to <paramref name="rawTitle"/>, so the set-relative split is a no-op
    /// and a per-chart <see cref="InferTitle"/> split is used instead. Returns empty
    /// when nothing can be inferred; the caller is expected to fall back to a
    /// #SUBTITLE or filename.
    /// </para>
    /// </summary>
    public static string InferDifficultyName(string rawTitle, string setTitle)
    {
        if (setTitle.Length > 0)
        {
            if (!rawTitle.StartsWith(setTitle, StringComparison.Ordinal))
                return rawTitle;

            if (rawTitle.Length > setTitle.Length)
            {
                var suffix = StripDifficultyDelimiters(rawTitle[setTitle.Length..]);
                if (!string.IsNullOrEmpty(suffix))
                    return suffix;
            }
        }

        var baseTitle = InferTitle(rawTitle.Trim());
        if (baseTitle.Length < rawTitle.Length)
        {
            var suffix = StripDifficultyDelimiters(rawTitle[baseTitle.Length..]);
            if (!string.IsNullOrEmpty(suffix))
                return suffix;
        }

        return string.Empty;
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
