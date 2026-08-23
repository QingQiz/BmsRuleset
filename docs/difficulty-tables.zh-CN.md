# 难度表

[返回 README](../README.zh-CN.md) | [English](./difficulty-tables.md)

难度表（LR2/beatoraja 格式）为 BMS 谱面提供难度评级和标记。规则集支持从 JSON 文件或 URL 导入难度表，
并通过 MD5 哈希自动匹配谱面。

## 预设表

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

## 导入难度表

1. 打开 **设置 → BMS** → 滚动到 **Difficulty Tables**。
2. 在文本框中粘贴 URL（例如 `http://zris.work/bmstable/turbow/header.json` ）或本地文件路径。
3. 点击 **Import**（或按 Enter）。

导入器支持三种 JSON 格式：

- **分离文件** — `header.json` + `data.json`，通过 `data_url` 链接
- **合并文件** — 单个 JSON，同时包含 header 字段（名称、符号、level_order）和 `"charts": [...]`
- **HTML 页面** — 包含 `<meta name="bmstable" content="URL">` 指向 JSON 的网页

## 段位

如果难度表表头包含 bmstable `course` 数组，其中的段位也会加入 BMS 段位模式。每个段位对象需要提供 `name`
和 stage 哈希列表。段位会保留声明顺序，并通过 MD5 或 SHA-256 匹配本地谱面；歌曲标题、作曲家和难度等级从
难度表的谱面数据中补齐。尚未导入对应谱面的 stage 会显示为不可用，导入匹配谱面后即可游玩。

段位不声明血条档位；与 beatoraja 一致，游玩时把所选的血条 Mod 映射到三个段位血条档位
（Class、EX Class、EX Hard Class）。段位的 `constraint` 数组会显示在段位选择界面的左上角，
并在游玩时强制执行。

| 约束          | 效果                         |
|---------------|----------------------------|
| `grade`       | 仅默认布局；禁用 Mirror 和 random   |
| `grade_mirror`| Mirror 或默认布局               |
| `grade_random`| —                          |
| `no_speed`    | 锁定游戏内卷动速度并禁用 Constant mod  |
| `no_good`     | 移除 GOOD 判定窗口               |
| `no_great`    | 移除 GREAT 和 GOOD 判定窗口       |
| `gauge_lr2`   | 强制 LR2 血条配置                |
| `gauge_5k`    | 强制 5 键血条配置                 |
| `gauge_7k`    | 强制 7 键血条配置                 |
| `gauge_9k`    | 强制 PMS 血条配置                |
| `gauge_24k`   | 强制 24 键血条配置                |
| `ln`          | 强制经典长条（LN）模式               |
| `cn`          | 强制 Charge Note（CN）模式       |
| `hcn`         | 强制 Hell Charge Note（HCN）模式 |

## 标记

导入后，MD5 匹配的谱面难度名后会自动追加标记：

```
DP ☆NOTHER [TT★1 TT★2]
```

标记显示表符号和条目等级。添加或删除表时标记会自动更新。

> [!IMPORTANT]
> 不要在**选歌界面**添加或删除难度表。删除表会触发所有 BMS 谱面的完整标记重建，
> 与谱面轮播的活跃 Realm 读取竞争，导致 UI 冻结。请在导入或删除难度表之前先切换到**主菜单**。

## 收藏夹

每个表同时创建一个名为 `[BMS] {表名}` 的 **BeatmapCollection**，包含所有匹配的谱面。可以直接在选歌界面的收藏夹列表中浏览。
（收藏夹名底层使用了不可见字符，不必担心与你自己的收藏夹撞名。）

## 表格行显示

每个已导入的表都以可展开表项显示在设置列表中。悬停表项可查看每个等级的谱面数量。

点击表项可显示可用操作。远程表提供**拆分/合并**、**更新**和**删除难度表**；本地表不提供**更新**。
执行操作后这些操作仍保持可见，难度表列表重建时也会保留展开状态。

展开表项后点击**拆分**，可将表的收藏夹拆分为每级独立且带序号的收藏夹（例如 `[BMS] Table [00] ★1`、`[BMS] Table [01] ★2`）。
序号位数根据等级数量自适应（&lt;10 级用 1 位，&lt;100 级用 2 位，依此类推）。
点击**合并**可恢复为单个收藏夹。

远程表提供**更新**操作，可从原始来源 URL 重新导入难度表。

点击**删除难度表**并在对话框中确认，可删除该难度表、对应标记及自动生成的收藏夹。
