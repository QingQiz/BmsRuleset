using System;
using System.Text;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

/// <summary>
/// Strips BMS comment syntax from lines: //, ;, /* */, with "..." quote protection
/// and \ escape sequences. Maintains /* */ block comment state across lines.
/// </summary>
internal class BmsCommentStripper
{

    // Characters that trigger special handling.
    // '/' → // or /*, '"' → quote toggle, '\' → escape, ';' → line comment.
    private static readonly char[] trigger_chars = ['"', '/', '\\', ';'];
    private bool inBlockComment;

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

            return string.Empty;
        }

        return stripComments(line, ref inBlockComment);
    }

    /// <summary>
    /// Strips both /* */ block comments and // / ; line comments in a single pass.
    /// For pure line-comment truncation (e.g. "AAAAA//BB"), slices the original string
    /// without any allocation. Only allocates a <see cref="StringBuilder"/> when
    /// content is removed from the middle of the line (block comments, escapes).
    /// </summary>
    private static string stripComments(string line, ref bool inBlock)
    {
        // Fast path: if not inside a block comment and the line contains no
        // comment-relevant characters, return it unchanged (zero allocation).
        if (!inBlock && line.AsSpan().IndexOfAny(trigger_chars) < 0)
            return line;

        StringBuilder? sb = null; // lazy — only allocated when middle-of-line removal happens
        var inQuote = false;
        var i = 0;
        var runStart = 0;

        while (i < line.Length)
        {
            var c = line[i];

            // Escape sequence — strip backslash, keep the escaped character.
            // This modifies the output, so we need the StringBuilder.
            if (c == '\\' && i + 1 < line.Length && isEscapeChar(line[i + 1]))
            {
                sb ??= new StringBuilder(line.Length);
                if (i > runStart) sb.Append(line, runStart, i - runStart);
                sb.Append(line[i + 1]);
                i += 2;
                runStart = i;
                continue;
            }

            // Quote toggle.
            if (c == '"')
                inQuote = !inQuote;

            var charsLeft = line.Length - i;

            if (!inQuote)
            {
                // Semicolon line comment — truncate at this position.
                if (c == ';')
                {
                    if (sb != null && i > runStart) sb.Append(line, runStart, i - runStart);
                    return sb != null ? finalize(sb) : slice(line, runStart, i);
                }

                if (c == '/' && charsLeft >= 2)
                {
                    // // line comment — truncate at this position.
                    if (line[i + 1] == '/')
                    {
                        if (sb != null && i > runStart) sb.Append(line, runStart, i - runStart);
                        return sb != null ? finalize(sb) : slice(line, runStart, i);
                    }

                    // /* block comment — middle-of-line removal needs StringBuilder.
                    if (line[i + 1] == '*')
                    {
                        sb ??= new StringBuilder(line.Length);
                        if (i > runStart) sb.Append(line, runStart, i - runStart);

                        var closeIdx = indexOfOutsideQuotes(line, "*/", i + 2);
                        if (closeIdx >= 0)
                        {
                            i = closeIdx + 2;
                            runStart = i;
                            continue;
                        }

                        inBlock = true;
                        runStart = i;
                        break;
                    }
                }
            }

            i++;
        }

        // End of line reached — flush remaining run.
        if (sb != null)
        {
            if (i > runStart) sb.Append(line, runStart, i - runStart);
            return finalize(sb);
        }

        return slice(line, runStart, i);
    }

    /// <summary>Return a slice of the original string when no middle-of-line modification occurred.</summary>
    private static string slice(string line, int start, int end) =>
        start == 0 && end == line.Length ? line : end <= start ? string.Empty : line[start..end];

    /// <summary>Finalize the StringBuilder result.</summary>
    private static string finalize(StringBuilder sb)
    {
        var result = sb.ToString();
        return result.Length == 0 ? string.Empty : result;
    }

    private static bool isEscapeChar(char c) => c is '"' or ';' or '\\' or '/';

    /// <summary>
    /// Finds the first occurrence of <paramref name="substring"/> in <paramref name="line"/>
    /// starting at <paramref name="startIndex"/> that is NOT inside a quoted string.
    /// Tracks quote state inline (O(n)) rather than rescanning from the start per position (O(n²)).
    /// Returns -1 if not found.
    /// </summary>
    private static int indexOfOutsideQuotes(string line, string substring, int startIndex = 0)
    {
        if (string.IsNullOrEmpty(substring) || startIndex > line.Length - substring.Length)
            return -1;

        var inQuote = false;

        for (var i = 0; i <= line.Length - substring.Length; i++)
        {
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
