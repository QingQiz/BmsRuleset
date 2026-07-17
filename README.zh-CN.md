# osu! BMS 规则集

osu! 原生 BMS 规则集插件，支持 `.bms`、`.bme`、`.bml`、`.pms` 谱面格式。

---

[English](./README.md)

## 安装

### 从 Release 安装

1. 从 [Releases](https://github.com/QingQiz/BmsRuleset/releases) 的附件中下载
   `osu.Game.Rulesets.BmsRuleset.dll`。Release 标题通常为 `xxxx.yyy.z` 格式，请选择 `xxxx.yyy` 与你安装的
   osu!lazer 版本相同的 Release；也可以通过 Release 中的 **Compatibility** 说明确认目标版本。
2. 在 osu! 中点击**设置 → 常规 → 打开 osu! 文件夹**，将 DLL 放入其中的 `rulesets` 子文件夹；如果该文件夹
   不存在，请手动创建。
3. 重启 osu!，规则集会出现在规则集选择器中。

### 手动编译

<details>
<summary>点击展开</summary>

安装 [Git](https://git-scm.com/) 和 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，然后将本仓库
和 osu! 克隆到任意工作目录下的两个同级文件夹。以下 sparse checkout 会跳过体积较大的测试歌曲文件夹：

```bash
git clone --filter=blob:none --sparse https://github.com/QingQiz/BmsRuleset.git
git -C BmsRuleset sparse-checkout set --no-cone '/*' '!bms_test_songs'
git clone --filter=blob:none https://github.com/ppy/osu.git
```

查看 `BmsRuleset/Directory.Build.props` 中的 `OsuBase`，然后切换到对应的 osu! 标签。例如 `OsuBase` 为
`2026.711` 时：

```bash
git -C osu checkout 2026.711.0-lazer
```

项目默认启用 `UseLocalOsu`，会自动引用同级的 `osu` checkout。这是因为 ppy 发布 osu!lazer 时，往往不会同步更新
`ppy.osu.Game` NuGet 包。使用匹配标签的本地 osu! 源码可以确保引用的 API 和 osu! 版本一致，而无需等待 NuGet
包更新。

使用 Release 配置编译规则集：

```bash
cd BmsRuleset
dotnet build osu.Game.Rulesets.BmsRuleset -c Release
```

输出文件位于 `osu.Game.Rulesets.BmsRuleset/bin/Release/net8.0/osu.Game.Rulesets.BmsRuleset.dll`。按照上面的安装
步骤放入 osu! 的 `rulesets` 文件夹即可。

</details>

---

## 导入 BMS 谱面

1. 打开 osu! → **设置** → 找到 **BMS** 栏目。
2. 点击 **"Import BMS files"** 打开导入界面。
3. 选择你的 BMS 文件夹。文件夹中的每个谱面文件（`.bms`/`.bme`/`.bml`/`.pms`）都会成为一个独立的难度；
   整个文件夹成为一个谱面集。
4. **谱面数据库中仅储存 chart 文件。** 音频和图像资源保留在原始文件系统中，游戏时通过 `Metadata.Source`
   记录的谱面目录路径直接读取。导入后请勿移动或删除原始 BMS 文件夹 — 可在设置界面中清理孤儿谱面集（见下文）。

要删除所有已导入的 BMS 内容，使用同一设置栏目中的 **"Delete all imported BMS files"** 按钮。此操作仅移除 osu!
中的谱面元数据，不会影响原始 BMS 文件夹。

要清理源目录已被移动或删除的谱面，点击同一设置栏目中的 **"Clean up orphaned BMS sets"**。此操作会扫描所有源目录已不存在的
BMS 谱面并将其标记为删除。

---

## 亮点

### osu!mania 皮肤支持

本插件支持标准 osu!mania 皮肤。如果你是 mania 玩家，规则集通常会直接渲染你当前使用的 mania 皮肤。以 BMS 7K
（7 键加皿）为例，皮肤配置按以下顺序选择：

1. `Layout: 7K` 的 `[BMS]` 配置
2. `Keys: 8` 且 `SpecialStyle: 1` 的 `[Mania]` 配置
3. `Keys: 8` `[Mania]` 配置
4. `Keys: 7` 的 `[Mania]` 配置
5. 规则集内置的回退皮肤

> [!NOTE]
> 由于 BMS 专用皮肤组件会与 mania 皮肤中的组件一同显示，初始布局可能会有些杂乱，例如出现两个偏移条。请打开
> 皮肤编辑器，拖拽组件并调整布局。

---

### 可视化编辑的皮肤组件

本插件可以让你直接对游玩画面中的以下内容进行可视化编辑：

- 显示当前连击数的数字；
- 每次击打后出现的判定；
- 显示距离过关或失败还有多远的血量槽；
- 显示歌曲当前播放位置的指示块；
- 谱面指定在轨道后方播放的图片和视频；
- 将当前 EX 分数和判定数量与个人最佳成绩及目标进行对比的面板；
- 包含轨道、音符和判定线的整个游玩区域；
- 谱面指定在游玩过程中显示的文字消息。

你可以直接在 osu! 皮肤编辑器中添加、删除、排列、缩放和配置这些内容，无需编写繁琐的 `skin.ini`。详细的编辑
选项见 [皮肤组件](#皮肤组件)。

---

### 歌曲预览

选歌界面的预览音频从原始 BMS 文件夹生成，而不是从已存入 osu! 的音频文件读取。规则集会先尝试声明的
`#PREVIEW` 文件，然后尝试谱面文件夹中的 `preview.*` 文件，最后回退到谱面的 BGM/keysound 事件时间线。
因此，即使谱面没有专用预览文件，在选歌界面也仍然可以听到预览。在 BMS 设置中关闭 **Use dedicated preview audio**
即可始终使用 BGM/keysound samples 合成预览，并完全跳过专用预览文件的加载。

---

### Clear Lamps

BMS 谱面面板会根据最匹配的本地最好成绩显示 clear lamp。Lamp 覆盖常见 BMS 结果状态：
No Play、Failed、Assist Clear、Easy Clear、Clear、Hard Clear、EX Hard Clear、Full Combo、Perfect 和 Max。

Lamp 会跟随当前选择的 mod：降低难度时仍显示高难度条件下取得的 lamp；提高难度时不显示低难度条件下取得的
lamp。Lamp 筛选中只有 Double Time 算提高难度。

<details>
<summary>示例</summary>

https://github.com/user-attachments/assets/a89deadb-bde8-4e50-9d04-0aaaf65a65b9

</details>

---

### 结算界面

结算界面提供针对 BMS 的分析面板，包括血量历史、判定时间线、击打散点和击打偏移。散点与偏移视图还可按键位
分别查看，便于发现整体时机偏差和容易失误的列。已保存成绩的统计可直接查看，无需先播放 replay。

<details>
<summary>示例</summary>

https://github.com/user-attachments/assets/df369f72-a4a6-4017-9f9c-80501f0037a6

</details>

---

## 输入与键位

默认键位（可在 **设置 → Key Bindings → osu!BMS** 中重新绑定）：

**游戏中控制：** 上/下 箭头临时调整滚动速度

每次按下按键时，无论判定结果如何，都会播放对应列中下一个音符的 key sound。

---

## 判定与计分

**判定等级（beatoraja 判定窗口，由 `#RANK` / EXRANK 决定）：**

| 名称         | EX 分 | 连击      | RANK 2 (Normal) 窗口    |
|------------|------|---------|-----------------------|
| **PGREAT** | 2    | 保持      | ±15 ms                |
| **GREAT**  | 1    | 保持      | ±45 ms                |
| **GOOD**   | 0    | 保持      | ±112.5 ms             |
| **BAD**    | 0    | 重置      | -220ms / +280ms       |
| **POOR**   | 0    | 重置      | > +280ms              |
| **E-POOR** | 0    | **不中断** | [-500ms,-220ms]，不消耗音符 |

`#RANK` 0 = Very Hard (±5/15/37.5 ms，BAD -220/+280 ms) → 4 = Very Easy (±25/75/187.5 ms，BAD -220/+280 ms)。
`#DEFEXRANK` 和不带编号的 `#EXRANK` 会以百分比设置初始判定窗口宽度，`100` 等同 Normal（`#RANK 2`）。
带编号的 `#EXRANKxx` 可由 `A0` 通道在谱面中途切换；未定义的 `A0` 引用会保持当前判定窗口不变。

**分数：** `总 EX 分 / 最大 EX 分 × 1,000,000`

**DJ LEVEL 评级：** X（全 PGREAT）· S ≥ 8/9 · A ≥ 7/9 · B ≥ 6/9 · C ≥ 5/9 · D 其他

---

## 血量

规则集实现了 6 种可选 BMS 血量类型，涵盖 Groove 和 Survival 两种模式。
默认使用 Normal 血量。通过 Mod 选择不同血量类型：

> [!NOTE]
> 血量数值和算法改编自 **beatoraja**（SEVENKEYS 模式），而 beatoraja 本身是对 LR2 groove gauge 的复现。
> Survival 血量（H1/H2）的 `(2 × #TOTAL − 320) / notes` recovery 缩放系数遵循 beatoraja 的 `LIMIT_INCREMENT` 修饰器。
> 地雷伤害使用 BMS 规范公式。

| Mod         | 缩写     | 类型   | 算法              | 初始 HP | 通关    | 血条         |
|-------------|--------|------|-----------------|-------|-------|------------|
| Assist Easy | **E2** | 难度降低 | TOTAL (#TOTAL)  | 20%   | ≥ 60% | Groove（动态） |
| Easy        | **E1** | 难度降低 | TOTAL (#TOTAL)  | 20%   | ≥ 80% | Groove（动态） |
| Normal      | *(默认)* | —    | TOTAL (#TOTAL)  | 20%   | ≥ 80% | Groove（动态） |
| Hard        | **H1** | 难度增加 | Limit Increment | 100%  | 存活    | 固定红色       |
| EX Hard     | **H2** | 难度增加 | Limit Increment | 100%  | 存活    | 固定紫色       |
| Hazard      | **H3** | 难度增加 | Fixed           | 100%  | 存活    | 固定金色       |

- **Groove 血量**（E2/E1/Normal）：可回复，从 20% 开始，结束时需达到通关线。血条颜色按阈值动态变化：红（< 20%）→ 黄（< 通关线）→
  绿（≥ 通关线）。
- **Survival 血量**（H1/H2/H3）：满血开局，仅扣血（H3 无回复）。血条使用固定颜色，无 clear 线。通关条件仅为存活（HP 从未归零）。
- Hard（H1）有 **guts 保护**：低血量时减少伤害（50% → ×0.8, 40% → ×0.7, …, 10% → ×0.4）。
- `#TOTAL` 控制 TOTAL 算法血量的最大回复速度。默认公式：`max(7.605 × N / (0.01 × N + 6.5), 160)`（LR2 公式，N = 总可玩音符数）。
- 地雷伤害：36进制值 ÷ 2 百分比（例如 `ZZ` = 647.5% → 直接清空）。
- 各血量 Mod 互相排斥。
- **Auto Gauge (AG)**：将六种血量按最难优先串联（Hazard → EX Hard → Hard → Normal → Easy → Assist Easy）。从最难档位开局；HP
  归零时当前档位降级到下一档并继续游戏——只有所有档位都耗尽才会失败。最终成绩按所达到的最难档位归属。

---

## Mods

| Mod                      | 说明                         |     |
|--------------------------|----------------------------|-----|
| Autoplay                 | 自动播放                       |     |
| Double Time / Half Time  |                            | |
| No Fail                  |                            |     |
| Mirror                   | 镜像键位布局                     |     |
| 2P                       | 将玩家布局从 1P 切换为 2P           |     |
| Auto Scratch (AS)        | 自动播放 皿                     |     |
| Hide Scratch (HS)        | 删除 皿 列的 note，并隐藏该列         |     |
| Background Keysound (BK) | 移除按键音，把它们当作背景音播放           |     |
| Lane Random (LR)         | RANDOM：随机排列轨道列             |     |
| Note Random (NR)         | S-RANDOM / H-RANDOM：逐音符随机  |     |
| Rotation Random (RR)     | R-RANDOM：旋转 + 可选镜像         |     |
| Assist Easy Gauge (E2)   | 使用 Assist Easy BMS 血量      |     |
| Easy Gauge (E1)          | 使用 Easy BMS 血量             |     |
| Hard Gauge (H1)          | 使用 Hard BMS 血量             |     |
| EX Hard Gauge (H2)       | 使用 EX Hard BMS 血量          |     |
| Hazard Gauge (H3)        | 使用 Hazard BMS 血量           |     |
| Auto Gauge (AG)          | 从最难血量起；失败时降一档              |     |
| Long Note (L1)           | LN 判定：LN 模式：尾部单一判定         |     |
| Charge Note (L2)         | LN 判定：CN 模式：头尾各自独立判分       |     |
| Hell Charge Note (L3)    | LN 判定：HCN 模式：CN + 持续身体血量流失 |     |

---

## 设置

| 设置                   | 默认值 | 范围       | 说明                                                                     |
|----------------------|-----|----------|------------------------------------------------------------------------|
| Scroll Speed         | 8.0 | 1.0–60.0 | 音符下落速度。游戏中使用 `Up`/`Down` 键临时调整。按键可在 设置 → Key Bindings → osu!BMS 中重新绑定。 |
| Reference BPM        | Main BPM | 枚举       | 当谱面未声明 `#BASEBPM` 时使用的滚动速度参考 BPM：Start BPM、Max BPM、Main BPM 或 Min BPM。Main BPM 取可玩音符数量最多的 BPM；并列时取最早出现者。 |
| BGA Dim              | 0.7 | 0–1      | BGA暗化。0 = 全亮度，1 = 隐藏（BGA 仍存在，仅不可见）                                     |
| Show 5K / 7K / 9K    | ✓   | 开关       | 切换选歌界面中单人布局的显示                                                         |
| Show DP 5K / 7K / 9K | ✓   | 开关       | 切换选歌界面中双人布局的显示                                                         |

布局可见性过滤在选歌界面实时生效 — 取消勾选某个布局可隐藏该类型的所有谱面。

你也可以在搜索框中使用 `k=`、`key=` 或 `keys=` 按键数过滤（支持 `=`、`!=`、`<`、`<=`、`>`、`>=` 运算符和逗号分隔值，例如
`keys=7` 或 `k>5`）。

---

## 难度表

难度表（LR2/beatoraja 格式）为 BMS 谱面提供难度评级和标记。规则集支持从 JSON 文件或 URL 导入难度表，
并通过 MD5 哈希自动匹配谱面。

### 预设表

以下知名难度表可在自动完成下拉菜单中一键选择：

| 表                     | 标记 | URL                                                    |
|-----------------------|----|--------------------------------------------------------|
| Satellite (sl)        | sl | `http://zris.work/bmstable/satellite/header.json`      |
| Stella (st)           | st | `http://zris.work/bmstable/stella/header.json`         |
| 発狂BMS難易度表 (★)         | ★  | `http://zris.work/bmstable/insane/insane_header.json`  |
| 通常難易度表 (☆)            | ☆  | `http://zris.work/bmstable/normal/normal_header.json`  |
| NEW GENERATION 発狂 (▼) | ▼  | `http://zris.work/bmstable/insane2/insane_header.json` |
| 第三期Overjoy (★★)       | ★★ | `http://zris.work/bmstable/overjoy/header.json`        |
| Scramble (SB)         | SB | `http://zris.work/bmstable/scramble/header.json`       |
| Luminous (ln)         | ln | `http://zris.work/bmstable/luminous/header.json`       |
| BMS図書館 (T)            | T  | `http://zris.work/bmstable/turbow/header.json`         |

### 导入难度表

1. 打开 **设置 → BMS** → 滚动到 **Difficulty Tables**。
2. 在文本框中粘贴 URL（例如 `http://zris.work/bmstable/turbow/header.json` ）或本地文件路径。
3. 点击 **Import**（或按 Enter）。

导入器支持三种 JSON 格式：

- **分离文件** — `header.json` + `data.json`，通过 `data_url` 链接
- **合并文件** — 单个 JSON，同时包含 header 字段（名称、符号、level_order）和 `"charts": [...]`
- **HTML 页面** — 包含 `<meta name="bmstable" content="URL">` 指向 JSON 的网页

### 标记

导入后，MD5 匹配的谱面难度名后会自动追加标记：

```
DP ☆NOTHER [TT★1 TT★2]
```

标记显示表符号和条目等级。添加或删除表时标记会自动更新。

> [!IMPORTANT]
> 不要在**选歌界面**添加或删除难度表。删除表会触发所有 BMS 谱面的完整标记重建，
> 与谱面轮播的活跃 Realm 读取竞争，导致 UI 冻结。请在导入或删除难度表之前先切换到**主菜单**。

### 收藏夹

每个表同时创建一个名为 `[BMS] {表名}` 的 **BeatmapCollection**，包含所有匹配的谱面。可以直接在选歌界面的收藏夹列表中浏览。
（收藏夹名底层使用了不可见字符，不必担心与你自己的收藏夹撞名。）

### 表格行显示

每个已导入的表都以可展开表项显示在设置列表中。悬停表项可查看每个等级的谱面数量。

点击表项可显示可用操作。远程表提供**拆分/合并**、**更新**和**删除难度表**；本地表不提供**更新**。
执行操作后这些操作仍保持可见，难度表列表重建时也会保留展开状态。

### 拆分

展开表项后点击**拆分**，可将表的收藏夹拆分为每级独立且带序号的收藏夹（例如 `[BMS] Table [00] ★1`、`[BMS] Table [01] ★2`）。
序号位数根据等级数量自适应（&lt;10 级用 1 位，&lt;100 级用 2 位，依此类推）。
点击**合并**可恢复为单个收藏夹。

### 更新

远程表提供**更新**操作，可从原始来源 URL 重新导入难度表。

### 删除

点击**删除难度表**并在对话框中确认，可删除该难度表、对应标记及自动生成的收藏夹。

---

## 皮肤系统

皮肤配置的选择顺序见上文的 [osu!mania 皮肤支持](#osumania-皮肤支持)。

### 制作皮肤

将图片和 `skin.ini` 放入文件夹中，然后作为普通 osu! 皮肤导入。

#### skin.ini — `[BMS]` 段

为每个你想支持的布局编写一个 `[BMS]` 段。`Layout:` 键为必填。

**布局值：** `5K`、`7K`、`9K`、`10K`、`14K`、`18K`
（别名：`BMS5K`、`BME7K`、`PMS9K`、`BMS5KDouble`、`BME7KDouble`、`PMS9KDouble`）

#### 所有支持的键

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

#### 帧动画（N 后缀）

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

#### osu!mania 皮肤兼容

你也可以使用标准 osu!mania 皮肤的 `[Mania]` 段。规则集会匹配：

- `[Mania]` 带有 `SpecialStyle: 1` 和包含 scratch 的按键数（`6`、`8`、`12`、`16`）
- `[Mania]` 带有包含 scratch 的按键数（`6`、`8`、`12`、`16`），优先于精确 key-only 回退
- `[Mania]` 带有精确的按键数

在 `[Mania]` 段中，使用 mania 标准判定名称：`Hit300g`（PGREAT）、`Hit300`（GREAT）、`Hit200`（GOOD）、
`Hit50`（BAD）、`Hit0`（POOR）。

### 皮肤组件

皮肤组件可在游戏内通过**皮肤编辑器**自由拖拽位置和调整大小。在游戏中打开皮肤编辑器，拖动任意组件到合适位置。

下次游玩时自动加载保存的布局。

此外，在皮肤编辑器中点击组件，可在侧边栏中配置以下属性：

#### Combo

可配置自动隐藏延迟和最低显示连击数。

#### Judgement

没有组件专属属性。

#### 成绩图

可配置当前成绩、个人最好成绩和目标 EX 分数所使用的颜色。成绩柱、成绩差以及当前成绩与个人最好成绩的判定数量
对比均可独立显示或隐藏。

#### 血条

**Groove 三段颜色**可配置低血量区（红）、中血量区（黄）和高血量区（绿）。**固定颜色模式**可分别配置 Hard、
ExHard 和 Hazard 的颜色。所有颜色均使用侧边栏中的颜色选择器。

#### 歌曲进度

**指示块颜色**控制用于显示当前播放位置的发光竖向标记。该组件默认贴在 Stage 左边缘，也可在皮肤编辑器中重新定位。

#### BGA

没有组件专属属性。该组件在 playfield 后方渲染，并固定使用 aspect-fit（letterbox）模式。BGA dim 是全局设置，
而非组件属性。

#### Stage

可配置判定线偏移和音符高度缩放。游玩区域的 Stage 已集成 osu! 的游戏内皮肤编辑器，可直接在画面中调整位置和
判定线。不同方向的缩放控制柄具有不同作用：

- **横向拉伸**（左、右控制柄）：改变 Stage 宽度，横向拉伸轨道、音符和 Stage 图像，但不改变可见轨道长度。
- **纵向拉伸**（上、下控制柄）：改变可见轨道长度，但不会纵向缩放音符或其他 Stage 内容。
- **斜向拉伸**（四角控制柄）：等比缩放整个 Stage，包括轨道宽度、音符大小和 Stage 图像，同时保持其比例和
  显示的轨道内容范围不变。

在 osu! 皮肤编辑器中保存后，这些改动会写入当前皮肤的 BMS 专用游玩界面布局。之后使用同一皮肤游玩 BMS 时会
再次应用该布局；谱面文件和 `skin.ini` 均不会被修改。

Stage 皮肤组件是必需组件，因此不会出现在组件工具箱中。将其删除后，规则集会自动创建新的默认实例，并重置其
位置、大小和设置，包括判定线偏移。如果保存的布局中存在重复项，则只保留第一个实例。

<details>
<summary>示例</summary>

https://github.com/user-attachments/assets/7d88d698-1e06-4488-9b45-c9aa462adb64

</details>

#### Text

没有组件专属属性。编辑时会显示 "Sample Text Event" 占位文本，便于定位正常运行时透明的组件框。运行时由
通道 `99` / `#TEXTxx` 驱动。

### 示例 skin.ini (7K)

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
