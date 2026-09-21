# 通用 Gameplay 性能测试模板

测试器运行真实 Player，读取任意磁盘 BMS/BME/BML 及其音频、BGA，使用自动回放和固定分支选择 1。普通谱、长音谱、地雷谱共用同一入口，没有特定谱面名称或文件路径的判断。性能采样需事先获得许可（根目录 AGENTS.md）；此入口不会被普通 NUnit 测试自动执行。

**默认静音采样**：在诊断宿主中将主输出音量设为 0，仍执行音频解码、BGM/KeySound 调度与混音，不改变系统音量或正式游戏配置。仅做听感/音频输出测试时显式传 `--audio-output`（清单中为 `audioOutput: true`）。报告记录 `AudioOutput` 模式，比较时应保持一致；headless 使用无声设备。

## 构建

已有本地引用构建产物时，只构建本仓库，不修改或重建兄弟参考仓库：

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj -c Release --no-restore -p:BuildProjectReferences=false
dotnet build osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false
```

## 单次采样

```powershell
dotnet osu.Game.Rulesets.BmsRuleset.Tests/bin/Release/net10.0/osu.Game.Rulesets.BmsRuleset.Tests.dll --gameplay-diagnostic --filter gameplay --chart "D:/charts/example/chart.bms" --start 0 --duration 180 --output artifacts/gameplay/example
```

`--chart`、`--output` 和 `--filter gameplay` 必填。输出目录必须为空。`--start` / `--duration` 单位为秒，默认 0 / 30，duration 范围 3–3600；超过谱面长度时自动在结束前收尾。

| 参数 | 默认值 | 选项 |
| --- | --- | --- |
| `--scroll-speed` | 8 | 1–50 |
| `--update-hz` | 0 | 0 表示更新不限速；可显式设置上限，最大 10000 Hz |
| `--draw-hz` | 0 | 0 表示绘制不限速；可显式设置上限，最大 10000 Hz |
| `--reference-bpm` | MainBpm | MainBpm / StartBpm / MaxBpm / MinBpm |
| `--skin` | Argon | Argon / Classic / Legacy；另有 LegacyKeysUnderNotes、LegacyMissingColumnLightTexture 测试皮肤 |
| `--long-note-mode` | Undefined | Undefined 保留谱面声明；LongNote / ChargeNote / HellChargeNote 强制模式 |
| `--invert` | 关闭 | 启用 IN mod，将每列相邻普通物件转换为长条，保留每列最后一个普通音符 |
| `--invert-random-seed` | 不随机 | 配合 `--invert` 使用；指定整数种子后启用 IN 随机长度，保证重复采样的谱面一致 |
| `--headless` | 关闭 | 无参数值；跳过 GPU 渲染，仅用于更新线程诊断 |
| `--audio-output` | 关闭 | 无参数值；仅在需要听感／音频输出测试时开启 |
| `--capture-audio` | 关闭 | 从 seek 完成、开始播放时录制真实玩法混音，保存为报告目录下的 `audio.wav`；默认在录制后静音，配合 `--audio-output` 才会外放 |
| `--show-invisible-notes` | 关闭 | 无参数值；以黄色方框显示不可见 note，报告记录总数和活动／呈现数量 |
| `--hit-explosion-limit` | 0 | 仅诊断实验；0 不启用，1–256 限制每列每种光效的叠加数和普通光效预热容量 |
| `--hit-explosion-policy` | KeepExisting | 仅配合非零实验上限；KeepExisting / ReplaceOldest |

Legacy 是测试资源中的合成皮肤，不是任意用户自定义皮肤。窗口 1280×720，默认桌面运行，配置和数据库均独立。

**默认更新与绘制均不限速、VSync 关闭，并固定多线程执行**。测试器启用框架的 `AllowBenchmarkUnlimitedFrames`，否则 `FrameSync.Unlimited` 仍可能被框架钳到 1000 Hz。更新、绘制线程的前台与后台上限相同，避免失焦改变这两类负载。需要模拟固定帧率时，显式使用如 `--update-hz 1000 --draw-hz 240`；仅诊断更新线程时可使用 `--headless --update-hz 0`。两种限制都设为 0 才是软件层面的更新与绘制不限速；只解除更新限制而保留绘制上限也是单独一种负载，比较前后时必须一致。

加载后先停止播放时钟，seek 到起点前两秒（最低 0），等待回放模拟追上，再启动播放并预热两秒真实时间。加载与 seek 追赶分别记录为 `LoadMs` / `SeekMs`。起点为 0 时，前两秒不计入稳态样本；完整性检查仍覆盖整曲。报告记录实际采样范围。

## 叠加上限实验（仅诊断进程）

`--hit-explosion-limit 64 --hit-explosion-policy KeepExisting` 会在独立诊断进程中安装方法探针，让已有光效自然播完、跳过超限新增光效。`ReplaceOldest` 则回收最旧光效并从头播放新光效。上限按每列、普通 / 长条光效分别计算；两种策略都会有意识地改变超限时的亮度与动画相位，不修改音符判定或直接跳过 KeySound。

默认值 0 不安装此探针，正式游戏默认行为和用户配置均不改变。`summary.json` 中的 `HitExplosionExperiment` 记录上限、策略、触发次数、替换 / 跳过的视觉脉冲数量、每列每种光效的峰值、原始预热计划；未启用时为 null。这些计数覆盖探针存活期间，包含 seek 和预热，不等于单独采样区间的事件数。反射和探针自身有开销，实验收益不能直接当作最终集成后的精确成本。

这两个参数目前用于单次 CLI 入口，不属于下面 JSON 清单脚本的可选字段。

## 复用模板与阈值

复制 [JSON 模板](../scripts/gameplay-performance.template.json)，替换谱面路径并调整区间后，用 **PowerShell 7** 运行：

```powershell
pwsh -NoProfile -File scripts/Measure-BmsGameplay.ps1 -Cases "D:/perf/cases.json" -Output artifacts/gameplay/candidate
```

模板的 `cases` 每项设置 `name`、`chart`，以及可选的 `start`、`duration`、`repeat`（1–20）、`scrollSpeed`、`updateHz`、`drawHz`、`referenceBpm`、`skin`、`longNoteMode`、`invert`、`invertRandomSeed`、`headless`、`audioOutput`、`showInvisibleNotes`。相对谱面路径以清单文件所在目录解析，支持空格和方括号。`name` 只接受字母、数字、横线及下划线，且不能重复。

可选 `maxUpdateP99Ms` 为该场景的更新耗时 P99 上限；不填时只检查运行与判定完整性。模板里的 8 ms 只是示例，实际应按机器和需求设置。脚本依次启动独立进程，生成 `case/run-XX/capture/summary.json`、`runner.log` 和总表 `matrix.json`，任一运行或阈值失败时返回非零。输出根目录必须不存在，防止覆盖基线；`-Assembly` 可指定独立构建目录中的测试器 DLL。

同一份清单分别运行基线与候选构建，比较多次运行的 P99、最大值、慢帧比例、加载/seek 时间和内存。建议至少包含普通密集谱、LN/CN/HCN、STOP/反向 SCROLL、地雷与双侧谱；组合 Argon、Classic、Legacy，并固定相同速度和 BPM 设置。

轻量谱面应另外使用固定更新与绘制上限验收，例如 `updateHz: 1000` / `drawHz: 240`，并加入未启用 IN 的普通音符对照。基线和候选应使用相同自动回放修复、谱面、区间、皮肤与测试宿主，交替运行顺序并至少重复三次；只有两版均通过区间完整性检查后，才能比较相同工作量。除 P99 外，使用 `frames.csv` 中 `update_ms` 总和除以 `Summary.WallSeconds` 得到每秒累计更新耗时，同时比较每秒分配、超过帧预算的更新数、模拟落后和内存。保留每次结果及中位数，避免把单次最大值或 GC 时机差异当作稳定回退。

内存快照包含音频、BGA、宿主和诊断器；`ManagedBytes` 也没有在采样前强制 GC，不能视为精确的存活对象大小。比较时应对齐快照对应的谱面时刻：结束边界处某次运行可能多记录一个快照，直接比较各自最大值会混入不同的采样覆盖范围。全曲按更新次数计算的 P99 可能被大量低成本更新稀释，仍需查看密集段逐秒结果和最坏更新，不能单凭全曲 P99 判断流畅度。

## 使用 IN 扩展长条覆盖

模板包含 IN + LN/CN/HCN 三个示例，填入普通谱即可复用；`longNoteMode` 只改变判定模式，必须同时启用 `invert` 才会把普通音符转成长条。IN 和模式 mod 通过 Player 的正常转换流程应用，自动回放、报告物件数与完整性检查均使用转换后的谱面。未启用 IN 的同谱场景可作为普通物件对照，但优化前后应比较参数完全相同的场景。

IN 可能生成不足 1 ms 的长条；自动回放必须在实际尾部松键，不能套用普通音符的 10 ms 松键延迟，否则会吞掉后续同列按键。比较 LN 性能时，应先确认基线和候选均使用正确的回放输入；未通过完整性检查的报告可用于复现卡顿，但不能当作完成相同判定工作量的吞吐量比较。

仅包含普通音符和长条的列会合并同一游戏更新内的皮肤遍历。已开始长条的被动尾判定、HCN 血条变化、头部固定位置和按住光效仍逐个模拟时间戳推进；未判定头部的 POOR 截止时间、物件激活和回退会强制完整更新。LN 光效预热有独立的全场 8192 对象预算，超出后仍按需增长，因此应同时观察加载时间、内存和密集段尖峰。

```powershell
dotnet osu.Game.Rulesets.BmsRuleset.Tests/bin/Release/net10.0/osu.Game.Rulesets.BmsRuleset.Tests.dll --gameplay-diagnostic --filter gameplay --chart "D:/charts/example/chart.bms" --invert --invert-random-seed 12345 --long-note-mode HellChargeNote --skin Legacy --start 30 --duration 30 --output artifacts/gameplay/in-hcn
```

建议同时使用固定长度与固定种子的随机长度，并保留原生 LN 谱：IN 无法代替原生长条的尾音、重叠或特殊声明覆盖。含地雷的谱面经 IN 转换后可能产生长条与同列地雷重叠，自动回放会为避雷提前松开，无法保证全 Perfect；完整性检查仍会报失败，此类运行不能充当通过验证的性能基线。

## 完整性与报告解释

- 超时、加载失败、无样本、结束时模拟落后超过 100 ms、非 Perfect 判定均视为失败。
- 整曲检查全部非地雷物件是否获得结果，并要求每个长条的头尾均有 Perfect 记录（包括 CN/HCN 的独立尾判定）；区间检查起点后开始且在终点前至少 400 ms 结束的物件。跨区间边界的长音不计入此项缺失检查。Perfect 和物件计数不等于逐帧画面或地雷伤害验证，后者由功能测试覆盖。
- `frames.csv` 包含播放时钟、模拟时钟、整个游戏 `UpdateSubTree` 耗时、更新帧间隔、GC 暂停差值、该更新线程分配字节数和活动物件数。
- `summary.json` 包含谱面 SHA256、程序集 MVID、运行参数/平台/渲染器、谱面构成、判定结果、总体及逐秒 P50/P95/P99/最大值、超过 8/16.667 ms 的帧数、GC 次数与最大模拟落后。
- `StartupMaxUpdateMs` 和 `StartupSlowFrames` 记录加载、seek 及两秒预热阶段的主线程停顿。它们不等于 loading screen 的可见持续时间；`LoadMs` 记录真实 Player 加载至可用的耗时。
- `audio_blocked` / `AudioBlockedFrames` / `AudioBlockedMs` 记录共享 PCM 控制器禁止新触发的状态；`audio_paused` / `AudioPausedFrames` / `AudioPausedMs` 记录控制器是否要求暂停已有声音。时长按对应更新帧间隔求和，是状态采样估计，不能直接当成声卡实际静音长度。暂停、显式跳转和回退会禁止新触发；正常播放因卡顿而向前追赶时，同帧合并后的新键音与已有声音继续播放。声音跟随实际处理的判定，不能据此推断模拟没有落后。旧版本可能在向前追赶时也禁止新触发；若没有独立的 `voicesPaused` 字段，诊断器回退到原有 `playbackBlocked` 状态。
- `Audio` 通过诊断专用方法探针测量原生混音回调：回调次数、输出帧数、最大执行耗时以及执行耗时超过该缓冲区时长的次数。无超时只能排除已观测到的回调执行超时，不能证明没有声卡调度延迟或主观听感问题。探针同时用于基线和候选版本，普通测试和正式游戏不启用。
- `--capture-audio` 记录混音输出，不是扬声器回录。`AudioCaptureStartChartMs` 标记录音开始时的谱面时间；包含预热阶段，受音频缓冲延迟影响，不能按录音采样点直接断言逐音符同步精度。录制缓冲区占用额外内存，性能对比双方应使用相同录制设置。
- `FramePacing` 记录请求的更新/绘制上限、框架不限速开关、执行模式，以及采样期间窗口活动状态、线程时钟实际限制、节流开关、VSync 的变化。`ObservedUpdateHz` 按实际更新间隔计算，`AllocatedBytesPerUpdate` 与 `AllocatedBytesPerSecond` 分别反映单次更新开销和单位时间分配压力。
- 每十秒记录活动/呈现物件数、音频 voice 数、托管堆大小、进程私有内存与工作集。内存快照并非精确堆峰值；进程峰值工作集包含加载阶段。诊断器自身的帧数组和宿主也占用内存。

`UpdateMs` 不包含绘制节点生成、GPU 执行或节流等待；`IntervalMs` 包含调度和等待。不要将其等同于 GPU 帧时长。线程分配计数只覆盖该次更新子树；GC 计数器是进程级别。headless 数据不能证明显示流畅度。短区间 seek 也可能无法重建之前已开始的长音频，最终应从头完整播放。

不限速模式可能每秒执行更多更新，优化后每秒总分配未必下降，应同时比较每次更新分配与实际更新率。限速模式适合模拟固定负载，不能据其帧间隔推算最大吞吐量。旧报告若缺少 `FramePacing`，可能来自 1000 Hz 更新 / 240 Hz 前台绘制的旧入口，后台绘制还可能升至 1000 Hz；不要与新入口直接混合比较帧耗时。

线程时钟上限为 0 时，`Throttling: true` 仅保留框架的线程让步（`Sleep(0)`），不施加固定 Hz 上限。桌面报告的 `RendererVerticalSync` 应为 false；headless 或无法读取渲染器状态时为 null。

不要同时运行构建、单元测试或其他性能测试器。需要调用栈时另跑一次，使用 `dotnet-trace` 附加诊断进程；先用 `dotnet-trace list-profiles` 确认当前版本支持的采样 profile。框架日志通常位于 `%APPDATA%/bms-gameplay-diagnostic/logs`，每次独立配置保存在报告的 `storage/`。

## 定向功能回归

```powershell
dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Normal.Gameplay.Judgement|FullyQualifiedName~Normal.Gameplay.Health|FullyQualifiedName~Normal.Gameplay.Scoring|FullyQualifiedName~BmsGameplayVirtualisationTest|FullyQualifiedName~BmsHitObjectPoolPlanTest|FullyQualifiedName~BmsGameplayDiagnostic|FullyQualifiedName~BmsModInvertTest|FullyQualifiedName~TestSceneBmsInvertGameplay|FullyQualifiedName~TestSceneBmsReplayRewind|FullyQualifiedName~TestSceneBmsVisualCulling|FullyQualifiedName~TestSceneBmsMineCulling|FullyQualifiedName~TestSceneBmsLongNoteJudgement|FullyQualifiedName~TestSceneBmsLongNoteBodyTint|FullyQualifiedName~TestSceneBmsPauseRewind|FullyQualifiedName~BmsGameplayScrollControllerTest"
```

显示裁剪、对象池和测试时钟的设计约束及剩余瓶颈见 [开发文档](development.md#gameplay-performance)。
