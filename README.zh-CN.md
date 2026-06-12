# osu! BMS 规则集

osu! 原生 BMS 规则集插件，支持 `.bms`、`.bme`、`.bml`、`.pms` 谱面格式。

---

[English](./README.md)

## 安装

1. 克隆

   克隆仓库时可以跳过 `bms_test_songs` 文件夹以节省时间和磁盘空间，它包含仅运行测试所需的大型音频文件：

   ```bash
   git clone --filter=blob:none --sparse https://github.com/QINGQIZ/BmsRuleset.git
   cd BmsRuleset
   git sparse-checkout set --no-cone '/*' '!bms_test_songs'
   ```

   如果需要恢复该文件夹（例如运行测试）：

   ```bash
   git sparse-checkout add bms_test_songs
   ```

1. 构建规则集：
   ```
   dotnet build "osu.Game.Rulesets.BmsRuleset"
   ```
   输出：`osu.Game.Rulesets.BmsRuleset/bin/Debug/net8.0/osu.Game.Rulesets.BmsRuleset.dll`

2. 将 DLL 复制到 osu! 的 `rulesets/` 文件夹。

3. 重启 osu!。规则集会出现在规则集选择器中（暂时使用 osu!mania 图标作为占位符）。

---

## 导入 BMS 谱面

1. 打开 osu! → **设置** → 找到 **BMS** 栏目。
2. 点击 **"Import BMS files"** 打开导入界面。
3. 选择你的 BMS 文件夹。文件夹中的每个谱面文件（`.bms`/`.bme`/`.bml`/`.pms`）都会成为一个独立的难度；
   整个文件夹成为一个谱面集。
4. **谱面数据库中仅储存 chart 文件。** 音频和图像资源保留在原始文件系统中，游戏时通过 `Metadata.Source` 记录的谱面目录路径直接读取。导入后请勿移动或删除原始 BMS 文件夹 — 可在设置界面中清理孤儿谱面集（见下文）。

要删除所有已导入的 BMS 内容，使用同一设置栏目中的 **"Delete all imported BMS files"** 按钮。此操作仅移除 osu! 中的谱面元数据，不会影响原始 BMS 文件夹。

要清理源目录已被移动或删除的谱面，点击同一设置栏目中的 **"Clean up orphaned BMS sets"**。此操作会扫描所有源目录已不存在的 BMS 谱面并将其标记为删除。

---

## 已实现的 BMS 功能

### 解析器

**已解码的 Header 字段：**

| 字段                 | 命令                                                                                                                                    | 说明                                                    |
|----------------------|----------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------|
| 标题、艺术家、子标题 | `#TITLE`、`#ARTIST`、`#SUBTITLE`                                                                                                       | 子标题追加到标题为 `"Title - Subtitle"`               |
| 子艺术家             | `#SUBARTIST`                                                                                                                           | 追加到艺术家为 `"Artist (SubArtist)"`                 |
| 制作人               | `#MAKER`                                                                                                                               | 映射到谱面 Creator                                      |
| 流派                 | `#GENRE`（也支持 `#GENLE`）                                                                                                            | 歌曲风格，存入 Tags                                     |
| URL、Email           | `%URL`、`%EMAIL`                                                                                                                       | 存入 Tags                                               |
| 注释                 | `#COMMENT`                                                                                                                             | 存入 Tags                                               |
| Play level           | `#PLAYLEVEL`                                                                                                                           | 作为难度名显示                                          |
| 判定等级             | `#RANK`（0–4）                                                                                                                         | 影响判定窗口，映射到 `OD`                              |
| 血量总量             | `#TOTAL`                                                                                                                               | 血量恢复系数，映射到 `AR`                              |
| 视觉 BPM             | `#BASEBPM`                                                                                                                             | 滚动速度参考 BPM，不影响音符时序                       |
| 初始 BPM             | `#BPM`                                                                                                                                 | 默认 130                                                |
| 扩展 BPM 表          | `#BPMxx`                                                                                                                               | 实数 BPM（超出通道 `03` 的 0–255 范围）               |
| STOP 表              | `#STOPxx`                                                                                                                              | 停止序列时长（1 单位 = 1/192 个 4/4 小节）            |
| 采样定义             | `#WAVxx`                                                                                                                               | 音频文件路径（WAV/OGG）                                 |
| 长音符类型           | `#LNTYPE 1` / `#LNTYPE 2`                                                                                                             | LN 记法：1=RDM（默认），2=MGQ                          |
| 长音符标记           | `#LNOBJ`                                                                                                                               | LN 终点标记值（存储在 HashSet 中）                     |
| 文本事件             | `#TEXTxx`、`#SONGxx`                                                                                                                   | 游戏过程中在通道 `99` 上显示                           |
| Base 62 扩展         | `#BASE 62`                                                                                                                             | 命令和通道的大小写敏感 base-62 编码                    |
| 玩家模式             | `#PLAYER`                                                                                                                              | **已忽略** — 布局仅根据通道存在和文件扩展名推断        |
| 随机块               | `#RANDOM` / `#RONDAM`（容错拼写）、`#ENDRANDOM`、`#SETRANDOM`                                                       | 随机分支及条件子块；`#SETRANDOM` 固定随机值            |
|                      | `#IF`、`#ELSEIF`、`#ELSE`、`#ENDIF` / `#END` / `#IFEND` / `#END IF`                                                  |                                                         |
| 开关块               | `#SWITCH`、`#ENDSW` / `#ENDSWITCH`、`#SETSWITCH`、`#CASE`、`#DEF`、`#SKIP`                                             | 带 case 的开关控制流；`#SETSWITCH` 固定开关值         |

