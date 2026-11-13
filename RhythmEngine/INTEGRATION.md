# Standalone rhythm engine

`RhythmEngine.sln` builds independently of the parent solution. The local
`Directory.Build.props` intentionally stops inheritance of osu! build settings.
Core and Hosting reference only .NET; Profiles references Core. None references
BmsRuleset, osu! or osu!framework. BmsRuleset imports the engine through its own chart and output adapters; live play and replay reconstruction share that session.

```sh
dotnet build RhythmEngine/RhythmEngine.sln -c Release
dotnet test RhythmEngine/RhythmEngine.Tests/RhythmEngine.Tests.csproj -c Release --filter FullyQualifiedName~RhythmEngine.Tests
```

## Configuration boundary

Core schedules immutable `INoteRule` configurations, arbitrates input across a
lane, and applies configured score and health reducers. It has no built-in game
names, judgement enum, note-kind enum, timing table, score formula or gauge.
Profiles compiles source-neutral `KeyChart` objects into those contracts. Shared
tap, hold and mine patterns belong to Profiles, so another game can replace them
without changing Core. Custom note rules may also define completely different
immutable state and scoring opportunities.

`HealthConfiguration` owns its initial state, the complete health reducer and
the outcome function. The outcome receives completion, score, health and the
score plan. Bounds, failure latching, recovery and score-dependent qualification
are configuration decisions. Existing presets explicitly select
`HealthRules.LatchedGauge`; custom rules can return a different transition.
Score reduction runs before health reduction for each emitted result.

All rule methods, reducers and captured configuration must be deterministic and
transitively immutable. Delegates are executable C# configuration, not serialized
scripts. Mutable fields, random generators and wall-clock reads in a configuration
violate the contract. Store a profile revision and its exact options/calibration
alongside external replays; a game name alone does not identify replay rules.

## Game-facing API

```csharp
var source = new KeyChart(4,
[
    new KeyNote(10, 0, Time.FromMilliseconds(1000), Time.FromMilliseconds(1000)),
    new KeyNote(11, 1, Time.FromMilliseconds(2000), Time.FromMilliseconds(3000), KeyNoteKind.Hold),
]);
var profile = ManiaProfiles.CreateLazer(source);
IGameplaySession session = new GameplaySession(profile.Chart, profile.Configuration);

var frame = session.AdvanceTo(Time.FromMilliseconds(1000),
[
    new InputEvent(Time.FromMilliseconds(1000), 0, true),
]);
// frame.Transitions includes silent lifecycle changes as well as judgements.
// frame.Events projects just the judgements for statistics consumers.
```

The host imports notes, tempos, breaks and original source order, maps physical
controls to lanes, supplies song-clock input timestamps and owns audio/rendering.
Times are signed integer microseconds; `Time.FromMilliseconds` converts once at
the boundary. Rate handling belongs to the selected profile and the host clock.
Inputs must be chronological, retaining their original order for equal times.
Controls have independent IDs, so releasing one of two bound keys keeps the lane
held. Repeated presses of an already pressed control are ignored.

Submit every input at the same timestamp in one `AdvanceTo` call. Advancing closes
that timestamp. Earlier timers run first; at equal timestamps the timer's explicit
`BeforeInput`/`AfterInputState`/`AfterInput` order applies. Simultaneous timers retain chart source
order. Inclusive timing windows expire at their late boundary plus one microsecond.
A note rule must handle simultaneous internal work in one update and schedule its
next timer strictly after the invocation time. Notes can emit several results,
finish silently, or emit unscored results. The precomputed `ScorePlan` identifies
which object parts contribute accuracy potential; it is independent of emissions.

## Presentation without a second judgement engine

`INoteRule.GetParts` projects a rule's authoritative state into stable part IDs
with `Resolved` and `Active` flags. A host never casts `NoteState` to a profile's
state type. A resolved part can remain in input arbitration, and a sustained
body can remain active after an endpoint decision; `NoteSnapshot.Completed`
means the whole rule has retired. Timing changes belong in scheduled rule
updates, not in a host's rendering code or in a clock-dependent projection.

Every rule invocation produces one ordered `NoteTransition`: before/after
snapshots, the time, the causal input (null for timers), all judgements with
their resulting score/health, and the outcome after the invocation. This also
covers silent starts, releases, regrabs and retirement. A batch preserves
every intermediate transition. `Sequence` is ordered within a generation;
each judgement also retains its own emission sequence. The host displays
these values directly and binds part IDs to its own visuals and audio.

On a live frame, consume transitions in order and compare part activity for
sustained effects; use the per-judgement accumulators for combo/health animations.
The final frame totals are authoritative even when it contains no judgements.
The engine continues evaluating the supplied timeline after a failed outcome,
allowing configurations with recovery. The host owns pausing or leaving play.

`Reconstruct(target, replayInputs)` rebuilds an independent session before replacing
the live one. Its frame has `IsSnapshot = true`, contains all `RestoredNotes`,
and changes `Generation`. Replace presentation state and synchronize active
effects from this snapshot; historical transitions are for history/statistics,
not for triggering live audio. `Restart` produces the same kind of replacement
with initial states and a new generation. `Current` supplies a complete snapshot
with no historical transitions, suitable for attaching or recreating a view.
`GetNote(id)` supplies the same public projection for a single object.

