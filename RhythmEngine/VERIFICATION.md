# Profile verification

This ledger tracks the current source review. Passing existing tests is not a
claim of complete original-client parity. Each row must have explicit evidence
and a regression or reference fixture before it is described as verified.

| Profile | Boundary under review | Evidence |
| --- | --- | --- |
| beatoraja | all rank/key/scratch/head/tail windows; candidate replacement and empty POOR including the excluded -500ms search boundary; missed heads; LN offset attribution; CN early release; HCN balance and gauge attribution; sampled mines; all nine gauges including EXCLASS/EXHARDCLASS; strict hard/grade guts thresholds | `9cddf911113e74da87458344bb85a4ea45431d0b`, `JudgeProperty`, `JudgeAlgorithm`, `JudgeManager`, `GaugeProperty`, `GrooveGauge` |
| LR2 compatibility | rank windows, LN result/error aggregation, early empty POOR, late BAD chaining, gauge bounds and low TOTAL | LunaticVibes `f6286f316f68770e07ca76cd0672ce4f5c50ef8d`; this is not an original LR2 executable oracle |
| LR2 beta3 100201 | original four rank tables with inclusive integer edges and 1000ms early empty-POOR search; LN Good head timeout, delayed head grade, release lenience and held-end resolution; same-input late-BAD chaining; native EX/combo and normal/easy/hard/hazard gauges | `LR2body.exe` SHA-256 `a46ef15343e41d3411771c83ac7b0c42d95e5f473db69c6b45a9584c07e94480`; `lr2-100201.json` executes original parser bootstrap blocks, input dispatch, tap/LN routines and score/gauge arithmetic. 124 head cases, 120 LN traces, four arbitration cases and 24 gauge sequences. |
| lazer mania | OD/rate windows, head/tail misses, dropped bodies, late regrab exclusion, overlap routing, combo effects, scoring potential and HP calibration; mixed judgement scoring sequence | osu! `9a10d935b05a3b896f4bd811d620f2023331db93`; `lazer-score.json` is consumed by `ProfileConformanceTest` |
| stable mania | native/convert windows; one combined hold grade; reflected early/late release error; regrab and early-drop Good cap; 100ms body combo/HP; held-tail completion; exact late-OK tap expiry; live increment truncation/final rounding; 384-combo Bonus reset; separate stable recovery calibration | Shipped `osu!.exe` SHA-256 `3adc2d001aacced5f92ba8ae24121980d5e23f5653f37d14cf4765bca1abd9f9`, MVID `aa9db4d0-6aa6-4bb7-2f5e-8c1ae12ad43a`. `stable-holds.json` interprets original CIL method `0x06003a0d`; the manager timeout method is `0x060049db`. Original score/Bonus/Combo/HP CIL method `0x0600119e` also supplies 1260 mixed-result states; chart recovery calibration is compared statically. Complete application frame traces remain outside those samples. |
| Open2Jam compatibility | fixed/beat windows including tempo boundaries, hold lifecycle, JAM/pill ordering and caps, score floor, difficulty HP | `11384b3ca957828ae66a72c9e28edd42c97952d5`; the original O2Jam client is not the reference |
| O2Jam e-Games 3.38 | original clock interpolation and integer tick conversion; 6/18/25 tick grades; nearest note selection with backward ties; hold release and automatic head/tail MISS; raw head BAD immediately missing its tail; Bad-to-Cool pills; strict JAM threshold; Good JAM score; displayed combo; Easy/Normal/Hard HP | `OTwo.exe` SHA-256 `3f40d7f4e828da8e97bab391597d73b16d9580d46ff51d24ee185e1b1405a38b`; `o2jam-338.json` executes the original input loop, clock mapper, timeout pass, event handler and score/HP methods. 175 head inputs, 84 hold traces, 24 expiry boundary cases and 483 score/HP states. |
| Etterna | scaled windows and fixed W5 search; hold/roll life and retrigger boundaries; taps inside active holds; lifts; nominal-time mine sampling, nearest-row conflicts, forward ties and passed-mine exclusion; Wife3; combo; gameplay lifebar | `2d706147cc4bcf95a8ceb24dc4f4129be207f803`, Player/ScoreKeeperNormal/LifeMeterBar/fallback metrics. The gameplay Held recovery is +0.008; the replay estimation helper's zero is a separate path. 390 states in `etterna-life.json` execute the original float `ChangeLife` method, including dead-state latching. The source-tempo row search includes the 48 rows/beat padding; optimized nerv-index search state, combo segments and alternate life policies remain outside this normalized preset. |
| Malody classic PC 4.3.7 | A-E base/rank thresholds; separate integer Bonus and float HP; 110ms release lenience; 100ms body combo/regrab; cumulative displayed score floor, accuracy, combo and Bonus clamps | x86 `malody.exe` SHA-256 `d0cf9ecb493f3fdbd1e3ce48cbfad06ef816d19931ab0c51d9e9655f5eb63ce3`; `malody-pc-437.json` executes original constructor/rank/head/release/tick and Score routines. 25 native manual Key routing/expiry/tick block traces cover loaded same-lane objects; application scheduling and physical control counts remain outside the fixture. |
| Malody classic Android 4.3.7 | all five ranks and both window sides; fixed late MISS bound; 130ms release lenience; 100ms body combo/regrab; native Score, accuracy, HP, Bonus and combo | APK SHA-256 `7b6f5650481f6b1e04ebe8eec93f18c2205521522f94da3f129cfb0c37044138`; arm64 library SHA-256 `1140dc9374886d3326dc067bf4576159bc3c18d1dc3f34c2aaf5a41d55bc3bb0`. `malody-android-437.json` contains original judgement/hold and 580 Score/HP states, invariant under two inverse cipher permutations, plus 25 original manual Key routing/expiry/tick block traces. |
| Malody V RC 2.0 | Standard/Pro window generator; strict grade edges; optional early MISS; default hold ticks/release flush; float HP and cumulative score floor | Explicit `MalodyVVersion.Rc20`. GameAssembly SHA-256 `21c73a27b7e129bf63fd22f530376388f190f25a1c51a845da899589c8e84a9f`; linked metadata SHA-256 `14ec77e30ea7aaea4bee886659274fd0c6b66ba5466e8f776fdf9755632531ff`. Static functions `0x180422660`, `0x180422850`, `0x180422970`, `0x180415cb0`, `0x1802a9630`, `0x180415af0`. |
| Malody V 6.7.22 | all five Standard/Pro ranks; original Key input/expiry/repeat routing; input-generated and timed head MISS also missing the graded tail; strict float grade comparisons; scaled and converted windows; Good+34ms MISS cap; critical sub-count; unscaled Good head expiry; release tick draining and regrab; separately scored promoted tails; missed-head tail MISS; held-tail timeout; float Bonus factor and double score accumulation | Explicit `MalodyVVersion.V6722` (default for V). arm64 library SHA-256 `2f1a4f10a95ce62ba4fc37f4f848c95826c89214d239f98077b237cdd34ce16c`; metadata SHA-256 `abe99b3b5571db549ac8b5414d804375c04ff9c299465207c36345b9beeb8e0a`. `malody-v-6722.json` contains 60 native window tables, 2184 comparisons, 260 release traces, 300 score/HP states and 100 routing traces. |