**未解析：** `#BANNER`、`#STAGEFILE`、`#BACKBMP`、`#BMPxx`、`#BGAxx`、`#EXWAVxx`、
`#WAVCMD`、`#VOLWAV`、`#MIDIFILE`、`#DIFFICULTY`、`#SCROLLxx`、`#SPEEDxx`、`#EXRANK` / `#EXRANKxx`、
`#DEFEXRANK`、`#EXBPMxx`、`#LNMODE`、`#PREVIEW`、`#STP`、`#PATH_WAV` / `#PATH_BMP`、`#OPTION`、
`#CHANGEOPTIONxx`、`#POORBGA`、`#SWBGAxx`、`#@BGAxx`、`#ARGBxx`、视频相关命令、`#CHARFILE`、
`#ExtChr`、`#OCT/FP`、`#MATERIALS`、`#SONGxx` / `#TEXTxx`（已合并），
BGA 通道（`04`、`06`、`07`、`0A`–`0E`）、动态音量通道（`97`、`98`）、
动态判定通道（`A0`）和动态选项通道（`A6`）。

**已解析的通道：**

| 通道        | 含义                                               |
|-------------|----------------------------------------------------|
| `01`        | BGM 自动播放采样                                   |
| `02`        | 小节长度（拍号变化）                               |
| `03`        | 行内十六进制 BPM 变化（0–255）                    |
| `08`        | 扩展 BPM 变化（`#BPMxx` 查找）                    |
| `09`        | STOP 事件（`#STOPxx` 查找）                        |
| `99`        | TEXT 事件（`#TEXTxx`/`#SONGxx` 查找）              |
| `11`–`15`   | 可玩音符 — P1 轨道 1–5（起源：5键）               |
| `16`        | Scratch / 转盘 — P1                                |
| `17`        | Free-zone — P1                                     |
| `18`–`19`   | 可玩音符 — P1 轨道 6–7（7键扩展）                 |
| `21`–`25`   | 可玩音符 — P2 轨道 1–5                             |
| `26`        | Scratch / 转盘 — P2                                |
| `27`        | Free-zone — P2                                     |
| `28`–`29`   | 可玩音符 — P2 轨道 6–7                             |
| `51`–`59`   | 长音符 — P1（映射到 `11`–`19`）                  |
| `61`–`69`   | 长音符 — P2（映射到 `21`–`29`）                  |
| `D1`–`D9`   | 地雷 — P1（36 进制编码）                          |
| `E1`–`E9`   | 地雷 — P2（36 进制编码）                          |

**未解析：** 隐形音符通道（`31`–`39`、`41`–`49`），BGA 图层
（`04`、`06`、`07`、`0A`–`0E`），动态 BGM 音量（`97`），动态 KEY 音量（`98`），
动态判定变化（`A0`），动态选项变化（`A6`）。

---

### 布局和轨道

布局根据通道存在和文件扩展名推断（`.pms` → PMS 变体；P2 通道存在 → Double Play；
通道 `18`/`19` 存在 → 7K）。`#PLAYER` 被忽略。

---

### 音符类型

| 类型           | 行为                                                                                                                       |
|----------------|----------------------------------------------------------------------------------------------------------------------------|
| **普通音符**   | 在到达判定线时按下                                                                                                         |
| **长音符**     | 按下并保持直到尾部通过判定线；提前松开会判定为 POOR                                                                       |
| **地雷**       | **不要**按下 — 在通过判定线时按住会扣除血量并播放爆炸音效 `#WAV00`                                                        |

