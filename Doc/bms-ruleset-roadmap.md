# BmsRuleset 开发路线

本文档是 native BMS ruleset 的开发 todo。每个阶段都包含验收目标和测试步骤。执行顺序以稳定迁移为优先：先建立 BMS domain 和 parser，再替换 mania adapter，最后补全 BGA、gauge、editor 等高级能力。

## 全局验证命令

每个阶段完成后至少运行：

```powershell
dotnet build "osu.Game.Rulesets.BmsRuleset"
dotnet test "osu.Game.Rulesets.BmsRuleset.Tests"
```

如果 `dotnet test` 因 visual test runner 或环境限制不可用，记录失败原因，并运行：

```powershell
dotnet run --project "osu.Game.Rulesets.BmsRuleset.Tests"
```

## Phase 0: 文档和边界确认

- [x] 明确 BmsRuleset 是 native BMS design，不以 mania object 为目标模型。
- [x] 修正 `bms-parsing-guide.md`：`#LNTYPE 2` 必须支持。
- [x] 修正 `bms-parsing-guide.md`：Control Flow import 保留、play runtime 选分支。
- [x] 修正 `bms-parsing-guide.md`：补充已知 BMS tags/header/channel 说明。
- [x] 修正 `bms-parsing-guide.md`：tick-first，time 只是 projection。
- [x] 新增项目设计文档 `bms-ruleset-design.md`。
- [x] 新增开发路线文档 `bms-ruleset-roadmap.md`。

测试步骤：

1. 打开 `Doc/bms-parsing-guide.md`，确认没有把 branch selection 描述为 import-time preprocessing。
2. 搜索 `mania object`、`ManiaBeatmap`，确认只作为当前 adapter 或反例出现。
3. 打开 `Doc/bms-ruleset-design.md`，确认包含 import set/beatmap 区分、tick-first、runtime control-flow、minimal resource import。
4. 运行 `dotnet build "osu.Game.Rulesets.BmsRuleset"`。

## Phase 1: Native BMS Domain Types

- [x] 新增 `Timing/BmsTick.cs`。
- [x] 新增 `Timing/BmsTickResolution.cs`，默认 `192`，但可按 chart 动态扩展。
- [x] 新增 `Timing/BmsTimingMap.cs`。
- [x] 新增 `Timing/BmsTimingSegment.cs`。
- [x] 新增 `Beatmaps/BmsMetadata.cs`。
- [x] 新增 `Beatmaps/BmsResourceManifest.cs`。
- [x] 新增 `Beatmaps/BmsLayout.cs` 和 `BmsLayoutVariant.cs`。
- [x] 扩展 `BmsBeatmap`，加入 layout、timing map、sample definitions、BGM sample events。
- [x] 扩展 `BmsHitObject.TickInfo`，加入 `Tick`、`EndTick`、`TickResolution`；`BmsHitObject` 保留 source/sample metadata。
- [x] 保持 `StartTime`、`Duration` 由 tick projection 设置，作为兼容字段。

验收标准：

- `BmsHitObject` 可以表达 normal note 和 long note 的 tick 起止。
- `BmsBeatmap` 可以表达 chart layout、timing map、resource manifest 和 non-note events。
- 旧的 converter tests 若仍存在，应继续通过或被替换为 native tests。

测试步骤：

