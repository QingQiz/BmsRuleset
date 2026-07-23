using System.Collections.Generic;
using System.IO;
using osu.Game.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Audio.Samples;

public sealed class BmsSampleInfo(string path, int volume = 100) : ISampleInfo
{
    public IEnumerable<string> LookupNames
    {
        get
        {
            yield return path;
            yield return Path.ChangeExtension(path, null);
            yield return Path.GetFileName(path);
            yield return Path.ChangeExtension(Path.GetFileName(path), null);
        }
    }

    public int Volume { get; } = volume;
}
