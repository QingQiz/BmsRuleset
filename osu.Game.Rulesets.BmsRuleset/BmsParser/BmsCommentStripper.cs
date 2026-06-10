using System;
using System.Text;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

/// <summary>
/// Strips BMS comment syntax from lines: //, ;, /* */, with "..." quote protection
/// and \ escape sequences. Maintains /* */ block comment state across lines.
/// </summary>
internal class BmsCommentStripper
{

    // Characters that trigger special handling in removeBlockComments.
    // '*', '*' alone doesn't trigger anything outside a block comment;
    // we only care about '/' (for // and /*), '"' (quote toggle), and '\' (escape).
    private static readonly char[] block_comment_trigger_chars = ['"', '/', '\\'];

    // Characters that trigger special handling in stripLineComments.
    private static readonly char[] line_comment_trigger_chars = ['"', '/', ';', '\\'];
    private bool inBlockComment;

    /// <summary>
    /// Static one-shot: strips all comments from a single line without maintaining
    /// block comment state. Use for independent scanning passes where block comments
    /// spanning lines are not expected.
    /// </summary>
    public static string StripAll(string line)
    {
        var inBlock = false;
        var stripped = removeBlockComments(line, ref inBlock);

        if (stripped == null)
            return string.Empty;

        return stripLineComments(stripped);
    }

    /// <summary>
    /// Processes a raw BMS line and returns the content with all comments removed.
    /// Returns <c>null</c> if the entire line is inside a /* */ block.
    /// Returns <see cref="string.Empty"/> if the line becomes empty after stripping.
    /// </summary>
    public string? ProcessLine(string line)
    {
        if (inBlockComment)
        {
            var idx = indexOfOutsideQuotes(line, "*/");
            if (idx >= 0)
            {
                inBlockComment = false;
                return ProcessLine(line[(idx + 2)..]);
            }

            // Lines inside a block comment become empty (preserving newline structure).
            return string.Empty;
        }

        var stripped = removeBlockComments(line, ref inBlockComment);

        if (stripped == null)
            return null;

        return stripLineComments(stripped);
    }

    /// <summary>
    /// Removes /* */ block comments from a line. If the block is not closed,
    /// sets <paramref name="inBlock"/> to true and returns content before /*.
    /// Maintains quote state inline to avoid O(n²) rescans.
    /// </summary>
    private static string? removeBlockComments(string line, ref bool inBlock)
    {
        // Fast path: if not inside a block comment and the line contains no
        // comment-relevant characters, return it unchanged (zero allocation).
        if (!inBlock && line.AsSpan().IndexOfAny(block_comment_trigger_chars) < 0)
            return line;

        var sb = new StringBuilder(line.Length);
        var inQuote = false;
        var i = 0;
        var runStart = 0;

        while (i < line.Length)
        {
            if (inBlock)
            {
                var closeIdx = indexOfOutsideQuotes(line, "*/", i);

                if (closeIdx < 0)
                    return null;

                inBlock = false;
                i = closeIdx + 2;
                runStart = i;
                continue;
            }

            // Track quote/escape state before checking comment tokens at this position.
            if (line[i] == '\\' && i + 1 < line.Length && isEscapeChar(line[i + 1]))
            {
                i += 2;
                continue;
            }

            if (line[i] == '"')
                inQuote = !inQuote;

            var charsLeft = line.Length - i;

            // Check for // line comments before /* so that /* inside // is not treated as a block comment.
            if (!inQuote && charsLeft >= 2 && line[i] == '/' && line[i + 1] == '/')
            {
                // Flush the accumulated run before the //.
                if (i > runStart)
                    sb.Append(line, runStart, i - runStart);
                runStart = i; // prevent double-flush in final append
                break;
            }

            // Check for /* only outside quotes
            if (!inQuote && charsLeft >= 2 && line[i] == '/' && line[i + 1] == '*')
            {
                // Flush the accumulated run before the /*.
                if (i > runStart)
                    sb.Append(line, runStart, i - runStart);

                var closeIdx = indexOfOutsideQuotes(line, "*/", i + 2);

                if (closeIdx >= 0)
                {
                    i = closeIdx + 2;
                    runStart = i;
                    continue;
                }

                // Block continues to next line
                inBlock = true;
                runStart = i; // prevent double-flush in final append
                break;
            }

            i++;
        }

        // Flush any remaining run after the loop.
        if (i > runStart)
            sb.Append(line, runStart, i - runStart);

        return sb.ToString();
    }

    /// <summary>
    /// Removes // and ; line comments, with "..." quote protection and \ escape
    /// for comment-related characters only (\, ", ;, /). Other \ sequences (e.g.
    /// file paths) pass through unchanged.
    /// </summary>
    private static string stripLineComments(string s)
    {
        // Fast path: if the line contains no comment-relevant characters,
        // return it unchanged (zero allocation).
        if (s.AsSpan().IndexOfAny(line_comment_trigger_chars) < 0)
            return s;

        var sb = new StringBuilder(s.Length);
        var inQuote = false;
        var runStart = 0;
        var i = 0;

        for (; i < s.Length; i++)
        {
            var c = s[i];

            // Only treat \ as escape when followed by a comment-relevant character.
            if (c == '\\' && i + 1 < s.Length && isEscapeChar(s[i + 1]))
            {
                // Flush run before the escape sequence.
                if (i > runStart)
                    sb.Append(s, runStart, i - runStart);
                // Append the escaped character (skip the backslash).
                sb.Append(s[i + 1]);
                i++;
                runStart = i + 1;
                continue;
            }

            // Toggle quote state (only outside escaped sequences)
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            // Line comments (only outside quotes)
            if (!inQuote)
            {
                if (c == ';')
                {
                    if (i > runStart)
                        sb.Append(s, runStart, i - runStart);
                    runStart = i; // prevent double-flush in final append
                    break;
                }

                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    if (i > runStart)
                        sb.Append(s, runStart, i - runStart);
                    runStart = i; // prevent double-flush in final append
                    break;
                }
            }
        }

        // Flush any remaining run.
        if (i > runStart)
            sb.Append(s, runStart, i - runStart);

        return sb.ToString();
    }

    private static bool isEscapeChar(char c) => c is '"' or ';' or '\\' or '/';

    /// <summary>
    /// Finds the first occurrence of <paramref name="substring"/> in <paramref name="line"/>
    /// starting at <paramref name="startIndex"/> that is NOT inside a quoted string.
    /// Tracks quote state inline (O(n)) rather than rescaling from the start per position (O(n²)).
    /// Returns -1 if not found.
    /// </summary>
    private static int indexOfOutsideQuotes(string line, string substring, int startIndex = 0)
    {
        if (string.IsNullOrEmpty(substring) || startIndex > line.Length - substring.Length)
            return -1;

        var inQuote = false;

        for (var i = 0; i <= line.Length - substring.Length; i++)
        {
            // Track quote/escape state at this position before checking the match.
            if (line[i] == '\\' && i + 1 < line.Length && isEscapeChar(line[i + 1]))
            {
                i++;
                continue;
            }

            if (line[i] == '"')
                inQuote = !inQuote;

            if (i < startIndex || inQuote)
                continue;

            var match = true;
            for (var j = 0; j < substring.Length; j++)
            {
                if (line[i + j] != substring[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }
}
