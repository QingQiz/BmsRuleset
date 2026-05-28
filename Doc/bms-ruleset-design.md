# BmsRuleset 项目设计

本文档定义 BmsRuleset 的目标架构。它基于 `osu-ruleset编写指南.md` 中的 Ruleset 结构、对象生命周期、Playfield、DrawableHitObject、Scoring、Health、Replay、Import、Testing 等机制，但设计目标是 native BMS ruleset，而不是把 BMS 谱面转换成 osu!mania 谱面。

## 设计目标

- BMS 是一等规则集格式，`.bms`、`.bme`、`.bml`、`.pms` 解析结果必须保留 BMS 语义。
- 谱面位置以 tick 为源数据，只有 timing map 负责 tick 和毫秒时间之间的投影。
- note、BGM、BGA、STOP、BPM、text、option、judge-rank、branch 等事件都绑定到 tick。
- `#RANDOM`、`#SWITCH` 等 Control Flow 在 import 时保留，在 play 时选择分支。
- import 时区分 beatmapset 和 beatmap；同一 set 内多个 chart 共享资源，但只导入实际被 chart 引用的文件。
- BmsRuleset 拥有自己的 HitObject、Drawable、Playfield、Input、Judgement、Score、Gauge、Replay、BGA、Sample 设计。
- 可以参考 osu!mania 的多列布局和滚动玩法实现，但不要把 mania object 作为 BMS 领域模型，也不要复制 mania source。

## 非目标

- 不把 BMS BGM、BGA、control-flow branch 伪装成 mania note。
- 不在 import 阶段固定随机分支。
- 不把目录内所有文件都导入到 beatmapset。
- 不为了快速显示而丢弃 BMS tag、未知扩展命令、未实现但已知的事件。
- 不让 `StartTime` 成为谱面位置源数据；它只是 osu! runtime 需要的投影值。

## 当前状态

当前代码已切到 native BMS gameplay，不再依赖 osu!mania 类型。仍有若干系统是 skeleton 或简化实现：

| 文件 | 当前职责 | 目标变化 |
|---|---|---|
| `BmsRuleset.cs` | Native ruleset 入口，创建 `BmsDrawableRuleset`、native bindings、autoplay、legacy mania skin transformer。 | 补全 BMS-specific mods、icon、performance。 |
| `Beatmaps/BmsBeatmapDecoder.cs` | 解析 real BMS/BME/BML/PMS chart，保留 tick timing map、layout、WAV/BGM、LN 和 projected osu! time。 | 实现完整 control-flow AST、BGA/text/option/landmine/invisible event models。 |
| `Beatmaps/BmsBeatmapConverter.cs` | Pass-through native BMS converter，恢复 decoder sidecar timing/sample/layout data。 | 只保留非 BMS 来源转换需要的最小处理。 |
| `Beatmaps/BmsFileImporter.cs` | Folder import 生成一个 set 多个 beatmap，single chart import 生成单 chart set，并导入引用资源。 | 实现完整 import planner、relative resource paths、stream/archive import。 |
| `Objects/BmsHitObject.cs` | `Column`、`IsLongNote`、`Duration`、`SamplePath`、`TickInfo`。 | 增加 mine、invisible、native LN head/tail/body 等 BMS 数据。 |
| `UI/*` / `Skinning/*` | Native playfield/stage/columns/note drawables，time-based render。`BmsEmbeddedSkin` 改为 `ISkin+IDisposable`，内置皮肤枚举 `LegacyOld`/`LegacyModern`。`BmsLegacySkinTransformer` 继承 `LegacySkinTransformer`；`BmsBuiltInSkinTransformer` 包装 HUD 为 `HealthFilteredHudContainer`。`CreateSkinTransformer` 使用显式 switch。`BmsSkinConfigurationDecoder` 独立解析 `[BMS]` skin.ini 节。三处 UI bug 已修复：`JudgementArea.X` 非 scratch 列居中；BME 7K/BMS 5K `ColumnLineWidth` OOB；判定 drawable `Anchor.TopCentre` 对齐。 | 补全 LN body、STOP freeze/soflan、BGA、key beams、native skin format、Argon-native transformer。 |
| `Scoring/*` | Native BMS scoring: EX score (Perfect=2, Great=1, else 0), DJ LEVEL rank, combo reset on BAD/POOR, `RankFromScore` never returns F. Normal gauge via `BmsHealthProcessor` (`#TOTAL`-driven, starts at 20%, fails at gauge=0 or <80% at end). Empty POOR via `RegisterEmptyPoor` on both processors. | Implement clear lamp storage, results screen, `#EXRANK`, easy/hard gauge variants. |

