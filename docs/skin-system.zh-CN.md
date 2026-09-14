# 皮肤系统

[返回 README](../README.zh-CN.md) | [English](./skin-system.md)

## 配置优先级

以 BMS 7K（7 键加皿）为例，皮肤配置按以下顺序选择：

1. `Layout: 7K` 的 `[BMS]` 配置
2. `Keys: 8` 且 `SpecialStyle: 1` 的 `[Mania]` 配置
3. `Keys: 8` 的 `[Mania]` 配置
4. `Keys: 7` 的 `[Mania]` 配置
5. 规则集内置的回退皮肤

## 制作皮肤

将图片和 `skin.ini` 放入文件夹中，然后作为普通 osu! 皮肤导入。

### skin.ini — `[BMS]` 段

每种布局使用独立的 `[BMS]` 段，必须填写 `Layout:`。

**布局值：** `5K`、`7K`、`9K`、`10K`、`14K`、`18K`
（别名：`BMS5K`、`BME7K`、`PMS9K`、`BMS5KDouble`、`BME7KDouble`、`PMS9KDouble`）

### 所有支持的键

**布局与位置：**

| 键               | 说明                  | 示例    |
|-----------------|---------------------|-------|
| `Layout`        | 布局标识（必填）            | `7K`  |
| `HitPosition`   | 判定位置距底部的 Y 坐标（基准高度 480） | `440` |
| `LightPosition` | 列键灯 Y               | `440` |
| `ScorePosition` | 判定弹出 Y              | `250` |
| `ComboPosition` | 连击数字距顶部 Y           | `300` |
| `JudgementLine` | 在判定位置显示白线（`1`/`0`）  | `1`   |
| `KeysUnderNotes`| 按键图像绘制在音符下方（`1`/`0`） | `0`   |

**轨道尺寸与间距：**

| 键                         | 说明              | 示例                        |
|---------------------------|-----------------|---------------------------|
| `ColumnWidth`             | 所有 N 列的逗号分隔宽度   | `45,45,45,45,45,45,45,45` |
| `ColumnLineWidth`         | 分隔线宽度 — N+1 个值  | `0,1,1,1,1,1,1,1,0`       |
| `ColumnSpacing`           | 列间距             | `2,2,2,2,2,2,2`           |
| `WidthForNoteHeightScale` | 音符高度缩放参考宽度      | `50`                      |
| `BarlineHeight`           | 小节线高度乘数（每小节前的线） | `1.2`                     |

> **皿轨说明**
>
> 无论显示在哪一侧，列索引 0 **始终**是皿轨。
>
> - `ColumnWidth` — 第一个值为皿轨宽度
> - `NoteImage0` / `KeyImage0` — 引用皿轨
>
> 使用 **2P Mod**（SecondPlayer）时，皿轨移至最右侧，配置会自动适配，无需另写 `[BMS]` 段。
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

**音符图像：**

| 键                                           | 说明                        |
|---------------------------------------------|---------------------------|
| `NoteImage0`–`NoteImageN`                   | 逐列普通音符图像                  |
| `NoteImage0L`–`NoteImageNL`（或 `NoteImageL`） | 逐列 LN 主体图像                |
| `NoteImage0T`–`NoteImageNT`（或 `NoteImageT`） | 逐列 LN 尾部图像                |
| `NoteImage0H`–`NoteImageNH`                 | 逐列 LN 头部（回退到 `NoteImage`） |
| `MineImage` / `MineImage0`–`MineImageN`     | 地雷图像                      |

**按键图像：**

| 键                         | 说明        |
|---------------------------|-----------|
| `KeyImage0`–`KeyImageN`   | 逐列按键（未按下） |
| `KeyImage0D`–`KeyImageND` | 逐列按键（按下）  |

**Stage 与特效：**

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

在纹理名后追加 `-0`、`-1`、`-2`、…，即可提供多帧动画：

```
lightingN-0.png
lightingN-1.png
lightingN-2.png
```

