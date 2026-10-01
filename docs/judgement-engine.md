# Judgement engine

[Back to development](development.md)

The ruleset runs one judgement engine per playfield. Rules use chart-object identity and numerical frame data; graphical representations adapt framework results and render the resulting state.

## Ownership

| Component | Responsibility |
| --- | --- |
| `BmsJudgementEngine` | Shares stateless rules across columns, queues physical inputs, and orders simulation batches. |
| `BmsColumnJudgementContext` | Owns activation, candidate indexes, input history, typed long-note and landmine state, and operation bindings. |
| `BmsCoreJudgementSchedule` | Executes passive and long-note completion phases using binding tokens and simulation-step stamps. |
| Note, LN, CN, HCN and landmine rules | Implement their own state transitions, judgement windows, endpoints and rewind behaviour. |
| `BmsJudgementExecutionContext` | Coordinates output calls with the core's history and index checkpoints. |
| `IBmsJudgementOutput` | Defines the replaceable result and feedback boundary used by production and recording tests. |
| `BmsDrawableJudgementOutput` | Binds current representations, owns framework result references and forwards generation-checked lifecycle notifications. |
| `BmsColumnHitObjectContainer` | Reports activation/removal and traversal order, samples visual time, and applies shared layout and culling. |
| Concrete drawables and presentation mappings | Compose graphics and consume read-only state. Long-note geometry belongs to the long-note drawable. |

Normal notes use slot data and shared operation delegates. LN, CN and HCN have separate runtime types; only HCN owns a body accumulator and its lossless rewind history. Pooling a drawable does not discard the object's logical history. Framework main results remain authoritative.

## Frame and result contracts

Each input batch samples its final physical key state for all columns. The engine updates all landmines, then all HCN bodies, then consumes physical transitions in order. AutoScratch runs after those transitions. Inputs queued by synchronous feedback remain for the next batch, including another batch at the same timestamp.

`InputTime` identifies the physical transition. `BmsFrameContext.SimulationTime` identifies the application frame; `GameplayRate` carries its rate. Framework main results use the representation's matching frame and rate. Supplementary endpoints retain separate event, application and expected times. Resume rewind freezes completed judgement state while presentations follow the lead-in clock.

Passive and completion plans preserve the host's observed traversal order. Tokens invalidate replaced bindings, while epoch/step stamps prevent repeated completion in the same simulation step. Removal first drops the object from candidates, then performs its final passive check while its representation remains available. `ApplyDefaults` replaces a binding without replaying removal or discarding applicable history.

Framework history notification and core index refresh are separate synchronous checkpoints. The output adapter does not duplicate candidate selection or rule transitions. `ObserveLongNoteStep` supplies hold feedback after the passive check and before a delayed tail can settle; the schedule checks the binding again after that callback.

## Presentation and audio

Layout prepares object bounds once, applies culling and reuses the prepared geometry. Long-note time observations are registered separately from layout; repeated layout reads do not append visual history or advance rules. Geometry observes the state before that traversal's passive and completion phases.

The binding selects an LN, CN or HCN presentation mapping. Successful LN/CN completion clears the body immediately. Natural body expiry retains the final child geometry while hiding the parent. Resume visibility is applied even when culling skips the drawable's children.

Key and background sample requests continue through `BmsSamplePlayback` and `BmsPcmPlaybackSession`. The PCM controller takes the audio manager's aggregate master volume; the framework's effects/sample-volume setting is not part of that route. Empty-press sample selection queries core activation and results through `BmsKeySoundCursor`.

## Verification

Rule and engine tests run without drawables through the recording output. Execution tests compare recording and framework outputs at fixed checkpoints. Player tests cover actual input generation, replay catchup, simultaneous stages, pool reuse, reapplication, rewind, Mods, culling and presentation.

`TestSceneBmsGitComparison` and `BmsGitComparisonRecorder` can be copied unchanged to the original Git baseline. They record ordered results, intermediate score/gauge histories, requested samples, hit lights and visual snapshots. `scripts/compare-judgement-traces.py` compares both directories and rejects missing scenarios. HCN effects are observed at the health processor, including an assertion on the forwarded event time.

Run the migration's selected regression scope from the repository root:

```powershell
./scripts/verify-judgement-migration.ps1
```

The script builds only this repository against existing reference binaries, then runs two filtered processes. Ordinary functional tests retain default JIT settings. Fixtures tagged `JudgementInstrumentation` use `osu.Game.Rulesets.BmsRuleset.Tests/judgement-instrumentation.runsettings`, which disables method inlining so entry probes remain observable after warmup. Both Git comparison sides must use that same settings file. Neither the production project nor the benchmark runtime is changed. Logs, exact arguments, TRX files, counts and comparison traces are saved in the reported artifact directory.

Tests must always use `--filter`. Performance benchmarks require explicit approval. Functional comparison does not establish performance acceptance; see the [migration acceptance requirements](hitobject-refactor-plan.zh-CN.md#performance-acceptance) while the migration remains open.