## 总体架构

目标结构遵循 `osu-ruleset编写指南.md` 的标准 Ruleset 分层：

```text
BMS files / folder / archive
  -> BmsImportPlanner
  -> BmsSetManifest
  -> minimal Realm files
  -> BmsBeatmapDecoder
  -> BmsCommandScript with preserved control flow
  -> runtime BmsBranchState
  -> active BMS command stream
  -> BmsTickTimeline
  -> BmsBeatmap
  -> BmsBeatmapProcessor
  -> BmsDrawableRuleset
  -> BmsPlayfield / BmsBgaLayer / BmsSamplePlayback
  -> BmsScoreProcessor / BmsGaugeProcessor
```

### Ruleset Hook 设计

`BmsRuleset` 最终应直接提供 BMS 的所有 factory：

| Hook | 目标返回 | 说明 |
|---|---|---|
| `CreateDrawableRulesetWith()` | `BmsDrawableRuleset` | Native BMS 游戏实例。 |
| `CreateBeatmapConverter()` | `BmsBeatmapConverter` 或 no-op converter | `.bms` 已是 BMS native object；转换只处理非 BMS 来源。 |
| `CreateBeatmapProcessor()` | `BmsBeatmapProcessor` | 运行时分支选择、tick 投影、LN 嵌套对象、默认值。 |
| `CreateDifficultyCalculator()` | `BmsDifficultyCalculator` | 基于 BMS lane、density、LN、scratch、stop、soflan。 |
| `CreatePerformanceCalculator()` | `BmsPerformanceCalculator` | 后续实现 BMS-specific PP 或返回空。 |
| `CreateScoreProcessor()` | `BmsScoreProcessor` | EX score、combo、rank、统计。 |
| `CreateHealthProcessor()` | `BmsGaugeProcessor` | Normal/Easy/Hard/EX-Hard 等 gauge。 |
| `GetDefaultKeyBindings()` | BMS layout key bindings | 5K、7K、10K、14K、9K PMS 等。 |
| `GetModsFor()` | BMS mods/options | Random、Mirror、Gauge、Assist、Autoplay、Scroll speed。 |
| `CreateSkinTransformer()` | BMS skin transformer | BMS lane/BGA/keybeam/noteskin。 |
| `CreateSettings()` | BMS settings | Import、scroll speed、lane layout、resource handling。 |

所有新增 parser/domain/gameplay 设计都必须面向 BMS native 类型；不要重新引入 osu!mania implementation dependency。

## Tick 时间轴

### 核心原则

- `192` 只是 BMS 的默认基准分辨率：普通 4/4 measure 默认可表示为 `192` base ticks。
- 内部 timeline resolution 不是固定 `192`。如果 payload 分割或 `#xxx02` measure length 会在 `192` 下产生大量小数，必须动态扩展分辨率，让事件尽量落在整数 tick 上。
- `#xxx02` 改变该 measure 的 tick 长度。
- channel payload 的 pair index 先转换成 tick。
- BPM、STOP、note、BGM、BGA、option、text、judge-rank 都绑定 tick。
- `StartTime`、`Duration` 由 tick-to-time projection 计算；当前 render position 使用这些 projected time 字段。
- BPM 变化时，一个 tick 对应的毫秒长度变化，note 仍保持原 tick。
- STOP 是 tick-to-time 投影中的停顿；STOP 期间当前 tick 不前进，视觉滚动应冻结在对应 tick。

### Tick 类型

建议不要用裸 `double` 表示谱面位置。BmsRuleset 应保存 timeline resolution，并让 `BmsTick` 表示该 resolution 下的整数位置：

```csharp
public readonly record struct BmsTick(long Value) : IComparable<BmsTick>;

public sealed class BmsTickResolution
{
    public const int DefaultTicksPerMeasure = 192;

    public required long TicksPerMeasure { get; init; }
}
```

`TicksPerMeasure` 初始为 `192`，然后根据 chart 中所有需要精确落点的分割动态扩展。常见做法是对所有 channel payload 的 `pairCount`、所有 `#xxx02` 的有理数分母做 LCM，使下面的计算尽量得到整数。