---

### 输入与键位

默认键位（可在 **设置 → Key Bindings → osu!BMS** 中重新绑定）：

**游戏中控制：** 上/下 箭头临时调整滚动速度

每次按下按键时，无论判定结果如何，都会播放对应列中下一个音符的 key sound。

---

### 判定与计分

**判定等级（LR2 判定窗口，由 `#RANK` 决定）：**

| 名称       | EX 分 | 连击        | RANK 2 (Normal) 窗口            |
|------------|--------|--------------|---------------------------------|
| **PGREAT** | 2      | 保持         | ±18 ms                          |
| **GREAT**  | 1      | 保持         | ±40 ms                          |
| **GOOD**   | 0      | 保持         | ±100 ms                         |
| **BAD**    | 0      | 重置         | ±200 ms                         |
| **POOR**   | 0      | 重置         | < -200ms / > +200ms              |
| **E-POOR** | 0      | **不中断**   | [-1000ms,-200ms]，不消耗音符    |

`#RANK` 0 = Very Hard (±8/24/40/200 ms) → 4 = Very Easy (±21/60/200/200 ms)。

**分数：** `总 EX 分 / 最大 EX 分 × 1,000,000`

**DJ LEVEL 评级：** X（全 PGREAT）· S ≥ 8/9 · A ≥ 7/9 · B ≥ 6/9 · C ≥ 5/9 · D 其他

---

### 血量（仅普通血量）

- 初始 **20%**，无被动衰减。
- **通关条件：** 结束时 ≥ 80%。
- `#TOTAL` 控制最大回复速度。默认公式：`max(7.605 × N / (0.01 × N + 6.5), 160)`（LR2 公式，N = 总可玩音符数）。
- 各判定增减：PGREAT/GREAT +`TOTAL/100/N` · GOOD +一半 · BAD −4% · POOR −6% · E-POOR −2%。
- 地雷伤害：36进制值 ÷ 2 百分比（例如 `ZZ` = 647.5% → 直接清空）。

Easy / Hard / Ex-Hard / Hazard 血量变体尚未实现。

---

### 音频

- **BGM 通道 01：** 与谱面时钟同步播放，支持跳转、暂停/恢复。
- **Key sounds（`#WAVxx`）：** 每次按下从音符声明的采样播放。`.wav` 声明会自动解析为存在的 `.ogg` 文件。
- **背景采样和 key sounds 不受 osu! 特效音量滑块影响** — 仅跟随主音量和音乐音量。

---

### Mods

| Mod                        | 说明                                   |                 |
|----------------------------|----------------------------------------|-----------------|
| Autoplay                   | 自动播放                               |                 |
| Double Time / Half Time    |                                        | 未测试          |
| No Fail                    |                                        |                 |
| Cinema                     |                                        | 正常            |
| Mirror                     | 镜像键位布局                           |                 |
| 2P                         | 将玩家布局从 1P 切换为 2P              |                 |
| Auto Scratch /Hide Scratch | 自动/隐藏 scratch 轨道                 |                 |
| Lane Random (LR)           | RANDOM：随机排列轨道列                 |                 |
| Note Random (NR)           | S-RANDOM / H-RANDOM：逐音符随机        |                 |
| Rotation Random (RR)       | R-RANDOM：旋转 + 可选镜像              |                 |

---

### 设置

| 设置                  | 默认值 | 范围      | 说明                                                                                                                         |
|-----------------------|--------|-----------|------------------------------------------------------------------------------------------------------------------------------|
| Scroll Speed          | 8.0    | 1.0–60.0 | 音符下落速度。游戏中使用 `Up`/`Down` 键临时调整。按键可在 设置 → Key Bindings → osu!BMS 中重新绑定。                         |
| Show 5K / 7K / 9K     | ✓      | 开关      | 切换选歌界面中单人布局的显示                                                                                                  |
| Show DP 5K / 7K / 9K  | ✓      | 开关      | 切换选歌界面中双人布局的显示                                                                                                  |

布局可见性过滤在选歌界面实时生效 — 取消勾选某个布局可隐藏该类型的所有谱面。

你也可以在搜索框中使用 `k=`、`key=` 或 `keys=` 按键数过滤（支持 `=`、`!=`、`<`、`<=`、`>`、`>=` 运算符和逗号分隔值，例如 `keys=7` 或 `k>5`）。

