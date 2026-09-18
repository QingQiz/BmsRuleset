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
dotnet osu.Game.Rulesets.BmsRuleset.Tests/bin/Release/net8.0/osu.Game.Rulesets.BmsRuleset.Tests.dll --gameplay-diagnostic --filter gameplay --chart "D:/charts/example/chart.bms" --start 0 --duration 180 --output artifacts/gameplay/example
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
| `--show-invisible-notes` | 关闭 | 无参数值；以黄色方框显示不可见 note，报告记录总数和活动／呈现数量 |

Legacy 是测试资源中的合成皮肤，不是任意用户自定义皮肤。窗口 1280×720，默认桌面运行，配置和数据库均独立。

**默认更新与绘制均不限速、VSync 关闭，并固定多线程执行**。测试器启用框架的 `AllowBenchmarkUnlimitedFrames`，否则 `FrameSync.Unlimited` 仍可能被框架钳到 1000 Hz。更新、绘制线程的前台与后台上限相同，避免失焦改变这两类负载。需要模拟固定帧率时，显式使用如 `--update-hz 1000 --draw-hz 240`；仅诊断更新线程时可使用 `--headless --update-hz 0`。两种限制都设为 0 才是软件层面的更新与绘制不限速；只解除更新限制而保留绘制上限也是单独一种负载，比较前后时必须一致。

加载后先停止播放时钟，seek 到起点前两秒（最低 0），等待回放模拟追上，再启动播放并预热两秒真实时间。加载与 seek 追赶分别记录为 `LoadMs` / `SeekMs`。起点为 0 时，前两秒不计入稳态样本；完整性检查仍覆盖整曲。报告记录实际采样范围。

## 复用模板与阈值

复制 [JSON 模板](../scripts/gameplay-performance.template.json)，替换谱面路径并调整区间后，用 **PowerShell 7** 运行：

```powershell
pwsh -NoProfile -File scripts/Measure-BmsGameplay.ps1 -Cases "D:/perf/cases.json" -Output artifacts/gameplay/candidate
```

模板的 `cases` 每项设置 `name`、`chart`，以及可选的 `start`、`duration`、`repeat`（1–20）、`scrollSpeed`、`updateHz`、`drawHz`、`referenceBpm`、`skin`、`longNoteMode`、`invert`、`invertRandomSeed`、`headless`、`audioOutput`、`showInvisibleNotes`。相对谱面路径以清单文件所在目录解析，支持空格和方括号。`name` 只接受字母、数字、横线及下划线，且不能重复。

可选 `maxUpdateP99Ms` 为该场景的更新耗时 P99 上限；不填时只检查运行与判定完整性。模板里的 8 ms 只是示例，实际应按机器和需求设置。脚本依次启动独立进程，生成 `case/run-XX/capture/summary.json`、`runner.log` 和总表 `matrix.json`，任一运行或阈值失败时返回非零。输出根目录必须不存在，防止覆盖基线；`-Assembly` 可指定独立构建目录中的测试器 DLL。

同一份清单分别运行基线与候选构建，比较多次运行的 P99、最大值、慢帧比例、加载/seek 时间和内存。建议至少包含普通密集谱、LN/CN/HCN、STOP/反向 SCROLL、地雷与双侧谱；组合 Argon、Classic、Legacy，并固定相同速度和 BPM 设置。

## 使用 IN 扩展长条覆盖

模板包含 IN + LN/CN/HCN 三个示例，填入普通谱即可复用；`longNoteMode` 只改变判定模式，必须同时启用 `invert` 才会把普通音符转成长条。IN 和模式 mod 通过 Player 的正常转换流程应用，自动回放、报告物件数与完整性检查均使用转换后的谱面。未启用 IN 的同谱场景可作为普通物件对照，但优化前后应比较参数完全相同的场景。

```powershell
dotnet osu.Game.Rulesets.BmsRuleset.Tests/bin/Release/net8.0/osu.Game.Rulesets.BmsRuleset.Tests.dll --gameplay-diagnostic --filter gameplay --chart "D:/charts/example/chart.bms" --invert --invert-random-seed 12345 --long-note-mode HellChargeNote --skin Legacy --start 30 --duration 30 --output artifacts/gameplay/in-hcn
```

建议同时使用固定长度与固定种子的随机长度，并保留原生 LN 谱：IN 无法代替原生长条的尾音、重叠或特殊声明覆盖。含地雷的谱面经 IN 转换后可能产生长条与同列地雷重叠，自动回放会为避雷提前松开，无法保证全 Perfect；完整性检查仍会报失败，此类运行不能充当通过验证的性能基线。

## 完整性与报告解释

- 超时、加载失败、无样本、结束时模拟落后超过 100 ms、非 Perfect 判定均视为失败。
- 整曲检查全部非地雷物件是否获得结果，并要求每个长条的头尾均有 Perfect 记录（包括 CN/HCN 的独立尾判定）；区间检查起点后开始且在终点前至少 400 ms 结束的物件。跨区间边界的长音不计入此项缺失检查。Perfect 和物件计数不等于逐帧画面或地雷伤害验证，后者由功能测试覆盖。
- `frames.csv` 包含播放时钟、模拟时钟、整个游戏 `UpdateSubTree` 耗时、更新帧间隔、GC 暂停差值、该更新线程分配字节数和活动物件数。
- `summary.json` 包含谱面 SHA256、程序集 MVID、运行参数/平台/渲染器、谱面构成、判定结果、总体及逐秒 P50/P95/P99/最大值、超过 8/16.667 ms 的帧数、GC 次数与最大模拟落后。
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