Original source checkouts are read-only. Reference generators compile in ignored
artifacts directories and write their JSON to Reference by default, or an explicit
output path. They must not use BmsRuleset's legacy implementation as an oracle.

The standalone goal excludes a game adapter. Hosting remains independently
tested; no gameplay project references the new engine.

Consuming tests use the checked-in JSON; original source checkouts and binaries
are required only when regenerating fixtures. `Reference/source-manifest.json`
records source revisions and digests. Run the standalone checks from the
repository root:

```sh
dotnet test RhythmEngine/RhythmEngine.Tests/RhythmEngine.Tests.csproj -c Release --filter FullyQualifiedName~RhythmEngine.Tests
```

Gauge flow fixtures execute the original
`GrooveGauge.Gauge` class, including modifiers, float accumulation, half-rate HCN
updates, guts, clamping and dead-gauge latching: 2160 state transitions across all
nine seven-key gauges and two TOTAL values.

The Android generator executes the pinned constructor, rank setter,
head/release/tick routines and loaded manual Key traversal/expiry/tick blocks
in Unicorn 2.1.4. It restores ELF relative relocations,
uses the real note vtable, and stubs the hit-error statistics collector. The
fixture covers 122 head inputs, 20 release inputs and 10 scheduled ticks. It does
not execute Android, JNI, rendering or the application scheduler. Score/HP code
now has separate native execution samples with controlled cipher tables.
Fixture generation requires the downloaded library; consuming tests need only
the checked-in JSON. The generator rejects every other binary digest.

