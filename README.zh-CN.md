<h1 align="center">osu! BMS 规则集</h1>

<p align="center"><strong>在 osu!lazer 中原生游玩 BMS 系谱面</strong></p>

<p align="center">
  <a href="https://github.com/QingQiz/BmsRuleset/releases"><img alt="最新版本" src="https://img.shields.io/github/v/release/QingQiz/BmsRuleset?label=release&color=ff66aa"></a>
  <a href="./LICENSE"><img alt="许可证：AGPL-3.0" src="https://img.shields.io/github/license/QingQiz/BmsRuleset?color=5c7cfa"></a>
</p>

<p align="center">
  <a href="https://github.com/QingQiz/BmsRuleset/releases"><strong>下载</strong></a>
  · <a href="./docs/gameplay.zh-CN.md">游玩指南</a>
  · <a href="./docs/skin-system.zh-CN.md">皮肤指南</a>
  · <a href="./README.md">English</a>
</p>

在熟悉的 osu! 界面中体验传统 BMS 游玩：keysound 音频、BGA、原生判定、六种血量、难度表、Clear Lamp
以及详细的成绩分析。支持 `.bms`、`.bme`、`.bml` 和 `.pms` 谱面。

<p align="center">
  <img src="https://github.com/user-attachments/assets/816fb844-e684-48d0-8c37-0217fdd2d9b2" width="900" alt="在 osu!lazer 中原生运行的 BMS 游玩画面">
  <br>
  <sub>在 osu!lazer 中原生游玩 BMS，使用开发者的可自定义皮肤</sub>
</p>

## 安装

