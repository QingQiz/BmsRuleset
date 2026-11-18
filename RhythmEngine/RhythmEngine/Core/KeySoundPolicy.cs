using System.Collections.Immutable;

namespace RhythmEngine.Core;

/// <summary>A sample source ID and, optionally, one of its parts. The host owns the samples.</summary>
public readonly record struct KeySound(int SourceId, PartId? Part = null, KeySoundAction Action = KeySoundAction.Play);

public enum KeySoundAction
{
    Play,
    Stop,
    Mute,
    Unmute,
}

/// <summary>Playback for this press and the source to retain for future presses.</summary>
public readonly record struct KeySoundDecision(KeySound? Playback, int? Remember);

public readonly ref struct KeySoundContext
{
    private readonly JudgementSession session;

    public InputEvent Input { get; }

    public Chart Chart { get; }

    public ReadOnlySpan<NoteTransition> Transitions { get; }

    internal int FirstIncomplete { get; }

    internal KeySoundContext(InputEvent input, Chart chart, ReadOnlySpan<NoteTransition> transitions, JudgementSession session, int firstIncomplete)
    {
        Input = input;
        Chart = chart;
        Transitions = transitions;
        this.session = session;
        FirstIncomplete = firstIncomplete;
    }

    public NoteSnapshot GetNote(int id) => session.SnapshotAt(Chart.NoteIndices[id], Input.Time);

    internal bool IsCompleted(int index) => session.IsNoteCompleted(index);

    internal ImmutableArray<NotePartState> GetParts(int index) => session.GetPartsAt(index, Input.Time);
}

public delegate KeySoundDecision SelectKeySound(KeySoundContext context, int? remembered);

/// <summary>Null delegates to the next rule. A decision with no playback stops selection silently.</summary>
public delegate KeySoundDecision? KeySoundRule(KeySoundContext context, int? remembered);

/// <summary>Selects press feedback in input order. Transition observers can update the same lane memory even without a press.</summary>
public sealed record KeySoundPolicy(SelectKeySound Select, Func<NoteTransition, int?, int?>? Observe = null)
{
    public Func<NoteTransition, ImmutableArray<KeySound>>? TransitionSounds { get; init; }

    public KeySoundPolicy WithTransitionSounds(params Func<NoteTransition, KeySound?>[] rules)
    {
        var sequence = rules.ToArray();
        return this with
        {
            TransitionSounds = transition =>
            {
                KeySound? first = null;
                ImmutableArray<KeySound>.Builder? sounds = null;
                foreach (var rule in sequence)
                {
                    if (rule(transition) is not { } sound)
                        continue;

                    if (first == null)
                    {
                        first = sound;
                        continue;
                    }

                    if (sounds == null)
                    {
                        sounds = ImmutableArray.CreateBuilder<KeySound>(sequence.Length);
                        sounds.Add(first.Value);
                    }

                    sounds.Add(sound);
                }

                if (first is not { } firstResult)
                    return [];
                if (sounds == null)
                    return [firstResult];

                return sounds.DrainToImmutable();
            },
        };
    }

    public static KeySoundPolicy FirstOf(params KeySoundRule[] rules)
    {
        var sequence = rules.ToArray();
        return new((context, remembered) =>
        {
            foreach (var rule in sequence)
            {
                if (rule(context, remembered) is { } decision)
                    return decision;
            }

            return new(null, remembered);
        });
    }
}