The PC fixture also executes the actual Score constructor, rank setter, update
and public score/accuracy getters, covering 116 steps at each of five ranks.
PC routing enters `0x5a6e75`, expiry `0x5a6372..0x5a63e3` and held ticks
`0x5a6412..0x5a64ee`; the clock block uses the original 160ms constant. Android
routing enters `0x39b9f0`, expiry/held ticks `0x39f3b0..0x39f548` and the
original 170ms clock block. Both harnesses supply valid loaded carrier/deque
layouts and sink event/presentation callbacks. Each has 25 traces across five
ranks for missed-head regrab, active-hold blocking, explicit release, held tails
and exact expiry boundaries. Their result ordering is compared independently
of frame-delayed notification timestamps. Physical control-count bookkeeping,
application scheduling, rendering and audio are outside the fixture.

Its byte permutation container is initialized with valid inverse tables; identity
and rotation-37 keys produce the same results. Score/HP arithmetic is not stubbed.

The V 6.7.22 fixture executes original ARM64 window generation, comparisons,
Key hold methods and Score arithmetic. Runtime metadata setup, GC write barriers,
managed array allocation and hit-error statistics are supplied by the harness.
Encrypted score wrappers use plain 64-bit getter/setter storage; the original
instructions still perform all numerical scoring, float rounding, HP, Bonus,
combo and grade counting. The additional 100 Key traces execute original `OnIndexedInput`
(`0x1b389b8`), expiry (`0x1b385ac`), base/repeat dispatch (`0x1b2eb1c`,
`0x1b2ebe4`) and Key result processing (`0x1b38d2c`), with collection storage
and the notification sink supplied by the harness. Cases include missed heads,
active-hold blocking/regrab, overlapping taps, explicit releases, held tails
and the exact Good boundary. Silent native notifications are excluded from
judgement comparisons. Unity and the application scheduler are outside these
fixtures. E-style end judgement scores both the head and graded tail. An input-generated
head MISS also immediately misses the graded tail, matching the original caller.

LR2 beta3 100201 was obtained as a complete CRC-checked ZIP. Its archive SHA-1
matches archive.org's independent `lr2beta3` record. The fixture enters the original
rank/gauge bootstrap with completed-parse note counts, TOTAL and options; all
bootstrap instructions are original. The parser itself is outside this fixture.
The native input loop then mutates real note-cell layouts and calls its score
reducer. Replay/ghost output, audio and skin timer side effects are stubbed.
`Lr2Beta3Profile` exposes those pinned rules separately from compatibility data;
its current source contract uses nonnegative integer-millisecond note endpoints.

The stable executable was downloaded directly from ppy's installer endpoint; it
is itself a managed `osu!.exe`. Static ILSpy 9.1.0.7988 output and REA metadata/CIL
agree on the hold, manager, score, HP and calibration paths. The CIL fixture
interpreter runs the original hold result method (372 decoded instructions),
rejects missing state/unsupported opcodes. Hold-result callbacks supply only
visual/audio and display-list effects; the score path also supplies plain managed
score/combo/HP storage accessors and no-mod metadata. All arithmetic and branch
instructions on the covered paths execute from the admitted CIL. Its 360
released-state and six held-state samples cover numeric hold resolution; 1260
mixed score/HP states also exercise the original score reducer. Consuming session tests restrict releases to the manager's accepted
interval; a release at or after end+OK is blocked before that method is called.
The executable is never loaded into the CLR or launched. Score/HP CIL fixtures
and behavior tests also cover 100ms body combo/recovery, short-hold regrab, live score truncation,
rounded final score and the 384-combo Bonus reset. The original mania recovery
calibration simulates one perfect per hold; lazer's endpoint simulation has two.