---

## 难度表

难度表（LR2/beatoraja 格式）为 BMS 谱面提供难度评级和标记。规则集支持从 JSON 文件或 URL 导入难度表，
并通过 MD5 哈希自动匹配谱面。

### 预设表

以下知名难度表可在自动完成下拉菜单中一键选择：

| 表                     | 标记 | URL                                                    |
|-----------------------|--------|--------------------------------------------------------|
| Satellite (sl)        | sl     | `http://zris.work/bmstable/satellite/header.json`      |
| Stella (st)           | st     | `http://zris.work/bmstable/stella/header.json`         |
| 発狂BMS難易度表 (★)         | ★      | `http://zris.work/bmstable/insane/insane_header.json`  |
| 通常難易度表 (☆)            | ☆      | `http://zris.work/bmstable/normal/normal_header.json`  |
| NEW GENERATION 発狂 (▼) | ▼      | `http://zris.work/bmstable/insane2/insane_header.json` |
| 第三期Overjoy (★★)       | ★★     | `http://zris.work/bmstable/overjoy/header.json`        |
| Scramble (SB)         | SB     | `http://zris.work/bmstable/scramble/header.json`       |
| Luminous (ln)         | ln     | `http://zris.work/bmstable/luminous/header.json`       |
| BMS図書館 (T)            | T      | `http://zris.work/bmstable/turbow/header.json`         |

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

每个已导入的表在设置列表中显示名称和符号。名称过长时会自动换行。悬停行可查看提示，
显示包含每级谱面数量。

### 拆分

点击 **Subdivide** 将表的收藏夹拆分为每级独立且带序号的收藏夹（例如 `[BMS] Table [00] ★1`、`[BMS] Table [01] ★2`）。
序号位数根据等级数量自适应（&lt;10 级用 1 位，&lt;100 级用 2 位，依此类推）。
点击 **Unsubdivide** 合并回去。

### 更新

点击 **Upd** 从原始来源 URL 或文件路径重新导入表。

---

## 皮肤系统

### 使用方式

规则集使用**三层皮肤回退机制**：

```
1. 谱面自带的嵌入式皮肤（如果存在）
2. 当前 osu! 用户皮肤（如果提供 BMS 资源）
3. 规则集内置回退皮肤（始终可用）
```

用户皮肤在以下情况被识别为提供 BMS 资源：

- 其 `skin.ini` 包含 `[BMS]` 段，**或**
- 其 `skin.ini` 包含按键数正确的 `[Mania]` 段，**或**
- 拥有 `mania-key1` 或 `mania-keyS` 纹理

如果以上条件均不满足，则使用规则集内置皮肤。内置皮肤有两种变体：

- **LegacyModern** — 当你的活跃 osu! 皮肤为 Argon/Triangles 时使用
- **LegacyOld** — 当你的活跃 osu! 皮肤为默认 legacy 皮肤时使用

### 制作皮肤

将图片和 `skin.ini` 放入文件夹中，然后作为普通 osu! 皮肤导入。

#### skin.ini — `[BMS]` 段

为每个你想支持的布局编写一个 `[BMS]` 段。`Layout:` 键为必填。

**布局值：** `5K`、`7K`、`9K`、`10K`、`14K`、`18K`
（别名：`BMS5K`、`BME7K`、`PMS9K`、`BMS5KDouble`、`BME7KDouble`、`PMS9KDouble`）

#### 所有支持的键

**布局与位置：**

| 键              | 说明                                | 示例   |
|-----------------|------------------------------------|--------|
| `Layout`        | 布局标识（必填）                    | `7K`   |
| `HitPosition`   | 判定目标距底部 Y（480 高度空间）    | `440`  |
| `LightPosition` | 列键灯 Y                            | `440`  |
| `ScorePosition` | 判定弹出 Y                          | `250`  |
| `ComboPosition` | 连击数字距顶部 Y                    | `300`  |
| `JudgementLine` | 在判定位置显示白线（`1`/`0`）       | `1`    |

**列几何：**

| 键                        | 说明                           | 示例                       |
|---------------------------|--------------------------------|----------------------------|
| `ColumnWidth`             | 所有 N 列的逗号分隔宽度         | `45,45,45,45,45,45,45,45` |
| `ColumnLineWidth`         | 分隔线宽度 — N+1 个值           | `0,1,1,1,1,1,1,1,0`       |
| `ColumnSpacing`           | 列间距                          | `2,2,2,2,2,2,2`           |
| `WidthForNoteHeightScale` | 音符高度缩放参考宽度            | `50`                       |
| `BarlineHeight`           | 小节线高度乘数（每小节前的线）  | `1.2`                      |

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