1. 新增 `BmsTickTest.TestIntegerComparison`：`0 < 1 < 192`。
2. 新增 `BmsTickResolutionTest.TestDefaultResolution`：无特殊分割时 resolution = `192`。
3. 新增 `BmsTickResolutionTest.TestExpandsForFiveWayPayload`：payload length `5` 时 resolution 至少扩展到 `960`。
4. 新增 `BmsTickResolutionTest.TestExpandsForMeasureLengthDenominator`：`#00102:0.333333` 或 `1/3` 表达时 resolution 能让 measure tick length 成整数，或进入 rational fallback。
5. 新增 `BmsTickTest.TestMeasurePosition`：默认 resolution 下 measure 1 的 pair `1/2` 是 tick `288`。
6. 新增 `BmsTickTest.TestMeasurePositionExpanded`：resolution `960` 下 5 等分 pair ticks 为 `960, 1152, 1344, 1536, 1728`。
7. 新增 `BmsTimingMapTest.TestProjectTickToTimeAt120Bpm`：默认 resolution 下 tick `192` -> `2000ms`。
8. 新增 `BmsTimingMapTest.TestProjectTickToTimeExpandedResolution`：resolution `960` 下 tick `960` -> `2000ms`。
9. 运行 `dotnet test "osu.Game.Rulesets.BmsRuleset.Tests" --filter BmsTick`。
10. 运行完整 build/test。

## Phase 2: Parser Foundation

- [x] 新增 parser namespace，例如 `Beatmaps/Parsing`。
- [x] 实现 `BmsLineParser`：header、indexed header、channel line、comment line。
- [x] 实现 channel payload pair splitter。
- [x] 实现 raw command preservation。
- [x] 实现 warning collector。
- [ ] 实现 known/unknown tag preservation。
- [x] 实现 duplicate header rule：scalar last wins，multi tags append。
- [ ] 实现 duplicate channel merge rule。

验收标准：

- parser 不创建 gameplay object，只创建 intermediate parse state。
- invalid line 不会中断整个 parse，除非 corrupt timing 无法恢复。
- unknown known-extension tag 能保留 raw line。

测试步骤：

1. `TestParseHeaderWithSpaces`：`#TITLE My Song` -> `TITLE` + value。
2. `TestParseHeaderWithTab`：`#ARTIST\tComposer` 可解析。
3. `TestParseLowercaseCommand`：`#wav0a clap.wav` -> `WAV` index `0A`。
4. `TestParseChannel`：`#00111:0100` -> measure `1` channel `11` data `0100`。
5. `TestOddPayloadWarns`：`#00111:010` 记录 warning 并忽略尾字符。
6. `TestBase36`：`ZZ` -> `1295`，`0A` -> `10`。
7. `TestDuplicateMerge`：示例三行 merge 为 `11 22 33 22 66 22 44 00`。
8. `TestBgmDoesNotMerge`：两行 `#00101` 同 tick 保留两个 BGM events。
9. `TestMeasureLengthLastWins`：重复 `#00102` 使用最后一行。
10. 运行 `dotnet test --filter BmsLineParser` 和完整 test。

## Phase 3: Control Flow AST and Runtime Branch Selection

- [ ] 实现 `BmsCommandScript`。
- [ ] 实现 `#RANDOM`、`#SETRANDOM`、`#IF`、`#ELSEIF`、`#ELSE`、`#ENDIF`、`#ENDRANDOM` AST parse。
- [ ] 实现 `#SWITCH`、`#SETSWITCH`、`#CASE`、`#SKIP`、`#DEF`、`#ENDSW` AST parse。
- [x] 实现 conservative import-time resource scan across all possible branches。
- [ ] 实现 runtime `BmsBranchState`。
- [x] 实现 decode-time active command stream materialisation。
- [x] 设计 replay branch decision serialization。

当前实现说明：playable conversion 时 materialise active stream；`#RANDOM/#SWITCH` 每次 play conversion 使用新的随机值，因此不同 play 可以不同。Replay branch decisions 通过 hidden system mod `BmsModBranchReplay` 保存到 `ScoreInfo.Mods`，replay conversion 在 hit object materialise 前复用这些 values。嵌套 random/switch、random 内 if 外命令、switch fallthrough 和 `#DEF` 已有覆盖测试。完整 AST、`BmsBranchState` 仍未完成。

验收标准：

- import 不固定随机分支。
- gameplay load/decode 可以输出 active stream。
- replay 可以使用保存的 branch state 重放同一 stream。

测试步骤：

