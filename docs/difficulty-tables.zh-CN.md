# 难度表

[返回 README](../README.zh-CN.md) | [English](./difficulty-tables.md)

从 JSON 文件或 URL 导入 LR2/beatoraja 难度表，按 MD5 匹配谱面，并生成等级标记和收藏夹。

## 预设表

可在自动完成下拉菜单中选择以下预设：

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
2. 在文本框中粘贴 URL（例如 `http://zris.work/bmstable/turbow/header.json`）或本地文件路径。
3. 点击 **Import**（或按 Enter）。

导入器接受三种格式：

- **分离文件** — `header.json` + `data.json`，通过 `data_url` 链接
- **合并文件** — 单个 JSON，包含表头字段（名称、符号、level_order）和 `"charts": [...]`
- **HTML 页面** — 包含 `<meta name="bmstable" content="URL">` 指向 JSON 的网页

## 缺失谱面

难度表中尚未导入本地曲库的谱面仍会显示在选歌界面。橙色感叹号表示有可用的下载链接，红色表示没有。
尝试游玩缺失谱面时，会打开可用的下载页面。

## 段位

表头的 `course` 数组定义段位，每个段位需提供 `name` 和按游玩顺序排列的曲目哈希列表。
曲目通过 MD5 或 SHA-256 匹配本地谱面，标题、作曲家和等级取自难度表。缺失曲目在导入对应谱面前显示为不可用。

备齐所有曲目后才能开始段位。否则，尝试开始时，若所有缺失曲目都有有效链接，会批量打开下载页面；
只要有一首没有可用链接，就不打开任何页面，并提示曲目不齐。

段位游玩中不能暂停；曲目间最多停留 99 秒，之后自动开始下一首。

与 beatoraja 一致，段位血条档位由所选血条 Mod 决定（Class、EX Class 或 EX Hard Class），不在段位中指定。
`constraint` 数组定义的约束会显示在段位选择界面的左上角，并在游玩时强制执行。

| 约束          | 效果                         |
|---------------|----------------------------|
| `grade`       | 仅默认布局；禁用镜像和随机   |
| `grade_mirror`| Mirror 或默认布局               |
| `grade_random`| —                          |
| `no_speed`    | 锁定游戏内滚速并禁用 Constant Mod  |
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

标记包含难度表符号和等级，随难度表的添加或删除自动更新。

> [!IMPORTANT]
> 导入或删除难度表前，请先切换到**主菜单**。删除操作会重建所有 BMS 标记；
> 在选歌界面执行会与谱面轮播的 Realm 读取冲突，导致界面冻结。

## 收藏夹

每个表会创建名为 `[BMS] {表名}` 的收藏夹，供你在选歌界面浏览匹配的谱面。
名称中的不可见字符用于避免与自建收藏夹重名。

## 表格行显示

在 BMS 设置中悬停难度表可查看各等级的谱面数量，点击可展开操作：

- **拆分/合并**：按等级拆分收藏夹，或合并回单个收藏夹。拆分后带有排序序号，
  例如 `[BMS] Table [00] ★1`、`[BMS] Table [01] ★2`。序号在 &lt;10 级时用 1 位，&lt;100 级时用 2 位，以此类推。
- **更新**：从来源 URL 重新导入远程表。本地表不提供此操作。
- **删除难度表**：确认后删除难度表、标记及自动生成的收藏夹。

操作后表项保持展开，列表重建时也会保留该状态。