`StageLight` 按 `LightFramePerSecond` 指定的帧率播放；`LightingN`/`LightingL` 根据帧数计算每帧时长。
无后缀的单个文件（如 `lightingN.png`）显示为静态图像。

适用于任何图像键：`NoteImage`、`KeyImage`/`KeyImageD`、`StageLight`、`LightingN`、
`LightingL`、`StageHint`、判定图像等。

### 图片资源缺失

未填写配置键时才使用默认图片名。已指定名称时，规则集会在当前皮肤及其回退皮肤中查找同名资源；
若均未找到，则不绘制图片，也不尝试默认名称。

例如，未配置 `KeyImage1` 时使用 `mania-key1`，可从回退皮肤加载；配置为 `KeyImage1: custom-key` 后，
若缺少 `custom-key`，按键未按下时保持空白，不会改用 `mania-key1`。

LN 尾部依次回退到头部、普通音符，头部回退到普通音符，与 osu!mania 一致。
当前皮肤无法提供判定组件时，可由优先级更低的皮肤提供整个组件。

### osu!mania 皮肤兼容

`[Mania]` 配置按以下顺序匹配：

- `SpecialStyle: 1`，且按键数包含皿轨（`6`、`8`、`12`、`16`）
- 按键数包含皿轨（`6`、`8`、`12`、`16`）
- 按键数与不含皿轨的键数一致

在 `[Mania]` 段中使用 osu!mania 标准判定名称：`Hit300g`（PGREAT）、`Hit300`（GREAT）、`Hit200`（GOOD）、
`Hit50`（BAD）、`Hit0`（POOR）。

## 皮肤组件

游玩时打开**皮肤编辑器**，可添加、删除、移动和缩放组件；选中组件可在侧边栏调整设置。
位置、大小和设置保存在当前皮肤的 BMS 布局中，再次使用该皮肤时自动加载，不修改谱面文件或 `skin.ini`。

移动和缩放仅影响外观；特殊的缩放规则见下文。

### Combo

使用 `ComboPrefix` 指定的数字纹理显示连击数，默认前缀为 `score`。连击增加时播放动画，断连时以 `ColourBreak`
指定的颜色闪烁。缺少数字纹理时隐藏。

- **自动隐藏延迟**：距上次连击增加多久后隐藏，每次增加连击重新计时。范围 -1 至 100 秒，默认 3 秒；-1 禁用自动隐藏。
- **最低可见连击数**：达到该值才显示，低于时立即隐藏。范围 0 至 100，默认 10。

### Judgement

显示最新的 PGREAT、GREAT、GOOD、BAD 或 POOR/E-POOR，替换上一结果并重新播放动画。
使用 `[BMS]` 的 `HitPGreat` 至 `HitPoor` 图像，或上文列出的 osu!mania 对应图像。

- **显示 E-POOR**：控制是否在弹窗中显示空 POOR 判定，默认开启。

图案和动画通过皮肤图像文件修改。

### 误差条

横向显示击打误差：左侧 Fast，右侧 Slow，白色 0 ms 标记居中。两侧等宽，不对称判定窗口在较短一侧留空。

判定窗口组成连续色条。E-POOR 在 Fast BAD 外增加灰色区段，不压缩其他区段；POOR 没有有限的 Slow 边界，显示在最右端。

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

游玩时将当前 EX Score 与两个参照成绩比较：

- **个人最好**：使用当前或更难 Mods 取得的最高已保存 EX Score。有回放判定数据时，会还原其成绩进度。
- **目标**：比个人最好高一级所需的最低 EX Score，依次为 C、B、A、AA、AAA、S；已达 S 时仍以 S 为目标。

图中显示等级分界线、三根实时成绩柱、与个人最好及目标的差值，以及 PGREAT 至 E-POOR 的判定数量对比。
缺少回放判定数据时，个人最好的判定数量显示为不可用。

