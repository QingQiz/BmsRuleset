using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Common;

/// <summary>Part identities shared by the built-in key-mode rules. Custom rules may declare additional parts.</summary>
public static class NoteParts
{
    public static readonly PartId TAP = new("tap");
    public static readonly PartId EMPTY = new("empty");
    public static readonly PartId HEAD = new("head");
    public static readonly PartId BODY = new("body");
    public static readonly PartId TAIL = new("tail");
    public static readonly PartId MINE = new("mine");
    public static readonly PartId NOTE = new("note");
}
