using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

internal static class BmsManiaReplay
{
    internal static Score? Convert(Score native, ScoreInfo requested, IBeatmap beatmap)
    {
        var converted = new Score { ScoreInfo = requested.DeepClone() };
        converted.Replay.HasReceivedAllFrames = native.Replay.HasReceivedAllFrames;
        foreach (var frame in native.Replay.Frames)
        {
            // mania exposes its lane mask through the shared legacy interface. Bit zero is Key1, not scratch.
            if (frame is not IConvertibleReplayFrame mania || mania.ToLegacy(beatmap).MouseX is not { } mask || mask is < 0 or > 127)
                return null;

            var input = new BmsReplayFrame(frame.Time);
            for (var lane = 0; lane < 7; lane++)
            {
                if (((int)mask & (1 << lane)) != 0)
                    input.Actions.Add(BmsAction.Key1 + lane);
            }

            converted.Replay.Frames.Add(input);
        }

        return converted;
    }
}