`Clone` copies controls, pending timers, rule states, scores, health and history
while sharing immutable definitions. Generations belong to a session branch;
consume a clone's `Current` when switching branches. The host retains the
canonical input recording; transition causes do not replace that recording
because inputs without a note candidate produce no note transition.

`RhythmEngine.Tests/Hosting/RecordingHost.cs` is an executable consumer example.
It imports only Hosting and consumes no profile IDs, result names, timing
windows or internal rule states. `HostLifecycleTest` drives that same consumer
with independent configurations and existing presets, including within-batch
release/regrab, timing boundaries, custom scoring, recovery, seek and cloning.

The implemented input contract remains fixed lanes with digital controls.
Analog trajectories, moving lanes, session timers for continuous drain require additional contracts. Concurrent gauges can be expressed by immutable `HealthState.Layers` and a single configured reducer/outcome function. Original-client audiovisual timing remains outside the standalone fixtures.

The session is single-threaded, owns no external resources and requires no dispose
callback. A returned frame can be read elsewhere as an immutable snapshot.

## Rule coverage and evidence

| Configuration | Implemented behavior | Evidence boundary |
| --- | --- | --- |
| beatoraja seven-key | Rank-scaled asymmetric key/scratch windows, empty POOR, LN/CN/HCN, directional tails, mines, EX score, all nine gauges and float life accumulation | Public source revision in `ProfileDescriptor`; five-key/PMS are separate unsupported modes; frame-by-frame binary parity is unverified |
| LR2 | Four published rank tables, LN, EX score, LR2 gauge tables and low-TOTAL damage scaling | LunaticVibes/beatoraja compatibility references; original closed client not verified |
| LR2 beta3 100201 | Shipped rank windows, LN head timeout/release lenience, EX score, combo, late-BAD chaining and original four gauges | `Lr2Beta3Profile.Create` takes integer-millisecond `KeyChart` endpoints and normalized TOTAL. Native instruction fixtures cover timing, holds, arbitration and gauge/score sequences; fractional import, dynamic rank, scratch controls and courses remain outside this preset. |
| lazer mania | OD windows, independent hold endpoints, release lenience, hold-break cap, standardized score, chart-calibrated recovery | Pinned osu! source; mod-specific score policies and pre-v8 break behavior excluded |
| stable mania ScoreV1 | Native/converted windows, combined hold error, regrab, 100ms body combo/HP, live/final score rounding, Bonus resets, classic accuracy, recovery gauge | Pinned shipped executable with original-CIL hold and 1260 score/Bonus/Combo/HP states, plus static manager/calibration comparisons. Integer millisecond input reproduces the stable clock; full application frame traces remain outside the fixtures. |
| Open2Jam compatibility | Time or beat windows across BPM changes, hold endpoints, JAM score, pills and difficulty life bounds | Pinned Open2Jam model, including its pill-to-GOOD behavior |
| O2Jam e-Games 3.38 | Quantized tick judgement, tempo clocks, nearest-note routing, separately judged hold endpoints, head-BAD/timeout tail MISS, pills to COOL, native JAM/score/HP and displayed combo | `O2Jam338Profile.Create` takes `O2JamTickNote` positions and an `O2JamClock` of original integer millisecond/tick anchors. Native input, timeout and scoring fixtures cover four BPMs and a tempo change; client frame/audio scheduling and items are excluded. |
| Etterna | J1–J9 selection with upstream scale cap, Wife3 continuous credit, hold grace, rolls, lifts, mines, fallback life table with original float accumulation | Pinned source; importer must remove fakes/warps and resolve combo segments; other life-meter options excluded |
| Malody Key | Classic 4.3.7 PC/Android; explicitly selected V RC 2.0 or V 6.7.22 Standard/Pro; rank-scaled HP, Bonus, body combo, regrab, graded tails, timing scale/conversion and calibration | PC, Android and current-V fixtures execute original native instructions, including manual Key routing/expiry blocks. V 6.7.22 is the default V version, scores graded tails separately, and has original Key routing/expiry/repeat traces. See VERIFICATION.md for fixture scope and unresolved application call paths. |

Each compiled profile exposes revision, source links and limitations through
`ProfileDescriptor`. These are source-backed implementations and compatibility
models, not a claim that all original clients have passed differential testing.

## BmsRuleset integration

`BmsEngineChart` freezes imported timing windows, note modes, scratch controls and
mod policies into engine rules. `BmsJudgementEngine` submits physical edges and
projects ordered results into osu! score/health processors. Those processors use
the engine accumulators during play and replay; their framework simulation hooks
remain available for score calibration and direct framework results.

`BmsJudgementView` exposes read-only note snapshots and input state. The UI owns
`BmsLongNoteState`, drawable bindings, animation retirement and object pooling.
Binding, reading or discarding presentation never writes into the session.
Endpoint metadata preserves the observed grade/offset independently of the
aggregate score; `NoteSnapshot.Held` records the physical state for that event.

`JudgementSession.AdvanceTo` returns input dispatch ranges, so unmatched inputs
retain their audio position even when identical press/release/press edges share
a timestamp. `AppendInputs` accepts another batch only at the closed timestamp;
already dispatched timers are not rerun. `EndAt` asks active rules to settle an
attempt at its recorded endpoint. Backward replay seeks reconstruct a session
from physical inputs and the configured initial gauge layers.

Time is rounded once to signed integer microseconds at the host boundary.
Strict deadlines occur one microsecond after their inclusive window. The shipped
ruleset DLL embeds Core and Profiles; neither engine assembly references osu!.
