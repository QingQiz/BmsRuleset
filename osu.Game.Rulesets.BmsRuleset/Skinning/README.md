# BMS Ruleset 皮肤查询流程

## 概述

BMS ruleset 的皮肤系统在 osu! 标准皮肤链的基础上增加了一个三层兜底机制，由
`BmsEmbeddedSkinSource` 管理，并通过 `[Cached(typeof(ISkinSource))]` 注入到
`BmsPlayfield` 的 DI 树中，所有子 drawable 均从这里解析皮肤。

---

## 关键类

| 类                           | 职责                                                     |
|-----------------------------|--------------------------------------------------------|
| `BmsEmbeddedSkinSource`     | 三层查找的入口，`[Cached]` 到 DI 树                              |
| `BmsEmbeddedSkinFallbackFactory` | 根据当前 osu! 皮肤风格创建 embedded fallback chain         |
| `BmsEmbeddedSkinFallbackChain` | 持有并释放 primary/fallback embedded transformers              |
| `BmsBuiltInSkinTransformer` | 包装 osu! 内置皮肤，屏蔽 BMS 特定 lookup，剥离 global HUD 中的血条       |
| `BmsLegacySkinTransformer`  | 包装任意皮肤，提供 BMS 特定 drawable 和 config                     |
| `BmsEmbeddedSkin`           | 从 ruleset DLL 内嵌资源加载贴图/音效的最小 ISkin                     |
| `BmsEmbeddedSkinDefinition` | 用户皮肤类型 → `BmsEmbeddedSkinKind` 的映射注册表                  |
| `rulesetResourcesSkin`      | osu! 框架创建的 `ResourceStoreBackedSkin`，服务于通用贴图/音效 lookup |

## 目录边界

| 目录              | 职责                                               |
|------------------|--------------------------------------------------|
| `Components/`    | BMS 专用 skin lookup 类型                           |
| `Configuration/` | `[BMS]` / `[Mania]` 配置解析和配置 lookup             |
| `Embedded/`      | ruleset 内嵌 fallback skin、风格选择、fallback chain |
| `Legacy/`        | osu! skin transformer 与 legacy 配置/资源名解析       |
| `LegacyDrawables/` | legacy skin 生成的具体 drawable                   |
| `Resources/`     | legacy 纹理解析和 LN body 切片资源                    |
| `Runtime/`       | gameplay 缓存、drawable factory、note metric 解析    |
| `HudComponents/` | BMS HUD 组件                                      |
| `Drawables/`     | skin drawable 尺寸/解析结果 DTO                      |

---

## 皮肤链构建过程

### 第一步：`RulesetSkinProvidingContainer` 包装用户皮肤

由 osu! 游戏层在 `Player.cs:289` 创建，不由 ruleset 自身创建。
它调用 `BmsRuleset.CreateSkinTransformer(skin, beatmap)`（`BmsRuleset.cs:140`）
对 `SkinManager.AllSources` 中的每个皮肤进行包装：

```
ArgonSkin / ArgonProSkin / TrianglesSkin / DefaultLegacySkin / RetroSkin
    → BmsBuiltInSkinTransformer(skin)

其他 Skin 子类（用户导入的 legacy 皮肤）
    → BmsLegacySkinTransformer(skin, beatmap)

其他 ISkin（非 Skin 子类）
    → null（不包装，原样保留）
```

包装后的结果写入 `RulesetSkinProvidingContainer.skinSources`，
并在 `TrianglesSkin` 之前插入 `rulesetResourcesSkin`（`RulesetSkinProvidingContainer.cs:107`）。

`rulesetResourcesSkin` 由框架从 `Ruleset.CreateResourceStore()` 创建
（`RulesetSkinProvidingContainer.cs:74`），默认读取
`osu.Game.Rulesets.BmsRuleset.dll` 内嵌的 `Resources/Textures/` 和 `Resources/Samples/`，
只响应 `GetTexture` / `GetSample`，`GetDrawableComponent` 和 `GetConfig` 始终返回 null。