如果 LCM 爆炸到不可接受的大小，可以降级为 rational tick 或 fixed-point tick，但不能用 `double` 作为唯一源数据。

### Measure 到 Tick

```text
measureStartTick[0] = 0
resolution = BmsTickResolution.TicksPerMeasure
measureTickLength = resolution * measureLength
eventTick = measureStartTick[measure] + measureTickLength * pairIndex / pairCount
```

`#00111:0100` 在默认长度下：

```text
measure 001 start tick = 192
pairCount = 2
pair 0 tick = 192
pair 1 tick = 288
```

如果某个 chart 使用 `#00111:0101010101`，`pairCount = 5`。固定 `192` 会得到 `192/5 = 38.4` 的小数 tick。此时应把 resolution 至少扩展到 `960`：

```text
resolution = lcm(192, 5) = 960
measure 001 start tick = 960
pair step = 960 / 5 = 192
pair ticks = 960, 1152, 1344, 1536, 1728
```

### Tick 到 Time

```text
msPerTick = (4 * 60000 / bpm) / resolution
timeAtTick = segment.StartTime + (tick - segment.StartTick) * msPerTick + stopOffsetBeforeTick
```

当 `resolution = 192` 时，上式等价于 `msPerTick = 60000 / (bpm * 48)`。不要在动态 resolution 下继续使用固定 `48`。

STOP 的定义仍以 BMS base `1/192` whole note 为单位；计算 STOP 时使用 STOP 自身公式，不使用动态 `msPerTick` 直接替代。

同 tick 顺序：

1. 先投影该 tick 的 object 时间。
2. 同 tick BPM 先于 STOP 生效。
3. STOP 使用同 tick BPM 变化后的 BPM 计算长度。
4. STOP delay 影响后续 tick，不改变当前 tick object 的判定时间。

### Render Time

当前 native BMS Playfield 使用 decode 阶段投影出的 `StartTime` 按时间渲染：

```text
timeUntilHit = objectStartTime - clock.CurrentTime
screenY = judgementLineY - timeUntilHit / preempt * travelDistance
```

`BmsHitObject.TickInfo.Tick`/`EndTick` 保留 BMS 原生时间线数据，但 gameplay 渲染暂不做 runtime tick 反投影。

当前 BGM channel `01` 和 playable note key sounds 通过 BMS sample lookup 播放。播放层会绕过 osu! global Effect volume，但仍跟随 universal audio volume。

## 数据模型

### BmsBeatmap

目标字段：

```csharp
public class BmsBeatmap : Beatmap<BmsHitObject>
{
    public int TotalColumns { get; set; }
    public BmsLayout Layout { get; set; }
    public BmsTimingMap Timing { get; set; }
    public BmsCommandScript ControlScript { get; set; }
    public BmsResourceManifest Resources { get; set; }
    public BmsMetadata BmsMetadata { get; set; }
    public List<BmsTimedSampleEvent> BgmEvents { get; } = new();
    public List<BmsBgaEvent> BgaEvents { get; } = new();
    public List<BmsTextEvent> TextEvents { get; } = new();
    public List<BmsOptionEvent> OptionEvents { get; } = new();
}
```

### BmsHitObject

目标字段：

```csharp
public class BmsHitObject : HitObject
{
    public BmsTick Tick { get; set; }
    public BmsTick? EndTick { get; set; }
    public int Column { get; set; }
    public string SourceChannel { get; set; }
    public string? WavIndex { get; set; }
    public BmsNoteKind Kind { get; set; }
    public bool IsLongNote => EndTick != null;
}
```

`StartTime` and `Duration` remain populated for osu! infrastructure, but are derived:

```text
StartTime = Timing.ProjectTickToTime(Tick)
Duration = Timing.ProjectTickToTime(EndTick) - StartTime
```

### Event 类型

| 类型 | 绑定 | 用途 |
|---|---|---|
| `BmsTimingEvent` | tick | BPM、STOP、measure length segment。 |
| `BmsTimedSampleEvent` | tick | BGM autoplay sample。 |
| `BmsNoteSampleEvent` | note tick | Key sound。 |
| `BmsBgaEvent` | tick | BGA base/layer/poor/layer2/alpha/video。 |
| `BmsTextEvent` | tick | `#TEXTxx`/channel `99`。 |
| `BmsOptionEvent` | tick | `#OPTION`、`#CHANGEOPTIONxx`/channel `A6`。 |
| `BmsJudgeRankEvent` | tick | `#EXRANKxx`/channel `A0`。 |
| `BmsLandmineEvent` | tick | `D1-D9`、`E1-E9`。 |
| `BmsInvisibleEvent` | tick | `31-49` hidden key-sound assist。 |

