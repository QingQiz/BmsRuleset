using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

internal readonly record struct BmsPreviewSampleEvent(BmsSampleEvent Event, bool ResumeAfterSeek);