### 第二步：`BmsPlayfield` 刷新 embedded fallback source

`BmsPlayfield.updateEmbeddedSkinFallback()` 在游戏开始及用户切换皮肤时执行。
UI 层只负责把当前 parent skin chain 和 beatmap 交给 Skinning 层：

1. 若当前 playfield 没有 `BmsBeatmap`，调用 `activeSkin.SetSources(parentSkin, null)`。
2. 否则调用 `BmsEmbeddedSkinFallbackFactory.Create(parentSkin.AllSources, beatmap, renderer, audio)`。
3. factory 通过 `BmsEmbeddedSkinDefinition.TryGetKind` 识别当前用户皮肤对应的内嵌资源风格：
    - `ArgonSkin / ArgonProSkin / TrianglesSkin` → `LegacyModern`
    - `DefaultLegacySkin / RetroSkin` → `LegacyOld`
    - 其他 → `LegacyOld`
4. factory 创建 `BmsEmbeddedSkinFallbackChain`，其中 primary 使用匹配风格；当匹配风格不是 `LegacyOld` 时，额外创建 `LegacyOld` fallback。
5. `BmsEmbeddedSkinSource` 只接收 parent 与 fallback chain，并负责后续 lookup 路由。

---

## 完整查找链

```
BmsEmbeddedSkinSource                              [Cached into DI]
│
├─ parent = BeatmapSkinProvidingContainer          [RulesetSkinProvidingContainer.cs:60]
│   │
│   ├─ [包装后的 beatmapSkin]
│   │       谱面自带皮肤，经 CreateSkinTransformer 包装
│   │
│   └─ [fallback] RulesetSkinProvidingContainer.skinSources
│        │         (AllowFallingBackToParent = false)
│        │
│        ├─ [包装后的 CurrentSkin]                 [SkinManager.cs:333]
│        │       例：BmsBuiltInSkinTransformer(ArgonSkin)
│        │           或 BmsLegacySkinTransformer(用户 legacy 皮肤)
│        │
│        ├─ rulesetResourcesSkin                   [RulesetSkinProvidingContainer.cs:74]
│        │       来源：BmsRuleset.dll / Resources/Textures/ + Resources/Samples/
│        │       GetTexture ✓  GetSample ✓  GetDrawableComponent ✗  GetConfig ✗
│        │       插入位置：TrianglesSkin 之前（cs:107），若无 TrianglesSkin 则追加末尾（cs:109）
│        │
│        └─ [包装后的 TrianglesSkin]               [SkinManager.cs:343，无条件追加]
│                例：BmsBuiltInSkinTransformer(TrianglesSkin)
│                （用户选 TrianglesSkin 本身时此项不存在）
│
└─ embeddedFallbacks = BmsEmbeddedSkinFallbackChain
    │
    ├─ primary = BmsLegacySkinTransformer(BmsEmbeddedSkin(kind))
    │       kind 由当前用户皮肤决定：ArgonSkin 系 → LegacyModern，其余 → LegacyOld
    │       BmsEmbeddedSkin 读取 DLL 内嵌对应风格资源
    │       IsProvidingLegacyResources = true（内嵌资源始终包含 BMS 贴图）
    │
    └─ fallback = BmsLegacySkinTransformer(BmsEmbeddedSkin(LegacyOld))
            仅当 kind != LegacyOld 时创建
            BmsEmbeddedSkin 读取 DLL 内嵌旧式资源
```

---

## 各 lookup 类型的查找路由

`BmsEmbeddedSkinSource.GetDrawableComponent` 根据 lookup 类型决定是否启用三层兜底：

```csharp
// BMS 特定 lookup：走三层兜底
lookup is BmsSkinComponentLookup
    or SkinComponentLookup<HitResult>
    or GlobalSkinnableContainerLookup { Ruleset: not null }
    ? parent ?? embeddedFallbacks
    : parent   // 其他所有 lookup：只走 parent
```

### BMS 特定 lookup 的命中层