1. 从 [最新 Release](https://github.com/QingQiz/BmsRuleset/releases) 下载
   `osu.Game.Rulesets.BmsRuleset.dll`。选择 `xxxx.yyy` 与已安装 osu!lazer 版本相同的 Release。
2. 在 osu! 中打开**设置 → 常规 → 打开 osu! 文件夹**，将 DLL 放入 `rulesets` 文件夹；如果该文件夹
   不存在，请手动创建。
3. 重启 osu!，规则集选择器中会出现 **BMS**。

## 导入 BMS 谱面

1. 打开 osu! 设置并找到 **BMS** 栏目。
2. 选择**导入 BMS 文件**。
3. 进入包含 BMS 谱面的文件夹。
4. 导入所选谱面、当前文件夹中的全部谱面，或递归导入整个目录。
5. 等待谱面出现在选歌界面。

<p align="center">
  <img src="https://github.com/user-attachments/assets/62ff2f6d-a74a-4114-a90f-9b71d13713ca" width="900" alt="BMS 谱面导入界面">
  <br>
  <sub>选择单个谱面、导入当前文件夹，或递归扫描整个目录。</sub>
</p>

> [!IMPORTANT]
> osu! 数据库中仅导入谱面数据。音频、图片和视频仍保留在原始 BMS 文件夹中，并在游玩时从该位置加载。
> 导入后请勿移动或删除源文件夹。

如果源文件夹已被移动或删除，可在 BMS 设置中使用**清理失去源文件的 BMS 谱面集**。也可以删除所有已导入的
BMS 元数据，而不会影响原始文件。

## 亮点

从 BPM 和 STOP 变化，到长音符、地雷、随机分支、滚速变化、BGA 与 keysound，规则集围绕真正影响 BMS
游玩体验的功能构建。判定使用兼容 beatoraja 的时间窗口，在 osu! 标准化分数之外保留传统 EX Score。

规则集还补充了标准 osu! 没有的 BMS 工作流：根据谱面音频合成选歌预览、难度表收藏夹与标记、跟随 Mods
判断的 Clear Lamp，以及无需先观看 replay 就能直接查看的已保存成绩详细分析。

### osu!mania 皮肤支持

可以直接从正在使用的 osu!mania 皮肤开始，需要精细控制时添加专用 `[BMS]` 配置，也可以使用规则集内置皮肤。
规则集支持常见的单人和双人布局，包括 5 键、7 键与 9 键谱面。

以 BMS 7K（7 键加皿）为例，皮肤配置按以下顺序选择：

1. `Layout: 7K` 的 `[BMS]` 配置
2. `Keys: 8` 且 `SpecialStyle: 1` 的 `[Mania]` 配置
3. `Keys: 8` 的 `[Mania]` 配置
4. `Keys: 7` 的 `[Mania]` 配置
5. 规则集内置的回退皮肤

> [!NOTE]
> BMS 专用组件会与 osu!mania 皮肤中的组件同时显示，因此初始布局可能较为杂乱，例如出现两个偏移条。
> 请打开皮肤编辑器，重新排列或移除重叠的组件。

完整的 `skin.ini` 参考和各布局行为见[皮肤系统指南](./docs/skin-system.zh-CN.md)。

### 可视化编辑的皮肤组件

通过 osu! 的可视化皮肤编辑器，直接排列、缩放和配置游玩区域、Combo、判定显示、血条、歌曲进度、BGA、
谱面文字以及实时成绩对比。

<p align="center">
  <img src="https://github.com/user-attachments/assets/c3690c49-3a00-4970-abd8-0e93220d6f11" width="900" alt="osu! 可视化皮肤编辑器中的 BMS 组件">
  <br>
  <sub>用可视化方式构建游玩布局，无需手动填写每个组件的位置。</sub>
</p>

布局会按皮肤保存，不会修改谱面或 `skin.ini`。所有可编辑组件与选项见
[皮肤组件](./docs/skin-system.zh-CN.md#皮肤组件)。

### 歌曲预览

**即使没有预先渲染的音频文件，谱面也可以拥有选歌预览。** 规则集会依次尝试 `#PREVIEW`、`preview.*`，
最后直接根据谱面的 BGM 与 keysound 时间线合成预览。原本会在选歌界面保持静音的谱面也能在游玩前试听。

在 BMS 设置中关闭**使用专用预览音频**，可以始终使用 BGM 与 keysound 时间线。

### Clear Lamps

选歌界面会显示最匹配的本地最佳 Clear Lamp，从 No Play、Failed、Assist Easy、Easy、Normal、Hard 和
EX Hard Clear，一直到 Full Combo、Perfect 与 Max。降低难度时仍会显示在更困难条件下获得的 Lamp；提高难度时
则隐藏在更简单条件下获得的 Lamp。Lamp 筛选中只有 Double Time 会被视为提高难度。

### 难度表

**将 LR2/beatoraja 基于难度表发现谱面的方式直接带入 osu!。** 从 URL 或本地 JSON 文件导入难度表后，规则集会
通过 MD5 匹配谱面、在难度名中添加等级标记，并自动生成可浏览的收藏夹。Satellite、Stella、发狂 BMS、
Overjoy、Scramble 和 Luminous 等常用难度表可直接从预设中选择。

<p align="center">
  <img src="https://github.com/user-attachments/assets/29b2c98c-0293-4b39-8c08-7341108476d9" width="900" alt="选歌界面中的 Clear Lamp 和难度表标记">
  <br>
  <sub>Clear Lamp 与已导入的难度表标记会直接显示在选歌界面中。</sub>
</p>

远程难度表可以从来源更新。所有难度表都可在 BMS 设置中按等级拆分为多个收藏夹、重新合并或删除。完整操作见
[难度表指南](./docs/difficulty-tables.zh-CN.md)。

### 结算界面

**直接打开已保存的成绩即可查看详细结算分析，无需先播放 replay。** 详细的结算界面不只适用于刚刚结束的游玩，过去保存的成绩也可以随时直接查看。

除了标准 osu! 成绩摘要，结算界面还提供血量历史、音符与判定时间线、Fast/Late 分布、击打散点与偏移图，以及逐键位时机分析。

<p align="center">
  <img src="https://github.com/user-attachments/assets/38f62e99-8977-47eb-a0bc-d87837714905" width="900" alt="提供详细游玩分析的 BMS 结算界面">
  <br>
  <sub>从整体时机方向到单独键位，定位一次游玩中的失分原因。</sub>
</p>

## 详细文档

README 只保留上手所需内容，详细行为与技术参考见：

| 文档                                           | 内容                                                   |
|----------------------------------------------|------------------------------------------------------|
| [游玩机制与设置](./docs/gameplay.zh-CN.md)          | 判定、计分、血量、Mods、键位与设置                              |
| [难度表](./docs/difficulty-tables.zh-CN.md)         | 导入和管理 LR2/beatoraja 难度表                            |
| [皮肤系统](./docs/skin-system.zh-CN.md)             | `skin.ini` 参考、osu!mania 兼容与可编辑组件                   |
| [BMS 格式支持（英文）](./docs/bms-format-support.md) | Parser 行为、支持的命令与通道、参考资料                           |
| [开发（英文）](./docs/development.md)                | 从源码编译、缺失功能与已知问题                                    |
