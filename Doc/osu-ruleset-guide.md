# osu! Ruleset 编写指南

> 基于 osu! lazer 内置 Ruleset (osu/taiko/catch/mania) 与社区 17 个可玩 Ruleset 的源码分析

> 本仓库的 BMS 设计见 [BmsRuleset 项目设计](bms-ruleset-design.md)、[BmsRuleset 开发路线](bms-ruleset-roadmap.md)、[BMS Parsing Guide](bms-parsing-guide.md)。

---

## 目录

### 第一部分：快速开始
1. [架构概述](#1-架构概述)
2. [项目搭建](#2-项目搭建)
3. [Ruleset 类（入口）](#3-ruleset-类（入口）)

### 第二部分：数据层 —— 谱面里有什么
4. [HitObject 系统](#4-hitobject-系统)
5. [Judgement 与 HitWindows 判罚与时间窗口](#5-judgement-与-hitwindows-判罚与时间窗口)
6. [BeatmapConverter 转谱系统](#6-beatmapconverter-转谱系统)

### 第三部分：表现层 —— 屏幕上怎么画
7. [DrawableHitObject 绘制物系统](#7-drawablehitobject-绘制物系统)
8. [Playfield 游戏区域系统](#8-playfield-游戏区域系统)
9. [Object Pooling 对象池](#9-object-pooling-对象池)
10. [物品绘制与布局系统](#10-物品绘制与布局系统)
11. [DrawableRuleset 游戏实例系统](#11-drawableruleset-游戏实例系统)
12. [Scrolling Ruleset 滚动式玩法](#12-scrolling-ruleset-滚动式玩法)

### 第四部分：规则层 —— 怎么玩
13. [Input 输入系统](#13-input-输入系统)
14. [Mod 系统](#14-mod-系统)
15. [Scoring & Health 积分与血量系统](#15-scoring--health-积分与血量系统)
16. [Difficulty & Performance 难度与PP系统](#16-difficulty--performance-难度与pp系统)
17. [Replay 回放系统](#17-replay-回放系统)

### 第五部分：进阶定制 —— 打磨你的 Ruleset
18. [设置与配置系统](#18-设置与配置系统)
19. [皮肤系统](#19-皮肤系统)
20. [谱面编辑器系统](#20-谱面编辑器系统)
21. [谱面验证系统](#21-谱面验证系统)
22. [结果与统计系统](#22-结果与统计系统)
23. [选歌界面自定义](#23-选歌界面自定义)
24. [本地化系统](#24-本地化系统)
25. [高级主题](#25-高级主题)
26. [测试系统](#26-测试系统)

### 第六部分：完整体验
27. [完整示例：最小 Ruleset](#27-完整示例实现一个最小-ruleset)

---

## 1. 架构概述

```
Beatmap (.osu 文件)
  │
  ▼
BeatmapConverter<TObject>     → 将通用 HitObject 转换为 Ruleset 专用对象
  │
  ▼
BeatmapProcessor              → 可选的后处理 (如滑条路径生成)
  │
  ▼
Ruleset.CreateDrawableRulesetWith()  → 创建视觉游戏实例
  │
  ▼
DrawableRuleset<TObject>
  ├── Playfield               → 游戏区域，管理所有 DrawableHitObject
  │     ├── HitObjectContainer → 生命周期管理 + 对象池
  │     └── DrawableHitObjects → 每个 HitObject 的视觉表现
  ├── PassThroughInputManager → Ruleset 专属输入处理
  └── HUD / Overlays          → 用户界面覆盖层

判罚流程:
  输入 → DrawableHitObject.CheckForResult()
       → JudgementResult (Miss/Meh/Ok/Good/Great/Perfect/...)
       → DrawableHitObject.ApplyResult()
       → Playfield.NewResult 事件冒泡
       → DrawableRuleset.NewResult 事件冒泡
       → ScoreProcessor.ApplyResult()   [分数]
       → HealthProcessor.ApplyResult()  [血量]
```

**核心类层次：**

```
Ruleset (抽象基类)
  ├── OsuRuleset, TaikoRuleset, CatchRuleset, ManiaRuleset
  └── 你的自定义 Ruleset

DrawableRuleset<TObject> (泛型基类)
  ├── DrawableOsuRuleset, DrawableTaikoRuleset, ...
  ├── DrawableScrollingRuleset<T> (滚动式中间类)
  └── 你的自定义 DrawableRuleset

Playfield (基类)
  ├── OsuPlayfield, TaikoPlayfield, ...
  ├── ScrollingPlayfield (滚动式中间类)
  └── 你的自定义 Playfield

HitObject (基类)
  ├── OsuHitObject, TaikoHitObject, ...
  └── 你的自定义 HitObject

DrawableHitObject<TObject> (泛型基类)
  └── 你的自定义 DrawableHitObject

Judgement (基类)
  └── 你的自定义 Judgement
```

---

## 2. 项目搭建

### 2.1 .csproj 配置

.osu 使用的**NuGet 包模型**（推荐）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <AssemblyName>osu.Game.Rulesets.MyGame</AssemblyName>
    <RootNamespace>osu.Game.Rulesets.MyGame</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <!-- 与目标 osu! 版本保持一致；本仓库当前使用 2026.518.0。 -->
    <PackageReference Include="ppy.osu.Game" Version="2026.518.0" />
  </ItemGroup>
</Project>
```

> **命名约定**：程序集名必须以 `osu.Game.Rulesets.` 开头，这是 Ruleset 发现机制的要求（`RulesetStore` 扫描 `"osu.Game.Rulesets.*.dll"` 文件）。

> **版本选择**：`RulesetAPIVersionSupported` 需要与 `Ruleset.CURRENT_RULESET_API_VERSION` 匹配（当前为 `"2022.822.0"`），否则 Ruleset 无法加载。

### 2.2 目录结构（推荐）

```
osu.Game.Rulesets.MyGame/
├── MyGameRuleset.cs              ← Ruleset 入口
├── MyGameInputManager.cs         ← 输入管理
├── Beatmaps/
│   └── MyGameBeatmapConverter.cs  ← 转谱器
├── Configuration/
│   └── MyGameRulesetConfigManager.cs
├── Difficulty/
│   ├── MyGameDifficultyCalculator.cs
│   └── MyGamePerformanceCalculator.cs
├── Judgements/
│   └── MyGameJudgement.cs
├── Mods/
│   └── MyGameMod*.cs
├── Objects/
│   ├── MyGameHitObject.cs        ← HitObject 数据类
│   └── Drawables/
│       └── DrawableMyGameHitObject.cs  ← 绘制表现
├── Replays/
│   ├── MyGameReplayFrame.cs
│   ├── MyGameFramedReplayInputHandler.cs
│   └── MyGameAutoGenerator.cs
├── Scoring/
│   ├── MyGameHitWindows.cs
│   └── MyGameScoreProcessor.cs
├── UI/
│   ├── MyGameDrawableRuleset.cs   ← 游戏实例
│   └── MyGamePlayfield.cs         ← 游戏区域
└── Resources/
    └── Textures/                  ← 贴图资源
```

---

## 3. Ruleset 类（入口）

`Ruleset` 是所有 Ruleset 的抽象基类，定义在 `osu.Game\Rulesets\Ruleset.cs:40`。它负责创建游戏所需的各种工厂对象。

### 3.1 必须实现的成员

| 成员 | 用途 |
|------|------|
| `ShortName` | 唯一短标识符，如 `"osu"`, `"taiko"` |
| `Description` | 可读名称，如 `"osu!"` |
| `CreateDrawableRulesetWith()` | 创建可视游戏实例 |
| `CreateBeatmapConverter()` | 创建转谱器 |
| `CreateDifficultyCalculator()` | 创建难度计算器 |
| `GetModsFor(ModType)` | 返回各类型的 Mod 列表 |

### 3.2 推荐重写的虚方法

| 方法 | 默认行为 | 说明 |
|------|---------|------|
| `CreateScoreProcessor()` | 返回 `new ScoreProcessor(this)` | 自定义计分 |
| `CreateHealthProcessor(double drainStartTime)` | 返回 `new DrainingHealthProcessor(drainStartTime)` | 自定义扣血 |
| `CreatePerformanceCalculator()` | 返回 `null` (无PP) | PP计算 |
| `CreateHitObjectComposer()` | 返回 `null` (无编辑器) | 谱面编辑器 |
| `GetDefaultKeyBindings()` | 返回空 | 默认键位 |
| `GetValidHitResults()` | 返回所有 HitResult | 合法的判罚类型 |
| `CreateIcon()` | 返回问号图标 | Ruleset 图标 |
| `RulesetAPIVersionSupported` | `string.Empty` | **必须设为 `CURRENT_RULESET_API_VERSION`** |
| `CreateConfig()` | 返回 `null` | Ruleset 配置管理器 |
| `CreateSettings()` | 返回 `null` | 设置面板 |
| `CreateConvertibleReplayFrame()` | 返回 `null` | 旧版回放兼容 |
| `CreateSkinTransformer()` | 返回 `null` | 皮肤系统 |

### 3.3 完整示例

```csharp
// 文件: osu.Game.Rulesets.MyGame/MyGameRuleset.cs
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MyGame.Beatmaps;
using osu.Game.Rulesets.MyGame.Difficulty;
using osu.Game.Rulesets.MyGame.Mods;
using osu.Game.Rulesets.MyGame.Scoring;
using osu.Game.Rulesets.MyGame.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.MyGame
{
    public class MyGameRuleset : Ruleset
    {
        // ★ 必须设置为最新 API 版本
        public override string RulesetAPIVersionSupported => CURRENT_RULESET_API_VERSION;

        // ★ 唯一短名
        public override string ShortName => "mygame";

        // ★ 显示名称
        public override string Description => "My Game";

        // ★ 创建游戏实例
        public override DrawableRuleset CreateDrawableRulesetWith(
            IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            => new DrawableMyGameRuleset(this, beatmap, mods);

        // ★ 创建转谱器
        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap)
            => new MyGameBeatmapConverter(beatmap, this);

        // ★ 创建难度计算器
        public override DifficultyCalculator CreateDifficultyCalculator(
            IWorkingBeatmap beatmap)
            => new MyGameDifficultyCalculator(RulesetInfo, beatmap);

        // ★ 创建 PP 计算器
        public override PerformanceCalculator? CreatePerformanceCalculator()
            => new MyGamePerformanceCalculator(this);

        // ★ 自定义计分器
        public override ScoreProcessor CreateScoreProcessor()
            => new MyGameScoreProcessor(this);

        // ★ 提供 Mod
        public override IEnumerable<Mod> GetModsFor(ModType type)
        {
            switch (type)
            {
                case ModType.DifficultyReduction:
                    return new Mod[] { new MyGameModEasy(), new MyGameModNoFail() };

                case ModType.DifficultyIncrease:
                    return new Mod[] {
                        new MyGameModHardRock(),
                        new MyGameModDoubleTime(),
                        new MyGameModHidden(),
                    };

                case ModType.Automation:
                    return new Mod[] { new MyGameModAutoplay() };

                default:
                    return System.Array.Empty<Mod>();
            }
        }

        // ★ 默认键位
        public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0)
            => new[]
            {
                new KeyBinding(InputKey.Z, MyGameAction.Action1),
                new KeyBinding(InputKey.MouseLeft, MyGameAction.Action1),
            };

        // ★ 合法的判罚类型（影响结果界面显示）
        public override IEnumerable<HitResult> GetValidHitResults()
            => new[]
            {
                HitResult.Perfect,
                HitResult.Great,
                HitResult.Ok,
                HitResult.Miss,
            };

        // ★ 自定义图标
        public override Drawable CreateIcon()
            => new SpriteIcon { Icon = FontAwesome.Solid.Gamepad };
    }
}
```

---

## 4. HitObject 系统

`HitObject` (定义在 `osu.Game\Rulesets\Objects\HitObject.cs:32`) 是游戏对象的抽象基类。

### 4.1 HitObject 基类关键成员

```csharp
public class HitObject
{
    // 开始时间
    public virtual double StartTime { get; set; }
    public readonly Bindable<double> StartTimeBindable;

    // 音效样本
    public IList<HitSampleInfo> Samples { get; set; }

    // 时间窗口
    public HitWindows HitWindows { get; set; }

    // 判罚
    public Judgement Judgement { get; }

    // 嵌套子对象（如滑条的 ticks / repeats）
    public SlimReadOnlyListWrapper<HitObject> NestedHitObjects { get; }

    // 在 ApplyDefaults 时调用
    public event Action<HitObject> DefaultsApplied;

    // ★ 必须重写：创建判罚
    public virtual Judgement CreateJudgement() => new Judgement();

    // ★ 必须重写：创建时间窗口
    protected virtual HitWindows CreateHitWindows() => new DefaultHitWindows();

    // 应用默认值（控制点、难度）
    public void ApplyDefaults(ControlPointInfo controlPointInfo,
        IBeatmapDifficultyInfo difficulty, CancellationToken cancellationToken = default);

    // 创建嵌套对象
    protected virtual void CreateNestedHitObjects(CancellationToken ct);
}
```

### 4.2 自定义 HitObject 示例

```csharp
// 文件: osu.Game.Rulesets.MyGame/Objects/MyGameHitObject.cs
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.MyGame.Judgements;
using osu.Game.Rulesets.MyGame.Scoring;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.MyGame.Objects
{
    public class MyGameHitObject : HitObject
    {
        // 自定义属性：所在轨道 (-1, 0, 1)
        public int Lane { get; set; }

        // 时间预显示量（对象提前多久出现）
        public double TimePreempt = 600;

        // ★ 创建判罚
        public override Judgement CreateJudgement()
            => new MyGameJudgement();

        // ★ 创建时间窗口
        protected override HitWindows CreateHitWindows()
            => new MyGameHitWindows();

        // ★ 应用默认值（根据 AR 调整 TimePreempt）
        protected override void ApplyDefaultsToSelf(
            ControlPointInfo controlPointInfo, IBeatmapDifficultyInfo difficulty)
        {
            base.ApplyDefaultsToSelf(controlPointInfo, difficulty);

            // 根据 AR 计算 TimePreempt
            TimePreempt = (float)IBeatmapDifficultyInfo.DifficultyRange(
                difficulty.ApproachRate, 1800, 1200, 450);
        }
    }
}
```

### 4.3 复杂 HitObject：Pippidon 示例（三轨道）

```csharp
// PippidonObject.cs - 最简单的 HitObject
public class PippidonObject : HitObject
{
    public int Lane; // -1, 0, 1
    // 其余全部继承基类
}
```

### 4.4 复杂 HitObject：Tau 示例（角度系统）

```csharp
// TauHitObject.cs - 带角度的 HitObject 层级
public class TauHitObject : HitObject, IHasComboInformation
{
    public double TimePreempt = 600;
    public double TimeFadeIn = 100;

    public override Judgement CreateJudgement() => new TauJudgement();
    protected override HitWindows CreateHitWindows() => new TauHitWindow();
}

// 带角度的子类
public class AngledTauHitObject : TauHitObject, IHasAngle
{
    public float Angle { get; set; }
}

// 普通单点
public class Beat : AngledTauHitObject { }

// 无角度方向拍
public class HardBeat : TauHitObject { }

// 滑条（角度路径）
public class Slider : AngledTauHitObject, IHasRepeats, IHasOffsetAngle
{
    // 使用 PolarSliderPath 定义极坐标路径
    // 生成嵌套的 SliderHeadBeat, SliderRepeat, SliderTick
}
```

### 4.5 嵌套对象（NestedHitObjects）

当你的 HitObject 有子对象时（如滑条的 ticks），重写 `CreateNestedHitObjects`：

```csharp
public class MySlider : MyGameHitObject
{
    public double EndTime { get; set; }
    public double TickInterval { get; set; } = 100;

    protected override void CreateNestedHitObjects(CancellationToken ct)
    {
        base.CreateNestedHitObjects(ct);

        // 为每个 tick 创建嵌套 HitObject
        for (double time = StartTime; time <= EndTime; time += TickInterval)
        {
            AddNested(new MyGameTickHitObject
            {
                StartTime = time,
                Samples = new[] { CreateHitSampleInfo() },
            });
        }
    }
}
```

---

## 5. Judgement 与 HitWindows 判罚与时间窗口

`Judgement` (定义在 `osu.Game\Rulesets\Judgements\Judgement.cs:12`) 定义了一个 HitObject 可以获得的**评分范围**和**血量增减**。

### 5.1 关键成员

```csharp
public class Judgement
{
    // 最高可获得的 HitResult（如 Perfect, Great, LargeBonus）
    public virtual HitResult MaxResult => HitResult.Perfect;

    // 最低可获得的 HitResult（根据 MaxResult 自动推断，通常是 Miss）
    public virtual HitResult MinResult { get; }

    // 最高判罚的血量增加值（默认 5%）
    protected const double DEFAULT_MAX_HEALTH_INCREASE = 0.05;

    // ★ 根据 HitResult 返回血量变化
    protected virtual double HealthIncreaseFor(HitResult result)
    {
        // Miss = -10%, Meh = +0.25%, Ok = +2.5%, Great = +5%, Perfect = +5.25%
    }
}
```

### 5.2 自定义 Judgement 示例

```csharp
// 文件: osu.Game.Rulesets.MyGame/Judgements/MyGameJudgement.cs
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.MyGame.Judgements
{
    public class MyGameJudgement : Judgement
    {
        // 最高只能打到 Great
        public override HitResult MaxResult => HitResult.Great;

        // 自定义血量
        protected override double HealthIncreaseFor(HitResult result)
        {
            switch (result)
            {
                case HitResult.Great:
                    return 0.03;  // +3%
                case HitResult.Ok:
                    return 0.01;  // +1%
                case HitResult.Miss:
                    return -0.06; // -6%
                default:
                    return 0;
            }
        }
    }
}
```

### 5.3 Touhosu 示例（位置判定而非时间判定）

```csharp
// TouhosuJudgement.cs - 生存类游戏，只有 Perfect/Miss
public class TouhosuJudgement : Judgement
{
    public override HitResult MaxResult => HitResult.Perfect;

    // 击中回血极少（0.002%），靠的是躲避
    protected override double HealthIncreaseFor(HitResult result)
    {
        switch (result)
        {
            case HitResult.Perfect:
                return 0.00002; // 几乎不回血
            default:
                return 0;
        }
    }
}
```

### 5.4 JudgementResult

每个 DrawableHitObject 持有一个 `JudgementResult`（定义在 `osu.Game\Rulesets\Judgements\JudgementResult.cs`）：

```csharp
public class JudgementResult
{
    public HitResult Type;        // 当前判罚结果
    public Judgement Judgement;   // 引用的判罚
    public HitObject HitObject;   // 对应对象
    public double TimeOffset;     // 时间偏移
    public double? RawTime;       // 原始判定时间
    public double GameplayRate;   // 游戏速率
    public bool HasResult;        // 是否已有结果
    public bool IsHit;            // 是否命中
}
```

自定义 JudgementResult（如 Tau）：

```csharp
// TauJudgementResult.cs - 添加了角度统计信息
public class TauJudgementResult : JudgementResult
{
    public float DeltaAngle; // 玩家角度与目标角度的差值

    public TauJudgementResult(HitObject hitObject, Judgement judgement)
        : base(hitObject, judgement) { }
}
```

`HitWindows` (定义在 `osu.Game\Rulesets\Scoring\HitWindows.cs:15`) 定义每个判罚等级的时间容差。

### 5.5 HitWindows：两种实现模式

**模式一：`SetDifficulty()` + 字段缓存模式（当前 osu! API，推荐）**

```csharp
// PippidonHitWindows.cs
using osu.Game.Beatmaps;

public class PippidonHitWindows : HitWindows
{
    private static readonly DifficultyRange[] pippidon_ranges =
    {
        new DifficultyRange(50, 35, 20),  // OD0:50 OD5:35 OD10:20
        new DifficultyRange(90, 60, 40),
        new DifficultyRange(130, 90, 60),
        new DifficultyRange(170, 120, 80),
        new DifficultyRange(200, 150, 100),
        new DifficultyRange(250, 200, 150),
    };

    public override bool IsHitResultAllowed(HitResult result)
        => result is HitResult.Perfect or HitResult.Great or HitResult.Good
            or HitResult.Ok or HitResult.Meh or HitResult.Miss;

    private double perfect;
    private double great;
    private double good;
    private double ok;
    private double meh;
    private double miss;

    // OD 越高，窗口越紧
    public override void SetDifficulty(double difficulty)
    {
        perfect = IBeatmapDifficultyInfo.DifficultyRange(difficulty, pippidon_ranges[0]);
        great = IBeatmapDifficultyInfo.DifficultyRange(difficulty, pippidon_ranges[1]);
        good = IBeatmapDifficultyInfo.DifficultyRange(difficulty, pippidon_ranges[2]);
        ok = IBeatmapDifficultyInfo.DifficultyRange(difficulty, pippidon_ranges[3]);
        meh = IBeatmapDifficultyInfo.DifficultyRange(difficulty, pippidon_ranges[4]);
        miss = IBeatmapDifficultyInfo.DifficultyRange(difficulty, pippidon_ranges[5]);
    }

    public override double WindowFor(HitResult result)
    {
        switch (result)
        {
            case HitResult.Perfect: return perfect;
            case HitResult.Great:   return great;
            case HitResult.Good:    return good;
            case HitResult.Ok:      return ok;
            case HitResult.Meh:     return meh;
            case HitResult.Miss:    return miss;
            // ...
            default: return double.MaxValue;
        }
    }
}
```

**模式二：固定窗口模式（不随 OD 改变）**

```csharp
// TauHitWindow.cs
public class TauHitWindow : HitWindows
{
    public override bool IsHitResultAllowed(HitResult result)
        => result is HitResult.Great or HitResult.Ok or HitResult.Miss;

    public override void SetDifficulty(double difficulty) { }

    public override double WindowFor(HitResult result)
    {
        switch (result)
        {
            case HitResult.Great: return 30;
            case HitResult.Ok:    return 110;
            case HitResult.Miss:  return 150;
            default: return double.MaxValue;
        }
    }
}
```

### 5.6 HitWindows.Empty

不需要时间判定的 Ruleset 使用 `HitWindows.Empty`（Touhosu 生存游戏）：

```csharp
public class TouhosuHitObject : HitObject, IHasPosition
{
    protected override HitWindows CreateHitWindows() => HitWindows.Empty;
    // WindowFor 总返回 0，表示瞬间判定
}
```

---

## 6. BeatmapConverter 转谱系统

`BeatmapConverter<TObject>` 将来自其他 Ruleset 的通用 `HitObject` 转换为你的 Ruleset 专用的对象。

### 6.1 基本结构

```csharp
// 文件: osu.Game.Rulesets.MyGame/Beatmaps/MyGameBeatmapConverter.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.MyGame.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.MyGame.Beatmaps
{
    public class MyGameBeatmapConverter : BeatmapConverter<MyGameHitObject>
    {
        public MyGameBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset)
        {
        }

        public override bool CanConvert()
            => Beatmap.HitObjects.All(h => h is IHasPosition);

        // ★ 转换单个 HitObject
        protected override IEnumerable<MyGameHitObject> ConvertHitObject(
            HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float laneX = (original as IHasPosition)?.Position.X ?? 0;

            // 将 X 坐标映射到轨道
            int lane = laneX < 256 ? 0 : laneX < 384 ? 1 : 2;

            yield return new MyGameHitObject
            {
                Samples = original.Samples,
                StartTime = original.StartTime,
                Lane = lane,
            };
        }
    }
}
```

### 6.2 Mania 转谱器（复杂示例）

Mania 转谱器展示了从 osu! Standard 到 Mania 的完整转换逻辑：

```csharp
public class ManiaBeatmapConverter : BeatmapConverter<ManiaHitObject>
{
    // ★ 可配置属性
    public int TotalColumns { get; }
    public int TargetColumns { get; set; }
    public bool Dual { get; set; }

    // ★ 使用 Pattern 系统做智能映射
    protected override IEnumerable<ManiaHitObject> ConvertHitObject(
        HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PatternGenerator conversion;

        if (original is IHasLegacyHitObjectType legacy)
        {
            switch (legacy.LegacyType & LegacyHitObjectType.ObjectTypes)
            {
                case LegacyHitObjectType.Circle:
                    conversion = new HitCirclePatternGenerator(...);
                    break;
                case LegacyHitObjectType.Slider:
                    conversion = new SliderPatternGenerator(...);
                    break;
                // ...
            }

            foreach (var pattern in conversion.Generate())
                foreach (var obj in pattern.HitObjects)
                    yield return obj;
        }
    }
}
```

### 6.3 Hitokori 转谱器（后处理模式）

Hitokori 展示了 `ConvertBeatmap` 覆盖 + `PostProcess` 的复杂模式：

```csharp
public class HitokoriBeatmapConverter : BeatmapConverter<HitokoriHitObject>
{
    // 使用 [Moddable] 让 Mod 可以控制转换行为
    [Moddable] public bool NoHolds { get; set; }
    [Moddable] public bool Flip { get; set; }
    [Moddable] public float Speed { get; set; } = 1;

    protected override Beatmap<HitokoriHitObject> ConvertBeatmap(
        CancellationToken ct)
    {
        var beatmap = base.ConvertBeatmap(ct);

        // ★ 后处理管线
        ConnectHitObjects(beatmap);  // 创建双向链表
        ApplyPatterns(beatmap);      // 生成各种模式
        RemoveUnhittable(beatmap);   // 清理重叠对象

        return beatmap;
    }

    private void ApplyPatterns(Beatmap<HitokoriHitObject> beatmap)
    {
        var generator = new PatternGenerator(Speed, Flip, NoHolds);
        generator.FuckHoldsPatern.Apply(beatmap.HitObjects);
        generator.ReverseHoldPattern.Apply(beatmap.HitObjects);
        generator.SpinPattern.Apply(beatmap.HitObjects);
        generator.StairsPattern.Apply(beatmap.HitObjects);
        // ...
    }
}
```


### 6.4 IBeatmapProcessor（谱面后处理）

`IBeatmapProcessor` (`osu.Game\Beatmaps\IBeatmapProcessor.cs:12`) 在 BeatmapConverter 之后执行，分 Pre/Post 两个阶段：

```csharp
public interface IBeatmapProcessor
{
    // ★ PreProcess：在 HitObject.ApplyDefaults() 之前执行
    //   此时嵌套对象尚未创建
    void PreProcess();

    // ★ PostProcess：在 HitObject.ApplyDefaults() 之后执行
    //   嵌套对象已创建，Mod 已应用
    void PostProcess();
}
```

```csharp
// 使用示例：调整所有对象的 combo 信息
public class MyGameBeatmapProcessor : BeatmapProcessor
{
    public MyGameBeatmapProcessor(IBeatmap beatmap) : base(beatmap) { }

    public override void PostProcess()
    {
        base.PostProcess();

        foreach (var obj in Beatmap.HitObjects.OfType<MyGameHitObject>())
        {
            // 对转换后的对象做规则集特有处理
            if (obj.Lane == 0)
                obj.Samples.Add(new HitSampleInfo("special.wav"));
        }
    }
}

// 在 Ruleset 中接线
public override IBeatmapProcessor CreateBeatmapProcessor(IBeatmap beatmap)
    => new MyGameBeatmapProcessor(beatmap);
```

### 6.5 自定义谱面格式（BeatmapDecoder）

如果你的 Ruleset 有专有谱面格式（`.osu` 无法表达的数据），可以注册自定义解码器。osu!lazer 使用**静态注册表 + 魔术字符串前缀匹配**机制，而非接口注入。

#### 6.5.1 核心类层次

没有 `IBeatmapDecoder` 接口，解码器体系完全基于抽象类继承：

```
Decoder                          (抽象，含静态注册表)
  └── Decoder<TOutput> : Decoder (泛型抽象，添加 Decode/ParseStreamInto)
        ├── LegacyDecoder<T>     (分段解析器，处理 [Section] 格式)
        │     └── LegacyBeatmapDecoder : LegacyDecoder<Beatmap>
        │           ├── KaraokeLegacyBeatmapDecoder
        │           └── SpaceLegacyBeatmapDecoder
        ├── JsonBeatmapDecoder : Decoder<Beatmap>
        │     └── KaraokeJsonBeatmapDecoder
        └── BmsBeatmapDecoder : Decoder<Beatmap>  (直接继承，非 Legacy)
```

**关键文件**：`osu.Game\Beatmaps\Formats\Decoder.cs` 包含 `Decoder` + `Decoder<TOutput>` 以及静态注册/查找方法。

#### 6.5.2 注册机制：AddDecoder + SetFallbackDecoder

`Decoder` 基类维护两个全局静态字典（`Decoder.cs:29-31`）：

```csharp
public abstract class Decoder
{
    // Type → (magicString → constructorFunction)
    private static readonly Dictionary<Type, Dictionary<string, Func<string, Decoder>>> decoders
        = new Dictionary<Type, Dictionary<string, Func<string, Decoder>>>();

    // Type → fallbackConstructor
    private static readonly Dictionary<Type, Func<Decoder>> fallback_decoders
        = new Dictionary<Type, Func<Decoder>>();

    // ★ 注册：魔术字符串 → 解码器工厂
    protected static void AddDecoder<T>(string magic, Func<string, Decoder> constructor)
    {
        if (!decoders.TryGetValue(typeof(T), out var typedDecoders))
            decoders.Add(typeof(T), typedDecoders = new Dictionary<string, Func<string, Decoder>>());
        typedDecoders[magic] = constructor;
    }

    // ★ 设置回退：无魔术匹配时使用
    protected static void SetFallbackDecoder<T>(Func<Decoder> constructor)
    {
        fallback_decoders[typeof(T)] = constructor;
    }
}
```

**魔术字符串约定**：`.osu` 派生格式使用第一行头（如 `"osu file format v14"`），JSON 格式使用 `"{"` 或注释头如 `"// karaoke json file format v1"`，BMS 使用 `"#"` 和 `"*"`。

内置注册（`Decoder` 静态构造函数自动触发）：

| 解码器 | 魔术字符串 | 输出类型 |
|--------|-----------|---------|
| `LegacyBeatmapDecoder` | `"osu file format v"` | `Beatmap` |
| `JsonBeatmapDecoder` | `"{"` | `Beatmap` |
| `LegacyDifficultyCalculatorBeatmapDecoder` | `"osu file format v"` | `Beatmap` |

#### 6.5.3 查找机制：GetDecoder 前缀匹配

**文件**：`Decoder.cs:54-87`

```csharp
public static Decoder<T> GetDecoder<T>(LineBufferedReader stream) where T : new()
{
    // 1. Peek 第一非空行（不消耗流，因为该行可能是文件内容的一部分）
    string? line = stream.PeekLine()?.Trim();
    while (line != null && line.Length == 0)
    {
        stream.ReadLine();          // 消耗空行
        line = stream.PeekLine()?.Trim();
    }

    // 2. 前缀匹配：第一个魔术字符串是行前缀的解码器胜出
    var decoder = typedDecoders
        .Where(d => line.StartsWith(d.Key, StringComparison.InvariantCulture))
        .Select(d => d.Value)
        .FirstOrDefault();

    if (decoder != null)
        return (Decoder<T>)decoder.Invoke(line);  // 用匹配行构造（如解析版本号）

    // 3. 回退：无匹配则用 fallback
    if (!fallback_decoders.TryGetValue(typeof(T), out var fallbackDecoder))
        throw new IOException($"Unknown file format ({line})");

    return (Decoder<T>)fallbackDecoder.Invoke();
}
```

**核心调用点**（生产代码中何处触发解码）：
- `FlatWorkingBeatmap.GetBeatmap()` — 每次加载谱面
- `WorkingBeatmapCache` — 缓存谱面时
- `BeatmapImporter` — 导入谱面时
- `LegacyEditorBeatmapPatcher` — 编辑器修补旧谱面

#### 6.5.4 模式一：Legacy 格式（.osu 风格，继承 LegacyBeatmapDecoder）

**Space Ruleset** 示例 — 最简注册，无回退：

```csharp
// SpaceLegacyBeatmapDecoder.cs
public class SpaceLegacyBeatmapDecoder : LegacyBeatmapDecoder
{
    public new const int LATEST_VERSION = 1;

    public new static void Register()
    {
        // ★ 魔术字符串：第一行匹配 "osuspaceruleset file format v1"
        AddDecoder<Beatmap>("osuspaceruleset file format v", m =>
            new SpaceLegacyBeatmapDecoder(Parsing.ParseInt(m.Split('v').Last())));
        // ★ 不设回退，非空间谱面不会被此解码器处理
    }

    public SpaceLegacyBeatmapDecoder(int version = LATEST_VERSION) : base(version) { }

    protected override void ParseLine(Beatmap beatmap, Section section, string line, ...)
    {
        // 自定义解析逻辑...
    }
}
```

在 Ruleset 构造函数中触发注册：

```csharp
public class SpaceRuleset : Ruleset
{
    public SpaceRuleset() => SpaceLegacyBeatmapDecoder.Register();
}
```

**关键技巧**：
- `LegacyBeatmapDecoder` 自动处理 `[General]`/`[Metadata]`/`[Difficulty]`/`[TimingPoints]` 等通用段
- 子类只需 `override ParseLine()` 处理 `[HitObjects]` 等 Ruleset 专属段
- `new` 关键字遮蔽父类的 `LATEST_VERSION` 和 `Register()`
- 版本号从魔术字符串 `"xxx file format v{n}"` 中解析

#### 6.5.5 模式二：JSON 格式（继承 JsonBeatmapDecoder 或直接继承 Decoder<Beatmap>）

**Karaoke** JSON 解码器示例：

```csharp
public class KaraokeJsonBeatmapDecoder : JsonBeatmapDecoder
{
    public new static void Register()
    {
        // ★ 魔术字符串：JSON 文件开头的注释行
        AddDecoder<Beatmap>("// karaoke json file format v", m =>
            new KaraokeJsonBeatmapDecoder());
        // ★ 覆盖回退：任何无魔术匹配的谱面都用 karaoke 解码
        SetFallbackDecoder<Beatmap>(() => new KaraokeJsonBeatmapDecoder());
    }

    protected override void ParseStreamInto(LineBufferedReader stream, Beatmap output)
    {
        // 先消费魔术注释行（否则 '{' 会匹配基类 JsonBeatmapDecoder）
        string? firstLine = stream.ReadLine();

        // 然后正常 JSON 反序列化
        var serializer = JsonSerializer.Create(...);
        // ...
    }
}
```

**BMS** 直接继承模式（非 Legacy、非 JSON）：

```csharp
public class BmsBeatmapDecoder : Decoder<Beatmap>
{
    public static void Register()
    {
        AddDecoder<Beatmap>("#", _ => new BmsBeatmapDecoder());
        AddDecoder<Beatmap>("*", _ => new BmsBeatmapDecoder());
        // ★ 可注册多个魔术字符串
    }

    protected override void ParseStreamInto(LineBufferedReader stream, ...)
    {
        // 全手动逐行解析 BMS 格式
    }
}
```

#### 6.5.6 回退（Fallback）的覆盖策略

`SetFallbackDecoder<Beatmap>()` 后注册者覆盖先注册者。这意味着：

```
LegacyBeatmapDecoder.Register()
  → fallback = LegacyBeatmapDecoder   // osu! 默认

KaraokeLegacyBeatmapDecoder.Register()
  → fallback = KaraokeLegacyBeatmapDecoder  // ★ 覆盖！所有无魔术匹配的谱面会用 karaoke 解析

KaraokeJsonBeatmapDecoder.Register()  
  → fallback = KaraokeJsonBeatmapDecoder    // ★ 再次覆盖！
```

**建议**：如果你的 Ruleset 不需要接管所有谱面的解码，不要设置 fallback（如 Space、BMS）。

#### 6.5.7 完整注册流程

```
应用启动
  │
  ├── Decoder 静态构造函数触发
  │     ├── LegacyBeatmapDecoder.Register()
  │     │     ├── AddDecoder<Beatmap>("osu file format v", ...)
  │     │     └── SetFallbackDecoder<Beatmap>(...)
  │     └── JsonBeatmapDecoder.Register()
  │           └── AddDecoder<Beatmap>("{", ...)
  │
  ├── Ruleset 程序集加载 → Ruleset 构造函数触发
  │     ├── KaraokeRuleset() → KaraokeLegacyBeatmapDecoder.Register()
  │     │     AddDecoder<Beatmap>("karaoke file format v", ...)
  │     │     SetFallbackDecoder<Beatmap>(...)  ← 覆盖 osu! 的 fallback
  │     │
  │     └── SpaceRuleset() → SpaceLegacyBeatmapDecoder.Register()
  │           AddDecoder<Beatmap>("osuspaceruleset file format v", ...)
  │           (不设 fallback)
  │
  └── 加载谱面 → Decoder.GetDecoder<Beatmap>(stream)
        ├── Peek 第一行
        ├── 遍历 decoders[typeof(Beatmap)]，前缀匹配
        ├── 匹配：用魔术字符串构造解码器
        └── 不匹配：用 fallback 构造（若无则 IOException）
```

#### 6.5.8 注意事项

| 注意点 | 说明 |
|--------|------|
| 后缀匹配失败 | 前缀匹配的精确度取决于魔术字符串长度；`"osu file format v"` 不会匹配 `"osusomething"` |
| 同魔术覆盖 | 两个解码器用同一魔术字符串时，后注册者覆盖先注册者（字典 key 覆盖） |
| fallback 全局性 | fallback 是 per-Type 的全局单例；后设置的覆盖前的 |
| 版本解析 | `LegacyBeatmapDecoder` 从魔术行 `"osu file format v14"` 解析版本号传给构造函数 |
| 流不消耗 | `GetDecoder` 只 Peek 首行，不消费。解码器自己决定如何消费（Legacy 保留首行，JSON 跳过注释行） |


---


## 7. DrawableHitObject 绘制物系统

`DrawableHitObject<TObject>` (定义在 `osu.Game\Rulesets\Objects\Drawables\DrawableHitObject.cs:34`) 是 HitObject 在屏幕上的视觉表现和交互实体。

### 7.1 生命周期

```
InitialLifetimeOffset  ────► StartTime  ────► EndTime  ────► MaximumJudgementOffset
   (出现)                    (开始)           (结束)          (超时自动Miss)
```

四种状态变换（`ArmedState`）：
- `Idle` → 等待玩家操作
- `Hit` → 玩家命中
- `Miss` → 玩家未命中 / 超时

对应三个 transform 阶段：
1. `UpdateInitialTransforms()` — 从出现到 StartTime 的动画
2. `UpdateStartTimeStateTransforms()` — StartTime 时的被动变换
3. `UpdateHitStateTransforms(state)` — 判罚结果后的动画

### 7.2 完整示例（Pippidon Coin）

```csharp
// Coin.cs - Pippidon 的 DrawableHitObject
public class Coin : DrawableHitObject<PippidonObject>
{
    private bool laneLost;
    private BindableInt pippidonLane;

    public Coin(PippidonObject hitObject, TextureStore textures)
        : base(hitObject)
    {
        Size = new Vector2(40);
        Anchor = Anchor.CentreLeft;
        Origin = Anchor.Centre;
        Y = hitObject.Lane * PippidonPlayfield.LANE_HEIGHT;

        AddInternal(new Sprite
        {
            RelativeSizeAxes = Axes.Both,
            Texture = textures.Get("coin"),
        });
    }

    [BackgroundDependencyLoader]
    private void load(PippidonPlayfield playfield)
    {
        pippidonLane = playfield.PippidonLane.GetBoundCopy();
        pippidonLane.ValueChanged += e =>
        {
            if (e.OldValue == HitObject.Lane && e.NewValue != HitObject.Lane)
            {
                laneLost = true;
                UpdateResult(true);
                laneLost = false;
            }
            else
            {
                UpdateResult(true);
            }
        };
    }

    protected override void Update()
    {
        base.Update();
        if (HitObject.HitWindows.CanBeHit(HitObject.StartTime - Time.Current))
            UpdateResult(true);
    }

    // ★ 判罚检查
    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        var absOffset = Math.Abs(timeOffset);

        if (laneLost && HitObject.HitWindows.CanBeHit(absOffset))
        {
            ApplyResult(HitObject.HitWindows.ResultFor(timeOffset));
        }
        else if (timeOffset >= 0)
        {
            if (HitObject.HitWindows.CanBeHit(absOffset))
            {
                if (pippidonLane.Value == HitObject.Lane)
                    ApplyResult(HitObject.HitWindows.ResultFor(timeOffset));
            }
            else
                ApplyResult(HitResult.Miss);
        }
    }

    // ★ 动画
    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        switch (state)
        {
            case ArmedState.Hit:
                this.ScaleTo(5, 1500, Easing.OutQuint)
                    .FadeOut(1500, Easing.OutQuint).Expire();
                break;
            case ArmedState.Miss:
                this.FadeColour(Color4.Red, 100, Easing.InQuint)
                    .Then().FadeOut(1000, Easing.InQuint).Expire();
                break;
        }
    }
}
```

### 7.3 判罚调用 API

```csharp
// 直接设置结果
ApplyResult(HitResult.Great);

// 使用 ResultFor 从 HitWindows 计算
ApplyResult(HitObject.HitWindows.ResultFor(timeOffset));

// 最高 / 最低结果
ApplyMaxResult();   // r.Type = Judgement.MaxResult
ApplyMinResult();   // r.Type = Judgement.MinResult
```

### 7.4 CheckHittable 委托模式

osu! 使用 `CheckHittable` 委托（由 Playfield / HitPolicy 注入）来判断一个 DrawableHitObject 当前是否可被按下。这是 Note Lock 机制的核心。

**委托定义**（`DrawableOsuHitObject.cs:44`）：

```csharp
// 签名：给定 Drawable、时间偏移、期望结果 → 返回 ClickAction
public Func<DrawableHitObject, double, HitResult, ClickAction> CheckHittable;
```

**强制命中/未命中方法**（用于 Note Lock 连锁处理）：

```csharp
// ★ 当用户点击时，HitPolicy 可能强制命中较早的未判定对象
public void HitForcefully() => ApplyMaxResult();
public void MissForcefully() => ApplyMinResult();
```

**catch 的位置判定委托**（`DrawableCatchHitObject.cs:57-71`）：

```csharp
// catch 不使用时间判定，而是位置判定
public Func<CatchHitObject, bool> CheckPosition;

protected override void CheckForResult(bool userTriggered, double timeOffset)
{
    if (timeOffset >= 0)
    {
        if (CheckPosition?.Invoke((CatchHitObject)HitObject) == true)
            ApplyResult(HitResult.Great);
        else
            ApplyResult(HitResult.Miss);
    }
}
```

**mania 的 CheckHittable 绑定**（`Column.cs:166-173`）：

```csharp
protected override void OnNewDrawableHitObject(DrawableHitObject drawable)
{
    // ★ 列的 HitPolicy 注入可判定委托
    if (drawable is DrawableManiaHitObject maniaObj)
    {
        maniaObj.CheckHittable = (dho, time, result) =>
            hitPolicy.IsHittable((DrawableManiaHitObject)dho, time, result);
    }
}
```

### 7.5 分段状态变换

当前 `DrawableHitObject` 使用三个分段方法管理状态变换：

```csharp
// ★ Phase 1: 从 InitialLifetimeOffset 到 StartTime 的入场动画
protected override void UpdateInitialTransforms()
{
    this.FadeInFromZero(TimePreempt);
    this.ScaleTo(1f, TimePreempt);
}

// ★ Phase 2: StartTime 时的被动变换
protected override void UpdateStartTimeStateTransforms()
{
    // 例如：变暗表示窗口已过
}

// ★ Phase 3: 判罚结果后的动画
protected override void UpdateHitStateTransforms(ArmedState state)
{
    switch (state)
    {
        case ArmedState.Hit:
            this.ScaleTo(1.5f, 200).Then().FadeOut(300).Expire();
            break;
        case ArmedState.Miss:
            this.FadeColour(Color4.Red, 100)
                .Then().FadeOut(500).Expire();
            break;
    }
}
```

> **注意**：当前本地 osu! API 没有 `UpdateStateTransforms` 覆盖点；应使用上述三个分段方法。

---

## 8. Playfield 游戏区域系统

`Playfield` (定义在 `osu.Game\Rulesets\UI\Playfield.cs:33`) 是游戏的核心容器，管理所有 `DrawableHitObject`。

### 8.1 关键职责

| 职责 | 说明 |
|------|------|
| 管理 HitObjectContainer | 生命周期管理 + 对象池 |
| 注册对象池 | `RegisterPool<T, TD>()` |
| 传递判罚事件 | `NewResult` → `DrawableRuleset` → `ScoreProcessor` |
| 管理光标 | `CreateCursor()` |
| 预加载音频 | 自动预加载 HitObject.Samples |

### 8.2 完整示例

```csharp
// 文件: osu.Game.Rulesets.MyGame/UI/MyGamePlayfield.cs
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.MyGame.Objects;
using osu.Game.Rulesets.MyGame.Objects.Drawables;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.MyGame.UI
{
    public partial class MyGamePlayfield : Playfield
    {
        public MyGamePlayfield()
        {
            // 设置固定大小（如基类 Cursor 坐标空间）
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
            Size = new Vector2(512, 384);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // ★ 注册对象池
            RegisterPool<MyGameHitObject, DrawableMyGameHitObject>(10, 200);
        }

        // ★ 每当新的 DrawableHitObject 被创建时调用
        protected override void OnNewDrawableHitObject(DrawableHitObject drawable)
        {
            base.OnNewDrawableHitObject(drawable);

            // 在这里连接自定义逻辑
            // 例如 Touhosu 在此连接 CheckHit / DistanceToPlayer 委托
        }

        // ★ 后处理（所有对象加载完成后）
        public override void PostProcess()
        {
            base.PostProcess();
            // 在此做最终计算
        }
    }
}
```

### 8.3 Touhosu Playfield 示例（碰撞判定）

```csharp
// TouhosuPlayfield.cs - 使用位置碰撞而非时间判定
protected override void OnNewDrawableHitObject(DrawableHitObject dhObject)
{
    if (dhObject is DrawableProjectile<TouhosuHitObject> projectile)
    {
        projectile.CheckHit += () =>
            Vector2.Distance(projectile.Position, player.Position) < HIT_RADIUS;

        projectile.DistanceToPlayer += () =>
            Vector2.Distance(projectile.Position, player.Position);
    }
}
```

### 8.4 Hit Policy 判定策略系统

Hit Policy 控制 **Note Lock**（锁键）——用户按下时判定哪个对象、是否允许"穿透"到后面的对象。

**osu! 的 StartTimeOrderedHitPolicy**——找到最后一个"阻塞"对象：

```csharp
public ClickAction CheckHittable(DrawableHitObject hitObject, 
    double time, HitResult result)
{
    var blocking = findLatestBlockingObject(hitObject);
    if (blocking != null && !blocking.Judged)
        return ClickAction.HitBlocked; // 阻塞 → 不可判定当前
    return ClickAction.CanHit;
}

// ★ 关键：只有 DrawableHitCircle 才会阻塞后续对象
// SliderTicks, SliderTails, Spinners 不阻塞
protected virtual bool hitObjectCanBlockFutureHits(DrawableHitObject dho) 
    => dho is DrawableHitCircle;
```

**mania 的 OrderedHitPolicy**——简单窗口截断：

```csharp
// ★ 有"下一个"对象时，当前时间 < 下一个的 StartTime 才可判定
public bool IsHittable(DrawableManiaHitObject hitObject, 
    double time, HitResult result)
{
    var next = findNextObject(hitObject);
    if (next == null) return true;
    return time < next.HitObject.StartTime;
}
```

**catch 的方案**：无 Hit Policy（纯位置判定，不存在 Note Lock 问题）。

### 8.5 ProxyContainer 代理容器

> 所有 4 个内置 Ruleset 都使用此模式，将特定内容提升到不同渲染层。

```csharp
// OsuPlayfield.cs — 三层代理
// 底层：普通 HitObject
// 中层：ApproachCircle 代理
// 顶层：Spinner 代理

private readonly ProxyContainer approachCircleProxy;
private readonly ProxyContainer spinnerProxy;

private void load()
{
    // ★ 代理容器在 HitObjectContainer 之上
    AddInternal(spinnerProxy = new ProxyContainer());
    AddInternal(approachCircleProxy = new ProxyContainer());
    AddInternal(HitObjectContainer);
}

private void onJudgementLoaded(DrawableHitObject dho)
{
    // ★ 将特定内容代理到更高层
    if (dho is DrawableSpinner)
        spinnerProxy.Add(dho.CreateProxy());
}
```

### 8.6 JudgementPooler 判罚池

> 所有 4 个内置 Ruleset 都使用。

```csharp
public partial class MyPlayfield : Playfield
{
    private readonly JudgementPooler judgementPooler;

    [BackgroundDependencyLoader]
    private void load()
    {
        AddInternal(judgementPooler = new JudgementPooler(DisplayJudgements)
            { RelativeSizeAxes = Axes.Both });
    }

    protected override void OnNewDrawableHitObject(DrawableHitObject dho)
    {
        dho.OnNewResult += (drawable, result) =>
        {
            var judgement = judgementPooler.Get(drawable, result);
            if (judgement != null)
            {
                AddInternal(judgement);
                judgement.Apply(result, drawable);
            }
        };
    }
}
```

### 8.7 自定义 LifetimeEntry

防止池化对象在非帧稳定模式下驻留过久：

```csharp
// OsuPlayfield.cs
public class OsuHitObjectLifetimeEntry : HitObjectLifetimeEntry
{
    public OsuHitObjectLifetimeEntry(HitObject hitObject)
        : base(hitObject)
    {
        // ★ LifetimeEnd = EndTime + Miss 窗口
        LifetimeEnd = HitObject.GetEndTime() + HitObject.HitWindows.WindowFor(HitResult.Miss);
    }
}

protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject)
    => new OsuHitObjectLifetimeEntry(hitObject);
```

### 8.8 Taiko BarLine 分离路由

```csharp
// TaikoPlayfield.cs — BarLine 路由到专用子 Playfield
public override void Add(HitObject hitObject)
{
    if (hitObject is BarLine barLine)
    {
        barLinePlayfield.Add(barLine); // ★ 不走 HitObjectContainer
        return;
    }
    base.Add(hitObject);
}
```

### 8.9 Mania Stage/Column 双层路由

```csharp
// ManiaPlayfield.cs
public override void Add(HitObject hitObject)
{
    var col = ((ManiaHitObject)hitObject).Column;
    getStageByColumn(col).Add(hitObject);
}
// Stage.Add → columns[colIdx].Add(hitObject)
```

### 8.10 Catch 的 CatcherArea 输入前置

输入不在单个 HitObject 上，而在 CatcherArea 上——唯一的例外：

```csharp
public partial class CatcherArea : Container, IKeyBindingHandler<CatchAction>
{
    public bool OnPressed(KeyBindingPressEvent<CatchAction> e)
    {
        switch (e.Action)
        {
            case CatchAction.MoveLeft: currentDirection = -1; return true;
            case CatchAction.MoveRight: currentDirection = 1; return true;
            case CatchAction.Dash: catcher.IsDashing = true; return true;
        }
        return false;
    }
}

// ★ 关键顺序：CatcherArea 必须在 HitObjectContainer 之前
AddInternal(catcherArea);
AddInternal(HitObjectContainer);
```

---

## 9. Object Pooling 对象池

对象池是提升性能的关键机制，避免大量创建/Destroy DrawableHitObject。

### 9.1 在 Playfield 中注册

```csharp
public partial class MyGamePlayfield : Playfield
{
    [BackgroundDependencyLoader]
    private void load()
    {
        // RegisterPool<HitObject类型, Drawable类型>(初始数, 最大数)
        RegisterPool<MyGameHitObject, DrawableMyGameHitObject>(20, 200);
        RegisterPool<MyGameTickObject, DrawableMyGameTick>(50, 500);
    }
}
```

### 9.2 池化的 DrawableHitObject

池化的 DrawableHitObject 需要有无参构造函数（`new()` 约束），通过 `OnApply()`/`OnFree()` 接收和释放 HitObject：

```csharp
public partial class DrawableMyGameHitObject : DrawableHitObject<MyGameHitObject>
{
    // 无参构造（池化要求）
    public DrawableMyGameHitObject()
        : base(null) // 初始无 HitObject
    {
    }

    // ★ 接收新 HitObject 时调用
    protected override void OnApply()
    {
        base.OnApply();
        // 从 HitObject 读取数据设置视觉效果
        Position = new Vector2(HitObject.X, HitObject.Y);
    }

    // ★ 释放/归还池时调用
    protected override void OnFree()
    {
        base.OnFree();
        // 清理状态，准备下次使用
        Alpha = 0;
        Scale = Vector2.One;
    }
}
```

### 9.3 DrawableRuleset 中返回 null 启用池

```csharp
public override DrawableHitObject<MyGameHitObject>? CreateDrawableRepresentation(
    MyGameHitObject h)
{
    // ★ 返回 null = 使用 Playfield 中注册的对象池
    return null;
}
```

---

## 10. 物品绘制与布局系统

DrawableHitObject **放在哪里、如何移动**，是 Ruleset 视觉设计的核心。不同玩法采用截然不同的坐标系和布局策略。

### 10.1 坐标系概述

osu! 中存在两套坐标系：

| 坐标系 | 定义 | 获取方式 |
|--------|------|---------|
| **Gamefield Space** (游戏场空间) | HitObjectContainer 的本地坐标，DrawableHitObject 的 `Position` 操作的坐标 | `HitObjectContainer.ToLocalSpace()` |
| **Screen Space** (屏幕空间) | 最终渲染到屏幕的像素坐标 | `HitObjectContainer.ToScreenSpace()` |

`Playfield` 基类提供了两个标准转换函数（`Playfield.cs:55-60`）：

```csharp
// 游戏场坐标 → 屏幕坐标
public Func<Vector2, Vector2> GamefieldToScreenSpace => HitObjectContainer.ToScreenSpace;

// 屏幕坐标 → 游戏场坐标
public Func<Vector2, Vector2> ScreenSpaceToGamefield => HitObjectContainer.ToLocalSpace;
```

所有 DrawableHitObject 的位置操作都在 **游戏场坐标系** 中进行，经过 Playfield 的缩放/旋转后再映射到屏幕。

### 10.2 五种布局模式

不同 Ruleset 采用以下布局策略之一或混合：

```
┌────────────────────┬──────────────────┬──────────────────────────────┐
│ 模式               │ 代表 Ruleset      │ 位置来源                      │
├────────────────────┼──────────────────┼──────────────────────────────┤
│ 绝对像素定位        │ osu!, Touhosu    │ HitObject 存储 (X,Y) 坐标     │
│ 轨道/通道分配       │ Pippidon, Mania  │ Lane/Column 属性              │
│ 极坐标角度定位      │ Tau              │ Angle 属性 + 径向动画          │
│ 时间→空间映射       │ Mania, Taiko     │ StartTime → 滚动位置          │
│ 相机跟随系统        │ Hitokori         │ 全局坐标系 − 相机偏移           │
└────────────────────┴──────────────────┴──────────────────────────────┘
```

### 10.3 模式一：绝对像素定位（osu! 标准）

osu! Standard 使用 (0..512, 0..384) 的游戏场坐标。

**HitObject 存储位置** (`OsuHitObject.cs:54-62`)：

```csharp
public class OsuHitObject : HitObject
{
    public readonly Bindable<Vector2> PositionBindable = new Bindable<Vector2>();

    public Vector2 Position
    {
        get => PositionBindable.Value;
        set => PositionBindable.Value = value;
    }

    // Stacking 偏移（重叠圆圈自动错开）
    public Vector2 StackedPosition => Position + StackOffset;
}
```

**DrawableHitObject 绑定位置** (`DrawableOsuHitObject.cs:57-65`)：

```csharp
protected override void OnApply()
{
    base.OnApply();
    // 将 Drawable 的位置绑定到 HitObject 的位置
    PositionBindable.BindTo(HitObject.PositionBindable);
    StackHeightBindable.BindTo(HitObject.StackHeightBindable);
    ScaleBindable.BindTo(HitObject.ScaleBindable);
}

protected override void OnFree()
{
    base.OnFree();
    PositionBindable.UnbindFrom(HitObject.PositionBindable);
    StackHeightBindable.UnbindFrom(HitObject.StackHeightBindable);
    ScaleBindable.UnbindFrom(HitObject.ScaleBindable);
}

// DrawableHitCircle 响应位置变化
private void load()
{
    PositionBindable.BindValueChanged(_ => UpdatePosition());
}

private void UpdatePosition()
{
    Position = HitObject.StackedPosition; // 取 stack 后的位置
}
```

**Playfield 尺寸** (`OsuPlayfield.cs:47`)：

```csharp
public partial class OsuPlayfield : Playfield
{
    public static readonly Vector2 BASE_SIZE = new Vector2(512, 384);

    [BackgroundDependencyLoader]
    private void load()
    {
        Size = BASE_SIZE;
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
    }
}
```

**PlayfieldAdjustmentContainer** 的层级结构（`DrawableRuleset.cs:177-188`）：

```
DrawableRuleset
  └── FrameStabilityContainer (帧稳定容器)
        ├── FrameStableComponents (HUD)
        └── AudioContainer
              └── PassThroughInputManager
                    └── PlayfieldAdjustmentContainer  ← 尺寸锚点，影响所有子元素
                          └── Playfield (512×384, 居中)
                                └── HitObjectContainer (填充)
                                      └── DrawableHitCircle @ Position=StackedPosition
```

`PlayfieldAdjustmentContainer` (`PlayfieldAdjustmentContainer.cs:14-17`) 是一个简单的全尺寸容器：

```csharp
public partial class PlayfieldAdjustmentContainer : Container
{
    public PlayfieldAdjustmentContainer()
    {
        RelativeSizeAxes = Axes.Both;
    }
}
```

Mod（如 Flashlight）可以附加到此容器，实现覆盖整个游戏区域的效果。

### 10.4 模式二：轨道/通道分配

多个轨道水平排列，HitObject 根据 `Lane` / `Column` 属性落入对应轨道。

**Pippidon 三轨道示例**（`PippidonPlayfield.cs`）：

```csharp
public const float LANE_HEIGHT = 100;

// 每个 Coin 的 Y 坐标 = Lane * LANE_HEIGHT
public Coin(PippidonObject hitObject, TextureStore textures) : base(hitObject)
{
    Anchor = Anchor.CentreLeft;
    Origin = Anchor.Centre;
    Y = hitObject.Lane * LANE_HEIGHT; // lane ∈ {-1, 0, 1}
}
```

**Mania 多列布局**（`Stage.cs:203-209`, `Column.cs:67-79`）：

```csharp
// Stage 包含多个 Column
public partial class Stage : CompositeDrawable, IHasKeyCounter
{
    public Stage(StageDefinition definition)
    {
        // ColumnFlow 自动水平排列 Column
        InternalChild = new ColumnFlow<Column>(definition.Columns)
        {
            RelativeSizeAxes = Axes.Y,
            AutoSizeAxes = Axes.X,
        };
    }
}

// Column 路由 HitObject 到对应列
public void Add(HitObject hitObject)
{
    int columnIndex = ((ManiaHitObject)hitObject).Column;
    columns[columnIndex].Add(hitObject);
}
```

```csharp
// Column 自身是固定宽度的垂直布局容器
public partial class Column : ScrollingPlayfield
{
    public const float COLUMN_WIDTH = 80;

    public Column(int index)
    {
        Index = index;
        RelativeSizeAxes = Axes.Y;
        Width = COLUMN_WIDTH;
    }

    // 通过 Add(HitObject) 添加池化管理条目
}
```

**通用轨道布局模板**：

```csharp
public partial class MyLanePlayfield : Playfield
{
    public const float LANE_WIDTH = 80;
    public int LaneCount { get; }

    public MyLanePlayfield(int laneCount)
    {
        LaneCount = laneCount;
        Size = new Vector2(LANE_WIDTH * laneCount, 600);
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
    }

    protected override void OnNewDrawableHitObject(DrawableHitObject drawable)
    {
        base.OnNewDrawableHitObject(drawable);

        if (drawable.HitObject is MyLaneHitObject laneObj)
        {
            // X：根据轨道索引居中排列
            drawable.X = (laneObj.Lane + 0.5f) * LANE_WIDTH;
            // Y：由 Drawable 动画控制滚动
        }
    }
}
```

### 10.5 模式三：极坐标/角度定位（Tau）

Tau 使用 **角度 + 径向距离** 的极坐标系。HitObject 不存储 (X,Y)，只存储角度值。

**HitObject 定义**（`AngledTauHitObject.cs:8-17`）：

```csharp
public class AngledTauHitObject : TauHitObject, IHasAngle
{
    public readonly BindableFloat AngleBindable = new BindableFloat();
    public float Angle
    {
        get => AngleBindable.Value;
        set => AngleBindable.Value = value;
    }
}
```

**Drawable 通过 Rotation 设置角度**（`DrawableAngledTauHitObject.cs:29-39`）：

```csharp
[BackgroundDependencyLoader]
private void load()
{
    // 角度变化 → 旋转
    angleBindable.BindValueChanged(r => Rotation = r.NewValue);
}

protected override void OnApply()
{
    base.OnApply();
    // 绑定到 HitObject 的角度
    angleBindable.BindTo(((AngledTauHitObject)HitObject).AngleBindable);
}

protected override void OnFree()
{
    base.OnFree();
    angleBindable.UnbindFrom(((AngledTauHitObject)HitObject).AngleBindable);
}
```

**径向距离通过动画控制**（`DrawableBeat.cs:46-56`）：

```csharp
// Beat 从边缘移向中心
protected override void UpdateInitialTransforms()
{
    // Y < 0 表示在中心上方（外部），通过 MoveToY 滑向中心
    DrawableBox.MoveToY(-0.5f, HitObject.TimePreempt);
}
```

**Playfield 是正方形**（`TauPlayfield.cs:30-53`）：

```csharp
public partial class TauPlayfield : Playfield
{
    public static readonly Vector2 BASE_SIZE = new Vector2(768); // 768×768
    // Anchor = Centre, Origin = Centre
}
```

**极坐标布局模板**：

```csharp
// 角度 + 半径布局
protected override void OnApply()
{
    base.OnApply();

    float angle = ((IAngledHitObject)HitObject).Angle;
    float radius = 200; // 固定半径

    // 将极坐标转为笛卡尔坐标作为 Position
    Position = new Vector2(
        (float)(Math.Cos(angle) * radius),
        (float)(Math.Sin(angle) * radius)
    );

    // 旋转使对象面向圆心方向
    Rotation = MathHelper.RadiansToDegrees(angle) + 90;
}

// 径向缩入动画
protected override void UpdateInitialTransforms()
{
    this.MoveTo(Vector2.Zero, HitObject.TimePreempt); // 移向中心
    this.ScaleTo(1.5f).Then().ScaleTo(1f, HitObject.TimePreempt);
}
```

### 10.6 模式四：时间→空间映射（Scrolling Ruleset）

滚动式玩法（Mania, Taiko, Pippidon）使用**时间到位置的函数映射**。

**核心滚动定位**（`ScrollingHitObjectContainer.cs:281-289`）：

```csharp
private void updatePosition(DrawableHitObject hitObject, double currentTime, ...)
{
    // ★ 时间 → 空间位置
    float position = PositionAtTime(
        hitObject.HitObject.StartTime,  // 对象的命中时间
        currentTime,                     // 当前游戏时间
        parentHitObjectStartTime         // 父对象的开始时间
    );

    // 根据滚动轴设置 X 或 Y
    if (scrollingAxis == Direction.Horizontal)
        hitObject.X = position;
    else
        hitObject.Y = position;
}
```

**PositionAtTime 的关键计算**（`ScrollingHitObjectContainer.cs:98-104`）：

```csharp
public float PositionAtTime(double time, double currentTime, double? parentHitObjectStartTime = null)
{
    // algorithm 是 IScrollAlgorithm 的实现
    float pos = algorithm.Value.PositionAt(
        time,                     // 对象的命中时间
        currentTime,              // 当前时间
        timeRange.Value,          // 可视时间窗口（如 5000ms）
        scrollLength              // 滚动轴长度（像素）
    );

    // 向下或向右滚动时反转
    if (Direction.Value == ScrollingDirection.Down
        || Direction.Value == ScrollingDirection.Right)
        pos = -pos;

    return pos;
}
```

**IScrollingInfo 控制参数**（`IScrollingInfo.cs:10-26`）：

```csharp
public interface IScrollingInfo
{
    IBindable<ScrollingDirection> Direction { get; }  // Up/Down/Left/Right
    IBindable<double> TimeRange { get; }               // 可视时间范围(ms)
    IBindable<IScrollAlgorithm> Algorithm { get; }     // 位置映射算法
}
```

**IScrollAlgorithm 接口**：

```csharp
public interface IScrollAlgorithm
{
    float PositionAt(double time, double currentTime, double timeRange, float scrollLength);
    double TimeAt(float position, double currentTime, double timeRange, float scrollLength);
    float GetLength(double startTime, double endTime, double timeRange, float scrollLength);
    double GetDisplayStartTime(double time, double timeRange);
}
```

**长对象（Hold Notes）的尺寸计算**（`ScrollingHitObjectContainer.cs:258-279`）：

```csharp
private void updateLayoutRecursive(DrawableHitObject hitObject, double currentTime)
{
    if (hitObject.HitObject is IHasDuration hasDuration)
    {
        float length = LengthAtTime(
            hitObject.HitObject.StartTime, hasDuration.EndTime, currentTime);

        if (scrollingAxis == Direction.Horizontal)
            hitObject.Width = length;
        else
            hitObject.Height = length;
    }

    // 递归处理所有嵌套对象
    foreach (var nested in hitObject.NestedHitObjects)
        updateLayoutRecursive(nested, currentTime);
}
```

### 10.7 模式五：相机跟随系统（Hitokori）

Hitokori 使用**全局瓦片坐标系 + 平滑相机跟随**，所有对象的屏幕位置 = 瓦片坐标 − 相机位置。

**瓦片坐标计算**（`TilePoint.cs:139-168`）：

```csharp
public class TilePoint : HitokoriHitObject, IHasTilePosition
{
    // ★ 递归计算标准化瓦片坐标
    public Vector2 NormalizedTilePosition
    {
        get
        {
            if (Parent == null) return Vector2.Zero;

            // 父位置 + (cos(入角), sin(入角)) × 距离
            return Parent.NormalizedTilePosition
                + new Vector2(
                    (float)Math.Cos(InAngle),
                    (float)Math.Sin(InAngle))
                * (float)Distance;
        }
    }

    // 实际像素位置 = 标准化坐标 × 瓦片间距
    public Vector2 TilePosition => NormalizedTilePosition * HitokoriTile.SPACING;
    // SPACING = 140
}

public interface IHasTilePosition
{
    Vector2 TilePosition { get; }
}
```

**每帧相机跟随更新**（`HitokoriPlayfield.cs:115-129`）：

```csharp
private void UpdateOffsets()
{
    // 计算焦点：玩家位置与活跃瓦片位置的平均值
    var followTiles = Tiles.AliveObjects.OfType<IHasTilePosition>();
    var averagePosition = (followTiles.AverageOr(x => x.TilePosition, Hitokori.TilePosition)
                           + Hitokori.TilePosition) / 2;

    // ★ 平滑相机移动
    CameraPosition.AnimateTo(averagePosition, 300 / CameraSpeed.Value);

    // ★ 所有对象的屏幕位置 = 瓦片坐标 − 相机位置
    foreach (var tile in Tiles.AliveObjects.OfType<IHasTilePosition>())
    {
        if (tile is Drawable drawable)
            drawable.Position = tile.TilePosition - CameraPosition;
    }

    // 玩家抖动容器
    HitokoriShakeContainer.Position = Hitokori.TilePosition - CameraPosition;
}
```

**AnimatedVector（平滑插值器）**（`AnimatedDouble.cs:29-99`）：

```csharp
public class AnimatedVector
{
    private readonly Transformable target;

    public Vector2 Value { get; private set; }

    public void AnimateTo(Vector2 targetValue, double duration)
    {
        // 使用 osu!Framework 的 Transforms 系统做平滑插值
        target.TransformBindableTo(valueBindable, targetValue, duration);
    }
}
```

**相机跟随布局模板**：

```csharp
public partial class MyCameraPlayfield : Playfield
{
    private Vector2 cameraPosition;
    private Vector2 playerPosition;

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        // ★ 计算相机目标位置
        Vector2 cameraTarget = playerPosition;

        // ★ 平滑插值
        float lerpFactor = (float)Clock.ElapsedFrameTime * 5;
        cameraPosition = Vector2.Lerp(cameraPosition, cameraTarget, lerpFactor);

        // ★ 应用相机偏移到所有活跃对象
        foreach (var dho in HitObjectContainer.AliveObjects)
        {
            if (dho is IHasWorldPosition worldObj)
                dho.Position = worldObj.WorldPosition - cameraPosition;
        }
    }
}
```

### 10.8 Touhosu 弹幕移动

Touhosu 结合了绝对像素定位 + MoveTo 动画实现弹幕飞行。

**弹幕终点计算**（`DrawableConstantMovingProjectile.cs:76-107`）：

```csharp
// 计算弹幕沿角度飞行后撞到哪个墙壁
private Vector2 getFinalPosition()
{
    float angle = ((AngeledProjectile)HitObject).Angle;
    Vector2 start = HitObject.Position;

    // 确定目标墙壁（上/右/下/左）
    Wall targetWall = getTargetWall(angle);

    // 射线—墙壁碰撞计算交点
    Vector2 wallNormal, wallPoint;
    switch (targetWall)
    {
        case Wall.Top:
            wallNormal = new Vector2(0, 1);
            wallPoint = new Vector2(0, 0);
            break;
        case Wall.Right:
            wallNormal = new Vector2(-1, 0);
            wallPoint = new Vector2(PLAYFIELD_WIDTH, 0);
            break;
        // ...
    }

    // 交点 = start + direction * t，其中 direction = (cos(angle), sin(angle))
    Vector2 direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    float t = Vector2.Dot(wallPoint - start, wallNormal) / Vector2.Dot(direction, wallNormal);
    return start + direction * t;
}
```

**弹幕飞行 transform**（`DrawableConstantMovingProjectile.cs:28-37`）：

```csharp
protected override void UpdateInitialTransforms()
{
    Vector2 finalPosition = getFinalPosition();
    float duration = Vector2.Distance(HitObject.Position, finalPosition)
                     / HitObject.SpeedMultiplier * 5;

    this.MoveTo(finalPosition, duration).Then().Expire();
}
```

### 10.9 Playfield 缩放适配

屏不同幕尺寸下，Playfield 的 `BASE_SIZE` 需要通过 `PlayfieldAdjustmentContainer` 等比例缩放适配。

**osu! 的缩放适配逻辑**（osu! 使用 `OsuPlayfieldAdjustmentContainer`）：

```csharp
public partial class OsuPlayfieldAdjustmentContainer : PlayfieldAdjustmentContainer
{
    private const float base_size = 512; // osu! 基准宽

    protected override void Update()
    {
        base.Update();

        // ★ 根据屏幕宽度计算缩放
        float scale = DrawWidth / base_size;
        Scale = new Vector2(scale);
    }
}
```

**通用等比例缩放模板**：

```csharp
public partial class MyPlayfieldAdjustmentContainer : PlayfieldAdjustmentContainer
{
    // Playfield 的设计尺寸
    public static readonly Vector2 DESIGN_SIZE = new Vector2(800, 600);

    protected override void Update()
    {
        base.Update();

        // 计算能填满屏幕的最大缩放比例（保持宽高比）
        float scaleX = DrawWidth / DESIGN_SIZE.X;
        float scaleY = DrawHeight / DESIGN_SIZE.Y;
        float scale = Math.Min(scaleX, scaleY);

        // 统一缩放，保持宽高比
        Playfield.Scale = new Vector2(scale);
    }
}
```

### 10.10 嵌套 Playfield 布局

某些玩法需要多个子游戏区域（如 Mania 的双场地模式、多玩家模式）。

```csharp
public partial class MyDualPlayfield : Playfield
{
    public readonly Playfield LeftField;
    public readonly Playfield RightField;

    public MyDualPlayfield()
    {
        LeftField = new MySubPlayfield { Anchor = Anchor.CentreLeft };
        RightField = new MySubPlayfield { Anchor = Anchor.CentreRight };

        // ★ 注册为嵌套 Playfield（共享判罚事件、DisplayJudgements）
        AddNested(LeftField);
        AddNested(RightField);

        // 添加为子 Drawable
        AddRangeInternal(new Drawable[] { LeftField, RightField });
    }
}
```

`AddNested()` 自动绑定：
- `NewResult` / `RevertResult` 事件向上冒泡
- `DisplayJudgements` 同步
- `HitObjectUsageBegan/Finished` 事件转发

### 10.11 SceneGraph 完整层级图

```
DrawableRuleset<TObject>                   ← 游戏实例顶层
  └── FrameStabilityContainer              ← 帧稳定容器
        ├── FrameStableComponents          ← HUD（不受 Playfield 缩放影响）
        └── AudioContainer                 ← 音频容器
              └── PassThroughInputManager  ← 输入转换
                    ├── PlayfieldAdjustmentContainer  ← 缩放锚点
                    │     └── Playfield               ← 游戏区域 (Size=BASE_SIZE)
                    │           ├── Background         ← 背景层
                    │           ├── HitObjectContainer ← 对象容器
                    │           │     ├── DrawableHitObject1 @ Position
                    │           │     ├── DrawableHitObject2 @ Position
                    │           │     └── ...
                    │           ├── Player/Cursor      ← 玩家角色/光标
                    │           └── NestedPlayfields   ← 嵌套游戏区域
                    └── Overlays                      ← 悬浮层（不受缩放影响）
```

### 10.12 布局策略选择指南

| 你的玩法特征 | 推荐布局模式 | 参考 |
|-------------|------------|------|
| 音符有固定屏幕坐标 | 绝对像素定位 | osu!, Touhosu |
| 音符落入固定轨道 | 轨道/通道分配 | Pippidon, Mania |
| 环形/圆形布局 | 极坐标角度定位 | Tau |
| 从一侧滚动到另一侧 | 时间→空间映射 + DrawableScrollingRuleset | Mania, Taiko |
| 地图漫游/开放世界 | 相机跟随系统 | Hitokori |
| 弹幕/Bullet Hell | 绝对像素 + MoveTo 动画 | Touhosu |

---

## 11. DrawableRuleset 游戏实例系统

`DrawableRuleset<TObject>` (定义在 `osu.Game\Rulesets\UI\DrawableRuleset.cs:43`) 是游戏实例的顶层容器。

### 11.1 两种基类选择

| 基类 | 适用场景 | Key 特性 |
|------|---------|---------|
| `DrawableRuleset<TObject>` | 通用 | 手动管理 Playfield |
| `DrawableScrollingRuleset<TObject>` | 滚动式 | 自动滚动方向/TimeRange，`ScrollingPlayfield` |

### 11.2 必须实现的方法

```csharp
public class DrawableMyGameRuleset : DrawableRuleset<MyGameHitObject>
{
    public DrawableMyGameRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
        : base(ruleset, beatmap, mods)
    {
    }

    // ★ 创建游戏区域
    protected override Playfield CreatePlayfield() => new MyGamePlayfield();

    // ★ 创建 DrawableHitObject 或返回 null（使用对象池模式）
    public override DrawableHitObject<MyGameHitObject>? CreateDrawableRepresentation(
        MyGameHitObject h)
    {
        // 返回 null 表示使用对象池
        return null;
    }

    // ★ 创建输入管理器
    protected override PassThroughInputManager CreateInputManager()
        => new MyGameInputManager(Ruleset.RulesetInfo);
}
```

### 11.3 两种对象管理模式

**模式 A：自行创建（老式，Pippidon 风格）**
```csharp
public override DrawableHitObject<PippidonObject> CreateDrawableRepresentation(
    PippidonObject h)
{
    return new Coin(h /*, textures */); // 每次都 new
}
```

**模式 B：对象池（新式，推荐）**
```csharp
public override DrawableHitObject<MyGameHitObject>? CreateDrawableRepresentation(
    MyGameHitObject h)
{
    return null; // Playfield 通过 RegisterPool 自动管理
}
```

### 11.4 滚动式 DrawableRuleset

```csharp
// PippidonDrawableRuleset.cs 使用了 DrawableScrollingRuleset
public class PippidonDrawableRuleset : DrawableScrollingRuleset<PippidonObject>
{
    public PippidonDrawableRuleset(PippidonRuleset ruleset, IBeatmap beatmap,
        IReadOnlyList<Mod>? mods = null)
        : base(ruleset, beatmap, mods)
    {
        // ★ 滚动方向
        Direction.Value = ScrollingDirection.Left;

        // ★ 时间窗口（对象从右边到左边的总时间ms）
        TimeRange.Value = 6000;
    }

    // 其余与普通 DrawableRuleset 相同
}
```

---

### 11.5 Resume Overlay 恢复覆盖层

暂停后恢复时的防作弊覆盖层，要求玩家将手/光标放回原位。

### 11.5.1 基类

```csharp
// ResumeOverlay 基类 (osu.Game\Screens\Play\ResumeOverlay.cs)
public abstract partial class ResumeOverlay : VisibilityContainer
{
    public GameplayCursorContainer GameplayCursor;
    public Action ResumeAction;

    protected void Resume()
    {
        Hide();
        ResumeAction?.Invoke();
    }
}
```

### 11.5.2 自定义 ResumeOverlay

```csharp
// 文件: UI/MyGameResumeOverlay.cs
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Game.Screens.Play;
using osuTK;

namespace osu.Game.Rulesets.MyGame.UI
{
    public partial class MyGameResumeOverlay : ResumeOverlay
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;

            // 背景暗化
            AddInternal(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Colour4.Black,
                Alpha = 0.7f,
            });
        }

        // ★ 弹出时暂停游戏光标，显示自定义提示
        protected override void PopIn()
        {
            GameplayCursor?.Hide();
            this.FadeIn(300);
        }

        // ★ 关闭时恢复游戏光标
        protected override void PopOut()
        {
            GameplayCursor?.Show();
            this.FadeOut(300);
        }
    }
}
```

### 11.5.3 Tau 的圆形进度恢复覆盖层

Tau 的 `TauResumeOverlay` 使用程序化生成的圆形进度环 + 角度匹配检测：

```csharp
public partial class TauResumeOverlay : ResumeOverlay
{
    // ★ 程序化生成环形进度贴图
    private Texture generateCircularTexture()
    {
        // 绘制 360° + 缺口 的进度环
        var texture = new Texture(radius * 2, radius * 2);
        // ...
        return texture;
    }

    // ★ 极坐标碰撞检测
    private bool checkForValidation(Vector2 mousePos, Vector2 centre)
    {
        var angle = Math.Atan2(mousePos.Y - centre.Y, mousePos.X - centre.X);
        angle = MathHelper.RadiansToDegrees(angle);
        angle = (angle + 360) % 360;

        return angle >= startAngle && angle <= endAngle;
    }
}
```

### 11.5.4 在 DrawableRuleset 中接线

```csharp
public class MyGameDrawableRuleset : DrawableRuleset<MyGameHitObject>
{
    // ★ 创建 ResumeOverlay
    protected override ResumeOverlay CreateResumeOverlay()
        => new MyGameResumeOverlay();

    // ★ 可选：跳过自动化 Mod 的 ResumeOverlay
    public override void RequestResume(Action continueResume)
    {
        if (Mods.Any(m => m is ModAutoplay))
        {
            continueResume();
            return;
        }
        base.RequestResume(continueResume);
    }
}
```


---

## 12. Scrolling Ruleset 滚动式玩法

适合需要从一侧滚动到另一侧的玩法（如 Mania、Taiko、Pippidon）。

### 12.1 使用滚动式基类的完整链路

```csharp
// 1. Playfield 继承 ScrollingPlayfield
public class MyScrollingPlayfield : ScrollingPlayfield
{
    // ScrollingPlayfield 自动绑定 IScrollingInfo.Direction
    // 提供 TimeAtScreenSpacePosition / ScreenSpacePositionAtTime
}

// 2. DrawableRuleset 继承 DrawableScrollingRuleset<T>
public class MyScrollingDrawableRuleset : DrawableScrollingRuleset<MyHitObject>
{
    public MyScrollingDrawableRuleset(Ruleset ruleset, IBeatmap beatmap,
        IReadOnlyList<Mod>? mods = null)
        : base(ruleset, beatmap, mods)
    {
        // ★ 设置滚动方向
        Direction.Value = ScrollingDirection.Left;

        // ★ 设置时间窗口（对象从出现到消失的ms数）
        TimeRange.Value = 5000;
    }
}
```

可用的 `ScrollingDirection` 值：
- `Left` — 对象从右向左移动，判定线通常在左侧
- `Right` — 对象从左向右移动，判定线通常在右侧
- `Up` — 对象从下向上移动，判定线通常在上方
- `Down` — 对象从上向下移动，判定线通常在下方

---

## 13. Input 输入系统

### 13.1 InputManager

```csharp
// 文件: osu.Game.Rulesets.MyGame/MyGameInputManager.cs
using osu.Framework.Input.Bindings;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.MyGame
{
    // ★ 定义 Actions 枚举
    public enum MyGameAction
    {
        Action1,
        Action2,
        MoveUp,
        MoveDown,
    }

    public class MyGameInputManager : RulesetInputManager<MyGameAction>
    {
        public MyGameInputManager(RulesetInfo ruleset)
            : base(ruleset, 0, SimultaneousBindingMode.Unique)
        {
        }
    }
}
```

### 13.2 处理输入（在 DrawableHitObject 中）

```csharp
// DrawableMyGameHitObject.cs
public class DrawableMyGameHitObject : DrawableHitObject<MyGameHitObject>,
    IKeyBindingHandler<MyGameAction>
{
    public bool OnPressed(KeyBindingPressEvent<MyGameAction> e)
    {
        // 记录按键，在 CheckForResult 中使用
        return true;
    }

    public void OnReleased(KeyBindingReleaseEvent<MyGameAction> e)
    {
    }
}
```

### 13.3 处理输入（在 Playfield 中 — Pippidon 模式）

```csharp
// PippidonPlayfield.cs
public class PippidonContainer : Container, IKeyBindingHandler<PippidonAction>
{
    public bool OnPressed(KeyBindingPressEvent<PippidonAction> e)
    {
        switch (e.Action)
        {
            case PippidonAction.MoveUp:
                changeLane(1);
                return true;
            case PippidonAction.MoveDown:
                changeLane(-1);
                return true;
            case PippidonAction.Boost:
                // 加速逻辑
                return true;
        }
        return false;
    }
}
```

---

## 14. Mod 系统

`Mod` (定义在 `osu.Game\Rulesets\Mods\Mod.cs:25`) 是游戏修改器的基类。

### 14.1 Mod 分类（ModType）

```csharp
public enum ModType
{
    DifficultyReduction,  // 降难度: Easy, NoFail, HalfTime
    DifficultyIncrease,   // 升难度: HardRock, DoubleTime, Hidden, Flashlight
    Conversion,           // 转换: 改键数, Mirror, Random
    Automation,           // 自动: Autoplay, Relax, Cinema
    Fun,                  // 娱乐: 各种视觉效果 Mod
    System,               // 系统: TouchDevice, ScoreV2
}
```

### 14.2 简单 Mod 示例（Autoplay）

```csharp
// 文件: osu.Game.Rulesets.MyGame/Mods/MyGameModAutoplay.cs
using System.Collections.Generic;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Users;

namespace osu.Game.Rulesets.MyGame.Mods
{
    public class MyGameModAutoplay : ModAutoplay
    {
        // 通过 CreateReplayData() 提供自动游玩回放

        public override string Name => "Autoplay";
        public override string Acronym => "AT";
        public override LocalisableString Description => "Watch a perfect automated playthrough.";

        public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods)
            => new ModReplayData(new MyGameAutoGenerator(beatmap).Generate(),
                new ModCreatedUser { Username = "autoplay" });
    }
}
```

### 14.3 自定义 Mod 实现接口

```csharp
// Mod 可以实现多个接口来影响不同系统
public class MyGameModDoubleTime : ModDoubleTime
{
    // ModDoubleTime 通过 ModRateAdjust 实现 IApplicableToRate (改变游戏速率)
}

// 自定义 Mod
public class MyGameModSpeedUp : Mod, IApplicableToDifficulty,
    IApplicableToHitObject, IApplicableToDrawableHitObject,
    IApplicableToBeatmapConverter
{
    public override string Name => "Speed Up";
    public override string Acronym => "SU";

    // 影响难度参数
    public void ApplyToDifficulty(BeatmapDifficulty difficulty)
    {
        difficulty.ApproachRate *= 1.2f;
    }

    // 影响 HitObject
    public void ApplyToHitObject(HitObject hitObject) { }

    // 影响 DrawableHitObject
    public void ApplyToDrawableHitObject(DrawableHitObject drawable) { }

    // 影响转谱器
    public void ApplyToBeatmapConverter(IBeatmapConverter converter) { }
}
```

### 14.4 重要 Mod 接口

| 接口 | 影响范围 |
|------|---------|
| `IApplicableToDifficulty` | 改变 CS/AR/OD/HP |
| `IApplicableToHitObject` | 改变 HitObject 属性 |
| `IApplicableToDrawableHitObject` | 改变 DrawableHitObject |
| `IApplicableToBeatmapConverter` | 改变转谱行为 |
| `IApplicableToBeatmap` | 改变 Beatmap 整体 |
| `IApplicableToScoreProcessor` | 改变计分 |
| `IApplicableToHealthProcessor` | 改变血量 |
| `IApplicableToRate` | 改变游戏速率 |
| `IUpdatableByPlayfield` | 每帧更新 |
| `IReadFromConfig` | 从配置读取 |

---

## 15. Scoring & Health 积分与血量系统

### 15.1 ScoreProcessor

```csharp
// 文件: osu.Game.Rulesets.MyGame/Scoring/MyGameScoreProcessor.cs
using System;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.MyGame.Scoring
{
    public class MyGameScoreProcessor : ScoreProcessor
    {
        public MyGameScoreProcessor(Ruleset ruleset)
            : base(ruleset)
        {
        }

        // ★ 自定义基础分值
        public override int GetBaseScoreForResult(HitResult result)
        {
            switch (result)
            {
                case HitResult.Great: return 300;
                case HitResult.Ok: return 100;
                case HitResult.Meh: return 50;
                default: return base.GetBaseScoreForResult(result);
            }
        }

        // ★ 自定义总分曲线
        protected override double ComputeTotalScore(
            double comboProgress, double accuracyProgress, double bonusPortion)
            => 300000 * Accuracy.Value * comboProgress
               + 700000 * Math.Pow(Accuracy.Value, 5) * accuracyProgress
               + bonusPortion;
    }
}
```

### 15.2 HealthProcessor

```csharp
// 文件: osu.Game.Rulesets.MyGame/Scoring/MyGameHealthProcessor.cs
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.MyGame.Scoring
{
    public class MyGameHealthProcessor : DrainingHealthProcessor
    {
        public MyGameHealthProcessor(double drainStartTime)
            : base(drainStartTime)
        {
        }

        // ★ 自定义失败条件：默认是 Health <= 0
        protected override bool CheckDefaultFailCondition(JudgementResult result)
            => Health.Value <= 0;

        // ★ 可覆盖 GetHealthIncreaseFor() 调整每个判罚造成的血量变化
        protected override double GetHealthIncreaseFor(JudgementResult result)
            => result.IsHit ? base.GetHealthIncreaseFor(result) : -0.08;
    }
}
```

### 15.3 Touhosu 生存类 HealthProcessor

```csharp
// TouhosuHealthProcessor.cs — 不回血、纯掉血的生存模式
public class TouhosuHealthProcessor : HealthProcessor
{
    public bool NoRegen { get; set; }
    public double LossMultiplier { get; set; } = 1;

    protected override double GetHealthIncreaseFor(JudgementResult result)
    {
        if (NoRegen && result.IsHit)
            return 0; // 不回血

        if (!result.IsHit)
            return -0.05 * LossMultiplier; // Miss 额外掉血

        return base.GetHealthIncreaseFor(result);
    }
}
```

---

## 16. Difficulty & Performance 难度与PP系统

### 16.1 DifficultyCalculator

```csharp
// 文件: osu.Game.Rulesets.MyGame/Difficulty/MyGameDifficultyCalculator.cs
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MyGame.Objects;

namespace osu.Game.Rulesets.MyGame.Difficulty
{
    public class MyGameDifficultyCalculator : DifficultyCalculator
    {
        public MyGameDifficultyCalculator(IRulesetInfo ruleset,
            IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        // ★ 创建预处理对象
        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(
            IBeatmap beatmap, double clockRate)
        {
            var objects = new List<DifficultyHitObject>();

            for (int i = 1; i < beatmap.HitObjects.Count; i++)
            {
                objects.Add(new DifficultyHitObject(
                    beatmap.HitObjects[i],
                    beatmap.HitObjects[i - 1],
                    clockRate,
                    objects,
                    objects.Count));
            }

            return objects;
        }

        // ★ 创建技能计算器
        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods,
            double clockRate)
        {
            return new Skill[]
            {
                new MyGameSpeedSkill(mods),
                new MyGameAimSkill(mods),
            };
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(
            IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
            => new DifficultyAttributes
            {
                StarRating = skills.Length == 0 ? 0 : skills.Sum(s => s.DifficultyValue()),
                Mods = mods,
                MaxCombo = beatmap.HitObjects.Count,
            };
    }
}
```

### 16.2 PerformanceCalculator

```csharp
// 文件: osu.Game.Rulesets.MyGame/Difficulty/MyGamePerformanceCalculator.cs
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.MyGame.Difficulty
{
    public class MyGamePerformanceCalculator : PerformanceCalculator
    {
        public MyGamePerformanceCalculator(Ruleset ruleset)
            : base(ruleset)
        {
        }

        // ★ PP 计算公式
        protected override PerformanceAttributes CreatePerformanceAttributes(
            ScoreInfo score, DifficultyAttributes difficulty)
        {
            var stars = difficulty.StarRating;

            double pp = Math.Pow(stars * 10, 1.5) * score.Accuracy;

            return new PerformanceAttributes { Total = pp };
        }
    }
}
```

### 16.3 难度显示微调

不同 Ruleset 的难度参数显示有微妙差异。对于 `GetAdjustedDisplayDifficulty`：

| Ruleset | 调整 AR | 调整 OD | 说明 |
|---------|---------|---------|------|
| osu! | ✓ | ✓ | OD 需区分 modAdjusted vs effective |
| taiko | - | ✓ | taiko 无 AR（时间同步进场） |
| catch | ✓ | - | catch 无时间判定，不调 OD |
| mania | - | ✓ | HR/EZ 乘窗口时长，非乘 OD |

**osu! 的 modAdjusted vs effective**：

```csharp
public override BeatmapDifficulty GetAdjustedDisplayDifficulty(
    IBeatmapInfo difficulty, IReadOnlyCollection<Mod> mods)
{
    var adjusted = base.GetAdjustedDisplayDifficulty(difficulty, mods);
    double rate = ModUtils.CalculateRateWithMods(mods);

    // AR: preempt 时间除以 rate，再反算 AR
    double preempt = IBeatmapDifficultyInfo.DifficultyRange(
        adjusted.ApproachRate, PREEMPT_MAX, PREEMPT_MID, PREEMPT_MIN);
    preempt /= rate;
    adjusted.ApproachRate = IBeatmapDifficultyInfo.InverseDifficultyRange(
        preempt, PREEMPT_MAX, PREEMPT_MID, PREEMPT_MIN);

    // OD: hit window 除以 rate，再反算 OD
    double greatWindow = IBeatmapDifficultyInfo.DifficultyRange(
        adjusted.OverallDifficulty, GREAT_WINDOW_RANGE);
    greatWindow /= rate;
    adjusted.OverallDifficulty = IBeatmapDifficultyInfo.InverseDifficultyRange(
        greatWindow, GREAT_WINDOW_RANGE);

    return adjusted;
}
```

**mania 的 HR/EZ 窗口处理**：

```csharp
// ★ 关键：mania 的 hit window 独立于 track rate。
// HR/EZ 直接乘窗口时长（线性），而不是乘 OD（非线性）。
// 必须：先算窗口 → 乘倍率 → 反算 OD
double perfectWindow = IBeatmapDifficultyInfo.DifficultyRange(od, ...);
foreach (var mod in mods.OfType<IApplicableToDifficulty>())
    mod.ApplyToDifficulty(tempDiff);
perfectWindow *= multiplier;
adjusted.OverallDifficulty = InverseDifficultyRange(perfectWindow, ...);
```

### 16.4 Mania PlayfieldType 编码

```csharp
public enum PlayfieldType
{
    Single = 0,   // 1K~10K
    Dual   = 1000 // 2K~20K
}
// 解码: (PlayfieldType)(variant / 1000 * 1000)
// 列数: variant - (int)GetPlayfieldType(variant)
```

### 16.5 Mania HoldNote Body 追踪器

Mania 的 HoldNote 创建 3 个嵌套对象（`HoldNote.cs:96-123`）：

```csharp
protected override void CreateNestedHitObjects(CancellationToken ct)
{
    AddNested(new Note { StartTime = StartTime, Column = Column });  // Head
    AddNested(new Note { StartTime = EndTime, Column = Column });    // Tail
    AddNested(new HoldNoteBody { StartTime = StartTime, EndTime = EndTime }); // Body
}
// MaximumJudgementOffset 基于 Tail 的 Miss 窗口
```

### 16.6 Catch 的 EffectiveX vs OriginalX

```csharp
// OriginalX：来自 .osu 文件的原始值
// EffectiveX：OriginalX + XOffset（HardRock 偏移），钳位到游戏区域
public float EffectiveX => Math.Clamp(OriginalX + XOffset, 0, WIDTH);
// XOffset 由 BeatmapProcessor 设置
```

### 16.7 Catch HyperDash 算法

```csharp
private void initialiseHyperDash(List<PalpableCatchHitObject> objects)
{
    for (int i = 0; i < objects.Count - 1; i++)
    {
        var cur = objects[i];
        var next = findNextObjectWithDifferentPosition(cur, objects);
        double timeDelta = next.StartTime - cur.StartTime;
        double distance = Math.Abs(next.EffectiveX - cur.EffectiveX);

        // 正常速度不足以到达 → 需要 HyperDash
        double requiredVelocity = distance / Math.Max(1.0, timeDelta - 16.67);
        if (requiredVelocity > BASE_DASH_SPEED)
            cur.HyperDashTarget = next;
    }
}
```

---

## 17. Replay 回放系统

### 17.1 ReplayFrame

```csharp
// 文件: osu.Game.Rulesets.MyGame/Replays/MyGameReplayFrame.cs
using System.Collections.Generic;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.MyGame.Replays
{
    public class MyGameReplayFrame : ReplayFrame
    {
        public List<MyGameAction> Actions = new List<MyGameAction>();

        public MyGameReplayFrame(double time, params MyGameAction[] actions)
            : base(time)
        {
            Actions.AddRange(actions);
        }
    }
}
```

### 17.2 FramedReplayInputHandler

```csharp
// 文件: osu.Game.Rulesets.MyGame/Replays/MyGameFramedReplayInputHandler.cs
using System.Collections.Generic;
using osu.Framework.Input.StateChanges;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;
using static osu.Game.Input.Handlers.ReplayInputHandler;

namespace osu.Game.Rulesets.MyGame.Replays
{
    public class MyGameFramedReplayInputHandler
        : FramedReplayInputHandler<MyGameReplayFrame>
    {
        public MyGameFramedReplayInputHandler(Replay replay)
            : base(replay)
        {
        }

        protected override bool IsImportant(MyGameReplayFrame frame)
            => frame.Actions.Count > 0;

        // ★ 生成待处理输入
        protected override void CollectReplayInputs(List<IInput> inputs)
        {
            inputs.Add(new ReplayState<MyGameAction>
            {
                PressedActions = CurrentFrame?.Actions ?? new List<MyGameAction>()
            });
        }
    }
}
```

### 17.3 AutoGenerator

```csharp
// 文件: osu.Game.Rulesets.MyGame/Replays/MyGameAutoGenerator.cs
using osu.Game.Beatmaps;
using osu.Game.Rulesets.MyGame.Objects;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.MyGame.Replays
{
    public class MyGameAutoGenerator : AutoGenerator<MyGameReplayFrame>
    {
        public MyGameAutoGenerator(IBeatmap beatmap)
            : base(beatmap)
        {
        }

        protected override void GenerateFrames()
        {
            foreach (var obj in Beatmap.HitObjects)
            {
                // 在音符命中时间生成完美判定帧
                Frames.Add(new MyGameReplayFrame(
                    obj.StartTime, MyGameAction.Action1));
            }
        }
    }
}
```

### 17.4 各 Ruleset ReplayFrame 独特模式

**catch 的动作推导**（从位置差推导按键）：

```csharp
// CatchReplayFrame.cs — 不直接存储 Action，而是从帧间位置差推导
public CatchReplayFrame(double time, float position, bool dashing, 
    CatchReplayFrame lastFrame)
    : base(time)
{
    Position = position;
    Dashing = dashing;

    // ★ 动作写入前一帧
    if (lastFrame != null)
    {
        if (Position > lastFrame.Position)
            lastFrame.Actions.Add(CatchAction.MoveRight);
        else if (Position < lastFrame.Position)
            lastFrame.Actions.Add(CatchAction.MoveLeft);
    }
}
```

**mania 的位运算 Legacy 解码**：

```csharp
// ManiaReplayFrame.cs — 从 MouseX 用位移解码多列按键
public void FromLegacy(ReplayFrame legacyFrame)
{
    int keys = (int)(legacyFrame.MouseX ?? 0);
    
    for (int i = 0; i < 10; i++)
    {
        // ★ 每位表示一个 Key 的按下状态
        if ((keys >> i & 1) > 0)
            Actions.Add(ManiaAction.Key1 + i);
    }
}
```

**taiko**：只有 4 个 Action，无位置数据。

---

## 18. 设置与配置系统

`RulesetConfigManager` 是 Ruleset 设置的持久化骨架，`RulesetSettingsSubsection` 是其在设置面板中的 UI 表现。

### 18.1 体系架构

```
用户设置面板
  └── RulesetSection                          ← 迭代所有 Ruleset
        └── RulesetSettingsSubsection          ← ruleset.CreateSettings()
              ├── SettingsCheckbox              ← 绑定到 Config.GetBindable<>(key)
              ├── SettingsSlider<float>         ← 绑定到 Config.GetBindable<>(key)
              └── SettingsEnumDropdown<T>       ← 绑定到 Config.GetBindable<>(key)

持久化层
  IRulesetConfigCache (DI 单例)
    └── ruleset.CreateConfig(settings)
          └── RulesetConfigManager<TEnum> : ConfigManager<TEnum>
                └── 存储到 Realm 数据库 (RealmRulesetSetting)
```

**Hook 位置**（`Ruleset.cs:290,296`）：

```csharp
// Ruleset 基类中的钩子
public virtual IRulesetConfigManager? CreateConfig(SettingsStore? settings) => null;
public virtual RulesetSettingsSubsection? CreateSettings() => null;
```

### 18.2 定义 Setting 枚举

```csharp
// 文件: Configuration/MyGameRulesetSetting.cs
namespace osu.Game.Rulesets.MyGame.Configuration
{
    public enum MyGameRulesetSetting
    {
        ShowEffects,
        AnimationSpeed,
        PlayfieldDim,
        NotesSize,
        PlayfieldBorderStyle,
    }
}
```

### 18.3 实现 ConfigManager

```csharp
// 文件: Configuration/MyGameRulesetConfigManager.cs
using osu.Game.Configuration;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.MyGame.Configuration
{
    public class MyGameRulesetConfigManager
        : RulesetConfigManager<MyGameRulesetSetting>
    {
        public MyGameRulesetConfigManager(SettingsStore? settings,
            RulesetInfo ruleset, int? variant = null)
            : base(settings, ruleset, variant)
        {
        }

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();

            // ★ bool 设置
            SetDefault(MyGameRulesetSetting.ShowEffects, true);

            // ★ float 设置（含范围约束）
            SetDefault(MyGameRulesetSetting.AnimationSpeed, 1.0f);
            SetDefault(MyGameRulesetSetting.PlayfieldDim, 0.5f,
                min: 0f, max: 1f, precision: 0.05f);

            // ★ int 设置
            SetDefault(MyGameRulesetSetting.NotesSize, 64, 32, 128);

            // ★ enum 设置
            SetDefault(MyGameRulesetSetting.PlayfieldBorderStyle,
                PlayfieldBorderStyle.Solid);
        }
    }
}
```

### 18.4 实现 SettingsSubsection UI

```csharp
// 文件: UI/MyGameSettingsSubsection.cs
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets;
using osu.Game.Rulesets.MyGame.Configuration;

namespace osu.Game.Rulesets.MyGame.UI
{
    public partial class MyGameSettingsSubsection : RulesetSettingsSubsection
    {
        public MyGameSettingsSubsection(Ruleset ruleset)
            : base(ruleset)
        {
        }

        protected override LocalisableString Header => "My Game Settings";

        // ★ 从基类获取 Config（已自动创建并缓存）
        [BackgroundDependencyLoader]
        private void load()
        {
            var config = (MyGameRulesetConfigManager)Config;

            Children = new Drawable[]
            {
                // ★ 复选框
                new SettingsCheckbox
                {
                    LabelText = "Show Effects",
                    Current = config.GetBindable<bool>(MyGameRulesetSetting.ShowEffects),
                },
                // ★ 滑块
                new SettingsSlider<float>
                {
                    LabelText = "Animation Speed",
                    Current = config.GetBindable<float>(MyGameRulesetSetting.AnimationSpeed),
                    KeyboardStep = 0.1f,
                    DisplayAsPercentage = true,
                },
                // ★ 枚举下拉
                new SettingsEnumDropdown<PlayfieldBorderStyle>
                {
                    LabelText = "Playfield Border",
                    Current = config.GetBindable<PlayfieldBorderStyle>(
                        MyGameRulesetSetting.PlayfieldBorderStyle),
                },
            };
        }
    }
}
```

### 18.5 在 Ruleset 中接线

```csharp
public class MyGameRuleset : Ruleset
{
    public override IRulesetConfigManager CreateConfig(SettingsStore? settings)
        => new MyGameRulesetConfigManager(settings, RulesetInfo);

    public override RulesetSettingsSubsection CreateSettings()
        => new MyGameSettingsSubsection(this);
}
```

### 18.6 Tracked Settings（带动态提示）

Sentakki 的模式——让设置值出现在 OSD（屏幕显示）中：

```csharp
public class SentakkiRulesetConfigManager : RulesetConfigManager<SentakkiRulesetSettings>
{
    // ★ 标记设置项为 "tracked"，值变化时显示 OSD 提示
    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();
        SetDefault(SentakkiRulesetSettings.AnimationSpeed, 200f, 100f, 500f, 10f);
    }

    // ★ 覆盖此方法，返回需要追踪显示的设置
    protected override IEnumerable<TrackedSettings> CreateTrackedSettings()
    {
        return new[]
        {
            // 动态 tooltip 文本
            new TrackedSettings(SentakkiRulesetSettings.AnimationSpeed,
                setting => new SettingDescription(
                    rawValue: ((float)setting).ToString("0"),
                    "Entry Speed",
                    // ★ 动态生成 tooltip
                    SentakkiSettingsSubsectionStrings.EntrySpeedTooltip(
                        DrawableSentakkiRuleset.ComputeLaneNoteEntryTime((float)setting))),
                    "Speed")
        };
    }
}
```

### 18.7 Hitokori 的颜色绑定技巧

`Color4` 无法被 Realm 数据库直接序列化。Hitokori 将颜色拆成 R/G/B 三个 float 设置：

```csharp
public class HitokoriSettingsManager : RulesetConfigManager<HitokoriSetting>
{
    // ★ 存储三个 float
    SetDefault(HitokoriSetting.HiColor_R, 1f);
    SetDefault(HitokoriSetting.HiColor_G, 1f);
    SetDefault(HitokoriSetting.HiColor_B, 1f);

    // ★ 动态组装 Bindable<Color4>
    private Bindable<Color4> hiColor;

    public Bindable<Color4> HiColor
    {
        get
        {
            if (hiColor != null) return hiColor;

            var r = GetBindable<float>(HitokoriSetting.HiColor_R);
            var g = GetBindable<float>(HitokoriSetting.HiColor_G);
            var b = GetBindable<float>(HitokoriSetting.HiColor_B);

            hiColor = new Bindable<Color4>(new Color4(r.Value, g.Value, b.Value, 1f));

            // ★ 双向同步，用 lock 防止循环更新
            bool hilock = false;
            hiColor.BindValueChanged(color =>
            {
                if (hilock) return;
                hilock = true;
                r.Value = color.NewValue.R;
                g.Value = color.NewValue.G;
                b.Value = color.NewValue.B;
                hilock = false;
            });

            r.BindValueChanged(_ => updateHiColor());
            g.BindValueChanged(_ => updateHiColor());
            b.BindValueChanged(_ => updateHiColor());

            void updateHiColor()
            {
                if (hilock) return;
                hilock = true;
                hiColor.Value = new Color4(r.Value, g.Value, b.Value, 1f);
                hilock = false;
            }

            return hiColor;
        }
    }
}
```

### 18.8 InMemoryConfigManager

karaoke 使用 `InMemoryConfigManager` 做编辑器专用设置（不持久化）：

```csharp
// 编辑器专用配置，不写入 Realm
public class KaraokeRulesetEditConfigManager
    : InMemoryConfigManager<KaraokeRulesetEditSetting>
{
    protected override void InitialiseDefaults()
    {
        base.InitialiseDefaults();
        // 仅内存中有效，重启后重置
        SetDefault(KaraokeRulesetEditSetting.AutoGenerateRomanisation, true);
    }
}
```

---

## 19. 皮肤系统

皮肤系统允许 Ruleset 根据用户皮肤替换 DrawableHitObject 的视觉元素。

### 19.1 核心接口

| 接口 | 用途 |
|------|------|
| `ISkin` | 皮肤查询接口：`GetDrawableComponent()`, `GetTexture()`, `GetSample()`, `GetConfig<>()` |
| `ISkinSource` | 多层皮肤源，支持回退 |
| `ISkinTransformer` | 对已有 ISkin 做转换包装 |

### 19.2 SkinTransformer 模式

**SkinTransformer** (`osu.Game\Skinning\SkinTransformer.cs:20`) 包装一个 `Skin` 并拦截查询：

```csharp
public class MyGameSkinTransformer : SkinTransformer
{
    public MyGameSkinTransformer(ISkin skin) : base(skin) { }

    // ★ 拦截特定组件的查找
    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        // 判罚结果可以使用自定义绘制
        if (lookup is SkinComponentLookup<HitResult> resultLookup)
        {
            switch (resultLookup.Lookup)
            {
                case HitResult.Great:
                    return new MyGreatHitExplosion();
                case HitResult.Miss:
                    return new MyMissXMark();
            }
        }

        // 规则集自定义组件类型
        if (lookup is MyGameSkinComponentLookup myLookup)
        {
            switch (myLookup.Lookup)
            {
                case MyGameSkinComponents.HitCircle:
                    return new MyGameHitCircleSprite();
                case MyGameSkinComponents.Cursor:
                    return new MyGameCursor();
            }
        }

        // ★ 回退到原始皮肤
        return base.GetDrawableComponent(lookup);
    }
}
```

### 19.3 定义自定义 Skin Component 枚举

```csharp
// 文件: Skinning/MyGameSkinComponents.cs
namespace osu.Game.Rulesets.MyGame.Skinning
{
    public enum MyGameSkinComponents
    {
        HitCircle,
        Cursor,
        CursorTrail,
        PlayfieldBackground,
    }

    // 自定义 Lookup 类型
    public class MyGameSkinComponentLookup : SkinComponentLookup<MyGameSkinComponents>
    {
        public MyGameSkinComponentLookup(MyGameSkinComponents component)
            : base(component) { }
    }
}
```

### 19.4 定义 Skin Configuration 枚举

```csharp
// 文件: Skinning/MyGameSkinConfiguration.cs
namespace osu.Game.Rulesets.MyGame.Skinning
{
    public enum MyGameSkinConfiguration
    {
        CursorScale,
        HitCircleScale,
        HitCircleOverlayAboveNumber,
    }

    // 在 SkinTransformer 中查询皮肤配置
    public float GetCursorScale()
    {
        var config = GetConfig<MyGameSkinConfiguration, float>(
            MyGameSkinConfiguration.CursorScale);
        return config?.Value ?? 1f;
    }
}
```

### 19.5 在 Ruleset 中接线

```csharp
public class MyGameRuleset : Ruleset
{
    public override ISkin? CreateSkinTransformer(ISkin skin, IBeatmap beatmap)
    {
        // 根据皮肤类型选择不同 Transformer
        switch (skin)
        {
            case LegacySkin:
                return new MyGameLegacySkinTransformer(skin);
            default:
                return new MyGameDefaultSkinTransformer(skin);
        }
    }
}
```

### 19.6 SkinProvidingContainer 流程

游戏时，皮肤按此顺序查找：

```
DrawableRuleset
  └── RulesetSkinProvidingContainer          ← 游戏时自动创建
        ├── BeatmapSkin (谱面自带的皮肤)        ← 如果存在
        ├── UserSkin (用户选择的皮肤)          ← 经过 CreateSkinTransformer 转换
        ├── RulesetResources (Ruleset嵌入资源)   ← 从 CreateResourceStore()
        └── DefaultSkin (osu!默认皮肤)         ← 最终回退
```

`RulesetSkinProvidingContainer` (`osu.Game\Skinning\RulesetSkinProvidingContainer.cs:27`) 自动调用 `ruleset.CreateSkinTransformer()` 包装每一层皮肤。

### 19.7 在 DrawableHitObject 中查询皮肤

```csharp
public partial class DrawableMyGameHitCircle : DrawableHitObject<MyGameHitObject>
{
    [Resolved]
    private ISkinSource skinSource { get; set; }

    [BackgroundDependencyLoader]
    private void load()
    {
        // ★ 通过 ISkinSource 获取皮肤配置
        float cursorScale = skinSource.GetConfig<MyGameSkinConfiguration, float>(
            MyGameSkinConfiguration.CursorScale)?.Value ?? 1f;

        Scale = new Vector2(cursorScale);
    }
}
```

---

### 19.14 皮肤编辑器系统

皮肤编辑器允许用户自定义 Ruleset 的视觉外观。**karaoke** 是目前唯一实现了完整皮肤编辑器的自定义 Ruleset。

### 19.14.1 整体架构

皮肤编辑器使用 **三栏布局 + 标签页** 的通用模式：

```
KaraokeSkinEditor : GenericEditor<KaraokeSkinEditorScreenMode>
  │
  ├── Tab: Config (布局配置)
  │     └── ConfigScreen
  │           ├── 左栏：选择列表（空/待实现）
  │           ├── 中栏：预览区域（歌词实时预览）
  │           └── 右栏：属性区
  │                 ├── IntervalSection（间距设置）
  │                 ├── PositionSection（位置设置）
  │                 └── RubyAndRomanisationSection（注音设置）
  │
  └── Tab: Style (视觉样式)
        └── StyleScreen
              ├── 左栏：选择列表
              ├── 中栏：预览区域（歌词/音符预览）
              └── 右栏：属性区
                    ├── LyricColorSection / LyricFontSection / LyricShadowSection
                    └── NoteColorSection  / NoteFontSection
```

### 19.14.2 GenericEditor 标签页编辑器基类

karaoke 自建了 `GenericEditor<TScreenMode>`，是所有编辑器的通用框架：

```csharp
// Screens/Edit/GenericEditor.cs
public abstract partial class GenericEditor<TScreenMode> : ScreenWithBeatmapBackground
    where TScreenMode : struct, Enum
{
    // ★ 当前选中的标签
    protected readonly Bindable<TScreenMode> Mode = new Bindable<TScreenMode>();

    // ★ 标签页容器
    private readonly Container<GenericEditorScreen<TScreenMode>> screenContainer;

    // ★ 标签切换
    private void onModeChanged(ValueChangedEvent<TScreenMode> e)
    {
        // 隐藏当前屏幕、查找缓存或调用 GenerateScreen() 创建新屏幕
        LastScreen?.Hide();
        var newScreen = screens.GetOrAdd(e.NewValue, _ => GenerateScreen(e.NewValue));
        LoadComponentAsync(newScreen, screenContainer.Add);
        updateMenuItems();
    }

    // ★ 子类实现：根据模式生成屏幕
    protected abstract GenericEditorScreen<TScreenMode> GenerateScreen(TScreenMode screenMode);

    // ★ 子类实现：根据模式生成菜单项
    protected abstract MenuItem[] GenerateMenuItems(TScreenMode screenMode);
}
```

```csharp
// Screens/Edit/GenericEditorScreen.cs
public abstract partial class GenericEditorScreen<TScreenMode> : EditorScreen
{
    [Resolved]
    protected GenericEditor<TScreenMode> Editor { get; private set; }

    // ★ 子类可通过 ChangeMode() 调用 Editor.Mode.Value = newMode 来切换标签
    protected void ChangeMode(TScreenMode mode) => Editor.Mode.Value = mode;
}
```

### 19.14.3 KaraokeSkinEditor

```csharp
// Screens/Skin/KaraokeSkinEditor.cs
public partial class KaraokeSkinEditor : GenericEditor<KaraokeSkinEditorScreenMode>
{
    [Cached]
    private readonly OverlayColourProvider colourProvider = new(OverlayColourScheme.Pink);

    private readonly ISkin skin;

    public KaraokeSkinEditor(ISkin skin) => this.skin = skin;

    // ★ 生成屏幕
    protected override GenericEditorScreen<KaraokeSkinEditorScreenMode> GenerateScreen(
        KaraokeSkinEditorScreenMode mode) => mode switch
    {
        KaraokeSkinEditorScreenMode.Config => new ConfigScreen(skin),
        KaraokeSkinEditorScreenMode.Style  => new StyleScreen(skin),
        _ => throw new InvalidOperationException()
    };
}

// Screens/Skin/KaraokeSkinEditorScreenMode.cs
public enum KaraokeSkinEditorScreenMode
{
    [LocalisableDescription(typeof(EditorSettingsStrings), nameof(EditorSettingsStrings.Config))]
    Config,

    [LocalisableDescription(typeof(EditorSettingsStrings), nameof(EditorSettingsStrings.Style))]
    Style,

    [LocalisableDescription(typeof(EditorSettingsStrings), nameof(EditorSettingsStrings.Layout))]
    Layout,  // TODO: 尚未实现
}
```

### 19.14.4 三栏布局屏幕基类

```csharp
// Screens/Skin/KaraokeSkinEditorScreen.cs
public abstract partial class KaraokeSkinEditorScreen
    : GenericEditorScreen<KaraokeSkinEditorScreenMode>
{
    // ★ 用 SkinProvidingContainer 包裹，让所有子元素看到当前皮肤
    [BackgroundDependencyLoader]
    private void load(ISkin skin)
    {
        InternalChild = new SkinProvidingContainer(skin)
        {
            RelativeSizeAxes = Axes.Both,
            Child = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                ColumnDimensions = new[]
                {
                    new Dimension(GridSizeMode.Absolute, 200),  // 左：选择区
                    new Dimension(),                             // 中：预览区
                    new Dimension(GridSizeMode.Absolute, 300),  // 右：属性区
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        // 左栏 - 缩放 0.75 + 滚动
                        new BasicScrollContainer { Child = selectionContainer.With(s => s.Scale = new Vector2(0.75f)) },
                        // 中栏 - 预览
                        CreatePreviewArea(),
                        // 右栏 - 缩放 0.75 + 滚动
                        new BasicScrollContainer { Child = propertiesContainer.With(s => s.Scale = new Vector2(0.75f)) },
                    }
                }
            }
        };
    }

    protected abstract Section[] CreateSelectionContainer();   // 左
    protected abstract Drawable CreatePreviewArea();            // 中
    protected abstract Section[] CreatePropertiesContainer();   // 右
}
```

### 19.14.5 Section 构建块

```csharp
// Screens/Section.cs
public abstract partial class Section : Container
{
    public abstract LocalisableString Title { get; }

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Padding = new MarginPadding(SECTION_PADDING);

        // 标题 + 内容的垂直布局
        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(SECTION_SPACING),
            Children = new Drawable[]
            {
                new SectionTitle(Title),
                content = new FillFlowContainer { ... },
            }
        };
    }

    protected readonly FillFlowContainer content;
}
```

具体 Section 示例 — 歌词颜色：

```csharp
// Screens/Skin/Style/LyricColorSection.cs
public partial class LyricColorSection : StyleSection
{
    public override LocalisableString Title => "Lyric Color";

    [BackgroundDependencyLoader]
    private void load(LyricFontInfoManager manager)
    {
        // ★ 绑定到 LyricFontInfoManager 的当前编辑项
        // 读取 → 显示当前值，写入 → 调用 ApplyCurrentLyricFontInfoChange()
        content.Add(new OsuColourPicker
        {
            Current = manager.GetBindable<Color4>(l => l.LyricColor),
        });
    }
}
```

### 19.14.6 LyricFontInfoManager（编辑状态管理器）

`LyricFontInfoManager` 是编辑器的核心状态容器，缓存在 DI 树中供所有 Section 共享：

```csharp
// Screens/Skin/Config/LyricFontInfoManager.cs
public partial class LyricFontInfoManager : Component
{
    // ★ 所有可选的配置列表
    public BindableList<LyricFontInfo> Configs = new BindableList<LyricFontInfo>();

    // ★ 当前加载的配置（来自皮肤的只读值）
    public Bindable<LyricFontInfo> LoadedLyricFontInfo = new Bindable<LyricFontInfo>();

    // ★ 当前编辑中的配置（工作副本）
    public Bindable<LyricFontInfo> EditLyricFontInfo = new Bindable<LyricFontInfo>();

    [BackgroundDependencyLoader]
    private void load(ISkinSource skinSource)
    {
        // 从皮肤加载配置列表
        var lookup = skinSource.GetConfig<KaraokeSkinLookup, LyricFontInfo>(
            new KaraokeSkinLookup(ElementType.LyricFontInfo));
        Configs.AddRange(lookup);

        // 绑定加载值
        LoadedLyricFontInfo.BindTo(lookup);
        EditLyricFontInfo.BindTo(LoadedLyricFontInfo);
    }

    // ★ 原子修改：对工作副本执行变更
    public void ApplyCurrentLyricFontInfoChange(Action<LyricFontInfo> change)
    {
        var newFontInfo = EditLyricFontInfo.Value.DeepClone();
        change(newFontInfo);
        EditLyricFontInfo.Value = newFontInfo;
    }
}
```

### 19.14.7 皮肤元素定义

**IKaraokeSkinElement 接口** —— 所有皮肤元素的抽象：

```csharp
// Skinning/Elements/IKaraokeSkinElement.cs
public interface IKaraokeSkinElement
{
    int ID { get; set; }
    string Name { get; set; }
    void ApplyTo(Drawable drawable);  // ★ 将样式应用到目标绘制物
}
```

**LyricFontInfo** —— 歌词字体布局元素：

```csharp
// Skinning/Elements/LyricFontInfo.cs
public class LyricFontInfo : IKaraokeSkinElement
{
    public int ID { get; set; }
    public string Name { get; set; } = "Default";

    public KaraokeTextSmartHorizon SmartHorizon { get; set; } = Multi;
    public int LyricsInterval { get; set; } = 4;
    public int RubyInterval { get; set; } = 2;
    public int RomanisationInterval { get; set; } = 2;

    public LyricTextAlignment RubyAlignment { get; set; } = EqualSpace;
    public LyricTextAlignment RomanisationAlignment { get; set; } = EqualSpace;
    public int RubyMargin { get; set; } = 4;
    public int RomanisationMargin { get; set; } = 4;

    public FontUsage MainTextFont { get; set; } = new("Torus", 48, "Bold");
    public FontUsage RubyTextFont { get; set; } = new("Torus", 20, "Bold");
    public FontUsage RomanisationTextFont { get; set; } = new("Torus", 20, "Bold");

    // ★ 将字体/间距/对齐应用到 DrawableLyric
    public void ApplyTo(Drawable drawable)
    {
        if (drawable is DrawableLyric lyric)
        {
            lyric.TopText.Font = MainTextFont;
            lyric.BottomText.Font = MainTextFont;
            lyric.RubyText.Font = RubyTextFont;
            lyric.RomanisationText.Font = RomanisationTextFont;
            lyric.Spacing = new Vector2(LyricsInterval, RomanisationInterval);
            // ...
        }
    }
}
```

**NoteStyle** —— 音符颜色样式元素：

```csharp
// Skinning/Elements/NoteStyle.cs
public class NoteStyle : IKaraokeSkinElement
{
    public int ID { get; set; }
    public string Name { get; set; } = "Default";

    public Color4 NoteColor { get; set; } = Color4Extensions.FromHex("#44AADD");
    public Color4 BlinkColor { get; set; } = Color4Extensions.FromHex("#FF66AA");
    public Color4 TextColor { get; set; } = Color4Extensions.FromHex("#FFFFFF");
    public bool BoldText { get; set; } = true;

    // ★ 将颜色应用到 DrawableNote
    public void ApplyTo(Drawable drawable)
    {
        if (drawable is DrawableNote note)
        {
            note.AccentColour.Value = NoteColor;
            note.HitColour.Value = BlinkColor;
            note.TextColour.Value = TextColor;
        }
    }
}
```

**ElementType 枚举**：

```csharp
public enum ElementType
{
    LyricFontInfo,
    NoteStyle,
}
```

### 19.14.8 皮肤数据存储（JSON 格式）

皮肤元素以 JSON 文件形式存储在皮肤资源中：

**Default Skin**（全局默认）— `default.json`：
```json
{
  "lyric_font_info": {
    "$type": "LyricFontInfo",
    "id": 0,
    "name": "Default",
    "smart_horizon": "Multi",
    "lyrics_interval": 4,
    "main_text_font": { "family": "Torus", "weight": "Bold", "size": 48 },
    "ruby_text_font":  { "family": "Torus", "weight": "Bold", "size": 20 }
  },
  "note_style": {
    "$type": "NoteStyle",
    "id": 0,
    "name": "Default",
    "note_color": "#44AADD",
    "blink_color": "#FF66AA",
    "text_color": "#FFFFFF"
  }
}
```

**Beatmap Skin**（谱面独有）— 分文件存储：
- `lyric-font-infos.json` → `LyricFontInfo[]`
- `note-styles.json` → `NoteStyle[]`

### 19.14.9 皮肤元素 JSON 序列化

核心是 `KaraokeSkinElementConverter`，利用 `$type` 字段实现多态序列化：

```csharp
// IO/Serialization/Converters/KaraokeSkinElementConverter.cs
public class KaraokeSkinElementConverter
    : GenericTypeConverter<IKaraokeSkinElement, ElementType>
{
    // ★ 序列化：写入 "$type": "LyricFontInfo"
    protected override string GetTypeName(ElementType type) => type.ToString();

    // ★ 反序列化：根据 "$type" 值映射到 CLR 类型
    protected override Type GetTypeByName(string name) => name switch
    {
        nameof(ElementType.LyricFontInfo) => typeof(LyricFontInfo),
        nameof(ElementType.NoteStyle)     => typeof(NoteStyle),
        _ => throw new InvalidOperationException()
    };
}

// 创建 JSON 序列化设置
public static JsonSerializerSettings CreateSkinElementGlobalSettings()
{
    var settings = JsonSerializableExtensions.CreateGlobalSettings();
    settings.ContractResolver = new SnakeCaseKeyContractResolver(); // snake_case
    settings.Converters.Add(new KaraokeSkinElementConverter());     // $type
    settings.Converters.Add(new FontUsageConverter());              // FontUsage
    settings.Converters.Add(new Vector2Converter());
    settings.Converters.Add(new ColourConverter());
    return settings;
}
```

### 19.14.10 Skin 模型层次

```csharp
// KaraokeSkin — 全局默认皮肤（单个元素）
public class KaraokeSkin : Skin
{
    // 每种 ElementType 只有一个默认元素
    public IDictionary<ElementType, IKaraokeSkinElement> DefaultElement;

    public KaraokeSkin(SkinInfo skinInfo, IResourceStore<byte[]> store)
    {
        // 从 default.json 加载
        string json = GetElementStringContent(skinInfo, "default.json");
        var format = JsonConvert.DeserializeObject<DefaultSkinFormat>(json, settings);
        DefaultElement[ElementType.LyricFontInfo] = format.LyricFontInfo;
        DefaultElement[ElementType.NoteStyle] = format.NoteStyle;
    }
}

// KaraokeBeatmapSkin — 谱面皮肤（每个类型可以有多个元素）
public class KaraokeBeatmapSkin : KaraokeSkin
{
    // 每种 ElementType 可以有多个元素（一个列表）
    public IDictionary<ElementType, IList<IKaraokeSkinElement>> Elements;

    public KaraokeBeatmapSkin(SkinInfo skinInfo, IResourceStore<byte[]> store)
        : base(skinInfo, store)
    {
        // 从 lyric-font-infos.json 和 note-styles.json 加载数组
        var lyricFonts = LoadElementArray<LyricFontInfo>(skinInfo, "lyric-font-infos.json");
        var noteStyles = LoadElementArray<NoteStyle>(skinInfo, "note-styles.json");
        Elements[ElementType.LyricFontInfo] = lyricFonts;
        Elements[ElementType.NoteStyle] = noteStyles;
    }
}
```

### 19.14.11 皮肤查询机制

三种 Lookup 类型覆盖不同查询场景：

| Lookup 类型 | 用途 | 返回值 |
|-------------|------|--------|
| `KaraokeSkinLookup(ElementType, int)` | 按类型+ID 查元素 | `IKaraokeSkinElement` |
| `KaraokeHitObject` (Lyric/Note) | 按 HitObject 查对应样式 | `LyricFontInfo` / `NoteStyle` |
| `KaraokeIndexLookup` | 查元素名称→ID 映射（供选择列表） | `IDictionary<int, string>` |

```csharp
// 查询示例：获取某个 Lyric 的 LyricFontInfo
var lookup = skin.GetConfig<KaraokeHitObject, LyricFontInfo>(lyric);
var fontInfo = lookup.Value;

// 查询示例：获取所有 LyricFontInfo 的名称列表（供 UI 选择器）
var indexLookup = skin.GetConfig<KaraokeIndexLookup, IDictionary<int, string>>(
    new KaraokeIndexLookup(ElementType.LyricFontInfo));
// → { 0: "Default", 1: "Large", 2: "Small" }
```

### 19.14.12 字体管理系统

```csharp
// Skinning/Fonts/FontManager.cs
public partial class FontManager : Component
{
    // ★ 扫描 "fonts/fnt/*.zipfnt" 和 "fonts/ttf/*.ttf"
    public BindableList<FontInfo> Fonts;

    [BackgroundDependencyLoader]
    private void load(Storage storage)
    {
        // 扫描字体目录
        var fntFiles = storage.GetFiles("fonts/fnt", "*.zipfnt");
        var ttfFiles = storage.GetFiles("fonts/ttf", "*.ttf");

        foreach (var file in fntFiles)
            Fonts.Add(new FontInfo(file, FontFormat.Fnt));
        foreach (var file in ttfFiles)
            Fonts.Add(new FontInfo(file, FontFormat.Ttf));

        // ★ 文件系统监听：字体文件变化时自动刷新
        watcher = new FileSystemWatcher(...);
    }
}

// FontInfo 结构
public readonly struct FontInfo
{
    public string FontName { get; }
    public string Family { get; }
    public string Weight { get; }
    public FontFormat Format { get; }  // Internal, Fnt, Ttf
}
```

### 19.14.13 创建自己的皮肤编辑器（通用模板）

```csharp
// 第一步：定义皮肤元素接口
public interface IMySkinElement
{
    int ID { get; set; }
    string Name { get; set; }
    void ApplyTo(Drawable target);
}

// 第二步：实现具体元素
public class MyHitCircleStyle : IMySkinElement
{
    public int ID { get; set; }
    public string Name { get; set; } = "Default";
    public Color4 FillColor { get; set; } = Color4.White;
    public Color4 BorderColor { get; set; } = Color4.Black;
    public float BorderThickness { get; set; } = 2f;

    public void ApplyTo(Drawable target)
    {
        if (target is DrawableMyHitCircle circle)
        {
            circle.FillColour.Value = FillColor;
            circle.BorderColour.Value = BorderColor;
            circle.BorderThickness = BorderThickness;
        }
    }
}

// 第三步：创建皮肤类
public class MyGameSkin : Skin
{
    public IDictionary<Type, IMySkinElement> Defaults;

    public MyGameSkin(IResourceStore<byte[]> store)
    {
        // 从 JSON 加载默认样式
        string json = new StreamReader(store.Get("skin.json")).ReadToEnd();
        Defaults = JsonConvert.DeserializeObject<Dictionary<Type, IMySkinElement>>(json);
    }

    public override Drawable GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (lookup is MySkinComponentLookup myLookup)
        {
            var element = Defaults[myLookup.ElementType];
            return element.CreateDrawable();
        }
        return base.GetDrawableComponent(lookup);
    }
}

// 第四步：创建编辑器屏幕
public partial class MySkinEditor : GenericEditor<MySkinEditorMode>
{
    protected override GenericEditorScreen<MySkinEditorMode> GenerateScreen(MySkinEditorMode mode)
        => mode switch
        {
            MySkinEditorMode.Style => new MyStyleScreen(),
            _ => new MyConfigScreen(),
        };
}

// 第五步：在 Ruleset 中接线
public class MyGameRuleset : Ruleset
{
    public override ISkin CreateSkinTransformer(ISkin skin, IBeatmap beatmap)
        => new MyGameSkinTransformer(skin);
}
```

### 19.14.14 编辑器 Section 控件速查

| 控件类型 | 用途 | 示例 |
|---------|------|------|
| `OsuColourPicker` | 颜色选择（HSV滑块） | `NoteColorSection` |
| `SettingsSlider<float>` | 数值滑块 | `IntervalSection` |
| `SettingsEnumDropdown<T>` | 枚举下拉 | `LyricTextAlignment` |
| `FontUsageSelector` | 字体选择（含预览） | `LyricFontSection` |
| `LabelledSwitchButton` | 开关按钮 | `BoldText` |
| `OutlinedTextBox` | 文本输入 | 皮肤名称 |


---

## 20. 谱面编辑器系统

### 20.1 HitObjectComposer 架构

`HitObjectComposer<TObject>` (`osu.Game\Rulesets\Edit\HitObjectComposer.cs:47`)：

```
HitObjectComposer<TObject>
  ├── Playfield (编辑器模式下的游戏区域)
  ├── BlueprintContainer (选中/拖拽/放置蓝图)
  ├── CompositionTools (左侧工具栏按钮列表)
  ├── HitObjectInspector (右侧属性面板)
  ├── 吸附 (Snapping) — TrySnapToDistanceGrid, TrySnapToNearbyObjects
  └── TernaryButtons (顶部三元状态按钮)
```

### 20.2 最简编辑器实现

```csharp
// 文件: Edit/MyGameHitObjectComposer.cs
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Tools;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MyGame.Objects;
using osu.Game.Rulesets.MyGame.UI;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Edit.Compose.Components;

namespace osu.Game.Rulesets.MyGame.Edit
{
    public partial class MyGameHitObjectComposer : HitObjectComposer<MyGameHitObject>
    {
        public MyGameHitObjectComposer(Ruleset ruleset)
            : base(ruleset)
        {
        }

        // ★ 左侧工具栏
        protected override IReadOnlyList<CompositionTool> CompositionTools
            => new CompositionTool[]
            {
                new MyGameCompositionTool(),
            };

        // ★ 创建编辑器用的 DrawableRuleset
        protected override DrawableRuleset<MyGameHitObject> CreateDrawableRuleset(
            Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> mods)
            => new MyGameDrawableRuleset(ruleset, beatmap, mods);

        // ★ 创建 Blueprint 容器
        protected override ComposeBlueprintContainer CreateBlueprintContainer()
            => new MyGameBlueprintContainer(this);
    }
}
```

### 20.3 CompositionTool（放置工具）

```csharp
// 文件: Edit/MyGameCompositionTool.cs
using osu.Framework.Graphics;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Tools;
using osu.Game.Rulesets.MyGame.Objects;

namespace osu.Game.Rulesets.MyGame.Edit
{
    public class MyGameCompositionTool : CompositionTool
    {
        public MyGameCompositionTool()
            : base(name: "Note")
        {
        }

        // ★ 创建放置蓝图（鼠标点击时使用）
        public override PlacementBlueprint CreatePlacementBlueprint()
            => new MyGamePlacementBlueprint();

        public override Drawable CreateIcon()
            => new MyGameToolIcon();
    }
}
```

Blueprint 容器负责选中蓝图、拖拽移动和选中处理。`CreateSelectionHandler()` 覆盖在容器上，而不是覆盖在 `HitObjectComposer` 上：

```csharp
// 文件: Edit/MyGameBlueprintContainer.cs
using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Screens.Edit.Compose.Components;
using osuTK;

namespace osu.Game.Rulesets.MyGame.Edit
{
    public partial class MyGameBlueprintContainer : ComposeBlueprintContainer
    {
        public new MyGameHitObjectComposer Composer => (MyGameHitObjectComposer)base.Composer;

        public MyGameBlueprintContainer(MyGameHitObjectComposer composer)
            : base(composer)
        {
        }

        protected override SelectionHandler<HitObject> CreateSelectionHandler()
            => new EditorSelectionHandler();

        protected override bool TryMoveBlueprints(DragEvent e, IList<(SelectionBlueprint<HitObject> blueprint, Vector2[] originalSnapPositions)> blueprints)
        {
            Vector2 distanceTravelled = e.ScreenSpaceMousePosition - e.ScreenSpaceMouseDownPosition;
            Vector2 movePosition = blueprints.First().originalSnapPositions.First() + distanceTravelled;

            const float gridSize = 32;
            Vector2 snapped = new Vector2(
                MathF.Round(movePosition.X / gridSize) * gridSize,
                MathF.Round(movePosition.Y / gridSize) * gridSize);

            var referenceBlueprint = blueprints.First().blueprint;
            // Real rulesets usually implement a custom EditorSelectionHandler.HandleMovement()
            // to apply the screen-space delta to their own hit object coordinates.
            return SelectionHandler.HandleMovement(new MoveSelectionEvent<HitObject>(referenceBlueprint, snapped - referenceBlueprint.ScreenSpaceSelectionPoint));
        }
    }
}
```

放置期间的吸附通常放在 `PlacementBlueprint.UpdateTimeAndPosition()` 中：

```csharp
// 文件: Edit/MyGamePlacementBlueprint.cs
using System;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.MyGame.Objects;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.MyGame.Edit
{
    public partial class MyGamePlacementBlueprint : HitObjectPlacementBlueprint
    {
        public new MyGameHitObject HitObject => (MyGameHitObject)base.HitObject;

        public MyGamePlacementBlueprint()
            : base(new MyGameHitObject())
        {
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button != MouseButton.Left)
                return false;

            EndPlacement(true);
            return true;
        }

        public override SnapResult UpdateTimeAndPosition(Vector2 screenSpacePosition, double fallbackTime)
        {
            const float gridSize = 32;
            Vector2 snapped = new Vector2(
                MathF.Round(screenSpacePosition.X / gridSize) * gridSize,
                MathF.Round(screenSpacePosition.Y / gridSize) * gridSize);

            return base.UpdateTimeAndPosition(snapped, fallbackTime);
        }
    }
}
```

### 20.4 EditorSetupSection（编辑器设置面板）

覆盖 `Ruleset.CreateEditorSetupSections()` 以自定义编辑器设置界面：

```csharp
public class MyGameRuleset : Ruleset
{
    public override IEnumerable<Drawable> CreateEditorSetupSections()
    {
        // 保留默认的 Metadata/Difficulty/Resources/Colours/Design
        yield return new MetadataSection();
        yield return new DifficultySection();
        yield return new ResourcesSection { RelativeSizeAxes = Axes.X };
        yield return new ColoursSection { RelativeSizeAxes = Axes.X };
        yield return new DesignSection();

        // ★ 添加 Ruleset 专属设置
        yield return new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                new MyGameEditorSettingsSection(),
            }
        };
    }
}
```

### 20.5 karaoke 多标签编辑器模式

karaoke 拥有最复杂的编辑器，提供多标签页 + 多种编辑模式：

```csharp
// KaraokeBeatmapEditor 包含多个标签页
public partial class KaraokeBeatmapEditor : GenericEditor<KaraokeBeatmapEditorScreenMode>
{
    [Cached] public FontManager FontManager { get; }
    [Cached] public LyricsProvider LyricsProvider { get; }
    [Cached] public DebugBeatmapManager DebugBeatmapManager { get; }

    // 多个 Config 管理器
    [Cached] public KaraokeRulesetEditConfigManager EditConfigManager { get; }
    [Cached] public KaraokeRulesetLyricEditorConfigManager LyricEditorConfigManager { get; }
    [Cached] public KaraokeRulesetEditGeneratorConfigManager GeneratorConfigManager { get; }
    [Cached] public KaraokeRulesetEditCheckerConfigManager CheckerConfigManager { get; }

    protected override IReadOnlyList<KaraokeBeatmapEditorScreenMode> GenerateScreens()
        => new[] { LyricEditor, NoteEditor, SingerEditor, TranslationEditor };
}
```

### 20.6 在 Ruleset 中接线

```csharp
public class MyGameRuleset : Ruleset
{
    public override HitObjectComposer CreateHitObjectComposer()
        => new MyGameHitObjectComposer(this);
}
```

---

## 21. 谱面验证系统

谱面验证器在编辑器中实时检查谱面问题。

### 21.1 核心接口

```csharp
// IBeatmapVerifier
public interface IBeatmapVerifier
{
    IEnumerable<Issue> Run(BeatmapVerifierContext context);
}

// ICheck — 单个检查项
public interface ICheck
{
    CheckMetadata Metadata { get; }
    IEnumerable<IssueTemplate> PossibleTemplates { get; }
    IEnumerable<Issue> Run(BeatmapVerifierContext context);
}

// Issue — 发现的问题
public class Issue
{
    public double? Time;
    public IReadOnlyList<HitObject> HitObjects;
    public IssueTemplate Template;
    public object[] Arguments;
}
```

### 21.2 实现自定义验证器

```csharp
// 文件: Edit/MyGameBeatmapVerifier.cs
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Checks.Components;

namespace osu.Game.Rulesets.MyGame.Edit
{
    public class MyGameBeatmapVerifier : IBeatmapVerifier
    {
        private readonly IssueTemplate noObjectsTemplate;
        private readonly IssueTemplate tooCloseTemplate;

        public MyGameBeatmapVerifier()
        {
            var check = new MyGameVerifierCheck();
            noObjectsTemplate = new IssueTemplate(check, IssueType.Problem,
                "Beatmap has no playable objects.");
            tooCloseTemplate = new IssueTemplate(check, IssueType.Warning,
                "Objects are too close together ({0}ms apart).");
        }

        public IEnumerable<Issue> Run(BeatmapVerifierContext context)
        {
            var beatmap = context.CurrentDifficulty.Playable;
            var hitObjects = beatmap.HitObjects.OfType<MyGameHitObject>().ToList();

            // ★ 检查：至少有一个 HitObject
            if (hitObjects.Count == 0)
            {
                yield return new Issue(noObjectsTemplate);
                yield break;
            }

            // ★ 检查：没有重叠的对象
            for (int i = 1; i < hitObjects.Count; i++)
            {
                double delta = hitObjects[i].StartTime - hitObjects[i - 1].StartTime;
                if (delta < 50) // 小于 50ms 间距
                    yield return new Issue(new[] { hitObjects[i - 1], hitObjects[i] }, tooCloseTemplate, delta);
            }
        }

        private class MyGameVerifierCheck : ICheck
        {
            public CheckMetadata Metadata => new CheckMetadata(CheckCategory.Compose, "MyGame verifier");
            public IEnumerable<IssueTemplate> PossibleTemplates => System.Array.Empty<IssueTemplate>();
            public IEnumerable<Issue> Run(BeatmapVerifierContext context) => System.Array.Empty<Issue>();
        }
    }
}
```

### 21.3 实现 ICheck（分项检查）

```csharp
// 文件: Edit/Checks/CheckMyGameTiming.cs
using System.Collections.Generic;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Edit.Checks.Components;
using osu.Game.Rulesets.MyGame.Objects;

namespace osu.Game.Rulesets.MyGame.Edit.Checks
{
    public class CheckMyGameTiming : ICheck
    {
        public CheckMetadata Metadata => new CheckMetadata(
            CheckCategory.Timing,
            "Invalid timing points");

        public IEnumerable<IssueTemplate> PossibleTemplates => new IssueTemplate[]
        {
            new IssueTemplateNoTimingPoints(this),
            new IssueTemplateExtremeBPM(this),
        };

        public IEnumerable<Issue> Run(BeatmapVerifierContext context)
        {
            var timingPoints = context.CurrentDifficulty.Playable
                .ControlPointInfo.TimingPoints;

            if (!timingPoints.Any())
            {
                yield return new Issue(PossibleTemplates.First());
                yield break;
            }

            foreach (var tp in timingPoints)
            {
                if (tp.BPM < 60 || tp.BPM > 300)
                {
                    yield return new Issue(tp.Time, PossibleTemplates.Last(), tp.BPM);
                }
            }
        }

        private class IssueTemplateNoTimingPoints : IssueTemplate
        {
            public IssueTemplateNoTimingPoints(ICheck check)
                : base(check, IssueType.Problem, "No timing points found.")
            {
            }
        }

        private class IssueTemplateExtremeBPM : IssueTemplate
        {
            public IssueTemplateExtremeBPM(ICheck check)
                : base(check, IssueType.Warning, "BPM ({0}) is too extreme.")
            {
            }
        }
    }
}
```

### 21.4 karaoke 的分层验证器

karaoke 有一些独特模式值得学习：

**模式 A：按对象类型分层检查**

```csharp
// 抽象基类 — 对特定 HitObject 类型迭代检查
public abstract class CheckHitObjectProperty<THitObject> : ICheck
    where THitObject : HitObject
{
    public IEnumerable<Issue> Run(BeatmapVerifierContext context)
    {
        var hitObjects = context.CurrentDifficulty.Playable
            .HitObjects.OfType<THitObject>();

        foreach (var hitObject in hitObjects)
        {
            var issue = Check(hitObject);
            if (issue != null)
                yield return issue;
        }
    }

    protected abstract Issue? Check(THitObject hitObject);
}

// 具体检查
public class CheckLyricText : CheckHitObjectProperty<Lyric>
{
    protected override Issue? Check(Lyric lyric)
    {
        if (string.IsNullOrEmpty(lyric.Text))
            return new Issue(lyric, lyricTextMissingTemplate);
        return null;
    }
}
```

**模式 B：编辑器内实时验证**

```csharp
// 编辑器内的实时验证器 — 绑定到 HitObject 的增删事件
public partial class LyricEditorVerifier : EditorVerifier<LyricEditorMode>
{
    [Resolved] private EditorBeatmap editorBeatmap { get; set; }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // ★ 对象变化时自动刷新检查结果
        editorBeatmap.HitObjectAdded += _ => Refresh();
        editorBeatmap.HitObjectRemoved += _ => Refresh();
        editorBeatmap.HitObjectUpdated += (_, _) => Refresh();
    }

    protected override void Refresh()
    {
        var issues = RunChecks();
        UpdateIssueDisplay(issues);
    }
}
```

### 21.5 在 Ruleset 中接线

```csharp
public class MyGameRuleset : Ruleset
{
    public override IBeatmapVerifier? CreateBeatmapVerifier()
        => new MyGameBeatmapVerifier();
}
```

---

## 22. 结果与统计系统

游戏结束后的结果显示界面。

### 22.1 StatisticItem

```csharp
// StatisticItem 结构
public class StatisticItem
{
    public readonly LocalisableString Name;
    public readonly Func<Drawable> CreateContent; // 延迟创建
    public readonly bool RequiresHitEvents;         // 是否需要 HitEvent 数据
}
```

### 22.2 在 Ruleset 中提供统计

```csharp
public class MyGameRuleset : Ruleset
{
    public override StatisticItem[] CreateStatisticsForScore(
        ScoreInfo score, IBeatmap playableBeatmap)
    {
        var timedHitEvents = score.HitEvents
            .Where(e => e.HitObject is MyGameHitObject).ToList();

        return new StatisticItem[]
        {
            // ★ 标准时间分布图
            new StatisticItem("Timing Distribution",
                () => new HitEventTimingDistributionGraph(timedHitEvents)
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 250
                }, true),

            // ★ 自定义统计图
            new StatisticItem("Lane Accuracy",
                () => new MyLaneAccuracyChart(timedHitEvents, playableBeatmap)
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 200
                }, true),

            // ★ 简单表格
            new StatisticItem("Statistics",
                () => new SimpleStatisticTable(3, new SimpleStatisticItem[]
                {
                    new AverageHitError(timedHitEvents),
                    new UnstableRate(timedHitEvents),
                }), true),
        };
    }
}
```

### 22.3 自定义统计图

```csharp
// 文件: Statistics/MyLaneAccuracyChart.cs
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MyGame.Statistics
{
    public partial class MyLaneAccuracyChart : CompositeDrawable
    {
        private readonly IReadOnlyList<HitEvent> hitEvents;
        private readonly IBeatmap beatmap;
        private const int lane_count = 5;

        public MyLaneAccuracyChart(
            IReadOnlyList<HitEvent> hitEvents, IBeatmap beatmap)
        {
            this.hitEvents = hitEvents;
            this.beatmap = beatmap;
            RelativeSizeAxes = Axes.X;
            Height = 200;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // ★ 按轨道统计命中率
            var lanes = new int[lane_count];
            var hits = new int[lane_count];

            foreach (var evt in hitEvents)
            {
                if (evt.HitObject is MyGameHitObject obj)
                {
                    lanes[obj.Lane]++;
                    if (evt.Result.IsHit)
                        hits[obj.Lane]++;
                }
            }

            // 绘制柱状图
            float barWidth = DrawWidth / lane_count;
            for (int i = 0; i < lane_count; i++)
            {
                float accuracy = lanes[i] > 0
                    ? (float)hits[i] / lanes[i] : 0f;
                float barHeight = accuracy * 200;

                AddInternal(new Container
                {
                    X = i * barWidth,
                    Size = new Vector2(barWidth - 4, barHeight),
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = accuracy > 0.9f
                            ? Color4.Green : Color4.Yellow,
                    }
                });
            }
        }
    }
}
```

### 22.4 Tau 的 PaddleDistributionGraph（极坐标统计图）

Tau 展示了更高级的自定义统计图：按角度分桶统计按键分布，用极坐标条形图渲染。

```csharp
// PaddleDistributionGraph 核心逻辑
foreach (var evt in paddleEvents)
{
    var tauResult = evt.JudgementResult() as TauJudgementResult;
    if (tauResult == null) continue;

    // 按 DeltaAngle 分桶
    float angle = tauResult.DeltaAngle;
    int bucket = (int)((angle + 180) / 10); // 每 10° 一个桶
    distribution[bucket]++;
}

// 用极坐标渲染条形图
foreach (var bar in bars)
{
    float rotation = bar.Angle;
    bar.Rotation = rotation;
    bar.Height = bar.Count * scaleFactor;
}
```

### 22.5 Sentakki 的 JudgementChart（分类型统计）

按 HitObject 类型（Tap/Hold/Slide/Touch）分别展示判罚分布：

```csharp
public partial class JudgementChart : TableContainer
{
    // ★ 每种 HitObject 类型一行，每种判罚结果一列
    // Tap:    100 0 0 0 0 0 0 0
    // Hold:    80 3 5 1 0 1 0 0
    // Slide:   45 2 1 0 2 0 0 0

    // ★ 底部有 TotalNoteCounter（3 秒平滑动画的滚动计数器）
    private partial class TotalNoteCounter : RollingCounter<int>
    {
        protected override double RollingDuration => 3000;
    }
}
```


## 23. 选歌界面自定义

### 23.1 难度属性显示

覆盖 `GetBeatmapAttributesForDisplay` 自定义选歌卡片上的 CS/AR/OD/HP 行：

```csharp
// Ruleset.cs:427
public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(
    IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
{
    // 返回任意组属性，可使用 AdditionalMetric 附加说明
    yield return new RulesetBeatmapAttribute("Keys", "KC",
        original: beatmapInfo.Difficulty.CircleSize,
        adjusted: adjustedCS, maxValue: 10)
    {
        Description = "Number of keys/columns",
        AdditionalMetrics = new[]
        {
            new RulesetBeatmapAttribute.AdditionalMetric(
                "Star Rating",
                difficulty.StarRating.ToLocalisableString("0.##")),
        }
    };

    yield return new RulesetBeatmapAttribute("HP Drain", "HP",
        originalDifficulty.DrainRate, adjustedDifficulty.DrainRate, 10);
}
```

**Ranked Play 卡片专用属性**（如 Mania 的 HN 替换 KC）：

```csharp
public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForRankedPlayCard(
    IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
{
    // 可用不同属性集（mania 在此显示 Hold Note 比率代替 Key Count）
    return GetBeatmapAttributesForDisplay(beatmapInfo, mods);
}
```

### 23.2 自定义搜索关键词

覆盖 `CreateRulesetFilterCriteria` 让用户在选歌界面按 Ruleset 专属条件筛选：

```csharp
public class MyGameFilterCriteria : IRulesetFilterCriteria
{
    public bool Matches(BeatmapInfo beatmapInfo, FilterCriteria criteria)
    {
        // ★ 检查谱面是否符合条件
        return true;
    }

    // ★ 解析自定义关键词：如 "lanes>=4", "key=5"
    public bool TryParseCustomKeywordCriteria(
        string key, Operator op, string value)
    {
        if (key == "lanes" && int.TryParse(value, out int lanes))
        {
            targetLanes = lanes;
            targetOp = op;
            return true;
        }
        return false;
    }

    // ★ Mod 变化时是否需重新筛选
    public bool FilterMayChangeFromMods(FilterCriteria criteria,
        ValueChangedEvent<IReadOnlyList<Mod>> mods) => false;
}

// Ruleset 中接线
public override IRulesetFilterCriteria? CreateRulesetFilterCriteria()
    => new MyGameFilterCriteria();
```

### 23.3 加载界面自定义

`PlayerLoader` (`osu.Game\Screens\Play\PlayerLoader.cs`) 是歌曲加载过渡页面：

| 关键点 | 用途 |
|--------|------|
| `OnPlayerLoaded()` | 玩家加载完成后的钩子（virtual） |
| `ReadyForGameplay` | 自定义准备就绪条件 |
| `PlayerSettings` | 设置组（Visual/Audio/Input 等） |
| 加载时显示 `BeatmapMetadataDisplay` | 谱面元数据 |

### 23.4 歌曲预览（Preview）

预览不是 Ruleset 的功能，而是选歌界面层面。`WorkingBeatmap.PrepareTrackForPreview()` 决定预览行为：

```csharp
// WorkingBeatmap.cs:123-143
public void PrepareTrackForPreview(bool looping, double? offsetFromPreviewPoint = null)
{
    Track.Looping = looping;
    Track.RestartPoint = Metadata.PreviewTime;  // ★ 从 PreviewTime 开始循环

    // PreviewTime 无效时默认在 40% 处
    if (Track.RestartPoint < 0 || Track.RestartPoint > Track.Length)
        Track.RestartPoint = 0.4f * Track.Length;

    offsetFromPreviewPoint ??= -MusicController.DELAY_BEFORE_FADE;
    Track.Seek(Track.RestartPoint + offsetFromPreviewPoint.Value);
}
```

**没有固定时长**——预览就是 `Looping=true` + `RestartPoint=PreviewTime`，hover 多久循环多久，鼠标移开就停。

**PreviewTime 的来源**：谱师在编辑器里设定，写入 `.osu` 文件的 `[General]` 段：

```
[General]
PreviewTime: 53498
```

Ruleset 无法通过正常 API 干预预览。但可以通过 Harmony 运行时补丁实现多段时间线预览——不替换预览系统，而是**替换 WorkingBeatmap 实例本身**。

### 23.5 用 Harmony + 继承替换 WorkingBeatmap

> **警告**：此方案依赖 IL 补丁，osu 更新可能失效，不能上架插件市场。
> 以下代码是架构示意，不保证可直接复制编译；`WorkingBeatmap`、`Track` 和 `BeatmapManager` 的具体构造/抽象成员会随 osu!/osu-framework 版本变化，落地时必须以当前本地源码为准。

**原理**：不 patch `Track` getter，而是 patch `BeatmapManager.GetWorkingBeatmap()`，返回我们自己的 `WorkingBeatmap` 子类，覆盖 `PrepareTrackForPreview`。

**第一步：引用 Harmony**

```xml
<PackageReference Include="Lib.Harmony" Version="2.3.3" />
```

**第二步：继承 WorkingBeatmap**

```csharp
// 文件: Preview/CustomWorkingBeatmap.cs
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;

namespace osu.Game.Rulesets.MyGame.Preview
{
    /// <summary>
    /// 继承 WorkingBeatmap，重写 PrepareTrackForPreview
    /// </summary>
    public class CustomWorkingBeatmap : WorkingBeatmap
    {
        private readonly ITrackStore trackStore;
        private readonly List<(string name, double triggerAt, double audioStart)> schedule;

        public CustomWorkingBeatmap(IBeatmap beatmap, BeatmapInfo info,
            ITrackStore trackStore,
            List<(string, double, double)> schedule)
            : base(info, null) // ★ 传 null 给 AudioManager 参数
        {
            this.trackStore = trackStore;
            this.schedule = schedule;
        }

        // ★ 覆盖预览方法
        public new void PrepareTrackForPreview(bool looping,
            double? offset = null)
        {
            Track.Looping = looping;
            Track.RestartPoint = Metadata.PreviewTime;
            if (Track.RestartPoint < 0)
                Track.RestartPoint = 0.4f * Track.Length;
        }

        // ★ 覆盖 Track，返回多段时间线 Track
        protected override ITrack GetTrack()
        {
            var segments = new List<ITrack>();
            foreach (var (name, _, audioStart) in schedule)
            {
                var t = trackStore.Get(name);
                t.Seek(audioStart);
                segments.Add(t);
            }
            return new TimelineTrack(segments, schedule);
        }

        protected override IBeatmap GetBeatmap() => null!;
        protected override Texture GetBackground() => null!;
        protected override Waveform GetWaveform() => null!;
    }
}
```

**第三步：TimelineTrack——编排多段 ITrack**

```csharp
// 文件: Preview/TimelineTrack.cs
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;

namespace osu.Game.Rulesets.MyGame.Preview
{
    public class TimelineTrack : Track
    {
        private readonly List<ITrack> tracks;
        private readonly List<(double triggerAt, double audioStart)> schedule;

        public TimelineTrack(List<ITrack> tracks,
            List<(double triggerAt, double audioStart)> schedule)
        {
            this.tracks = tracks;
            this.schedule = schedule;
        }

        public override void Start()
        {
            double startTime = Clock.CurrentTime;

            for (int i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                var (triggerMs, audioMs) = schedule[i];

                Scheduler.AddDelayed(() =>
                {
                    track.Seek(audioMs);
                    track.Start();
                }, triggerMs / 1000.0);
            }
        }

        public override void Stop()
        {
            foreach (var t in tracks) t.Stop();
        }

        public override bool Seek(double _) => false;
        public override bool IsDummyDevice => true;
        public override Task<bool> StartAsync() => Task.FromResult(true);
        public override Task StopAsync() => Task.CompletedTask;
        protected override void UpdateState() { }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                foreach (var t in tracks) t.Dispose();
            base.Dispose(disposing);
        }
    }
}
```

**第四步：Harmony 补丁——替换 BeatmapManager.GetWorkingBeatmap**

```csharp
// 文件: Preview/PreviewPatcher.cs
using HarmonyLib;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.MyGame.Objects;

namespace osu.Game.Rulesets.MyGame.Preview
{
    public static class PreviewPatcher
    {
        private static Harmony? harmony;
        private static ITrackStore? trackStore;

        public static void Install()
        {
            harmony ??= new Harmony("com.mygame.preview");
            harmony.Patch(
                AccessTools.Method(typeof(BeatmapManager),
                    "GetWorkingBeatmap",
                    new[] { typeof(BeatmapInfo), typeof(WorkingBeatmap?) }),
                postfix: new HarmonyMethod(
                    typeof(PreviewPatcher), nameof(OnGetWorkingBeatmap)));
        }

        public static void SetTrackStore(ITrackStore store) => trackStore = store;

        // ★ BeatmapManager.GetWorkingBeatmap 返回时替换
        private static void OnGetWorkingBeatmap(BeatmapInfo info,
            ref WorkingBeatmap __result)
        {
            if (trackStore == null) return;

            // ★ 只为我们的 Ruleset 定制
            if (info.Ruleset.ShortName != "mygame") return;

            var schedule = new List<(string, double, double)>
            {
                ("preview_seg_01.wav", 0, 0),
                ("preview_seg_02.wav", 1500, 0),
                ("preview_seg_03.wav", 3000, 500),
            };

            __result = new CustomWorkingBeatmap(
                __result.Beatmap, info, trackStore, schedule);
        }

        public static void Uninstall()
            => harmony?.UnpatchAll("com.mygame.preview");
    }
}
```

**第五步：在 Ruleset 中触发 + 传递 TrackStore**

```csharp
public class MyGameRuleset : Ruleset
{
    static MyGameRuleset()
    {
        PreviewPatcher.Install();
    }

    // ★ 在 DrawableRuleset 拿到 TrackStore 后传给 patcher
    // 或者用 Harmony 直接从 BeatmapManager 获取
}
```

**数据流对比**：

```
原流程：
  BeatmapManager.GetWorkingBeatmap(info)
    └── new WorkingBeatmap(info, audioManager)
          └── PrepareTrackForPreview()  ← 只有 Loop+RestartPoint

新流程：
  BeatmapManager.GetWorkingBeatmap(info)
    └── Postfix 换为 CustomWorkingBeatmap(info, trackStore, schedule)
          └── PrepareTrackForPreview()  ← 我们的实现
          └── GetTrack()               ← 返回 TimelineTrack
                └── 0s:    seg1 Start
                └── 1.5s:  seg2 Start
                └── 3s:    seg3 Start  ← 并行播放
```

**与直接替换 Track getter 相比**：

| 方面 | 替换 Track getter | 替换 WorkingBeatmap 实例 |
|------|-------------------|------------------------|
| BASS internal 依赖 | 需要反射 `MixerHandle` | **不需要**，走正常 ITrack API |
| Volume/Balance | 手动管理 | 框架自动管理 |
| 代码量 | 更多 | 更少，更干净 |
| 侵入点 | `get_Track` (property getter) | `GetWorkingBeatmap` (方法返回值) |

**仍存在的缺点**：Harmony 依赖、IL 更新即失效、段间无淡入淡出。

---

## 24. 本地化系统

### 24.1 TranslatableString 模式

osu! 使用 `LocalisableString` 支持多语言。最常用的是 `TranslatableString`：

```csharp
// 文件: Localisation/SettingStrings.cs
using osu.Framework.Localisation;

namespace osu.Game.Rulesets.MyGame.Localisation
{
    public static class SettingStrings
    {
        private const string prefix =
            @"osu.Game.Rulesets.MyGame.Resources.Localisation.SettingStrings";

        // ★ getKey 辅助
        private static LocalisableString getKey(string key, string fallback)
            => new TranslatableString(
                prefix + "." + key.Replace(' ', '_'), fallback);

        public static LocalisableString ShowEffects =>
            getKey("show_effects", "Show Effects");

        public static LocalisableString AnimationSpeed =>
            getKey("animation_speed", "Animation Speed");
    }
}
```

### 24.2 在代码中使用

```csharp
// Mod 描述
public override LocalisableString Description
    => SettingStrings.AnimationSpeed;

// Judgement 名称
public override LocalisableString GetDisplayNameForHitResult(HitResult result)
{
    switch (result)
    {
        case HitResult.Great:
            return "Nice!"; // 或使用 TranslatableString
        default:
            return base.GetDisplayNameForHitResult(result);
    }
}
```

### 24.3 组织模式

| 用途 | 推荐文件 |
|------|---------|
| Mod 名称/描述 | `Localisation/ModStrings.cs` |
| 设置面板文本 | `Localisation/SettingStrings.cs` |
| 统计面板文本 | `Localisation/StatsStrings.cs` |
| 按键绑定名称 | `Localisation/ActionStrings.cs` |
| 编辑器文本 | `Localisation/EditorStrings.cs` |
| 判罚结果名称 | `Localisation/JudgementStrings.cs` |

### 24.4 自定义 HitResult 显示名称

```csharp
public class MyGameRuleset : Ruleset
{
    // ★ 将 HitResult 映射到自定义名称
    public override LocalisableString GetDisplayNameForHitResult(HitResult result)
    {
        switch (result)
        {
            case HitResult.Great:
                return "Awesome";
            case HitResult.Ok:
                return "Fine";
            case HitResult.Meh:
                return "Bad";
            default:
                return base.GetDisplayNameForHitResult(result);
        }
    }
}
```

---

## 25. 高级主题

### 25.3 ModCompatibility 注册（Hitokori 模式）

```csharp
public class HitokoriRuleset : Ruleset
{
    public HitokoriRuleset()
    {
        // ★ Hitokori 旧源码中的社区兼容性注册模式。
        //   这是 ruleset 自己维护的辅助 API，不是当前 osu! 通用 API。
        var mods = CreateAllMods();
        foreach (var mod in mods)
        {
            ModCompatibility.RegisterMod(GetType(), mod.GetType());
        }
    }
}
```

### 25.4 Ruleset 内嵌资源

```csharp
public class MyGameRuleset : Ruleset
{
    // ★ 覆盖 CreateResourceStore 加载嵌入资源
    public override IResourceStore<byte[]> CreateResourceStore()
        => new NamespacedResourceStore<byte[]>(
            new DllResourceStore(GetType().Assembly), @"Resources");

    // ★ 在 DrawableHitObject 中通过 [Resolved] 获取
    // [Resolved] private TextureStore textures { get; set; }
}
```

### 25.5 平台检测（移动端适配）

```csharp
public class MyGameDrawableRuleset : DrawableRuleset<MyGameHitObject>
{
    // ★ 要求竖屏方向
    public override bool RequirePortraitOrientation => true;

    // ★ Sentakki 根据平台条件性显示设置
    [BackgroundDependencyLoader]
    private void load()
    {
        if (RuntimeInfo.IsMobile)
        {
            // 添加触屏专用设置
            AddTouchSettings();
        }
    }
}
```

### 25.6 Ruleset 全部 Hook 点总览

| Ruleset 方法 | 行号 | 用途 |
|-------------|------|------|
| `CreateDrawableRulesetWith()` | 244 | 游戏实例 |
| `CreateScoreProcessor()` | 250 | 计分 |
| `CreateHealthProcessor(double drainStartTime)` | 256 | 血量 |
| `CreateBeatmapConverter()` | 263 | 转谱 |
| `CreateBeatmapProcessor()` | 270 | 谱面后处理 |
| `CreateDifficultyCalculator()` | 272 | 难度计算 |
| `CreatePerformanceCalculator()` | 278 | PP 计算 |
| `CreateHitObjectComposer()` | 280 | 编辑器 |
| `CreateBeatmapVerifier()` | 282 | 谱面验证 |
| `CreateIcon()` | 284 | 图标 |
| `CreateResourceStore()` | 286 | 嵌入资源 |
| `CreateSettings()` | 290 | 设置面板 |
| `CreateConfig()` | 296 | 设置持久化 |
| `GetDefaultKeyBindings()` | 318 | 默认键位 |
| `CreateConvertibleReplayFrame()` | 343 | 回放兼容 |
| `CreateStatisticsForScore()` | 351 | 结果统计 |
| `CreateRulesetFilterCriteria()` | 447 | 选歌筛选 |
| `CreateEditorSetupSections()` | 452 | 编辑器设置区 |
| `CreateSkinTransformer()` | 224 | 皮肤转换 |
| `EditorShowScrollSpeed` | 479 | 编辑器显示滚速 |
| `RulesetAPIVersionSupported` | 62 | API 版本 |

---



### 25.8 通知与在线数据

### 25.8.1 通知系统

通过 DI 解析 `INotificationOverlay`，随时发送通知：

```csharp
[Resolved]
private INotificationOverlay notificationOverlay { get; set; }

// ★ 简单通知
notificationOverlay.Post(new SimpleNotification
{
    Text = "Beatmap converted successfully!",
    Icon = FontAwesome.Solid.CheckCircle,
});

// ★ 进度通知
var progress = new ProgressNotification
{
    Text = "Generating hit objects...",
    Progress = 0.0f,
    State = ProgressNotificationState.Active,
};
notificationOverlay.Post(progress);

// 更新进度
progress.Progress = 0.5f;
// 完成
progress.State = ProgressNotificationState.Completed;
```

### 25.8.2 在线数据（OnlineID 与排行榜）

自定义 Ruleset 默认 `OnlineID = -1`，不参与在线排行榜。要支持在线排名，需在 osu!web 注册：

```csharp
public class MyGameRuleset : Ruleset
{
    public MyGameRuleset()
    {
        // ★ 如果 osu!web 分配了 OnlineID = 100，则：
        // RulesetInfo.OnlineID 会自动设为 -1（未注册）
        // 可通过 ILegacyRuleset 或直接设 RulesetInfo.OnlineID
    }
}
```

排行榜查询完全通过 `RulesetInfo.ShortName` 自动处理，无需额外代码。

### 25.8.3 自定义加载页

```csharp
public partial class MyGamePlayerLoader : PlayerLoader
{
    protected override void OnPlayerLoaded()
    {
        base.OnPlayerLoaded();
        // ★ 游戏加载完成后的自定义逻辑
    }

    protected override bool ReadyForGameplay
        => base.ReadyForGameplay && MyCustomCondition;

    protected override void ContentOut()
    {
        // ★ 自定义淡出过渡
        this.FadeOut(300);
    }
}
```

### 25.8.4 音频预览

音频预览全由选歌界面自动处理（`PreviewTrackManager`），Ruleset 无需干预。

### 25.8.5 ILegacyRuleset

如果 Ruleset 需要兼容旧版 osu!stable 的排行榜/LegacyID：

```csharp
public class MyGameRuleset : Ruleset, ILegacyRuleset
{
    public int LegacyID => 100; // osu!web 分配的 ID

    public ILegacyScoreSimulator CreateLegacyScoreSimulator()
        => new MyGameLegacyScoreSimulator();
}
```

如果没有真实 legacy ID 和稳定端计分兼容需求，不要实现 `ILegacyRuleset`。

---

## 附录：关键文件参考表
## 26. 测试系统

### 26.1 测试基类层次

```
TestScene (osu.Framework)
  └── OsuTestScene                          ← 视觉测试基类
        ├── OsuManualInputManagerTestScene  ← 带手动输入管理
        │     ├── ScreenTestScene           ← 带屏幕栈
        │     │     └── PlayerTestScene     ← 完整游戏测试
        │     └── OsuGameTestScene          ← 全游戏流程测试
        └── OsuGridTestScene                ← 网格布局测试
```

### 26.2 视觉测试

```csharp
// 文件: osu.Game.Rulesets.MyGame.Tests/Visual/TestSceneMyGamePlayer.cs
using osu.Game.Rulesets.MyGame.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MyGame.Tests.Visual
{
    public partial class TestSceneMyGamePlayer : PlayerTestScene
    {
        // ★ 返回要测试的 Ruleset
        protected override Ruleset CreatePlayerRuleset()
            => new MyGameRuleset();

        // ★ 测试步骤
        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("load autoplay", () =>
            {
                // 加载并自动播放
            });
        }
    }
}
```

### 26.3 组件级视觉测试

```csharp
// 测试单个 DrawableHitObject
public partial class TestSceneMyHitCircle : OsuTestScene
{
    private TestDrawableMyHitCircle circle;

    [SetUp]
    public void Setup() => Schedule(() =>
    {
        Child = circle = new TestDrawableMyHitCircle(
            new MyGameHitObject { StartTime = 1000 });
    });

    [Test]
    public void TestFadeIn()
    {
        AddAssert("visible", () => circle.Alpha > 0);
    }

    [Test]
    public void TestHitState()
    {
        AddStep("hit", () => circle.ApplyTestResult(HitResult.Great));
        AddAssert("scaled up", () => circle.Scale.X > 1);
    }

    private partial class TestDrawableMyHitCircle : DrawableMyHitCircle
    {
        public TestDrawableMyHitCircle(MyGameHitObject hitObject)
            : base(hitObject)
        {
        }

        public void ApplyTestResult(HitResult result) => ApplyResult(result);
    }
}
```

### 26.4 单元测试

```csharp
// 文件: MyGameDifficultyCalculatorTest.cs
[TestFixture]
public class MyGameDifficultyCalculatorTest
{
    [Test]
    public void TestEmptyBeatmap()
    {
        var calculator = new MyGameDifficultyCalculator(
            new MyGameRuleset().RulesetInfo, new TestWorkingBeatmap(new Beatmap()));
        var difficulty = calculator.Calculate();
        Assert.That(difficulty.StarRating, Is.EqualTo(0));
    }

    [Test]
    public void TestSimpleBeatmap()
    {
        var beatmap = new Beatmap<MyGameHitObject>
        {
            HitObjects = { new MyGameHitObject { StartTime = 1000 } }
        };
        var calculator = new MyGameDifficultyCalculator(
            new MyGameRuleset().RulesetInfo, new TestWorkingBeatmap(beatmap));
        var difficulty = calculator.Calculate();
        Assert.That(difficulty.StarRating, Is.GreaterThan(0));
    }
}
```

### 26.5 测试项目结构

```
osu.Game.Rulesets.MyGame.Tests/
├── osu.Game.Rulesets.MyGame.Tests.csproj
├── Visual/
│   ├── TestSceneMyGamePlayer.cs        ← 完整游戏测试
│   ├── TestSceneMyHitCircle.cs         ← 组件测试
│   └── TestSceneMyPlayfield.cs         ← 游戏区测试
├── Difficulty/
│   └── MyGameDifficultyCalculatorTest.cs
├── Scoring/
│   └── MyGameScoreProcessorTest.cs
├── Beatmaps/
│   └── MyGameBeatmapConversionTest.cs
└── Mods/
    └── MyGameModTest.cs
```

.csproj 参考：
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>osu.Game.Rulesets.MyGame.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ppy.osu.Game" Version="..." />
    <PackageReference Include="NUnit" Version="..." />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="..." />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\osu.Game.Rulesets.MyGame\osu.Game.Rulesets.MyGame.csproj" />
  </ItemGroup>
</Project>
```

---
## 27. 完整示例：实现一个最小 Ruleset

以下是一个能运行的最小化 Ruleset 的所有文件。命名为 "PingPong"：两个球拍在左右，击中飞过的球。

### 27.1 项目文件

```xml
<!-- osu.Game.Rulesets.PingPong.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>osu.Game.Rulesets.PingPong</AssemblyName>
    <RootNamespace>osu.Game.Rulesets.PingPong</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ppy.osu.Game" Version="2026.518.0" />
  </ItemGroup>
</Project>
```

### 27.2 Action 枚举

```csharp
// PingPongAction.cs
namespace osu.Game.Rulesets.PingPong
{
    public enum PingPongAction
    {
        Left,
        Right,
    }
}
```

### 27.3 InputManager

```csharp
// PingPongInputManager.cs
using osu.Framework.Input.Bindings;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.PingPong
{
    public class PingPongInputManager : RulesetInputManager<PingPongAction>
    {
        public PingPongInputManager(RulesetInfo ruleset)
            : base(ruleset, 0, SimultaneousBindingMode.None) { }
    }
}
```

### 27.4 HitObject

```csharp
// Objects/PingPongHitObject.cs
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.PingPong.Objects
{
    public class PingPongHitObject : HitObject
    {
        public bool IsLeft; // true=左拍, false=右拍

        public override Judgement CreateJudgement()
            => new PingPongJudgement();

        protected override HitWindows CreateHitWindows()
            => new PingPongHitWindows();
    }

    public class PingPongJudgement : Judgement
    {
        public override HitResult MaxResult => HitResult.Great;
    }
}
```

### 27.5 HitWindows

```csharp
// Objects/PingPongHitWindows.cs
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.PingPong.Objects
{
    public class PingPongHitWindows : HitWindows
    {
        private static readonly DifficultyRange[] ranges =
        {
            new DifficultyRange(50, 35, 20),
            new DifficultyRange(100, 70, 40),
            new DifficultyRange(150, 100, 60),
            new DifficultyRange(200, 150, 100),
        };

        private double great;
        private double ok;
        private double meh;
        private double miss;

        public override bool IsHitResultAllowed(HitResult result)
            => result is HitResult.Great or HitResult.Ok or HitResult.Meh or HitResult.Miss;

        public override void SetDifficulty(double difficulty)
        {
            great = IBeatmapDifficultyInfo.DifficultyRange(difficulty, ranges[0]);
            ok = IBeatmapDifficultyInfo.DifficultyRange(difficulty, ranges[1]);
            meh = IBeatmapDifficultyInfo.DifficultyRange(difficulty, ranges[2]);
            miss = IBeatmapDifficultyInfo.DifficultyRange(difficulty, ranges[3]);
        }

        public override double WindowFor(HitResult result)
        {
            switch (result)
            {
                case HitResult.Great: return great;
                case HitResult.Ok: return ok;
                case HitResult.Meh: return meh;
                case HitResult.Miss: return miss;
                default: return double.MaxValue;
            }
        }
    }
}
```

### 27.6 DrawableHitObject

```csharp
// Objects/Drawables/DrawablePingPongBall.cs
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.PingPong.Objects;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.PingPong.Objects.Drawables
{
    public partial class DrawablePingPongBall : DrawableHitObject<PingPongHitObject>
    {
        public DrawablePingPongBall()
            : base(null)
        {
            Size = new Vector2(30);
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AddInternal(new Circle
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
            });
        }

        // ★ 玩家侧（左/右）
        private bool lastSide;

        public void OnSide(bool isLeft)
        {
            lastSide = isLeft;
            UpdateResult(true);
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (!userTriggered) return;

            if (lastSide == HitObject.IsLeft)
                ApplyResult(HitObject.HitWindows.ResultFor(timeOffset));
            else
                ApplyResult(HitResult.Miss);
        }

        protected override void UpdateHitStateTransforms(ArmedState state)
        {
            switch (state)
            {
                case ArmedState.Hit:
                    this.ScaleTo(2, 200).Then().FadeOut(300).Expire();
                    break;
                case ArmedState.Miss:
                    this.FadeOut(500).Expire();
                    break;
            }
        }
    }
}
```

### 27.7 Playfield

```csharp
// UI/PingPongPlayfield.cs
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.PingPong.Objects;
using osu.Game.Rulesets.PingPong.Objects.Drawables;
using osu.Game.Rulesets.UI;
using osuTK;

namespace osu.Game.Rulesets.PingPong.UI
{
    public partial class PingPongPlayfield : Playfield
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            RegisterPool<PingPongHitObject, DrawablePingPongBall>(10, 100);

            AddInternal(new PingPongInputContainer(this)
            {
                RelativeSizeAxes = Axes.Both,
            });
        }

        protected override void OnNewDrawableHitObject(DrawableHitObject drawable)
        {
            base.OnNewDrawableHitObject(drawable);

            if (drawable is DrawablePingPongBall ball)
            {
                // 设置初始位置
                ball.Position = ball.HitObject.IsLeft
                    ? new Vector2(100, 0)
                    : new Vector2(400, 0);
            }
        }

        public void OnSideAction(bool isLeft)
        {
            lastSide = isLeft;
            foreach (var d in HitObjectContainer.AliveObjects)
            {
                if (d is DrawablePingPongBall ball)
                    ball.OnSide(isLeft);
            }
        }
    }

    // 输入处理器（嵌入 Playfield）
    internal partial class PingPongInputContainer : Container,
        IKeyBindingHandler<PingPongAction>
    {
        private readonly PingPongPlayfield playfield;

        public PingPongInputContainer(PingPongPlayfield playfield)
        {
            this.playfield = playfield;
        }

        public bool OnPressed(KeyBindingPressEvent<PingPongAction> e)
        {
            playfield.OnSideAction(e.Action == PingPongAction.Left);
            return true;
        }

        public void OnReleased(KeyBindingReleaseEvent<PingPongAction> e) { }
    }
}
```

### 27.8 DrawableRuleset

```csharp
// UI/DrawablePingPongRuleset.cs
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.PingPong.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.PingPong.UI
{
    public partial class DrawablePingPongRuleset : DrawableRuleset<PingPongHitObject>
    {
        public DrawablePingPongRuleset(Ruleset ruleset, IBeatmap beatmap,
            IReadOnlyList<Mod>? mods = null)
            : base(ruleset, beatmap, mods) { }

        protected override Playfield CreatePlayfield() => new PingPongPlayfield();

        protected override PassThroughInputManager CreateInputManager()
            => new PingPongInputManager(Ruleset.RulesetInfo);

        public override DrawableHitObject<PingPongHitObject>? CreateDrawableRepresentation(
            PingPongHitObject h) => null;
    }
}
```

### 27.9 BeatmapConverter

```csharp
// Beatmaps/PingPongBeatmapConverter.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.PingPong.Objects;

namespace osu.Game.Rulesets.PingPong.Beatmaps
{
    public class PingPongBeatmapConverter : BeatmapConverter<PingPongHitObject>
    {
        public PingPongBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset) { }

        public override bool CanConvert()
            => Beatmap.HitObjects.All(h => h is IHasPosition);

        protected override IEnumerable<PingPongHitObject> ConvertHitObject(
            HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float x = (original as IHasPosition)!.Position.X;

            yield return new PingPongHitObject
            {
                Samples = original.Samples,
                StartTime = original.StartTime,
                IsLeft = x < 256,
            };
        }
    }
}
```

### 27.10 Ruleset 入口

```csharp
// PingPongRuleset.cs
using System.Collections.Generic;
using osu.Framework.Input.Bindings;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.PingPong.Beatmaps;
using osu.Game.Rulesets.PingPong.Mods;
using osu.Game.Rulesets.PingPong.Objects;
using osu.Game.Rulesets.PingPong.UI;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.PingPong
{
    public class PingPongRuleset : Ruleset
    {
        public override string RulesetAPIVersionSupported
            => CURRENT_RULESET_API_VERSION;

        public override string ShortName => "pingpong";
        public override string Description => "Ping Pong";

        public override DrawableRuleset CreateDrawableRulesetWith(
            IBeatmap beatmap, IReadOnlyList<Mod>? mods = null)
            => new DrawablePingPongRuleset(this, beatmap, mods);

        public override IBeatmapConverter CreateBeatmapConverter(
            IBeatmap beatmap)
            => new PingPongBeatmapConverter(beatmap, this);

        public override DifficultyCalculator CreateDifficultyCalculator(
            IWorkingBeatmap beatmap)
            => new PingPongDifficultyCalculator(RulesetInfo, beatmap);

        public override IEnumerable<Mod> GetModsFor(ModType type)
        {
            switch (type)
            {
                case ModType.Automation:
                    return new Mod[] { new PingPongModAutoplay() };
                default:
                    return System.Array.Empty<Mod>();
            }
        }

        public override IEnumerable<KeyBinding> GetDefaultKeyBindings(
            int variant = 0) => new[]
        {
            new KeyBinding(InputKey.Z, PingPongAction.Left),
            new KeyBinding(InputKey.MouseLeft, PingPongAction.Left),
            new KeyBinding(InputKey.X, PingPongAction.Right),
            new KeyBinding(InputKey.MouseRight, PingPongAction.Right),
        };
    }
}
```

### 27.11 难度计算器（最简桩）

```csharp
// Difficulty/PingPongDifficultyCalculator.cs
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.PingPong.Difficulty
{
    public class PingPongDifficultyCalculator : DifficultyCalculator
    {
        public PingPongDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap) { }

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(
            IBeatmap beatmap, double clockRate)
        {
            var objects = new List<DifficultyHitObject>();

            for (int i = 1; i < beatmap.HitObjects.Count; i++)
            {
                objects.Add(new DifficultyHitObject(
                    beatmap.HitObjects[i],
                    beatmap.HitObjects[i - 1],
                    clockRate,
                    objects,
                    objects.Count));
            }

            return objects;
        }

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods, double clockRate)
            => System.Array.Empty<Skill>();

        protected override DifficultyAttributes CreateDifficultyAttributes(
            IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
            => new DifficultyAttributes
            {
                StarRating = 0,
                Mods = mods,
                MaxCombo = beatmap.HitObjects.Count,
            };
    }
}
```

### 27.12 ModAutoplay（最简桩）

```csharp
// Replays/PingPongReplayFrame.cs
using System.Collections.Generic;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.PingPong.Replays
{
    public class PingPongReplayFrame : ReplayFrame
    {
        public List<PingPongAction> Actions { get; } = new List<PingPongAction>();

        public PingPongReplayFrame(double time, params PingPongAction[] actions)
            : base(time)
        {
            Actions.AddRange(actions);
        }
    }
}
```

```csharp
// Mods/PingPongModAutoplay.cs
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.PingPong.Objects;
using osu.Game.Rulesets.PingPong.Replays;
using osu.Game.Users;

namespace osu.Game.Rulesets.PingPong.Mods
{
    public class PingPongModAutoplay : ModAutoplay
    {
        public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods)
        {
            var replay = new Replay();

            foreach (var obj in beatmap.HitObjects)
            {
                if (obj is PingPongHitObject pingPong)
                    replay.Frames.Add(new PingPongReplayFrame(
                        obj.StartTime,
                        pingPong.IsLeft ? PingPongAction.Left : PingPongAction.Right));
            }

            return new ModReplayData(replay, new ModCreatedUser { Username = "autoplay" });
        }
    }
}
```

---


| 概念 | 基类定义位置 |
|------|------------|
| Ruleset 基类 | `osu.Game\Rulesets\Ruleset.cs:40` |
| DrawableRuleset | `osu.Game\Rulesets\UI\DrawableRuleset.cs:43` |
| Playfield | `osu.Game\Rulesets\UI\Playfield.cs:33` |
| HitObject | `osu.Game\Rulesets\Objects\HitObject.cs:32` |
| DrawableHitObject | `osu.Game\Rulesets\Objects\Drawables\DrawableHitObject.cs:34` |
| Judgement | `osu.Game\Rulesets\Judgements\Judgement.cs:12` |
| JudgementResult | `osu.Game\Rulesets\Judgements\JudgementResult.cs` |
| HitWindows | `osu.Game\Rulesets\Scoring\HitWindows.cs:15` |
| ScoreProcessor | `osu.Game\Rulesets\Scoring\ScoreProcessor.cs` |
| HealthProcessor | `osu.Game\Rulesets\Scoring\HealthProcessor.cs` |
| DifficultyCalculator | `osu.Game\Rulesets\Difficulty\DifficultyCalculator.cs` |
| PerformanceCalculator | `osu.Game\Rulesets\Difficulty\PerformanceCalculator.cs` |
| Mod | `osu.Game\Rulesets\Mods\Mod.cs:25` |
| BeatmapConverter | `osu.Game\Beatmaps\BeatmapConverter.cs` |
| ScrollingPlayfield | `osu.Game\Rulesets\UI\Scrolling\ScrollingPlayfield.cs:14` |
| DrawableScrollingRuleset | `osu.Game\Rulesets\UI\Scrolling\DrawableScrollingRuleset.cs` |
| RulesetStore | `osu.Game\Rulesets\RulesetStore.cs` |
| RulesetInfo | `osu.Game\Rulesets\RulesetInfo.cs:97` |
| BeatmapProcessor | `osu.Game\Beatmaps\IBeatmapProcessor.cs:12` |
| RulesetConfigManager | `osu.Game\Rulesets\Configuration\RulesetConfigManager.cs:18` |
| SkinTransformer | `osu.Game\Skinning\SkinTransformer.cs:20` |
| HitObjectComposer | `osu.Game\Rulesets\Edit\HitObjectComposer.cs:47` |
| IBeatmapVerifier | `osu.Game\Rulesets\Edit\IBeatmapVerifier.cs:12` |
| PreviewTrackManager | `osu.Game\Audio\PreviewTrackManager.cs` |

---

## 参考项目

| Ruleset | 源码 | 亮点 |
|---------|------|------|
| osu! Standard | `osu\osu.Game.Rulesets.Osu\` | 最完整实现，滑条/转盘 |
| osu!taiko | `osu\osu.Game.Rulesets.Taiko\` | 滚动式+简单HitObject |
| osu!catch | `osu\osu.Game.Rulesets.Catch\` | 角色移动+掉落物收集 |
| osu!mania | `osu\osu.Game.Rulesets.Mania\` | 多轨道+Pattern转谱 |
| pippidon | `osu-rulesets\pippidon\` | 最简滚动式Ruleset |
| tau | `osu-rulesets\tau\` | 极坐标角度判定系统 |
| Hitokori | `osu-rulesets\Hitokori\` | 双向链表+双窗口+相机 |
| Touhosu | `osu-rulesets\touhosu\` | 生存模式+位置碰撞+弹幕 |
| sentakki | `osu-rulesets\sentakki\` | maimaiDX 复刻，完整编辑器 |
| soyokaze | `osu-rulesets\soyokaze\` | 原神风物之诗琴 |
| karaoke | `osu-rulesets\karaoke\` | 最复杂：多标签编辑器+皮肤编辑器+自定义谱面格式 |