| lookup 类型                        | 内置皮肤（Argon 等）下的实际命中                               | 用户 legacy 皮肤下的实际命中                                   |
|----------------------------------|---------------------------------------------------|------------------------------------------------------|
| `BmsSkinComponentLookup`         | parent 全部 null → **primary**                      | 有对应贴图时 **parent**，否则 primary                         |
| `SkinComponentLookup<HitResult>` | parent 全部 null → **primary**                      | 有对应贴图时 **parent**，否则 primary                         |
| ruleset HUD                      | parent 全部 null → **primary**（`createLegacyHud()`） | `IsProvidingLegacyResources` 时 **parent**，否则 primary |
| global HUD                       | **parent**（内置皮肤的 HUD，blood 已剥离）                   | **parent**                                           |
| `GetTexture`（通用）                 | parent 链依次查找，`rulesetResourcesSkin` 兜底            | 同左                                                   |
| `BmsSkinConfigurationLookup`     | parent 全部 null → **primary**（内嵌 skin.ini）         | 用户 skin.ini 优先，否则 primary                            |

---

## 典型皮肤选择下的 skinSources 列表

### 选择 ArgonSkin

`SkinManager.AllSources` = `[ArgonSkin, TrianglesSkin]`（`SkinManager.cs:333,343`）

```
skinSources = [
  BmsBuiltInSkinTransformer(ArgonSkin),
  rulesetResourcesSkin,
  BmsBuiltInSkinTransformer(TrianglesSkin),
]
embeddedFallbacks = [
  primary  = BmsLegacySkinTransformer(BmsEmbeddedSkin(LegacyModern)),
  fallback = BmsLegacySkinTransformer(BmsEmbeddedSkin(LegacyOld)),
]
```

### 选择 TrianglesSkin

`SkinManager.AllSources` = `[TrianglesSkin]`（末尾追加条件不满足，`SkinManager.cs:342`）

```
skinSources = [
  BmsBuiltInSkinTransformer(TrianglesSkin),
  rulesetResourcesSkin,                      ← 无 TrianglesSkin 可插前，走 else 追加末尾
]
embeddedFallbacks = [
  primary  = BmsLegacySkinTransformer(BmsEmbeddedSkin(LegacyModern)),
  fallback = BmsLegacySkinTransformer(BmsEmbeddedSkin(LegacyOld)),
]
```

### 选择用户 legacy 皮肤（含 mania 贴图）

`SkinManager.AllSources` = `[UserLegacySkin, DefaultClassicSkin, TrianglesSkin]`

```
skinSources = [
  BmsLegacySkinTransformer(UserLegacySkin),  ← 有贴图时直接在此命中
  BmsBuiltInSkinTransformer(DefaultClassicSkin),
  rulesetResourcesSkin,
  BmsBuiltInSkinTransformer(TrianglesSkin),
]
embeddedFallbacks = [
  primary = BmsLegacySkinTransformer(BmsEmbeddedSkin(LegacyOld)),
]
```

---

## `BmsBuiltInSkinTransformer` 的作用

对内置皮肤，所有 BMS 特定 lookup 返回 null，使控制权落到 embedded fallback chain。
唯一的实质贡献是处理 global HUD：透传内置皮肤的 HUD drawable，但用
`HealthFilteredHudContainer`（`BmsBuiltInSkinTransformer.cs:87`）在 `LoadComplete`
时深度遍历并移除所有 `HealthDisplay` 子节点，避免与 `BmsPlayfield` 自有的
`LegacyHealthDisplay` 重复显示。

---

## `BmsLegacySkinTransformer.IsProvidingLegacyResources`

决定该 transformer 能否提供 BMS drawable 的关键开关（`BmsLegacySkinTransformer.cs:61`）：

```
true  如果满足以下任一条件：
  - 基类 LegacySkinTransformer.IsProvidingLegacyResources（有 legacy combo 字体）
  - 皮肤有 [BMS] 或 [Mania] skin.ini 段落
  - 皮肤有 mania-key1 贴图
  - 皮肤有 mania-keyS 贴图
```

