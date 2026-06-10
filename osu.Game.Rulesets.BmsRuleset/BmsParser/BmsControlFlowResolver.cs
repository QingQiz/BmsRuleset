using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static partial class BmsChartParser
{
    public static Func<int, int> CreateReplayDecisionSelector(IEnumerable<BmsBranchDecision> decisions)
    {
        var queue = new Queue<BmsBranchDecision>(decisions);

        return max =>
        {
            if (!queue.TryDequeue(out var decision))
                throw new InvalidOperationException("BMS replay is missing a random/switch branch decision.");

            if (decision.MaxValue != max)
                throw new InvalidOperationException($"BMS replay branch decision shape mismatch. Expected max {max}, got {decision.MaxValue}.");

            return decision.SelectedValue;
        };
    }

    public static string SerialiseBranchDecisions(IEnumerable<BmsBranchDecision> decisions) =>
        string.Join(",", decisions.Select(d => FormattableString.Invariant($"{d.MaxValue}:{d.SelectedValue}")));

    public static IReadOnlyList<BmsBranchDecision> DeserialiseBranchDecisions(string serialised)
    {
        if (string.IsNullOrWhiteSpace(serialised))
            return [];

        var result = new List<BmsBranchDecision>();

        foreach (var token in serialised.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = token.Split(':', 2, StringSplitOptions.TrimEntries);

            if (parts.Length != 2
                || !tryParseInt(parts[0], out var maxValue)
                || !tryParseInt(parts[1], out var selectedValue))
            {
                throw new FormatException($"Invalid BMS branch decision token: '{token}'.");
            }

            result.Add(new BmsBranchDecision(maxValue, selectedValue));
        }

        return result;
    }

    /// <summary>
    /// Applies a parsed control-flow command to the frame stack.
    /// </summary>
    private static void applyControlCommand(
        string command, string value, List<ControlFrame> frames,
        Func<int, int> randomValueSelector, ICollection<BmsBranchDecision> decisions)
    {
        switch (command)
        {
            case "RANDOM":
            case "RONDAM":
            {
                var parentActive = isActive(frames);
                // Do not consume a random decision for a nested block inside an inactive branch.
                var selectedValue = parentActive && tryParseInt(value, out var randomMax) ? chooseRandomValue(randomMax, randomValueSelector, decisions) : 0;
                frames.Add(new RandomControlFrame(parentActive, selectedValue));
                break;
            }

            case "SETRANDOM":
            {
                var parentActive = isActive(frames);
                frames.Add(new RandomControlFrame(parentActive, parentActive && tryParseInt(value, out var setRandomValue) ? setRandomValue : 0));
                break;
            }

            case "IF":
                if (frames.Count > 0 && frames[^1] is RandomControlFrame randomIf)
                    randomIf.BeginIf(tryParseInt(value, out var ifValue) ? ifValue : 0);
                break;

            case "ELSEIF":
                if (frames.Count > 0 && frames[^1] is RandomControlFrame randomElseIf)
                    randomElseIf.ElseIf(tryParseInt(value, out var elseIfValue) ? elseIfValue : 0);
                break;

            case "ELSE":
                if (frames.Count > 0 && frames[^1] is RandomControlFrame randomElse)
                    randomElse.Else();
                break;

            case "ENDIF":
            case "IFEND":
            case "END":
                if (frames.Count > 0 && frames[^1] is RandomControlFrame randomEndIf)
                    randomEndIf.EndIf();
                break;

            case "ENDRANDOM":
                if (frames.Count > 0 && frames[^1] is RandomControlFrame)
                    frames.RemoveAt(frames.Count - 1);
                break;

            case "SWITCH":
            {
                var parentActive = isActive(frames);
                // #SWITCH uses the same runtime decision source as #RANDOM.
                var selectedValue = parentActive && tryParseInt(value, out var switchMax) ? chooseRandomValue(switchMax, randomValueSelector, decisions) : 0;
                frames.Add(new SwitchControlFrame(parentActive, selectedValue));
                break;
            }

            case "SETSWITCH":
            {
                var parentActive = isActive(frames);
                frames.Add(new SwitchControlFrame(parentActive, parentActive && tryParseInt(value, out var setSwitchValue) ? setSwitchValue : 0));
                break;
            }

            case "CASE":
                if (frames.Count > 0 && frames[^1] is SwitchControlFrame switchCase)
                    switchCase.BeginCase(tryParseInt(value, out var caseValue) ? caseValue : 0);
                break;

            case "DEF":
                if (frames.Count > 0 && frames[^1] is SwitchControlFrame switchDefault)
                    switchDefault.BeginDefault();
                break;

            case "SKIP":
                if (frames.Count > 0 && frames[^1] is SwitchControlFrame switchSkip)
                    switchSkip.Skip();
                break;

            case "ENDSW":
            case "ENDSWITCH":
                if (frames.Count > 0 && frames[^1] is SwitchControlFrame)
                    frames.RemoveAt(frames.Count - 1);
                break;
        }
    }

    private static bool isActive(IReadOnlyList<ControlFrame> frames) => frames.Count == 0 || frames[^1].Active;

    private static bool tryReadControlCommand(string rawLine, out string command, out string value)
    {
        command = string.Empty;
        value = string.Empty;

        var span = rawLine.AsSpan().Trim();

        if (span.IsEmpty || span[0] != '#')
            return false;

        span = span[1..].TrimStart();

        if (span.IsEmpty)
            return false;

        // Fast rejection: channel/data lines start with a digit (e.g. #00111:...).
        // All control commands (#RANDOM, #IF, #SWITCH, etc.) start with a letter.
        // This avoids allocating strings for the 99.99% of lines that are data.
        if (span[0] is >= '0' and <= '9')
            return false;

        var split = span.IndexOfAny(' ', '\t');

        var cmdSpan = split < 0 ? span : span[..split];

        // Case-insensitive span comparison — no allocation.
        if (!isControlCommand(cmdSpan))
            return false;

        command = cmdSpan.ToString().ToUpperInvariant();
        value = split < 0 ? string.Empty : span[(split + 1)..].Trim().ToString();

        return true;
    }

    private static bool isControlCommand(ReadOnlySpan<char> cmd) =>
        cmd.Equals("RANDOM", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("RONDAM", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("SETRANDOM", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("IF", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("ELSEIF", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("ELSE", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("ENDIF", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("IFEND", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("END", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("ENDRANDOM", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("SWITCH", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("SETSWITCH", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("CASE", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("DEF", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("SKIP", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("ENDSW", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("ENDSWITCH", StringComparison.OrdinalIgnoreCase);

    private static bool tryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static int chooseRandomValue(int max, Func<int, int> randomValueSelector, ICollection<BmsBranchDecision> decisions)
    {
        if (max <= 0)
            return 0;

        var value = randomValueSelector(max);

        if (value < 1 || value > max)
            throw new ArgumentOutOfRangeException(nameof(randomValueSelector), value, $"BMS random selector must return a value in the range 1..{max}.");

        decisions.Add(new BmsBranchDecision(max, value));
        return value;
    }

    private static int selectRandomValue(int max)
    {
        if (max <= 0)
            return 0;

        return max == int.MaxValue
            ? RandomNumberGenerator.GetInt32(max) + 1
            : RandomNumberGenerator.GetInt32(1, max + 1);
    }

    private abstract class ControlFrame(bool parentActive)
    {

        public abstract bool Active { get; }

        protected bool ParentActive { get; } = parentActive;
    }

    private sealed class RandomControlFrame(bool parentActive, int value) : ControlFrame(parentActive)
    {

        // Lines inside #RANDOM but outside any #IF/#ELSEIF/#ELSE are unconditional within that random scope.
        public override bool Active => ParentActive && (!inBranch || branchActive);

        private bool branchActive;
        private bool groupMatched;
        private bool inBranch;

        public void BeginIf(int matchValue)
        {
            inBranch = true;
            branchActive = value == matchValue;
            groupMatched = branchActive;
        }

        public void ElseIf(int matchValue)
        {
            if (!inBranch)
            {
                BeginIf(matchValue);
                return;
            }

            branchActive = !groupMatched && value == matchValue;
            groupMatched |= branchActive;
        }

        public void Else()
        {
            inBranch = true;
            branchActive = !groupMatched;
            groupMatched = true;
        }

        public void EndIf()
        {
            inBranch = false;
            branchActive = false;
            groupMatched = false;
        }
    }

    private sealed class SwitchControlFrame(bool parentActive, int value) : ControlFrame(parentActive)
    {

        public override bool Active => ParentActive && caseActive && !exited;

        private bool caseActive;
        private bool exited;
        private bool hasMatchedCase;

        public void BeginCase(int matchValue)
        {
            if (exited)
            {
                caseActive = false;
                return;
            }

            if (caseActive)
                return;

            // Once a case matches, following cases fall through until #SKIP exits the switch.
            caseActive = value == matchValue;
            hasMatchedCase |= caseActive;
        }

        public void BeginDefault()
        {
            if (exited)
            {
                caseActive = false;
                return;
            }

            if (caseActive)
                return;

            caseActive = !hasMatchedCase;
            hasMatchedCase |= caseActive;
        }

        public void Skip()
        {
            if (!caseActive)
                return;

            exited = true;
            caseActive = false;
        }
    }
}
