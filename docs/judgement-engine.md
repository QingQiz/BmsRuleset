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
| `BmsJudgementEngine` | Shares stateless rules across columns, queues action inputs, and orders simulation batches. |
| `BmsColumnJudgementRuntime` | Coordinates one column's input selection, history and checkpoints; owns its component instances and stable chart slots. |
| `BmsColumnNoteIndex` | Owns active membership, traversal ranks, pending heads/tails, active bodies and passive deadlines. Exposes read-only views. |
| `BmsHoldRegistry` | Resolves stable note slots to immutable hold bindings; mode-specific shared operation adapters own typed LN/CN/HCN state. |
| `BmsLandmineRegistry` | Owns landmine state and active traversal order, and calls the independent landmine rule. |
| `BmsCoreJudgementSchedule` | Executes passive and long-note completion phases using binding tokens and simulation-step stamps. |
| Note, LN, CN, HCN and landmine rules | Implement their own state transitions, judgement windows, endpoints and rewind behaviour. |
| `BmsJudgementExecutionContext` | Coordinates output calls with the core's history and index checkpoints. |
| `IBmsJudgementOutput` | Defines the replaceable result and feedback boundary used by production and recording tests. |
| `BmsJudgementBridge` | Binds current representations, holds framework result references and forwards generation-checked lifecycle notifications and output callbacks. |
| `BmsColumnHitObjectContainer` | Reports activation/removal and traversal order, samples visual time, and applies shared layout and culling. |
| Concrete drawables and presentation mappings | Compose graphics and consume read-only state. Long-note geometry belongs to the long-note drawable. |

Normal notes use slot data and shared operation delegates. Each long note has one immutable binding containing its identity, shared operation adapter and typed-state index. A compact slot map connects mixed columns to the hold-only binding array; columns containing only ordinary notes need no hold slot map. The registry accepts chart objects and owns all hold lookup and rewind traversal, including inactive holds. The column decides when to rewind and never traverses a sparse long-note array. The adapters share only operations supported by all holds; the HCN adapter alone exposes body updates. Rules remain independent of one another.

LN, CN and HCN have separate runtime types; only HCN owns a body accumulator and its lossless rewind history. Pooling a drawable does not discard the object's logical history. Framework main results remain authoritative.

Note and landmine activation orders are validated completely before publication. Duplicate, inactive or missing entries leave the previous order intact. Cached ranks provide constant-time order lookup when sorting pending tails and mixed-column bodies. Direct start-time changes refresh pending-head keys and the sorted historical lookup. They invalidate the passive deadline immediately so an earlier head cannot wait behind an obsolete cached bound.

## AutoScratch input

`BmsModAutoScratch` owns its scratch action timeline, cursor, held directions and next action time. It reuses Autoplay's action points at setup and implements `IBmsActionProducer.UpdateActions(time)`. The input manager caches implementing mods once and calls this interface after device/replay input at each simulation frame. The mod compares the time with its cached deadline; due transitions call the framework's `TriggerPressed`/`TriggerReleased` before playfield judgement. Forward updates visit only crossed frames, preserve same-time release/press transitions, and unchanged frames allocate nothing. No separate updating component or shared action schedule is created.

Ordinary rewind locates the target input state with a binary search; resume rewind preserves the existing hold until gameplay catches up. Reapplying a drawable leaves framework input state intact. The mod also implements `IBmsActionFilter` to decide physical and replay input ownership. The input manager applies those generic decisions before framework action tracking; it contains no scratch-specific actions or replay policy. Manual scratch keys therefore cannot suppress generated presses or block their releases.

Generated actions are recorded through the normal replay recorder. A first-frame marker distinguishes recordings containing these actions from legacy AS recordings, which still generate scratch input locally. Marked recordings and live replay streams use their supplied actions. Note samples, hold bodies, mines and tail reversals all use the ordinary input rules.

## Frame and result contracts

Each input batch samples its final action-held state for all columns. The engine updates all landmines, then all HCN bodies, then consumes transitions in order. Generated scratch actions enter this same batch. Inputs queued by synchronous feedback remain for the next batch, including another batch at the same timestamp.