`BmsEmbeddedSkin` 始终满足条件（DLL 内嵌贴图保证存在），
用户皮肤若不满足则所有 `BmsSkinComponentLookup` 返回 null，
控制权落到 primary。

---

## Q&A

### Are beatmap resources (samples, music, etc.) provided through the skin system?

Yes. This is fully intentional osu! design, not an accident.

`WorkingBeatmapCache.GetSkin()` constructs a `LegacyBeatmapSkin` backed by a
`RealmBackedResourceStore` — every file in the beatmap folder. That skin is passed to
`RulesetSkinProvidingContainer` at `Player.cs:289` and inserted as the first (highest
priority) source in `BeatmapSkinProvidingContainer`. All children of the playfield see
beatmap files as the top of the `ISkinSource` chain.

`LegacyBeatmapSkin.GetSample()` serves those files directly:

```csharp
// LegacyBeatmapSkin.cs:92
public override ISample? GetSample(ISampleInfo sampleInfo)
{
    if (sampleInfo is HitSampleInfo hitSampleInfo && !hitSampleInfo.UseBeatmapSamples)
        return null;  // block standard hitsounds unless they are explicit per-note overrides

    return base.GetSample(sampleInfo);  // opens the .wav/.ogg from the beatmap folder
}
```

The gate is `HitSampleInfo.UseBeatmapSamples` — `true` only when a hitobject has an
explicit custom sample override (e.g. `0:0:0:0:kick.wav` in the `.osu`). Normal bank
hitsounds (`normal-hitnormal`, etc.) never reach the beatmap skin; they fall through to
the user skin or defaults.

There is a second gate at the container level: `BeatmapSkinProvidingContainer`
short-circuits the entire `LegacyBeatmapSkin` source when the user has the
"Beatmap Hitsounds" setting disabled (`OsuSetting.BeatmapHitsounds`).

### Why skin.ini cannot be officially extended by a ruleset plugin

`LegacySkinDecoder` detects section headers by matching against the `LegacyDecoder.Section`
enum: `General`, `Editor`, `Metadata`, `Difficulty`, `Events`, `TimingPoints`, `Colours`,
`Fonts`, `CatchTheBeat`, `Mania`. Any `[SectionName]` not in that enum is logged as a
warning and its lines continue to be parsed under the **previous** section.

`[Mania]` works because `Section.Mania` is in the core enum and `LegacySkin.ParseConfigurationStream`
runs a dedicated second-pass decoder (`LegacyManiaSkinDecoder`). Both of those are core
modifications — not a plugin API.

What the osu! core actually provides:

| Mechanism                             | Type                        | What it offers                                                                                                                             |
|---------------------------------------|-----------------------------|--------------------------------------------------------------------------------------------------------------------------------------------|
| `Ruleset.CreateSkinTransformer`       | `public virtual`            | Query-time interception only; parsing is already done                                                                                      |
| `LegacySkin.ParseConfigurationStream` | `protected virtual`         | Only reachable by subclassing `LegacySkin` — a ruleset plugin cannot do that                                                               |
| `SkinConfiguration.ConfigDictionary`  | `Dictionary<string,string>` | Flat catch-all for unhandled keys from recognised sections; no section prefix, same-name keys from different sections overwrite each other |

**Conclusion:** A ruleset plugin cannot hook into skin.ini parsing without reflection.
The only sanctioned runtime hook is `CreateSkinTransformer`, which operates at query time
after parsing is complete.

#### How BmsRuleset works around this

`BmsSkinConfigurationDecoder` bypasses `LegacySkinDecoder` entirely. It opens `skin.ini`
directly from the raw `IResourceStore<byte[]>` and parses `[BMS]` and `[Mania]` sections
in a single custom pass. For user skins (concrete `Skin` subclasses), the store is
obtained via reflection on the private `store` field — ugly but load-bearing until osu!
exposes a first-class `Resources` property on `Skin`. The TODO tracking this is in
`BmsSkinConfigurationDecoder.cs`.

For `BmsEmbeddedSkin`, no reflection is needed — `BmsEmbeddedSkin.Resources` is
`internal` and accessible directly.
