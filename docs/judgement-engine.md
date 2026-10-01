# Judgement engine

[Back to development](development.md)

The ruleset runs one judgement engine per playfield. Rules use chart-object identity and numerical frame data; graphical representations adapt framework results and render the resulting state.

The code has three dependency boundaries:

- `Gameplay/Judgement` contains the engine, column runtime, indexes, registries and schedule. Its `Rules` and `State` directories hold independent rules and typed state. This namespace has no dependency on the ruleset UI, drawables or graphical clocks.
- `UI/Gameplay/Judgement` contains `BmsJudgementBridge`, the framework adapter for lifecycle inputs and result/feedback outputs.
- `UI/Gameplay/Drawables/Objects/Presentation` maps read-only state to visual decisions. Concrete drawables consume these decisions and own animation timers and geometry.

## Ownership

| Component | Responsibility |
| --- | --- |
| `BmsJudgementEngine` | Shares stateless rules across columns, queues physical inputs, and orders simulation batches. |
| `BmsColumnJudgementRuntime` | Coordinates one column's input selection, history and checkpoints; owns its component instances and stable chart slots. |
| `BmsColumnNoteIndex` | Owns active membership, traversal ranks, pending heads/tails, active bodies and passive deadlines. Exposes read-only views. |
| `BmsHoldRegistry` | Resolves stable note slots to immutable hold bindings; mode-specific shared operation adapters own typed LN/CN/HCN state. |
| `BmsLandmineRegistry` | Owns landmine state and active traversal order, and calls the independent landmine rule. |
| `BmsAutoScratchController` | Owns automatic holds and cached traversal plans; exists only for enabled scratch columns. |
| `BmsCoreJudgementSchedule` | Executes passive and long-note completion phases using binding tokens and simulation-step stamps. |
| Note, LN, CN, HCN and landmine rules | Implement their own state transitions, judgement windows, endpoints and rewind behaviour. |
| `BmsJudgementExecutionContext` | Coordinates output calls with the core's history and index checkpoints. |
| `IBmsJudgementOutput` | Defines the replaceable result and feedback boundary used by production and recording tests. |
| `BmsJudgementBridge` | Binds current representations, holds framework result references and forwards generation-checked lifecycle notifications and output callbacks. |
| `BmsColumnHitObjectContainer` | Reports activation/removal and traversal order, samples visual time, and applies shared layout and culling. |
| Concrete drawables and presentation mappings | Compose graphics and consume read-only state. Long-note geometry belongs to the long-note drawable. |

Normal notes use slot data and shared operation delegates. Each long note has one immutable binding containing its identity, shared operation adapter and typed-state index. A compact slot map connects mixed columns to the hold-only binding array; ordinary notes do not allocate hold records. This replaces parallel arrays of operations whose correspondence previously had to be maintained separately. The adapters share only operations supported by all holds; the HCN adapter alone exposes body updates. Rules remain independent of one another.

LN, CN and HCN have separate runtime types; only HCN owns a body accumulator and its lossless rewind history. Pooling a drawable does not discard the object's logical history. Framework main results remain authoritative.

Activation order is validated before it replaces the live index. Cached ranks provide constant-time order lookup when sorting pending tails, mixed-column bodies and equal-time automatic notes. Direct start-time changes refresh pending-head keys, the sorted historical lookup and the automatic plan. They invalidate the passive deadline immediately so an earlier head cannot wait behind an obsolete cached bound.

## Frame and result contracts

Each input batch samples its final physical key state for all columns. The engine updates all landmines, then all HCN bodies, then consumes physical transitions in order. AutoScratch runs after those transitions. Inputs queued by synchronous feedback remain for the next batch, including another batch at the same timestamp.

`InputTime` identifies the physical transition. `BmsFrameContext.SimulationTime` identifies the application frame; `GameplayRate` carries its rate. Framework main results use the representation's matching frame and rate. Supplementary endpoints retain separate event, application and expected times. Resume rewind freezes completed judgement state while presentations follow the lead-in clock.

Passive and completion plans preserve the host's observed traversal order. Tokens invalidate replaced bindings, while epoch/step stamps prevent repeated completion in the same simulation step. Removal first drops the object from candidates, then performs its final passive check while its representation remains available. `ApplyDefaults` replaces a binding without replaying removal or discarding applicable history.

Framework history notification and core index refresh are separate synchronous checkpoints. The bridge implements `IBmsJudgementOutput` for core output and accepts framework lifecycle inputs; its name reflects both directions. It does not duplicate candidate selection or rule transitions. `ObserveSimulationStep` supplies feedback after the passive check and before a delayed tail can settle; the schedule checks the binding again after that callback. Concrete representations register their feedback callback at binding time, and the bridge checks and removes it by generation. The drawable base has no long-note-only update hook.

## Presentation and audio

Layout prepares object bounds once, applies culling and reuses the prepared geometry. Long-note time observations are registered separately from layout; repeated layout reads do not append visual history or advance rules. Geometry observes the state before that traversal's passive and completion phases.

The binding selects an LN, CN or HCN presentation mapping, including whether hold-light pulses are active. The long-note drawable owns pulse timing. Successful LN/CN completion clears the body immediately. Natural body expiry retains the final child geometry while hiding the parent. Resume visibility is applied even when culling skips the drawable's children.

Key and background sample requests continue through `BmsSamplePlayback` and `BmsPcmPlaybackSession`. The PCM controller takes the audio manager's aggregate master volume; the framework's effects/sample-volume setting is not part of that route. Empty-press sample selection queries core activation and results through `BmsKeySoundCursor`.

## Verification

Rule and engine tests run without drawables through the recording output. Architecture tests inspect every core type's fields, method signatures and constructor parameters for UI, drawable and clock dependencies. Execution tests compare recording and framework outputs at fixed checkpoints. Player tests cover actual input generation, replay catchup, simultaneous stages, pool reuse, reapplication, rewind, Mods, culling and presentation. Dedicated regressions cover direct timing changes, sparse hold storage, rejected activation plans and feedback callback generations.

`TestSceneBmsGitComparison` and `BmsGitComparisonRecorder` can be copied unchanged to the original Git baseline. They record ordered results, intermediate score/gauge histories, requested samples, hit lights and visual snapshots. `scripts/compare-judgement-traces.py` compares both directories and rejects missing scenarios. HCN effects are observed at the health processor, including an assertion on the forwarded event time.

Run the migration's selected regression scope from the repository root:

```powershell
./scripts/verify-judgement-migration.ps1
```

The script builds only this repository against existing reference binaries, then runs two filtered processes. Ordinary functional tests retain default JIT settings. Fixtures tagged `JudgementInstrumentation` use `osu.Game.Rulesets.BmsRuleset.Tests/judgement-instrumentation.runsettings`, which disables method inlining so entry probes remain observable after warmup. Both Git comparison sides must use that same settings file. Neither the production project nor the benchmark runtime is changed. Logs, exact arguments, TRX files, counts and comparison traces are saved in the reported artifact directory.

Tests must always use `--filter`. Performance benchmarks require explicit approval. Functional comparison does not establish performance acceptance; see the [migration acceptance requirements](hitobject-refactor-plan.zh-CN.md#performance-acceptance) while the migration remains open.