1. `TestRandomAstPreserved`：`#RANDOM 2` 两个 `#IF` branch 都存在 AST。
2. `TestRandomIfMaterialisesSelectedBranchOnly`：selected branch 只 materialise 对应 command。
3. `TestRandomDecisionsCanVaryBetweenDecodes`：不同 decode/play 可以使用不同 branch value。
4. `TestNestedRandomUsesIndependentBranchDecisions`：嵌套 random 独立消耗 active branch decisions。
5. `TestInactiveNestedRandomDoesNotConsumeDecision`：inactive nested random 不消耗 decision。
6. `TestChannelInsideRandomButOutsideIfRemainsActive`：random scope 内、if 外命令保持 active。
7. `TestSwitchInsideRandomUsesBothBranchDecisions` / `TestRandomInsideSwitchUsesBothBranchDecisions`：mixed random/switch 正确 materialise。
8. `TestSwitchFallsThroughUntilSkip`：`#SWITCH` case fallthrough 到 `#SKIP`。
9. `TestSwitchDefault`：无 matching case 时使用 `#DEF`。
10. `TestBranchResources`：branch A 用 `a.wav`、branch B 用 `b.wav`，manifest 包含两者。
11. `TestConverterUsesReplayBranchDecisions`：同一 branch state materialise 结果稳定。
12. `TestBranchReplayModAppliesDecisionsToConverter`：hidden replay mod 把 decisions 传入 converter。
13. 运行 `dotnet test --filter BmsBeatmapDecoderTest`。

## Phase 4: Tick Timeline, BPM, STOP, Measure Length

- [x] 实现 measure start tick table。
- [x] 实现 dynamic tick resolution 计算，默认 `192`，按 payload 和 measure length LCM 扩展。
- [x] 实现 channel `02` measure tick length。
- [x] 实现 `#BPM` initial BPM。
- [x] 实现 channel `03` hex BPM。
- [x] 实现 `#BPMxx` + channel `08` extended BPM。
- [x] 实现 `#STOPxx` + channel `09` STOP。
- [x] 实现 same-tick object/BPM/STOP ordering。
- [x] 实现 `BmsTimingMap.ProjectTickToTime()`。

验收标准：

- 所有 event 先有 tick，再投影 time。
- STOP 影响后续 tick，不影响同 tick note。
- BPM 变化会改变后续 tick 的 msPerTick。
- `192` 不能整除的 payload 不产生小数 tick；resolution 应动态扩展。

测试步骤：

1. `TestDefaultMeasureTicks`：measure 0 tick `0`，measure 1 tick `192`，measure 2 tick `384`。
2. `TestHalfMeasureTicks`：`#00102:0.5` 后 measure 2 tick `288`。
3. `TestBasicBpm03Hex`：`#00103:FE` -> BPM `254` at correct tick。
4. `TestExtendedBpm08`：`#BPM01 240` + `#00108:01` changes BPM at tick `192`。
5. `TestStopDuration`：`#STOP01 96` at 120 BPM -> `1000ms`。
6. `TestStopSameTick`：note at STOP tick remains `2000ms`，next measure delayed。
7. `TestBpmBeforeStopSameTick`：same tick BPM 240 then STOP 96 -> STOP `500ms`。
8. `TestFiveWayPayloadExpandsResolution`：payload length 5 makes normal measure resolution `960` and creates integer ticks。
9. `TestTupletPayloadCanStayDefaultWhenDivisible`：payload length 3 in normal measure creates integer ticks `0`、`64`、`128` relative to default resolution。
10. `TestResolutionExplosionFallsBack`：极端 pairCount LCM 超过阈值时进入 rational/fixed-point fallback 并 warning。
12. 运行 `dotnet test --filter Timing`。

## Phase 5: Notes, Layout, Long Notes