## Parser 设计

### Import Parse 和 Runtime Parse 分离

需要两个层次：

| 层次 | 时机 | 输出 | 是否选择 control-flow branch |
|---|---|---|---|
| Import scan | 文件导入时 | headers、command AST、resource references、set manifest | 否 |
| Runtime materialisation | gameplay load / replay load | active command stream、tick events、BmsBeatmap | 是 |

Import scan 必须足够轻量，可以扫描一个文件夹里所有 chart 并决定 set 结构和资源依赖。Runtime materialisation 可以更完整，因为它只针对当前要玩的 chart 和当前 branch state。

### Line Parse

所有 command line 先解析成：

```csharp
public sealed record BmsRawLine(int LineNumber, string RawText);
public sealed record BmsHeaderLine(int LineNumber, string Name, string? Index, string Value);
public sealed record BmsChannelLine(int LineNumber, int Measure, string Channel, string Data);
```

大小写识别不敏感，definition index 规范化为 uppercase。原始文本仍保留，用于 unknown tag 和 future support。

### Control Flow AST

Control Flow 是 runtime 事件，不是 import-time preprocessor。

```csharp
public abstract record BmsCommandNode(int LineNumber);
public sealed record BmsRawCommandNode(int LineNumber, BmsRawLine Line) : BmsCommandNode(LineNumber);
public sealed record BmsRandomBlockNode(int LineNumber, int MaxValue, IReadOnlyList<BmsIfBranch> Branches) : BmsCommandNode(LineNumber);
public sealed record BmsSwitchBlockNode(int LineNumber, int MaxValue, IReadOnlyList<BmsSwitchCase> Cases) : BmsCommandNode(LineNumber);
public sealed record BmsIfBranch(int MatchValue, IReadOnlyList<BmsCommandNode> Commands);
public sealed record BmsSwitchCase(int? MatchValue, IReadOnlyList<BmsCommandNode> Commands);
```

Branch selection state：

```csharp
public sealed class BmsBranchState
{
    public required string BeatmapHash { get; init; }
    public required int RulesetVersion { get; init; }
    public required IReadOnlyList<int> RandomValues { get; init; }
    public required IReadOnlyList<int> SwitchValues { get; init; }
}
```

Replay 必须保存 branch decisions，否则同一 replay 不能可靠复现。

### Duplicate Channel Merge

除 `01`、`02`、`A6` 等明确非 merge channel 外，同 measure 同 channel 多行要合并：

- later non-`00` overwrites earlier value at same grid position。
- `00` 不覆盖已有 object。
- BPM/STOP channel 也要 merge。
- `LNTYPE 2` 需要保留 `00` 作为 run terminator，因此 merge 后的数据结构必须还能表达 zero cells。

### Long Note

必须支持三种：

| 类型 | 输入 | 解析方式 |
|---|---|---|
| `#LNTYPE 1` | LN channels `51-69` | 同 lane non-`00` 交替 start/end。 |
| `#LNTYPE 2` | LN channels `51-69` | 连续 non-`00` run，遇 `00` 结束。 |
| `#LNOBJ` | visible channels | visible note 到 terminator WAV index 配对。 |

`EndTick` 是 long note 的权威结束位置，`Duration` 是投影值。

## Import 设计

### BeatmapSet 和 Beatmap 区别

BMS 文件夹通常包含多个 chart：

```text
song/
  normal.bms
  hyper.bms
  another.bms
  kick.wav
  snare.wav
  bga.bmp
```

目标 Realm 结构：

```text
BeatmapSetInfo: song set
  Files:
    normal.bms
    hyper.bms
    another.bms
    kick.wav
    snare.wav
    bga.bmp
  Beatmaps:
    normal BeatmapInfo
    hyper BeatmapInfo
    another BeatmapInfo
```

不要为每个 `.bms` 文件创建独立 set，除非用户确实只导入一个孤立 chart 且没有 set context。

### Import Scope