O2Jam 3.38 was recovered from the complete 112767299-byte archive, whose ZIP CRC
was checked by 7-Zip. Its InstallShield SFX members were carved without execution;
unshield 1.6.2 extracted the original `OTwo.exe`. Native fixtures supply valid input
ring buffers, note lists, clock lists, difficulty coefficients and JAM thresholds.
Audio and presentation notifications are stubbed. Both x86 generators explicitly
set x87 control word 0x027f for MSVC double precision and round-to-nearest; Unicorn's
uninitialized default control word is not used. The original O2Jam profile requires
the normalized tick/clock data instead of inferring it from rounded note times.

Classic Score offsets +72/+76
are float HP and integer Bonus respectively; Android uses +80/+84. Bonus updates
are +1/-2/-8/-32 percentage points. HP updates are +0.012/+0.004 and
-0.02/-0.05 times (rank+1) for Good/Miss. Tests keep those state variables separate.

The current-V Turbo panel defines the F/P fields:
`PanelTurbo` Setup/Save (`0x19e5fa8`/`0x19e60d0`) map E/F/M/P to settings
bytes 16/17/18/19. F is `toggleFinger`; its judgement read is in Slide's
`OnLoadChart` (`0x1b40788`) and its other observed use is replay recording
(`0x1b05dfc`). Key's load path reads E only (`0x1b383d4`), and its
`bnf.Init` reads E/M/speed (`0x1b459cc`). P is `togglePause`, consumed by
the pause panel (`0x19d503c`), outside note judgement. Neither is exposed as
an invented Key timing/hold policy. The original queued judge's `IsDied`
(`0x1b400d0`) checks float HP <= 0; host pause/termination scheduling is
separate from the engine's configured failure latch.

`AuditRegressionTest` covers real-session regressions for LR2
late short-LN completion, Etterna pre-start grace and missed-head hold intervals,
lazer Unique logical inputs, beatoraja resolved-head empty POOR and stable's
future judged-note lock.

`HostLifecycleTest` exercises a presentation-only `RecordingHost`:
silent heads, same-time releases/regrabs, eight hold presets, asymmetric custom
windows and scoring, recoverable health and score-dependent outcomes,
resolved-but-retained input targets, sustained bodies after tail decisions,
ordered retirement, source-order timers, lane identity, frame partitioning,
immutable snapshots, reconstruction, reset and clone isolation. The consumer
imports Hosting and uses no profile types, grade names or timing rules.

These checks establish the standalone presentation contract; they do not establish complete client parity or audio-resource behavior. Fixed digital lanes remain the supported input model; analog/cross-lane input and global session timers are outside this contract. Immutable health layers support configured concurrent gauges.

The BmsRuleset adapter has separate integration tests for physical input ordering,
replay reconstruction, strict microsecond boundaries, delayed frames, layered
gauge/score accumulation and presentation detachment. Core contract tests cover
input dispatch ranges, appended batches and layered health clone/reset isolation.
These integration tests validate the host boundary; they are not source evidence
for the independent profiles above.

Evidence boundaries before complete original-client parity can be claimed:

- Classic PC/Android fixtures now execute original manual Key traversal and
  expiry/tick blocks as well as judgement/hold and Score/HP routines. Their
  physical control-count bookkeeping is outside the fixture. Current-V Key
  routing and expiry execute original instructions. Application scheduling,
  pause and failure presentation remain host responsibilities.
- Extend LR2 traces to multiple controls, scratch routing, fractional chart times,
  dynamic RANK events and course-specific rules before exposing those variants.
  The explicit beta3 preset has native fixtures for its supported normal-key,
  integer-clock LN and four-gauge contract.
- Extend stable CIL fixtures to the complete manager routing; hold resolution
  and score/Bonus/Combo/HP now have original-CIL samples. Full application
  frame/input traces remain outside those fixtures.
- Etterna beat-row search padding and float life accumulation are covered.
  The historical optimized nerv-index branch depends on global input history
  and is not reproduced. Chart combo segments and non-default life policies
  must be normalized or implemented before exposing those variants.