- [x] 实现 5K SP layout。
- [x] 实现 7K SP layout。
- [x] 实现 10K/14K DP layout。
- [x] 实现 PMS 9K layout。
- [x] 实现 PMS DP layout。
- [x] 实现 BME-type PMS 9K layout。
- [ ] 实现 visible notes `11-29` layout-aware parse。
- [ ] 实现 invisible notes preservation `31-49`。
- [x] 实现 landmine preservation `D1-D9`、`E1-E9`。
- [x] 实现 `#LNTYPE 1`。
- [x] 实现 `#LNTYPE 2`。
- [x] 实现 `#LNOBJ`。
- [x] 实现 sparse chart `TotalColumns` from layout, not max note column。

验收标准：

- chart 的 column count 与 layout 一致，即使只有一个 lane 有 note。
- `17`/`27` 不会在普通 5K/7K/DP 中误变成 playable lane。
- 三种 LN 都能得到正确 `Tick`/`EndTick`。

测试步骤：

1. `Test5KMapping`：`16 -> 0`，`11 -> 1`，`15 -> 5`，`TotalColumns = 6`。
2. `Test7KMapping`：`18 -> 6`，`19 -> 7`，`TotalColumns = 8`。
3. `TestDpMapping`：任意 2P channel 触发 DP layout。
4. `TestPmsMapping`：`.pms` + `25` -> column `8`，`TotalColumns = 9`。
5. `TestBmeTypePmsMapping`：`.pms` + `17` maps only under BME-type PMS。
6. `TestFreeZoneIgnoredIn7K`：`#00117:01` 在 7K 不创建 normal note，并产生 warning。
7. `TestLnType1`：`#00151:01` + `#00351:01` -> one hold tick `192` to `576`。
8. `TestLnType2`：`#00151:11110000` -> one hold from cell 0 to cell 2。
9. `TestLnObj`：visible `01` 到 `ZZ` terminator -> one hold，terminator 不创建 normal note。
10. `TestSparseTotalColumns`：只有 channel `19` 的 7K chart 仍 `TotalColumns = 8`。
11. 运行 `dotnet test --filter Layout` 和 `dotnet test --filter LongNote`。

## Phase 6: Resource Manifest and Set Import

- [x] 设计 `BmsImportPlanner`。
- [ ] 设计 `BmsSetManifest`。
- [ ] 实现 selected file import planning。
- [x] 实现 folder import planning。
- [x] 实现 recursive import planning。
- [ ] 实现 archive import planning if current osu import API exposes archive contents。
- [ ] 实现 resource dependency graph。
- [x] 实现 `#PATH_WAV` search。
- [x] 实现 extension fallback search。
- [x] 实现 set-level dedup by content hash。
- [x] 修改 `BmsFileImporter`：一个 set 多个 BeatmapInfo。
- [x] 修改 `BmsFileImporter`：只导入 used files。

验收标准：

- 同目录多个 chart 合成一个 `BeatmapSetInfo`。
- 未被任何 chart event 引用的 sibling file 不导入。
- 被不同 chart 共用的资源只存储一次。
- branch 中可能被 runtime 选中的资源会导入。

测试步骤：

1. 创建 temp folder：`normal.bms`、`another.bms`、`used.wav`、`unused.wav`。
2. `normal.bms` 引用 `used.wav`，没有引用 `unused.wav`。
3. 调用 import planner，断言 manifest charts count = 2。
4. 断言 resource manifest 包含 `used.wav`，不包含 `unused.wav`。
5. 增加 branch：branch 1 用 `a.wav`，branch 2 用 `b.wav`，断言两者都包含。
6. 增加 `#PATH_WAV sounds`，资源在 `sounds/kick.wav`，断言解析成功。
7. 增加同名不同目录资源，断言 named usage 保留相对路径。
8. 使用 Realm integration test 导入 temp folder，断言一个 set 多个 beatmaps。
9. 断言 `BeatmapInfo.MD5Hash` 是 chart 文件 MD5，不是 `RealmFile.Hash` SHA-256。
10. 运行 import tests 和完整 build/test。

## Phase 7: Native DrawableRuleset and Playfield

