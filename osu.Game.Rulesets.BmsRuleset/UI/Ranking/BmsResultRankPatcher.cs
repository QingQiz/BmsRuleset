using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.Leaderboards;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Select;
using osu.Game.Screens.Ranking.Expanded.Accuracy;

namespace osu.Game.Rulesets.BmsRuleset.UI.Ranking;

internal static class BmsResultRankPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.ResultRank";
    private static readonly object install_lock = new();

    internal static void InstallOnce()
    {
        lock (install_lock)
        {
            var target = AccessTools.Method(typeof(RankText), "load");
            var postfixMethod = AccessTools.Method(typeof(BmsResultRankPatcher), nameof(postfix));
            var drawableLoad = AccessTools.Method(typeof(Drawable), "LoadComplete");
            var drawablePostfixMethod = AccessTools.Method(typeof(BmsResultRankPatcher), nameof(drawablePostfix));
            var leaderboardLoad = AccessTools.Method(typeof(BeatmapLeaderboardScore), "load");
            var leaderboardPostfixMethod = AccessTools.Method(typeof(BmsResultRankPatcher), nameof(leaderboardPostfix));

            if (target == null || postfixMethod == null || drawableLoad == null || drawablePostfixMethod == null || leaderboardLoad == null || leaderboardPostfixMethod == null)
            {
                BmsLogger.Log("BMS ResultRankPatcher: RankText.load() is unavailable.", level: LogLevel.Error);
                return;
            }

            try
            {
                new Harmony(harmony_id).Patch(target, postfix: new HarmonyMethod(postfixMethod));
                new Harmony(harmony_id).Patch(drawableLoad, postfix: new HarmonyMethod(drawablePostfixMethod));
                new Harmony(harmony_id).Patch(leaderboardLoad, postfix: new HarmonyMethod(leaderboardPostfixMethod));
            }
            catch (Exception ex)
            {
                BmsLogger.Error(ex, "BMS ResultRankPatcher: BMS rank text will use osu!'s default lettering.");
            }
        }
    }

    // ReSharper disable InconsistentNaming
    private static void postfix(RankText __instance, GlowingSpriteText ___rankText)
    {
        // RankText is loaded before AccuracyCircle is always attached to its parent. Delay the
        // lookup until the next update so the score context is available in the parent chain.
        __instance.Delay(0).Schedule(() => applyRankText(__instance, ___rankText));
    }

    private static void applyRankText(RankText rankText, GlowingSpriteText text)
    {
        var accuracyCircle = findParent<AccuracyCircle>(rankText);
        var scoreField = typeof(AccuracyCircle).GetField("score", BindingFlags.Instance | BindingFlags.NonPublic);

        if (accuracyCircle == null || scoreField?.GetValue(accuracyCircle) is not ScoreInfo score || score.Ruleset.ShortName != Constant.SHORT_NAME)
            return;

        text.Text = BmsRankDisplay.GetRankLetter(score.Rank);
        text.Spacing = new osuTK.Vector2(BmsRankDisplay.GetLargeLetterSpacing(score.Rank), 0);
    }

    private static void drawablePostfix(Drawable __instance)
    {
        if (__instance is not DrawableRank rankDrawable || findScore(rankDrawable) is not { } score || score.Ruleset.ShortName != Constant.SHORT_NAME)
            return;

        var text = findText(rankDrawable);
        if (text == null)
            return;

        // AccuracyCircle owns several threshold badges. Their parent score is the same, so use
        // the badge's original native letter instead of replacing every badge with the result rank.
        var rank = findParent<RankBadge>(rankDrawable) != null
            ? parseNativeRank(text.Text.ToString())
            : score.Rank;

        text.Text = BmsRankDisplay.GetRankLetter(rank);
        text.Spacing = new osuTK.Vector2(BmsRankDisplay.GetLetterSpacing(rank), 0);
    }

    private static ScoreRank parseNativeRank(string text) => text switch
    {
        "SS" => ScoreRank.X,
        "S" => ScoreRank.S,
        "A" => ScoreRank.A,
        "B" => ScoreRank.B,
        "C" => ScoreRank.C,
        "D" => ScoreRank.D,
        _ => ScoreRank.F,
    };

    private static void leaderboardPostfix(BeatmapLeaderboardScore __instance)
    {
        var score = __instance.Score;
        if (score.Ruleset.ShortName != Constant.SHORT_NAME)
            return;

        var nativeText = DrawableRank.GetRankLetter(score.Rank);
        foreach (var text in findTexts(__instance))
        {
            if (text.Text.ToString() != nativeText)
                continue;

            text.Text = BmsRankDisplay.GetRankLetter(score.Rank);
            text.Spacing = new osuTK.Vector2(BmsRankDisplay.GetLetterSpacing(score.Rank), 0);
        }
    }
    // ReSharper restore InconsistentNaming

    private static ScoreInfo? findScore(Drawable drawable)
    {
        for (var parent = drawable.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ScorePanel scorePanel)
                return scorePanel.Score;

            if (parent is BeatmapLeaderboardScore leaderboardScore)
                return leaderboardScore.Score;

            if (parent is AccuracyCircle accuracyCircle)
            {
                var scoreField = typeof(AccuracyCircle).GetField("score", BindingFlags.Instance | BindingFlags.NonPublic);
                return scoreField?.GetValue(accuracyCircle) as ScoreInfo;
            }
        }

        return null;
    }

    private static OsuSpriteText? findText(Drawable drawable)
    {
        if (drawable is OsuSpriteText text)
            return text;

        if (drawable is not CompositeDrawable composite)
            return null;

        var childrenProperty = typeof(CompositeDrawable).GetProperty("InternalChildren", BindingFlags.Instance | BindingFlags.NonPublic);
        if (childrenProperty?.GetValue(composite) is not IEnumerable<Drawable> children)
            return null;

        foreach (var child in children)
        {
            var result = findText(child);
            if (result != null)
                return result;
        }

        return null;
    }

    private static IEnumerable<OsuSpriteText> findTexts(Drawable drawable)
    {
        if (drawable is OsuSpriteText text)
            yield return text;

        if (drawable is not CompositeDrawable composite)
            yield break;

        var childrenProperty = typeof(CompositeDrawable).GetProperty("InternalChildren", BindingFlags.Instance | BindingFlags.NonPublic);
        if (childrenProperty?.GetValue(composite) is not IEnumerable<Drawable> children)
            yield break;

        foreach (var child in children)
        {
            foreach (var result in findTexts(child))
                yield return result;
        }
    }

    private static T? findParent<T>(Drawable drawable) where T : Drawable
    {
        for (var parent = drawable.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is T result)
                return result;
        }

        return null;
    }
}
