# 皮肤系统

[返回 README](../README.zh-CN.md) | [English](./skin-system.md)

皮肤配置的选择顺序见 [osu!mania 皮肤支持](../README.zh-CN.md#osumania-皮肤支持)。

## 制作皮肤

将图片和 `skin.ini` 放入文件夹中，然后作为普通 osu! 皮肤导入。

### skin.ini — `[BMS]` 段

为每个你想支持的布局编写一个 `[BMS]` 段。`Layout:` 键为必填。

**布局值：** `5K`、`7K`、`9K`、`10K`、`14K`、`18K`
（别名：`BMS5K`、`BME7K`、`PMS9K`、`BMS5KDouble`、`BME7KDouble`、`PMS9KDouble`）

### 所有支持的键

**布局与位置：**

| 键               | 说明                  | 示例    |
|-----------------|---------------------|-------|
| `Layout`        | 布局标识（必填）            | `7K`  |
| `HitPosition`   | 判定目标距底部 Y（480 高度空间） | `440` |
| `LightPosition` | 列键灯 Y               | `440` |
| `ScorePosition` | 判定弹出 Y              | `250` |
| `ComboPosition` | 连击数字距顶部 Y           | `300` |
| `JudgementLine` | 在判定位置显示白线（`1`/`0`）  | `1`   |
| `KeysUnderNotes`| 按键图像绘制在音符之上（`1`/`0`） | `0`   |

**列几何：**

| 键                         | 说明              | 示例                        |
|---------------------------|-----------------|---------------------------|
| `ColumnWidth`             | 所有 N 列的逗号分隔宽度   | `45,45,45,45,45,45,45,45` |
| `ColumnLineWidth`         | 分隔线宽度 — N+1 个值  | `0,1,1,1,1,1,1,1,0`       |
| `ColumnSpacing`           | 列间距             | `2,2,2,2,2,2,2`           |
| `WidthForNoteHeightScale` | 音符高度缩放参考宽度      | `50`                      |
| `BarlineHeight`           | 小节线高度乘数（每小节前的线） | `1.2`                     |

> **Scratch 列特别说明**
>
> 无论视觉位置如何，列索引 0 **始终**是 scratch 列。
> - `ColumnWidth` — 第一个值始终是 scratch 轨道宽度
> - `NoteImage0` / `KeyImage0` — 始终引用 scratch 列
>
> 使用 **2P mod**（SecondPlayer）时，scratch 移至最右侧视觉位置。
> 皮肤配置会自动应用 — 无需重复的 `[BMS]` 段。
>
> 对于 5K（`s,1,2,3,4,5`）：
>
> **2P 下的 ColumnLineWidth 索引重映射：**
>
> ```
> 1P 视觉顺序:   [s] [1] [2] [3] [4] [5]
> gap 索引:     0   1   2   3   4   5   6
>
> 2P 视觉顺序:   [1] [2] [3] [4] [5] [s]
> gap 索引:     0   2   3   4   5   1   6
> ```
>
> **2P 下的 ColumnSpacing 索引重映射：**
>
> ```
> 1P 视觉顺序: [s] [1] [2] [3] [4] [5]
> gap 索引:       1   2   3   4   5
>
> 2P 视觉顺序: [1] [2] [3] [4] [5] [s]
> gap 索引:       2   3   4   5   1
> ```

**颜色：**

| 键                             | 说明               | 示例                |
|-------------------------------|------------------|-------------------|
| `ColourColumnLine`            | 列分隔线颜色 (R,G,B,A) | `255,255,255,50`  |
| `ColourJudgementLine`         | 判定线颜色            | `255,255,255,255` |
| `ColourBarline`               | 小节线颜色            | `0,255,0,255`     |
| `ColourBreak`                 | 连击中断闪烁颜色         | `255,0,0`         |
| `Colour1`–`ColourN`           | 逐列背景颜色           | `0,0,0,0`         |
| `ColourLight1`–`ColourLightN` | 逐列键灯发光颜色         | `255,200,0`       |
| `Colour`                      | 全列背景简写           | `0,0,0,0`         |
| `ColourLight`                 | 全列灯简写            | `0,0,0`           |

**字体：**

| 键             | 说明          | 默认值     |
|---------------|-------------|---------|
| `ComboPrefix` | 连击数字纹理文件名前缀 | `score` |

连击数字加载 `{ComboPrefix}-0.png` 至 `{ComboPrefix}-9.png`。
若数字纹理缺失，连击显示将自动隐藏。

**note 图像：**

| 键                                           | 说明                        |
|---------------------------------------------|---------------------------|
| `NoteImage0`–`NoteImageN`                   | 逐列普通音符图像                  |
| `NoteImage0L`–`NoteImageNL`（或 `NoteImageL`） | 逐列 LN 身体图像                |
| `NoteImage0T`–`NoteImageNT`（或 `NoteImageT`） | 逐列 LN 尾部图像                |
| `NoteImage0H`–`NoteImageNH`                 | 逐列 LN 头部（回退到 `NoteImage`） |
| `MineImage` / `MineImage0`–`MineImageN`     | 地雷图像                      |

**按键图像：**

| 键                         | 说明        |
|---------------------------|-----------|
| `KeyImage0`–`KeyImageN`   | 逐列按键（未按下） |
| `KeyImage0D`–`KeyImageND` | 逐列按键（按下）  |

**Stage与特效：**

| 键                   | 说明                                                          |
|---------------------|--------------------------------------------------------------|
| `StageHint`         | 判定目标图像                                                     |
| `StageLeft`         | 左 Stage 边框图像                                               |
| `StageRight`        | 右 Stage 边框图像                                               |
| `StageBottom`       | 底部 Stage 前景图像                                              |
| `StageLight`        | 列灯/发光图像（按键按住时显示）                                          |
| `LightingN`         | 普通命中爆炸图像                                                   |
| `LightingL`         | LN 命中爆炸图像（LN 按住期间也会脉冲触发）                                  |
| `LightingNWidth`    | 普通爆炸缩放宽度，逐列逗号分隔                                            |
| `LightingLWidth`    | LN 爆炸缩放宽度，逐列逗号分隔                                            |
| `LightFramePerSecond` | 列灯动画 FPS（别名：`StageLightFramePerSecond`）                  |

**判定图像：**

| 键           | BMS 判定        |
|-------------|---------------|
| `HitPGreat` | PGREAT        |
| `HitGreat`  | GREAT         |
| `HitGood`   | GOOD          |
| `HitBad`    | BAD           |
| `HitPoor`   | POOR / E-POOR |

### 帧动画（N 后缀）

大多数图像资源可以作为多帧动画提供。在纹理名后追加 `-0`、`-1`、`-2`、…：

```
lightingN-0.png
lightingN-1.png
lightingN-2.png
```

如果找到帧序列，则按 `LightFramePerSecond`（`StageLight`）或根据帧数推导的帧长
（`LightingN`/`LightingL`）播放动画。如果仅有单帧（即直接使用名称，如 `lightingN.png`），
则作为静态精灵图使用。

适用于任何图像键：`NoteImage`、`KeyImage`/`KeyImageD`、`StageLight`、`LightingN`、
`LightingL`、`StageHint`、判定图像等。

### 图片资源缺失

只有对应配置键不存在时，才会采用默认图片名。配置键存在时，其值具有决定性：规则集会在活动皮肤层级中查找这个
确切的资源名；如果所有来源都不提供该资源，则不绘制图片，不会再尝试 legacy 默认文件名。

例如，未配置 `KeyImage1` 时会使用 `mania-key1`，并且可以从后备皮肤取得该资源；如果配置了
`KeyImage1: custom-key`，但没有 `custom-key` 资源，则未按下状态保持空白，不会再尝试 `mania-key1`。

LN 头尾保留 osu!mania 的语义后备链（尾部依次到头部、普通 note，头部到普通 note）。判定图片也保留
osu!mania 的组件级后备：当前皮肤无法提供判定组件时，可以由更低优先级的皮肤提供完整判定组件。

### osu!mania 皮肤兼容

你也可以使用标准 osu!mania 皮肤的 `[Mania]` 段。规则集会匹配：

- `[Mania]` 带有 `SpecialStyle: 1` 和包含 scratch 的按键数（`6`、`8`、`12`、`16`）
- `[Mania]` 带有包含 scratch 的按键数（`6`、`8`、`12`、`16`），优先于精确 key-only 回退
- `[Mania]` 带有精确的按键数

在 `[Mania]` 段中，使用 mania 标准判定名称：`Hit300g`（PGREAT）、`Hit300`（GREAT）、`Hit200`（GOOD）、
`Hit50`（BAD）、`Hit0`（POOR）。

## 皮肤组件

在游玩过程中打开**皮肤编辑器**，即可添加、删除、移动和缩放组件。选中组件后，侧边栏还会显示该组件的专属设置。
保存布局时，组件的位置、大小和专属设置都会写入当前皮肤的 BMS 专用游玩界面布局。之后使用同一皮肤游玩 BMS 时会
自动加载该布局；谱面文件和 `skin.ini` 均不会被修改。

### Combo

使用皮肤的连击数字纹理显示当前连击数；纹理文件名前缀由 `ComboPrefix` 指定，默认是 `score`。每次增加连击时数字
会播放动画，断连时则使用 `ColourBreak` 指定的颜色闪烁。如果皮肤缺少所需的数字纹理，此组件没有可绘制的内容，
因此会保持隐藏。

- **自动隐藏延迟**：连击停止增加后，计数器继续显示的秒数。每次增加连击都会重新计时。可设置为 -1 至 100 秒，
  默认值为 3 秒；设为 -1 时永不自动隐藏。
- **最低可见连击数**：连击达到该数值时才显示计数器；低于该值时会立即隐藏。可设置为 0 至 100，默认值为 10。

移动或缩放组件只会改变数字的显示位置和大小，不会改变上述两个阈值。

### Judgement

显示最近一个音符的判定结果。出现新的 PGREAT、GREAT、GOOD、BAD 或 POOR/E-POOR 时，会立即替换上一个结果，
并从头播放对应判定图像的动画。图像来自 `[BMS]` 皮肤中的 `HitPGreat` 至 `HitPoor`，也可以使用上文所述的
osu!mania 对应判定图像。

此组件没有专属侧边栏设置。判定弹窗的位置和大小应通过编辑器控制，图案和动画则通过皮肤图像文件修改。

### 误差条

在横向时间轴上显示击打误差，左侧为 Fast，右侧为 Slow。白色 0 ms 标记始终位于视觉中心。左右两侧占用等长
空间，因此 BMS 的不对称判定区间会在较短的一侧保留空位，而不会使零点偏移。

各级判定区间组成一条连续色条。E-POOR 会在 Fast BAD 之外额外添加灰色区段，不会缩短其他区段。POOR 没有
有限的最慢边界，因此固定绘制在 Slow 侧最末端。

- **判定线粗细**：控制每条击打误差线的宽度，可设置为 1 至 8，默认值为 4。
- **判定线淡出时间**：控制击打误差线淡出所需的秒数，可设置为 0.1 至 20 秒，默认值为 5 秒。
- **显示色条**：控制是否显示判定区间色条。
- **显示动态的平均值指示器**：控制是否显示平均位置箭头；POOR 和 E-POOR 不会参与该平均值。
- **显示 E-POOR**：同时控制 E-POOR 击打误差线和 Fast 侧附加灰色区段，默认开启。
- **显示 POOR**：控制是否在 Slow 侧最末端显示 POOR 击打误差线，默认开启。
- **中心标记样式**：可选择在 0 ms 处显示圆形、线形或不显示标记；两种可见样式均为白色。
- **标签样式**：可选择 Fast/Slow 图标、文字标签或不显示标签。

横向拉伸会改变时间轴长度，纵向拉伸会改变判定线跨度，两个方向可以独立调整。

### 成绩图

在谱面进行过程中，将当前 EX Score 与以下两个参照成绩持续比较：

- **个人最好**：在使用当前 Mods 或难度更高的 Mods 取得的已保存成绩中，选择 EX Score 最高的一次。如果该成绩
  包含回放判定数据，成绩图会根据原回放还原其在当前时刻的成绩进度。
- **目标**：个人最好等级的下一个等级所需的最低 EX Score，依次为 C、B、A、S、X；个人最好已经达到 X 时，目标
  仍为 X。

成绩图可以显示等级分界线、三根实时成绩柱、当前成绩相对个人最好和目标的差值，以及 PGREAT 至 E-POOR 的判定
数量对比。如果已保存成绩不含所需的回放判定数据，个人最好一栏的判定数量会显示为不可用。

- **当前成绩颜色**：控制当前 EX Score 柱和当前成绩的强调色。
- **个人最好颜色**：控制个人最好成绩柱、最终成绩虚影和相应强调色。
- **目标颜色**：控制目标成绩柱、最终成绩虚影和相应强调色。
- **显示成绩柱**、**显示成绩差**和**显示判定对比**：分别控制三个区域。组件至少会保留一个可见区域，因此无法
  关闭最后一个仍在显示的区域。

组件会限制最小宽度，并根据已启用的区域保留足够高度。继续放大只会为图表提供更多显示空间，不会改变成绩计算。

<details>
<summary>示例</summary>

![](https://github.com/user-attachments/assets/ab14a6e8-e853-42ed-ae08-61cc4427e667)

</details>

### 血条

以从下向上的填充条显示当前选择的 BMS 血条。使用 Assist Easy、Easy 和 Normal 时，通关线会标出该血条的通关
阈值，填充颜色也会在越过红血区阈值和通关阈值时改变。阈值由所选血条的规则决定：Assist Easy 的通关阈值为
60%，Easy 和 Normal 为 80%，三者的红血区均在 20% 结束。

- **Groove 血条低血量颜色**：用于低于红血区阈值的部分。
- **Groove 血条中等血量颜色**：用于红血区阈值至通关阈值之间。
- **Groove 血条高血量颜色**：用于达到或超过通关阈值时。
- **Hard、ExHard 和 Hazard 血条填充颜色**：分别设置这三种生存型血条使用的单一填充颜色。

Class 系列血条使用各自规则中固定的颜色。调整组件大小只会改变血条的可见宽度和高度，不会修改血量、阈值或血条
机制。

### 歌曲进度

沿竖直轨道显示歌曲播放位置：发光指示块从顶部开始，在前奏期间保持在顶部，随后随着谱面的可游玩部分向底部移动。

**指示块颜色**会同时改变清晰的指示块和周围光晕。

移动组件可以改变进度轨道的位置，改变其高度则会改变指示块的移动距离。默认布局将其贴在 Stage 左边缘。

### BGA

显示谱面编排的完整 BGA 时间线，包括 Base、Layer 1、Layer 2 和 POOR 图层。
该组件会应用谱面定义的裁剪与透明度事件，并在出现 Miss 后根据谱面的 POOR BGA 模式短暂显示 POOR 图层。

组件矩形范围就是 BGA 的显示窗口。内容始终使用 aspect-fit 并保持原始宽高比，按需放大或缩小以适应窗口。
BGA 始终渲染在 playfield 后方，并且在隐藏游玩 HUD 时仍保留显示。

- **铺满屏幕**：让组件覆盖整个 HUD 区域，默认布局会启用此选项。移动、调整大小或旋转组件时会自动关闭该选项，
  并保留编辑后的 BGA 窗口。

**BGA dim** 是规则集的全局游玩设置，而非组件设置。无论当前使用哪个已保存组件布局，都会作用于 BGA。

### Stage

代表完整的可游玩区域。移动此组件时，轨道、音符、小节线、按键区、判定线、命中特效和 Stage 图像都会一起移动。

- **判定线偏移**：相对于皮肤提供的位置移动判定线。正值向上移动，负值向下移动；编辑器会根据当前 Stage 的可见
  范围限制可选数值，确保判定线不会移出 Stage。此设置不会改变判定时机。
- **音符高度缩放**：将音符头和音符尾的高度缩放至 0.01x 到 5x，默认值为 1x。此设置不会移动音符、改变 LN 身体
  长度或缩放其他 Stage 元素。

Stage 的不同缩放控制柄会执行不同操作：

- **横向拉伸**（左、右控制柄）：改变 Stage 宽度，横向拉伸轨道、音符和 Stage 图像，但不改变可见轨道长度。
- **纵向拉伸**（上、下控制柄）：改变可见轨道长度，但不会纵向缩放音符或其他 Stage 内容。
- **斜向拉伸**（四角控制柄）：等比缩放整个 Stage，包括轨道宽度、音符大小和 Stage 图像，同时保持其比例和
  显示的轨道内容范围不变。

Stage 皮肤组件是必需组件，因此不会出现在组件工具箱中。将其删除后，规则集会自动创建新的默认实例，并重置其
位置、大小和设置，包括判定线偏移。如果保存的布局中存在重复项，则只保留第一个实例。

<details>
<summary>示例</summary>

https://github.com/user-attachments/assets/7d88d698-1e06-4488-9b45-c9aa462adb64

</details>

### Text

以带深色背景的横幅显示简短游玩消息。谱面开始时会短暂显示 `Game Start`；游玩过程中，通道 `99` 事件会显示
对应的 `#TEXTxx` 内容；如果定义了 `#TEXT00`，出现 POOR 时还会将其显示为失误消息。在游戏内改变滚速时则会
显示新的倍速和 `>>` 或 `<<` 方向标记。每条消息都会在短暂显示后自动淡出。

此组件没有专属侧边栏设置。由于没有消息时组件通常完全透明，皮肤编辑器会改为显示完全可见的
`Sample Text Event` 占位内容，供用户定位和缩放横幅；正常游玩时不会显示该占位内容。

## 示例 skin.ini (7K)

```ini
[General]
Name : My BMS Skin
Author : yourname

[BMS]
Layout : 7K

HitPosition : 440
LightPosition : 440
ScorePosition : 250
JudgementLine : 1
KeysUnderNotes : 0

LightFramePerSecond : 40
ColumnWidth : 45,45,45,45,45,45,45,45
WidthForNoteHeightScale : 50
BarlineHeight : 1.2

ColourBarline : 0,255,0,255
ColourColumnLine : 255,255,255,50
ColourJudgementLine : 255,255,255,255
ColumnLineWidth : 0,1,1,1,1,1,1,1,0

Colour : 0,0,0,0
ColourLight : 0,0,0

; BMS 7K 列顺序: scratch even odd even center even odd even
NoteImage0 : note-scratch
NoteImage1 : note-even
NoteImage2 : note-odd
NoteImage3 : note-even
NoteImage4 : note-center
NoteImage5 : note-even
NoteImage6 : note-odd
NoteImage7 : note-even

NoteImageL : note-ln-body
NoteImageT : note-ln-tail
MineImage : note-mine

KeyImage0 : mania-keyS
KeyImage0D : mania-keySD
KeyImage1 : mania-key1
KeyImage1D : mania-key1D
KeyImage2 : mania-key2
KeyImage2D : mania-key2D
KeyImage3 : mania-key1
KeyImage3D : mania-key1D
KeyImage4 : mania-key2
KeyImage4D : mania-key2D
KeyImage5 : mania-key1
KeyImage5D : mania-key1D
KeyImage6 : mania-key2
KeyImage6D : mania-key2D
KeyImage7 : mania-key1
KeyImage7D : mania-key1D

StageHint : mania-stage-hint
StageLeft : stage-left
StageRight : stage-right
StageBottom : stage-bottom
StageLight : stage-light
LightingN : lightingN
LightingL : lightingL
LightingNWidth : 50,50,50,50,50,50,50,50
LightingLWidth : 50,50,50,50,50,50,50,50

HitPGreat : j-pgreat
HitGreat : j-great
HitGood : j-good
HitBad : j-bad
HitPoor : j-poor
```

完整内置皮肤（覆盖全部 6 种布局）位于
`osu.Game.Rulesets.BmsRuleset/Resources/Skins/Modern/skin.ini` — 可作为参考。
