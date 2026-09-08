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

支持 `.bms`、`.bme`、`.bml` 和 `.pms` 谱面，以及单人、双人布局。

<p align="center">
  <img src="https://github.com/user-attachments/assets/816fb844-e684-48d0-8c37-0217fdd2d9b2" width="900" alt="在 osu!lazer 中原生运行的 BMS 游玩画面">
  <br>
  <sub>使用开发者自定义皮肤的游玩画面</sub>
</p>

## 安装

1. 从 [最新 Release](https://github.com/QingQiz/BmsRuleset/releases) 下载
   `osu.Game.Rulesets.BmsRuleset.dll`。选择 `xxxx.yyy` 与已安装 osu!lazer 版本相同的 Release。
2. 在 osu! 中打开**设置 → 常规 → 打开 osu! 文件夹**，将 DLL 放入 `rulesets` 文件夹；如果该文件夹
   不存在，请手动创建。
3. 重启 osu!，规则集选择器中会出现 **BMS**。

## 导入 BMS 谱面

1. 打开**设置 → BMS → 导入 BMS 文件**。
2. 进入 BMS 文件夹，导入单个谱面、当前文件夹，或递归导入整个目录。
3. 导入的谱面会出现在选歌界面。

<p align="center">
  <img src="https://github.com/user-attachments/assets/62ff2f6d-a74a-4114-a90f-9b71d13713ca" width="900" alt="BMS 谱面导入界面">
  <br>
  <sub>BMS 谱面导入界面</sub>
</p>

> [!IMPORTANT]
> osu! 数据库中仅导入谱面数据，音频、图片和视频仍从原始 BMS 文件夹加载。
> 导入后请勿移动或删除源文件夹。

在 BMS 设置中使用**清理失去源文件的 BMS 谱面集**，可移除源文件已丢失的条目。
也可以删除所有已导入的 BMS 元数据，不影响原始文件。

## 亮点

支持 BPM/STOP 变化、长音符、地雷、随机分支、滚速变化、BGA 和 keysound。
提供兼容 beatoraja 的判定窗口、[四种判定算法](./docs/gameplay.zh-CN.md#判定选择算法)和六种血量类型，
同时显示传统 EX Score 与 osu! 标准化分数。

### osu!mania 皮肤支持

可直接使用 osu!mania 皮肤、添加专用 `[BMS]` 配置，或使用内置皮肤。
支持 5 键、7 键、9 键谱面及其双人布局。

> [!NOTE]
> BMS 与 osu!mania 皮肤组件可能重叠，例如同时出现两个误差条。可在皮肤编辑器中重新排列或移除。

配置优先级和 `skin.ini` 参考见[皮肤系统指南](./docs/skin-system.zh-CN.md)。

### 可视化编辑的皮肤组件

在 osu! 可视化皮肤编辑器中排列、缩放和配置游玩区域、BGA、血条、判定显示等组件。
布局按皮肤保存，不修改谱面或 `skin.ini`。

<p align="center">
  <img src="https://github.com/user-attachments/assets/c3690c49-3a00-4970-abd8-0e93220d6f11" width="900" alt="osu! 可视化皮肤编辑器中的 BMS 组件">
  <br>
  <sub>可视化皮肤编辑器中的 BMS 组件</sub>
</p>

完整组件列表与选项见[皮肤组件](./docs/skin-system.zh-CN.md#皮肤组件)。

### 歌曲预览

选歌预览优先使用 `#PREVIEW`，其次是 `preview.*`；两者都没有时，根据谱面的 BGM 和 keysound 合成音频。

在 BMS 设置中关闭**使用专用预览音频**，可始终使用合成预览。

### 视觉偏移校准

调整音符到达判定线的画面时机，不改变音频、判定或计分。可在 BMS 设置中手动或自动应用本地游玩生成的偏移建议。

**LN 尾视觉偏移**可让长音符尾部提前显示，使长音符看起来更短。详见[校准与设置](./docs/gameplay.zh-CN.md#设置)。

### BMS 帧率解锁

在 BMS 设置中开启**解除帧率上限**，可解除 BMS 游玩时渲染、更新与输入轮询的 1000 Hz 上限。
更大的 GC 和 GPU 压力可能造成卡顿，遇到时请关闭此选项。

### Clear Lamps

选歌界面显示与当前 Mods 匹配的本地最佳通关灯，从 No Play 到 Max。
详见[通关灯类型与筛选规则](./docs/gameplay.zh-CN.md#灯lamp)。

### 难度表

从 URL 或本地 JSON 文件导入 LR2/beatoraja 难度表，自动添加等级标记和收藏夹。
预设包含 Satellite、Stella、发狂 BMS、Overjoy、Scramble 和 Luminous 等常用表。

表中尚未导入本地曲库的谱面也会显示在选歌界面；尝试游玩时，若难度表提供了下载链接，会打开对应页面。

<p align="center">
  <img src="https://github.com/user-attachments/assets/29b2c98c-0293-4b39-8c08-7341108476d9" width="900" alt="选歌界面中的 Clear Lamp 和难度表标记">
  <br>
  <sub>选歌界面中的通关灯与难度表等级</sub>
</p>

在 BMS 设置中管理难度表，导入、更新和收藏夹选项见[难度表指南](./docs/difficulty-tables.zh-CN.md)。

### 段位模式

游玩从难度表导入的段位，使用段位血条并遵循表中定义的约束。
成绩保存在本地，显示在选歌界面的段位卡片中。

段位须备齐所有曲目，游玩中不能暂停；曲目间最多停留 99 秒，之后自动开始下一首。
详见[段位规则与缺失谱面处理](./docs/difficulty-tables.zh-CN.md#段位)。

### 结算界面

已保存的成绩可直接查看详细分析，无需播放回放。分析内容包括血量历史、音符与判定时间线、Fast/Slow 分布、
击打散点与偏移图，以及逐键位时机。

<p align="center">
  <img src="https://github.com/user-attachments/assets/54c03242-a091-4824-9b7e-a6c8ffaa44b0" width="900" alt="提供详细游玩分析的 BMS 结算界面">
  <br>
  <sub>血量历史与击打时机分析</sub>
</p>

<p align="center">
  <img src="https://github.com/user-attachments/assets/33c14f89-29a9-42dc-a09f-1224563274d2" width="900" alt="BMS 段位模式结算界面">
  <br>
  <sub>查看段位汇总，或点击曲目卡片查看单曲统计</sub>
</p>

### osu!mania 7K 转谱

osu!mania 7K 谱面可使用 BMS 游玩，7 条轨道映射到 BME 键盘列，皿列留空。
音符时间、长音符、BPM、拍号、偏移和滚速变化均会保留，暂不支持其他键数。

选择 BMS，在选歌界面的筛选面板中开启**显示转谱**即可。

## 详细文档

| 文档                                           | 内容                                                   |
|----------------------------------------------|------------------------------------------------------|
| [游玩机制与设置](./docs/gameplay.zh-CN.md)          | 判定、计分、血量、Mods、键位与设置                              |
| [难度表](./docs/difficulty-tables.zh-CN.md)         | 导入和管理 LR2/beatoraja 难度表                            |
| [皮肤系统](./docs/skin-system.zh-CN.md)             | `skin.ini` 参考、osu!mania 兼容与可编辑组件                   |
| [BMS 格式支持（英文）](./docs/bms-format-support.md) | Parser 行为、支持的命令与通道、参考资料                           |
| [开发（英文）](./docs/development.md)                | 从源码编译、缺失功能与已知问题                                    |