`InputTime` identifies the action transition. `BmsFrameContext.SimulationTime` identifies the application frame; `GameplayRate` carries its rate. Framework main results use the representation's matching frame and rate. Supplementary endpoints retain separate event, application and expected times. Resume rewind freezes completed judgement state while presentations follow the lead-in clock.

Passive and completion plans preserve the host's observed traversal order. Tokens invalidate replaced bindings, while epoch/step stamps prevent repeated completion in the same simulation step. Removal first drops the object from candidates, then performs its final passive check while its representation remains available. `ApplyDefaults` replaces a binding without replaying removal or discarding applicable history.

Framework history notification and core index refresh are separate synchronous checkpoints. The bridge implements `IBmsJudgementOutput` for core output and accepts framework lifecycle inputs; its name reflects both directions. It does not duplicate candidate selection or rule transitions. `ObserveSimulationStep` supplies feedback after the passive check and before a delayed tail can settle; the schedule checks the binding again after that callback. Concrete representations register their feedback callback at binding time, and the bridge checks and removes it by generation. The drawable base has no long-note-only update hook.

The synchronous output protocol is shared by the bridge and recording output:

| Boundary | State visible inside the callback | State after the boundary |
| --- | --- | --- |
| `PublishState` | Updated rule snapshot and the preceding index checkpoint. An LN head can still have no main result. | `BmsJudgementExecutionContext` refreshes membership and invalidates the passive deadline. |
| Main result | Framework result and endpoint array are already assigned. History notification precedes the bound index callback; a successful LN tail is still pending during these callbacks. | The rule continues its transition and publishes its final state. |
| Synthetic endpoint | The supplementary result is submitted through score, gauge and HUD consumers while the rule retains its current transition state. | The rule updates the endpoint state and publishes it at the mode's existing checkpoint. |
| Framework revert notification | The old main result remains readable; drawable notification follows the change to `ArmedState.Idle`. | Framework `Reset()` clears the main result, then history restoration and index rebuild establish the next input's candidates. |

### Host checkpoints

The production container and recording hosts use the same core schedule. A timestamp identifies simulation time, not an input batch: the engine increments `Batch` on every processing call, `Step` when time changes, and `Epoch` when time moves backwards. A binding token identifies a core registration independently of a drawable's local generation.

| Host entry | Order and representation requirement | Deduplication and result work |
| --- | --- | --- |
| Input batch | All columns' mines, then bodies, then transitions in arrival order; result targets require a current representation. | Callback inputs wait for a new batch even at the same time. Suppressed or rewind frames only update held inputs. |
| Ordinary traversal | Sample visual time and prepare geometry before layout completion; layout uses alive-map order and passive checks use subtree order. | Each phase runs once per epoch, batch and binding revision. Long-note feedback and completion run once per epoch, step, rewind revision and binding. |
| Deferred traversal | Sample visual time, then visit the active long-note list in reverse order. | Uses the same completion stamp as ordinary traversal; the final skin pass cannot settle a tail twice. |
| Removal | Drop candidate membership, mark the current handle as removing, run its last passive check while the representation exists, then unbind. | Last check occurs once per removing handle. Replaced tokens cannot submit. |
| Reapply | Free the old binding and observers, register the new generation, refresh configuration and traversal order. | Retains logical history and active membership. The bridge's binding owns the schedule handle, so one unbind releases both. |
| Ordinary rewind | After framework result restoration, rewind every registry state, restore active visuals, publish restored snapshots, then rebuild indexes. | Once per epoch and step. Inactive hold history is restored too; rewind invalidates existing completion stamps. |
| Pause and resume lead-in | Complete held tails at pause; preserve completed state during the lead-in and evaluate resume visibility against its target time. | Pause-completion publications retain their existing mode-specific visual semantics. Body and mine ticks stay frozen during resume rewind. |

Synchronous callbacks may remove or replace representations. The schedule rechecks tokens after passive checks and feedback; a late free or callback from an old generation cannot release or operate on its replacement.

## Presentation and audio