- **当前成绩颜色**：控制当前 EX Score 柱和当前成绩的强调色。
- **个人最好颜色**：控制个人最好成绩柱、最终成绩虚影和相应强调色。
- **目标颜色**：控制目标成绩柱、最终成绩虚影和相应强调色。
- **显示成绩柱**、**显示成绩差**和**显示判定对比**：分别开关三个区域，至少保留一个可见区域。

组件有最小宽度限制，高度须容纳所有可见区域。

<details>
<summary>示例</summary>

![](https://github.com/user-attachments/assets/ab14a6e8-e853-42ed-ae08-61cc4427e667)

</details>

### 血条

血条从下向上填充。Assist Easy、Easy 和 Normal 显示通关线，并在红血区和通关阈值处变色。
红血区在 20% 结束；Assist Easy 的通关阈值为 60%，Easy 和 Normal 为 80%。

- **Groove 血条低血量颜色**：用于低于红血区阈值的部分。
- **Groove 血条中等血量颜色**：用于红血区阈值至通关阈值之间。
- **Groove 血条高血量颜色**：用于达到或超过通关阈值时。
- **Hard、ExHard 和 Hazard 血条填充颜色**：分别设置这三种生存型血条使用的单一填充颜色。

Class、ExClass、ExHard Class 分别使用 Hard、ExHard、Hazard 的填充颜色。

### 歌曲进度

用发光指示块显示播放进度：前奏时停在顶部，进入可游玩部分后向下移动。
轨道默认位于 Stage 左侧，其高度决定指示块的移动距离。

**指示块颜色**同时改变指示块及其光晕。

### BGA

显示谱面的 Base、Layer 1、Layer 2 和 POOR 图层，应用裁剪和透明度事件。判定后 Combo 为 0 时，按谱面的 POOR BGA 模式显示 POOR 图层 500 ms，包括 BAD、POOR，以及 Combo 已为 0 时的 E-POOR。每次触发都会重新计时。

BGA 等比缩放以适应组件窗口，位于游玩区域后方，隐藏游玩 HUD 时仍会显示。

- **铺满屏幕**：让组件覆盖整个 HUD 区域，默认布局会启用此选项。移动、调整大小或旋转组件时会自动关闭该选项，
  并保留编辑后的 BGA 窗口。

规则集设置中的 **BGA 暗化**对所有组件布局生效。

### Stage

移动 Stage 会带动轨道、音符、小节线、按键区、判定线、命中特效和皮肤图像。

- **判定线偏移**：相对皮肤中的位置移动判定线，正值向上，负值向下，范围限制在可见 Stage 内。不改变判定时机。
- **音符高度缩放**：将音符头尾高度缩放至 0.01x 到 5x，默认 1x。不影响音符位置、LN 主体长度或其他 Stage 元素。

Stage 缩放控制柄有三种功能：

- **横向拉伸**（左、右控制柄）：改变 Stage 宽度，横向拉伸轨道、音符和 Stage 图像，但不改变可见轨道长度。
- **纵向拉伸**（上、下控制柄）：改变可见轨道长度，但不会纵向缩放音符或其他 Stage 内容。
- **斜向拉伸**（四角控制柄）：等比缩放整个 Stage，包括轨道宽度、音符大小和 Stage 图像，同时保持其比例和
  显示的轨道内容范围不变。

Stage 是必需组件，不在工具箱中列出。删除后会自动创建默认实例，重置位置、大小和设置（包括判定线偏移）。
保存的布局中若有多个 Stage，只保留第一个。

<details>
<summary>示例</summary>

https://github.com/user-attachments/assets/7d88d698-1e06-4488-9b45-c9aa462adb64

</details>

### Text

以深色横幅显示 `Game Start`、通道 `99` 对应的 `#TEXTxx` 消息，以及 POOR 时的 `#TEXT00`（若有定义）。
滚速变化时显示新倍速及 `>>` 或 `<<` 方向标记。消息短暂显示后自动淡出。

此组件没有侧边栏设置。横幅在消息间隔中透明，编辑器会用 `Sample Text Event` 占位，便于定位和缩放；游玩时不显示占位内容。

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