- [x] 新增 `UI/BmsDrawableRuleset.cs`。Subscribes to `HealthProcessor.Failed` via DI for failed score saving.
- [x] 新增 `UI/BmsPlayfield.cs`。Pre-caches one `SkinnableDrawable` per `HitResult` at `LoadComplete`; reuses them on every judgement display without per-hit allocations.
- [x] 新增 `UI/BmsColumn.cs`。
- [x] 新增 `Objects/Drawables/DrawableBmsHitObject.cs`。
- [x] 注册 object pool。
- [x] 实现 time-based render position。
- [x] 实现 judgement line。
- [x] 实现 native stage/column visuals and scratch-excluded centering。
- [x] 实现 notes fill lane width and anchor at lane left-bottom。
- [x] 重构 Skinning 层：`BmsEmbeddedSkin` 改为 `ISkin+IDisposable`，暴露 `internal IResourceStore<byte[]> Resources`；内置皮肤枚举 `LegacyOld`/`LegacyModern`。
- [x] 新增 `BmsLegacySkinTransformer`：继承 `LegacySkinTransformer`，`override IsProvidingLegacyResources`，`Lazy<bool> hasBmsResources`，`UnsupportedSkinComponentException`。
- [x] 新增 `BmsBuiltInSkinTransformer`：内置皮肤适配；全局 HUD 透传套 `HealthFilteredHudContainer`；BMS 专用组件/hit result/ruleset HUD 返回 `null`。
- [x] 新增 `BmsSkinConfigurationDecoder`：自定义 `skin.ini` `[BMS]` 节解析，绕过 `LegacySkinDecoder`；新增 `Decode(IResourceStore<byte[]>)` 首选重载。
- [x] `CreateSkinTransformer` 改为显式 switch：built-in → `BmsBuiltInSkinTransformer`，`Skin` → `BmsLegacySkinTransformer`，纯 `ISkin` → `null`。
- [x] Bug fix：`BmsStage.Update()` 加入 `JudgementArea.X = nonScratchCentre - DrawWidth / 2`，修复判定容器未对齐非 scratch 列中心。
- [x] Bug fix：`BmsStage.updateFromSkin()` 边框线宽查询改用 `BmsSkinComponentLookup(ColumnBackground, layoutVariant, column)` + `ManiaColumnIndex`，修复 BME 7K/BMS 5K 的 `IndexOutOfRangeException`。
- [x] Bug fix：`BmsPlayfield` 判定 drawable 加入 `Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre`，修复图像贴近舞台左边缘问题。
- [x] 实现 LN body render。
- [x] 实现 STOP freeze render。
- [x] `BmsRuleset.CreateDrawableRulesetWith()` 改为返回 `BmsDrawableRuleset`。
- [x] 移除 `ppy.osu.Game.Rulesets.Mania` NuGet 依赖。

验收标准：

- 不再使用 `DrawableManiaRuleset` 作为默认 gameplay。
- Native gameplay 不依赖 mania renderer。
- 当前 placeholder renderer 按 projected time 计算 note 位置。
- LN body 后续根据 `StartTime`/`Duration` 或 native tick 数据绘制。

测试步骤：

1. 新增 `TestSceneBmsPlayfieldBasic`，加载 5K chart，确认 columns count = 6。
2. 新增 `TestSceneBmsPlayfield7K`，加载 7K chart，确认 columns count = 8。
3. 新增 visual step：note 按 projected `StartTime - Time.Current` 下落。
4. 新增 visual step：LN 从 start time 延伸到 end time。
5. 手动运行 visual test runner，进入 BMS player scene，确认无 crash。
6. 运行完整 build/test。
7. `TestSceneBmsPlayfield` 直接构造 simulated playfield，验证 note lane bounds 和 non-scratch centering。

## Phase 8: Input, Judgement, Score, Gauge

