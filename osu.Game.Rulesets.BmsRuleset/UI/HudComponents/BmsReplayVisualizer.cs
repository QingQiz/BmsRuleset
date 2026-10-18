using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play.HUD;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsReplayVisualizer : BmsHudComponent
{
    private const float judgement_line = 0.25f;

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ReplayVisualizerTimeRange), nameof(BmsStrings.ReplayVisualizerTimeRangeDescription))]
    public BindableNumber<float> TimeRange { get; } = new BindableFloat(4)
    {
        MinValue = 1,
        MaxValue = 10,
        Precision = 0.1f,
    };

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ReplayVisualizerShowNotes), nameof(BmsStrings.ReplayVisualizerShowNotesDescription))]
    public BindableBool ShowNotes { get; } = new(true);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ReplayVisualizerShowInputs), nameof(BmsStrings.ReplayVisualizerShowInputsDescription))]
    public BindableBool ShowInputs { get; } = new(true);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ReplayVisualizerBackgroundOpacity), nameof(BmsStrings.ReplayVisualizerBackgroundOpacityDescription),
        SettingControlType = typeof(SettingsPercentageSlider<float>))]
    public BindableNumber<float> BackgroundOpacity { get; } = new BindableFloat(0.65f)
    {
        MinValue = 0,
        MaxValue = 1,
        Precision = 0.01f,
    };

    [Resolved(CanBeNull = true)]
    private DrawableRuleset? drawableRuleset { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreProcessor? scoreProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private InputCountController? inputController { get; set; }

    private readonly List<TriggerSubscription> subscriptions = [];
    private readonly JudgementCursor judgements = new();
    private readonly InputHistory liveInputs = new();
    private NoteLane[] notes = [];
    private BmsColumn[] columns = [];
    private InputLane[]? replayInputs;
    private BmsLayoutVariant layout = BmsLayoutVariant.Bme7K;
    private int columnCount = 8;
    private GeometryState? preparedGeometry;

    internal PlotLayer Plot { get; }

    public BmsReplayVisualizer()
    {
        Size = new Vector2(240, 360);
        Anchor = Anchor.CentreLeft;
        Origin = Anchor.CentreLeft;
        X = 12;

        var background = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black };
        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            Children =
            [
                background,
                Plot = new PlotLayer { RelativeSizeAxes = Axes.Both },
            ],
        };
        BackgroundOpacity.BindValueChanged(change => background.Alpha = change.NewValue, true);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        if (drawableRuleset is BmsDrawableRuleset { Beatmap: BmsBeatmap beatmap })
        {
            layout = beatmap.LayoutVariant;
            columnCount = Math.Max(1, beatmap.TotalColumns);
            columns = ((BmsPlayfield)drawableRuleset.Playfield).Stage.Columns.Cast<BmsColumn>().ToArray();
            notes = Enumerable.Range(0, columnCount)
                .Select(column => new NoteLane(beatmap.HitObjects.Where(note => note.Column == column && note is not BmsLandmine)))
                .ToArray();
        }
        else
        {
            notes = Enumerable.Range(0, columnCount)
                .Select(column => new NoteLane([new BmsNote { Column = column, StartTime = -column * 250 }]))
                .ToArray();
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (inputController != null)
        {
            inputController.Triggers.CollectionChanged += triggersChanged;
            bindTriggers();
        }
    }

    private void triggersChanged(object? sender, NotifyCollectionChangedEventArgs e) => bindTriggers();

    private void bindTriggers()
    {
        foreach (var subscription in subscriptions)
            subscription.Dispose();
        subscriptions.Clear();
        liveInputs.Clear();

        foreach (var trigger in inputController!.Triggers.OfType<KeyCounterActionTrigger<BmsAction>>())
        {
            if (BmsKeyBindingConfiguration.ActionToColumn(trigger.Action, layout) is not { } column || column >= columnCount)
                continue;

            subscriptions.Add(new TriggerSubscription(trigger,
                forward =>
                {
                    if (forward && drawableRuleset?.ReplayScore == null)
                    {
                        liveInputs.Advance(trigger.Clock.CurrentTime);
                        liveInputs.Press(trigger.Action, column, trigger.Clock.CurrentTime);
                    }
                },
                forward =>
                {
                    if (forward && drawableRuleset?.ReplayScore == null)
                    {
                        liveInputs.Advance(trigger.Clock.CurrentTime);
                        liveInputs.Release(trigger.Action, trigger.Clock.CurrentTime);
                    }
                }));

            if (trigger.IsActive)
                liveInputs.Press(trigger.Action, column, currentTime);
        }
    }

    private double currentTime => drawableRuleset?.FrameStableClock.CurrentTime ?? 0;

    protected override void Update()
    {
        base.Update();

        if (DrawColourInfo.Colour.MaxAlpha <= 0.0001f)
            return;

        var now = currentTime;
        updateReplayInputs();
        liveInputs.Advance(now);
        var processor = scoreProcessor as BmsScoreProcessor;
        var geometry = new GeometryState(now, DrawSize, TimeRange.Value, ShowNotes.Value, ShowInputs.Value,
            liveInputs.Version, processor?.StatisticsVersion ?? 0, replayInputs);
        if (preparedGeometry == geometry)
            return;

        preparedGeometry = geometry;
        var range = TimeRange.Value * 1000;
        var firstTime = now - range * (1 - judgement_line);
        var lastTime = now + range * judgement_line;
        judgements.Update(processor?.JudgementEvents);
        Plot.Clear();

        var columnWidth = DrawWidth / columnCount;
        foreach (var lane in notes)
        {
            foreach (var note in lane.Visible(firstTime, lastTime))
            {
                var head = observationFor(note, tail: false, now);
                var tail = observationFor(note, tail: true, now);
                if (ShowNotes.Value)
                {
                    var x = (displayColumn(note.Column) + 0.1f) * columnWidth;
                    var top = positionAt(note.EndTime, now, range);
                    var bottom = positionAt(note.StartTime, now, range);
                    var colour = ColourFor(NoteResult(head, tail));
                    rectangle(x, top, columnWidth * 0.8f, Math.Max(5, bottom - top), colour, kind: PlotElementKind.Note, outline: true);
                }
                if (note.EndTime > note.StartTime)
                    judgements.ObserveLongNote(note.Column, head, tail, note.EndTime);
            }
        }

        if (ShowInputs.Value)
        {
            if (replayInputs != null)
            {
                foreach (var lane in replayInputs)
                {
                    foreach (var stroke in lane.Visible(firstTime, now)) drawInput(stroke, now, range, columnWidth);
                }
            }
            else if (indexedReplay == null)
            {
                for (var i = 0; i < liveInputs.Strokes.Count; i++)
                {
                    var stroke = liveInputs.Strokes[i];
                    if (stroke.StartTime <= now && stroke.EndTime >= firstTime) drawInput(stroke, now, range, columnWidth);
                }
            }
        }

        rectangle(0, DrawHeight * judgement_line - 0.5f, DrawWidth, 1, Colour4.White, PlotElementKind.Judgement);
        Plot.Commit();
    }

    private void drawInput(InputStroke stroke, double now, double range, float columnWidth)
    {
        var x = (displayColumn(stroke.Column) + 0.5f) * columnWidth;
        var start = positionAt(stroke.StartTime, now, range);
        var end = positionAt(Math.Min(now, stroke.EndTime), now, range);
        var colour = ColourFor(judgements.ForInput(stroke, now));
        rectangle(x - columnWidth * 0.22f, end - 1.5f, columnWidth * 0.44f, Math.Max(3, start - end + 3), colour);
        if (!ShowNotes.Value)
            return;

        var press = judgements.TimingForInput(stroke, release: false, now);
        var release = judgements.TimingForInput(stroke, release: true, now);
        drawConnection(stroke.Column, press, now, range, columnWidth, colour);
        if (release != press)
            drawConnection(stroke.Column, release, now, range, columnWidth, colour);
    }

    private void drawConnection(int column, BmsTimingObservation? timing, double now, double range, float columnWidth, Colour4 colour)
    {
        if (timing is not { } observation)
            return;

        var x = (displayColumn(column) + 0.5f) * columnWidth;
        var expected = positionAt(observation.ExpectedTime, now, range);
        if (observation.Kind == BmsTimingObservationKind.Note)
            expected += 2.5f;
        var actual = positionAt(observation.ActualTime, now, range);
        dashedLine(new Vector2(x, expected), new Vector2(x, actual), colour);
    }

    private void dashedLine(Vector2 start, Vector2 end, Colour4 colour)
    {
        const float dash_length = 4;
        const float dash_period = 7;
        const float thickness = 1.5f;
        var vertical = start.X == end.X;
        var delta = vertical ? end.Y - start.Y : end.X - start.X;
        var length = Math.Abs(delta);
        if (length <= 0)
            return;

        var direction = delta < 0 ? -1 : 1;
        var origin = vertical ? start.Y : start.X;
        var limit = vertical ? DrawHeight : DrawWidth;
        var firstVisible = Math.Max(0, direction > 0 ? -origin : origin - limit);
        var lastVisible = Math.Min(length, direction > 0 ? limit - origin : origin);
        // Preserve the phase at the note endpoint when either endpoint is clipped.
        var firstDash = Math.Max(0, Math.Floor(firstVisible / dash_period)) * dash_period;
        for (var offset = firstDash; offset < lastVisible; offset += dash_period)
        {
            var dashEnd = Math.Min(length, offset + dash_length);
            var from = origin + direction * offset;
            var to = origin + direction * dashEnd;
            if (vertical)
                rectangle(start.X - thickness / 2, (float)Math.Min(from, to), thickness, (float)(dashEnd - offset), colour, PlotElementKind.Connection);
            else
                rectangle((float)Math.Min(from, to), start.Y - thickness / 2, (float)(dashEnd - offset), thickness, colour, PlotElementKind.Connection);
        }
    }

    private BmsTimingObservation? observationFor(TimelineNote note, bool tail, double now)
    {
        var expectedTime = tail ? note.EndTime : note.StartTime;
        if (judgements.At(note.Column, expectedTime, now) is { } observation)
            return observation;

        if (note.Source is not BmsLongNote longNote || note.Column >= columns.Length)
            return null;

        // Normal LN heads are committed to the score ledger with the tail. Reading the
        // current state lets this independent HUD colour a held note immediately.
        var state = columns[note.Column].Judgements.GetLongNoteState(longNote);
        var actualTime = tail ? state.TailTime : state.HeadTime;
        var result = tail ? state.TailResult : state.HeadResult;
        return actualTime is { } time && time <= now && result != HitResult.None
            ? new BmsTimingObservation(tail ? BmsTimingObservationKind.LongNoteTail : BmsTimingObservationKind.LongNoteHead,
                expectedTime, time, null, result)
            : null;
    }

    private float positionAt(double time, double now, double range) => (float)(judgement_line + (now - time) / range) * DrawHeight;

    private int displayColumn(int column) => BmsLayout.Is2P(layout) ? column == 0 ? columnCount - 1 : column - 1 : column;

    internal static Colour4 ColourFor(HitResult? result) => result is null or HitResult.None
        ? Colour4.White
        : BmsHitResultColours.ForHitResult(result.Value);

    internal static HitResult? NoteResult(BmsTimingObservation? head, BmsTimingObservation? tail)
        => worseResult(scoringResult(head?.Result), scoringResult(tail?.Result));

    private static HitResult? scoringResult(HitResult? result) => result == HitResult.Miss ? HitResult.Meh : result;

    private static HitResult? worseResult(HitResult? first, HitResult? second)
        => first is null or HitResult.None ? second : second is null or HitResult.None ? first : (HitResult)Math.Min((int)first, (int)second);

    private void rectangle(float x, float y, float width, float height, Colour4 colour, PlotElementKind kind = PlotElementKind.Input, bool outline = false)
    {
        var left = Math.Max(0, x);
        var right = Math.Min(DrawWidth, x + width);
        var top = Math.Max(0, y);
        var bottom = Math.Min(DrawHeight, y + height);
        if (right <= left || bottom <= top)
            return;

        Plot.Add(new PlotRectangle(new(left, top, right - left, bottom - top), colour, outline, Kind: kind));
    }

    protected override void Dispose(bool isDisposing)
    {
        replayIndexCancellation?.Cancel();
        replayIndexCancellation?.Dispose();
        if (inputController != null)
            inputController.Triggers.CollectionChanged -= triggersChanged;
        foreach (var subscription in subscriptions)
            subscription.Dispose();
        subscriptions.Clear();
        base.Dispose(isDisposing);
    }

    private readonly record struct GeometryState(double Time, Vector2 Size, float Range, bool ShowNotes, bool ShowInputs,
                                                long InputVersion, long StatisticsVersion, InputLane[]? ReplayInputs);

    private sealed class TriggerSubscription : IDisposable
    {
        private readonly InputTrigger trigger;
        private readonly InputTrigger.OnActivateCallback activate;
        private readonly InputTrigger.OnDeactivateCallback deactivate;

        public TriggerSubscription(InputTrigger trigger, InputTrigger.OnActivateCallback activate, InputTrigger.OnDeactivateCallback deactivate)
        {
            this.trigger = trigger;
            this.activate = activate;
            this.deactivate = deactivate;
            trigger.OnActivate += activate;
            trigger.OnDeactivate += deactivate;
        }

        public void Dispose()
        {
            trigger.OnActivate -= activate;
            trigger.OnDeactivate -= deactivate;
        }
    }
}