Layout prepares object bounds once, applies culling and reuses the prepared geometry. Long-note time observations are registered separately from layout; repeated layout reads do not append visual history or advance rules. Geometry observes the state before that traversal's passive and completion phases.

The binding selects an LN, CN or HCN presentation mapping. It receives a rule snapshot plus `BmsLongNotePresentationContext` (time, held input, main-result presence and resume target) and returns body animation, released styling, head pinning, lifetime, hit visibility, retirement, resume visibility and hold-light decisions. Drawable code applies those decisions and owns geometry, skin resources, visual history and pulse timing.

State publication, visual observation/layout, framework transforms and simulation feedback sample their own current context. Visual reads do not reuse a timestamp-only cache: held input may change in another batch at the same time without publishing rule state. Layout preparation and application share one prepared presentation. The last published presentation is retained only to detect synchronous head transitions. Successful LN/CN completion clears the body immediately. Natural body expiry retains the final child geometry while hiding the parent. Resume visibility is applied even when culling skips the drawable's children.

Key and background sample requests continue through `BmsSamplePlayback` and `BmsPcmPlaybackSession`. The PCM controller takes the audio manager's aggregate master volume; the framework's effects/sample-volume setting is not part of that route. Empty-press sample selection queries core activation and results through `BmsKeySoundCursor`.

## Verification

Rule and engine tests run without drawables through the recording output. Architecture tests inspect every core type's fields, method signatures and constructor parameters, including the `Rules` and `State` namespace trees, for UI, drawable and clock dependencies. Execution tests compare recording and framework outputs at fixed checkpoints. Player tests cover actual input generation, replay catchup, simultaneous stages, pool reuse, reapplication, rewind, Mods, culling and presentation.

| Contract | Regression entry |
| --- | --- |
| Mixed ordinary/long-note columns, active and pooled history | `BmsRegistryRecoveryTest.MixedColumnRewindsActiveAndPooledHolds` for LN/CN/HCN |
| Invalid mine order leaves previous order usable | `BmsRegistryRecoveryTest.RejectedMineOrderPreservesPublishedOrder` |
| Head without main result, HCN tail with remaining body, resume visibility | `BmsLongNotePresentationTest` |
| Same-time input changes and repeated geometry reads | `TestSceneBmsJudgementLifecycleExecution.VisualRefreshReadsHeldStateAtTheSameTimeWithoutPublishingRuleState` |
| State publication and result callbacks | `BmsLongNoteExecutionTest`, `BmsChargeNoteExecutionTest`, `BmsHellChargeNoteExecutionTest`, `TestSceneBmsJudgementCheckpoints` |
| Actual ordinary/deferred host sequences and same-time batches | `TestSceneBmsCoreSchedulingGeneration`, `BmsCoreJudgementScheduleTest` |
| Reapply, old callbacks/tokens, pooling and pause restoration | `TestSceneBmsJudgementLifecycleExecution`, `TestSceneBmsLongNoteLifecycle` |

`TestSceneBmsGitComparison` and `BmsGitComparisonRecorder` can be copied unchanged to the original Git baseline. They record ordered results, intermediate score/gauge histories, requested samples, hit lights and visual snapshots. `scripts/compare-judgement-traces.py` compares both directories and rejects missing scenarios. HCN effects are observed at the health processor, including an assertion on the forwarded event time.

Run the migration's selected regression scope from the repository root:

```powershell
./scripts/verify-judgement-migration.ps1
```

The script builds only this repository against existing reference binaries, then runs two filtered processes. Ordinary functional tests retain default JIT settings. Fixtures tagged `JudgementInstrumentation` use `osu.Game.Rulesets.BmsRuleset.Tests/judgement-instrumentation.runsettings`, which disables method inlining so entry probes remain observable after warmup. Both Git comparison sides must use that same settings file. Neither the production project nor the benchmark runtime is changed. Logs, exact arguments, TRX files, counts and comparison traces are saved in the reported artifact directory.

Tests must always use `--filter`. Performance benchmarks require explicit approval. Functional comparison does not establish performance acceptance; see the [migration acceptance requirements](hitobject-refactor-plan.zh-CN.md#performance-acceptance) while the migration remains open.