| 用户操作 | Import scope | Set grouping |
|---|---|---|
| Import selected file | 该 chart 文件 + 同目录必要资源 | 单 chart set，或如果同目录 sibling charts 明显同 set，可询问/配置。 |
| Import current folder | 该目录下所有 BMS chart + 必要资源 | 同目录 chart 合并为 set。 |
| Import recursive | 每个含 BMS chart 的目录作为候选 set | 子目录各自成 set，避免跨目录误合并。 |
| Import archive | archive root 或每个 chart directory | 按 archive 内路径分组。 |

### Resource 依赖分析

只导入用到的文件：

1. 永远导入 chart 文件本身。
2. 解析 all possible runtime branches 的 `#WAVxx`、`#BMPxx`、video、stage/banner/back、BGA crop definitions。
3. 建立 slot definition 到 event use 的依赖图。
4. 只导入被事件引用的 slot 对应文件。
5. `#PATH_WAV`、`#MATERIALS*` 只作为搜索路径或 hint，不代表整目录导入。
6. 如果 `#WAVxx` 定义了文件但没有任何 note/BGM/LN/landmine/branch 使用，不导入。
7. 如果 branch A 和 branch B 分别引用不同资源，两者都导入，因为 play 时都可能被选择。
8. 同一 set 内重复文件按 content hash 存储一次，多个 `RealmNamedFileUsage` 可指向同一 `RealmFile`。

### Path 解析

资源解析顺序：

1. Chart 文件所在目录。
2. `#PATH_WAV` 指定目录。
3. `#MATERIALSWAV` / `#MATERIALSBMP` hint 目录。
4. 大小写宽容匹配，尤其 Windows 导入到大小写敏感存储时。
5. 后缀替代搜索，例如 `.wav`、`.ogg`、`.mp3` 同名资源。

所有导入的 named usage 应保留相对路径，避免同名不同目录资源冲突。

## Gameplay 设计

### DrawableRuleset

`BmsDrawableRuleset : DrawableRuleset<BmsHitObject>` 负责：

- 创建 `BmsPlayfield`。
- 创建 BMS input manager。
- 注入 `BmsTimingMap`、`BmsBranchState`、scroll settings。
- 协调 BGA layer、sample playback、HUD/gauge。

### Playfield

`BmsPlayfield` 不应依赖 mania `Stage`。建议结构：

```text
BmsPlayfield
  ├── BmsBgaContainer
  ├── BmsLaneContainer
  │     ├── BmsColumn 0
  │     ├── BmsColumn 1
  │     └── ...
  ├── BmsJudgementLine
  ├── BmsKeyBeamLayer
  └── HitObjectContainer
```

Column mapping 由 `BmsLayout` 决定：

| Layout | Columns | Source channels |
|---|---:|---|
| 5K SP | 6 | `16`, `11-15` |
| 7K SP | 8 | `16`, `11-15`, `18-19` |
| 10K DP | 12 | 5K left/right with scratches |
| 14K DP | 16 | 7K left/right with scratches |
| PMS 9K | 9 | `11-15`, `22-25` |
| PMS DP | 18 | PMS 9K left/right banks |
| BME-type PMS 9K | 9 | `11-15`, `18-19`, `16-17` |

### DrawableHitObject

`DrawableBmsHitObject` should:

- Bind to `BmsHitObject.TickInfo.Tick` and `EndTick` when native-tick visuals are implemented。
- Use projected `StartTime`/`Duration` for current render position。
- Fill the full lane width and anchor the note visual at lane left-bottom。
- Use object pool via `RegisterPool<BmsHitObject, DrawableBmsHitObject>()`。
- Support normal note, LN head/body/tail, landmine, invisible assist if enabled。
- Use `UpdateInitialTransforms`、`UpdateStartTimeStateTransforms`、`UpdateHitStateTransforms` per current osu! API。

### Input

Define BMS actions rather than mania actions：

```csharp
public enum BmsAction
{
    Key1,
    Key2,
    Key3,
    Key4,
    Key5,
    Key6,
    Key7,
    Scratch1,
    Key8,
    Key9,
    Key10,
    Key11,
    Key12,
    Key13,
    Key14,
    Scratch2,
}
```

Layout maps actions to columns. 5K, 7K, 5K DP, 7K DP, PMS 9K, and PMS DP have separate native keybinding mappings. PMS maps keys without scratch semantics.

### Judgement and Gauge

BMS judgement should support `#RANK` and later `#EXRANKxx` dynamic changes. Current implementation:

| Result | BMS name | Use |
|---|---|---|
| `Perfect` | PGREAT | Tightest window; 2 EX points. |
| `Great` | GREAT | 1 EX point; no combo break. |
| `Good` | GOOD | 0 EX points; no combo break. |
| `Ok` | BAD | 0 EX points; breaks combo (forced reset in `BmsScoreProcessor`). |
| `Meh` | POOR | 0 EX points; breaks combo. Two causes: (1) passive miss — BAD window expired; (2) in-POOR-zone keypress (−200 to −1000 ms before note). |
| `Miss` | E-POOR | **Not a note judgement result.** Repurposed as the Empty POOR counter in `Statistics`. `IsHitResultAllowed` returns false; `WindowFor(Miss)` = 0. Displayed in HUD and results screen as "E-POOR". |

`BmsRuleset.HIT_RESULT_LABELS` is the single source of truth for all label strings. Both `GetDisplayNameForHitResult` and `BmsDefaultJudgementPiece` read from it.

Gauge should not be passive osu! drain. It should be event-based：

- Normal gauge。
- Easy gauge。
- Hard gauge。
- EX-Hard gauge。
- Hazard / death mode later。
- `#TOTAL` influences recovery curve。

### Score

Native BMS scoring should expose：

- EX score。
- Combo。
- Max combo。
- PGREAT/GREAT/GOOD/BAD/POOR counts。
- Clear type。
- Gauge final percentage。
- Optional osu! score integration for result screen compatibility。

### BGM and Key Sound

BMS audio is key-sounded：

- Key press plays the keysound of the earliest judgeable note in the lane. `findNextSoundHitObject` skips only notes whose `StartTime < Time.Current − BadWindow` (200 ms), so a late keypress within the BAD window plays the current note's keysound, not the next note's.
- Miss judgement does not play a hit sound, and osu! default hitsounds are disabled for BMS notes。
- BGM channel `01` autoplay samples at projected tick time。
- Invisible notes may alter key sound behavior later。
- LN endpoint sound behavior differs by notation/player and must be represented explicitly。
- Sample resolution must support arbitrary filenames, relative paths, and overlapping playback of same file through different slots。

### BGA

BGA should be an event layer, not part of HitObject list：

- `04` base。
- `06` poor。
- `07` layer。
- `0A` layer 2。
- `0B-0E` alpha。
- `#BGAxx` and `#@BGAxx` cropped regions。
- Video and seek extensions later。

## Replay 设计

Replay must include：

- Key presses/releases by BMS action。
- Branch decisions for `#RANDOM` / `#SWITCH`。
- Ruleset version or parser compatibility version。
- Layout id。
- Scroll/gauge mods that affect play but not chart parse。

Replay playback must not reroll runtime branches。

## Editor 设计

Editor can be deferred, but data model must not block it：

- Tick grid editing。
- Measure length editing。
- BPM/STOP lane。
- BGM/BGA event lanes。
- LN type-aware editing。
- Resource slot panel for `#WAVxx` and `#BMPxx`。
- Control-flow editor can be read-only initially。

## Migration Plan

The project can migrate safely in layers：

1. Keep current mania adapter only to avoid breaking loading while parser/domain is built。
2. Add BMS native data fields and parser tests。
3. Add native `BmsDrawableRuleset` and `BmsPlayfield` behind a feature branch or setting。
4. Switch default gameplay to native BMS once tests cover basic 5K/7K/PMS/LN/BPM/STOP。
5. Remove `ppy.osu.Game.Rulesets.Mania` package only after native difficulty/mods/input/skin replacements exist。

## Risks

| Risk | Mitigation |
|---|---|
| osu! infrastructure expects `StartTime` ordering. | Keep projected `StartTime` populated from `BmsTick`; sort by tick then projected time. |
| Runtime branch affects metadata/resources. | Import all resources reachable from any branch; choose branch only for gameplay events. |
| Sparse charts infer wrong column count. | Store explicit `BmsLayout` and `TotalColumns`; never infer only from max note column. |
| Tuplet positions drift with fixed 192 ticks. | Dynamically expand timeline resolution from 192 with LCM; only fall back to rational/fixed-point when resolution explodes. |
| STOP/BPM render differs from judgement. | Use projected `StartTime` from the shared `BmsTimingMap` until a native scroll model is designed. |
| Too many unused files imported. | Build resource dependency graph; never import unreferenced sibling files. |
| BMS tag support grows indefinitely. | Preserve raw known and unknown tags, implement behavior incrementally. |