| 键                            | 说明                          | 示例              |
|-------------------------------|-------------------------------|-------------------|
| `ColourColumnLine`            | 列分隔线颜色 (R,G,B,A)        | `255,255,255,50`  |
| `ColourJudgementLine`         | 判定线颜色                    | `255,255,255,255` |
| `ColourBarline`               | 小节线颜色                    | `0,255,0,255`     |
| `ColourBreak`                 | 连击中断闪烁颜色              | `255,0,0`         |
| `Colour1`–`ColourN`           | 逐列背景颜色                  | `0,0,0,0`         |
| `ColourLight1`–`ColourLightN` | 逐列键灯发光颜色              | `255,200,0`       |
| `Colour`                      | 全列背景简写                  | `0,0,0,0`         |
| `ColourLight`                 | 全列灯简写                    | `0,0,0`           |

**字体：**

| 键            | 说明                          | 默认值  |
|---------------|-------------------------------|---------|
| `ComboPrefix` | 连击数字纹理文件名前缀        | `score` |

连击数字加载 `{ComboPrefix}-0.png` 至 `{ComboPrefix}-9.png`。
若数字纹理缺失，连击显示将自动隐藏。

**note 图像：**

| 键                                             | 说明                               |
|-----------------------------------------------|------------------------------------|
| `NoteImage0`–`NoteImageN`                     | 逐列普通音符图像                   |
| `NoteImage0L`–`NoteImageNL`（或 `NoteImageL`）| 逐列 LN 身体图像                   |
| `NoteImage0T`–`NoteImageNT`（或 `NoteImageT`）| 逐列 LN 尾部图像                   |
| `NoteImage0H`–`NoteImageNH`                   | 逐列 LN 头部（回退到 `NoteImage`） |
| `MineImage` / `MineImage0`–`MineImageN`       | 地雷图像                           |

**按键图像：**

| 键                         | 说明                    |
|---------------------------|------------------------|
| `KeyImage0`–`KeyImageN`   | 逐列按键（未按下）      |
| `KeyImage0D`–`KeyImageND` | 逐列按键（按下）        |

**Stage与特效：**

| 键                           | 说明                 |
|-----------------------------|----------------------|
| `StageHint`                 | 判定目标图像         |
| `StageLeft` / `StageRight`  | 左/右Stage边框图像    |
| `StageBottom`               | 底部Stage前景图像     |
| `StageLight` / `LightImage` | 列灯/发光图像        |
| `LightingN`                 | 普通命中图像     |
| `LightingL`                 | LN 命中图像      |
| `LightFramePerSecond`       | 列灯动画 FPS         |

### HUD 组件

连击计数器（`BmsComboCounter`）和血量显示（`BmsHealthDisplay`）实现了
`ISerialisableDrawable`，可在游戏内通过**皮肤编辑器**自由拖拽位置。
在游戏中打开皮肤编辑器，拖动连击数字或血量条到合适位置，下次游玩时自动加载保存的布局。

**判定图像：**

| 键           | BMS 判定      |
|-------------|---------------|
| `HitPGreat` | PGREAT        |
| `HitGreat`  | GREAT         |
| `HitGood`   | GOOD          |
| `HitBad`    | BAD           |
| `HitPoor`   | POOR / E-POOR |

#### osu!mania 皮肤兼容

你也可以使用标准 osu!mania 皮肤的 `[Mania]` 段。规则集会匹配：

- `[Mania]` 带有 `SpecialStyle: 1` 和 `Keys: 6` → 5K 布局
- `[Mania]` 带有 `SpecialStyle: 1` 和 `Keys: 8` → 7K 布局
- `[Mania]` 带有精确的按键数

在 `[Mania]` 段中，使用 mania 标准判定名称：`Hit300g`（PGREAT）、`Hit300`（GREAT）、`Hit200`（GOOD）、
`Hit50`（BAD）、`Hit0`（POOR）。

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
LightingN : lightingN
LightingL : lightingL

HitPGreat : j-pgreat
HitGreat : j-great
HitGood : j-good
HitBad : j-bad
HitPoor : j-poor
```

完整内置皮肤（覆盖全部 6 种布局）位于
`osu.Game.Rulesets.BmsRuleset/Resources/Skins/Modern/skin.ini` — 可作为参考。