- [x] 新增 `BmsAction`。
- [x] 新增 BMS input manager。
- [x] 实现 layout-to-action mapping。
- [x] 实现 `BmsHitWindows` with `#RANK`-driven LR2 windows (not mania hit windows). `BmsHitWindows.BadWindow` exposed as `public const double` for use by other systems.
- [x] 实现 `#RANK` judgement preset。
- [ ] 实现 dynamic `#EXRANKxx` / channel `A0` preservation，后续接入 judgement change。
- [x] 实现 key-sound press playback。`findNextSoundHitObject` skips notes more than `BadWindow` (200 ms) in the past, so late-BAD keypresses play the correct note's keysound.
- [x] 实现 BGM autoplay playback。
- [x] 实现 `BmsScoreProcessor` EX score / counts / DJ LEVEL / combo reset on BAD and POOR.
- [x] 实现 `BmsHealthProcessor` Normal gauge (`#TOTAL`-driven, starts 20%, clear at ≥80% end, fail at 0).
- [x] 实现 Empty POOR: `RegisterEmptyPoor` on both processors; POOR image displayed via pre-cached `SkinnableDrawable`.
- [ ] 实现 hard/easy gauge mods。

验收标准：

- 玩家按 BMS action 命中 BMS note。
- key sound 在 hit 时播放，BGM 自动播放。✓
- Late BAD keypress plays the current note's keysound, not the next note's. ✓
- score result 显示 BMS counts 和 EX score。✓
- gauge 无 passive drain，按 BMS Normal gauge 规则变化。✓
- Empty POOR breaks combo, drains gauge, shows POOR image. ✓
- hard/easy gauge mods 待实现。

测试步骤：

1. `TestDefaultKeyBindings5K`：5K layout has scratch + 5 keys。
2. `TestDefaultKeyBindings7K`：7K layout has scratch + 7 keys。
3. `TestHitWindowRank`：`#RANK 2` 设置对应窗口。
4. `TestNoteHit`：在窗口内 press 对应 action -> `Great`/`Perfect`。
5. `TestNoteMiss`：超过 miss window -> `Miss`。
6. `TestWrongColumnDoesNotHit`：按错 column 不命中目标 note。
7. `TestLongNoteHold`：按住到 tail 得到 LN 成功。
8. `TestLongNoteReleaseEarly`：提前松开得到 miss/bad。
9. `TestExScore`：PGREAT/GREAT 计分符合设计表。
10. `TestGaugeNoPassiveDrain`：无 note 时间段 gauge 不下降。
11. 运行 scoring/input tests。

## Phase 9: Replay and Runtime Branch Reproducibility

- [x] 新增 `BmsReplayFrame`。
- [x] 新增 `BmsFramedReplayInputHandler`。
- [x] 新增 `BmsReplayRecorder`。
- [x] 新增 `BmsAutoGenerator`。
- [x] Replay metadata 保存 branch decisions。
- [ ] Replay metadata 保存 layout id 和 parser compatibility version。
- [x] Autoplay 支持 normal notes and scratch lanes。
- [x] Autoplay 支持 LN release semantics。

验收标准：

- 同一 replay 不重新随机 branch。
- Autoplay 可以完整命中 basic/LN chart。
- Replay frame 使用 BMS action，不使用 mania action。

测试步骤：

1. `TestReplayStoresBranchDecision`：random chart replay 保存 selected values。
2. `TestReplayUsesStoredBranchDecision`：换 seed 后 replay 仍 materialise 原 branch。
3. `TestReplayFrameActions`：frame contains `BmsAction`。
4. `TestAutoplayBasic`：basic notes all hit。
5. `TestAutoplayLongNote`：LN head/tail 正确 press/release。
6. Visual runner 加载 autoplay chart，确认可完成。

## Phase 10: BGA, Text, Option Events

- [ ] 实现 `BmsBgaContainer`。
- [ ] 实现 `#BMPxx` image load。
- [ ] 实现 channel `04` base BGA。
- [ ] 实现 channel `07` layer。
- [ ] 实现 channel `06` poor BGA。
- [ ] 实现 channel `0A` layer 2。
- [ ] 实现 `#BGAxx` cropped region。
- [ ] 实现 alpha channels `0B-0E`。
- [ ] 实现 `#TEXTxx` + channel `99` text event。
- [ ] 实现 `#OPTION` initial runtime option preservation。
- [ ] 实现 `#CHANGEOPTIONxx` + channel `A6` runtime option event。

