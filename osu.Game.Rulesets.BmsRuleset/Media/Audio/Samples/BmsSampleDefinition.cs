using System;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

public sealed record BmsSampleDefinition(string Path, double StartOffset = 0, double? EndOffset = null, bool ResumeInPreview = false)
{
    public double GetLength(double sourceLength) => Math.Max(0, Math.Min(EndOffset ?? sourceLength, sourceLength) - StartOffset);
}