验收标准：

- BGA events 按 tick 切换。
- STOP 期间 BGA tick event 不提前触发。
- Text event 按 tick 显示。
- Option events 不在 import 阶段丢失。

测试步骤：

1. `TestBgaBaseEvent`：channel `04` creates BGA event at tick。
2. `TestBgaLayerEvent`：channel `07` creates layer event。
3. `TestBgaResourceImport`：referenced BMP imported，unreferenced BMP not imported。
4. `TestTextEvent`：`#TEXT01 Hello` + `#00199:01` creates text event。
5. `TestOptionEvent`：`#CHANGEOPTION01 MIRROR` + `#001A6:01` creates option event。
6. Visual test: BGA changes at visible beat。
7. Visual test: STOP before BGA event delays display according to tick projection。

## Phase 11: Difficulty and Performance

- [ ] 新增 `BmsDifficultyCalculator`。
- [ ] 新增 `BmsDifficultyHitObject`。
- [ ] 计算 density、jack、scratch、LN、chord、soflan、stop complexity。
- [ ] 新增 `BmsDifficultyAttributes`。
- [ ] 新增 `BmsPerformanceCalculator` 或明确返回 unsupported。
- [ ] 结果页显示 BMS stats。

验收标准：

- Empty chart star rating = 0。
- 增加 note density 会提高 difficulty。
- LN、scratch、soflan 对 attributes 有单独贡献。

测试步骤：

1. `TestDifficultyEmpty`：0 stars。
2. `TestDifficultyMoreNotesHigher`：高密度 > 低密度。
3. `TestDifficultyLnContribution`：LN chart attributes 中 LN factor > 0。
4. `TestDifficultyScratchContribution`：scratch-heavy chart scratch factor > 0。
5. `TestDifficultySoflanContribution`：BPM changes/STOP chart soflan factor > 0。
6. 运行 difficulty tests。

## Phase 12: Editor and Validation

- [ ] 新增 BMS beatmap verifier。
- [ ] 检查 missing resource。
- [ ] 检查 invalid BPM/STOP。
- [ ] 检查 unmapped channels。
- [ ] 检查 unclosed LN。
- [ ] 检查 unsupported tag warning。
- [ ] 新增 read-only BMS editor view。
- [ ] 后续新增 tick grid editing。

验收标准：

- 导入 malformed chart 有 warning，不 silent fail。
- 验证器能定位 line number。
- Editor 不破坏 control-flow AST。

测试步骤：

1. `TestVerifierMissingWav`：undefined WAV warning。
2. `TestVerifierInvalidBpm`：zero/negative BPM warning or error according policy。
3. `TestVerifierUnmappedChannel`：free-zone in 7K warning。
4. `TestVerifierUnclosedLn`：unclosed LN warning。
5. `TestVerifierUnknownKnownTag`：known but unsupported tag preserved warning。
6. Visual editor test loads chart in read-only view。

## Release Readiness Checklist

Known transitional/native gaps are tracked in [`native-gap-todo.md`](native-gap-todo.md). Clear that list before treating this roadmap as release-complete.

- [ ] `dotnet build` clean。
- [ ] `dotnet test` clean or documented environment limitation。
- [x] Basic 5K chart playable。
- [x] Basic 7K chart playable。
- [x] PMS 9K chart playable。
- [x] BPM/STOP chart visually and judgement-wise correct。
- [x] `#LNTYPE 1` playable。
- [x] `#LNTYPE 2` playable。
- [x] `#LNOBJ` playable。
- [x] Runtime random branch replay reproducible。
- [x] Folder import creates one set with multiple beatmaps。
- [x] Import does not include unreferenced sibling files。
- [x] No default gameplay path depends on `DrawableManiaRuleset`。
- [x] Documentation updated for any intentional deviations from this roadmap。
