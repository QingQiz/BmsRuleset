# 固定 Rate PCM 音频引擎开发规划

## 文档状态

- 状态：阶段 1–8 已实施；PCM backend 已成为 gameplay 与事件合成式 preview 的唯一 sample 播放路径
- 适用范围：BMS gameplay keysound、background sample，以及事件合成式预览
- 首期平台：Windows x64、Linux x64
- 固定决策：每局只使用一个不可变 rate；改变速度时保持音高
- 核心依赖：ManagedBass、BASSmix、BASS_FX、一个受控的 framework mixer bridge

## 实施记录

- Windows 与 Linux 使用同一 PCM processor、asset cache、voice mixer 和 native bridge 业务代码。
- 固定 rate 的 0.5、0.75、1、1.5、2 倍速音高与时长测试通过。
- gameplay sample 与事件合成式 preview 已接入 PCM backend；初始化失败时 sample 保持不可用，不再回退 framework Track。
- Alice `7n.bme` 全曲 Windows 非主设备 loopback 与 Linux/WSL headless capture 均为 0 artifacts。
- 全曲 1979 个播放请求中，1 个为同 frame 同终止域的预期折叠，其余 1978 个 voice 全部启动。
- 两平台全曲的 preload/playback underflow、command loss、voice overflow 和 callback failure 均为 0。
- 目标环境人工听测确认无杂音后，阶段 8 已删除 legacy Track backend、旧 Harmony patch 和过渡诊断/测试。

本文描述用规则集自有 PCM voice mixer 替换 framework `Track` 多实例播放的完整开发与验收流程。
framework 继续拥有最终音频设备和父 mixer，但不再负责 BMS sample 的 Track 创建、Seek、Restart、停止或 voice 生命周期。

## 背景与根因

当前音频链路使用多个 framework `Track` 表示 BMS sample。实验已经确认，主要杂音来自同 sample retrigger 时对旧
Track 的提前中断。禁用同 key 中断后，杂音消失；硬停止、活跃 Track 复用、Seek/Restart 以及 mixer flush 都可能把
非零波形直接切断。

beatoraja 的兼容语义是：background sample 和 keysound 进入同一个播放入口，并按 WAV ID、pitch 和 slice 共同决定
终止域。后续同域播放会终止旧实例。目标不是复制其硬切波形，而是保留其可感知语义，以极短淡出替代硬停止。

## 目标

- BGM 与 keysound 共用终止域，符合已确认的 beatoraja 行为。
- 每次触发创建轻量逻辑 voice，不复用正在播放或淡出的 voice。
- 同一 ruleset update 内的和弦在同一个输出 frame 开始。
- 同域旧 voice 从触发点开始短淡出，新 voice 不等待淡出完成。
- Windows 与 Linux 使用相同的解码、time-stretch、混音、淡出和 limiter 代码。
- 固定 rate 下保持音高，并与 rate-scaled gameplay clock 同步。
- 音频回调不分配、不等待锁、不访问文件、不执行异步操作。
- Background Sample 和 KeySound 不受全局 effect volume 影响。
- 事件合成式预览复用相同引擎，避免 gameplay 与 preview 行为分叉。
- framework 升级导致 bridge 失效时能够明确检测、记录并安全降级。

## 非目标

- 首期不支持游玩过程中连续变化的 rate。
- 首期不实现自有音频设备、WASAPI、ALSA 或 CoreAudio 后端。
- 首期不替换独立的单文件 dedicated preview Track。
- 首期不实现新的音频格式；继续使用现有资源解析与 BASS 支持的格式。
- 首期不实现通用 DAW 级 time-stretch、变调、效果器或任意总线图。
- 不修改 sibling `osu`、`osu-framework` 或 `rulesets` checkout。

## 不可破坏的语义

### 终止域

首期终止域定义为：

```text
SampleKey + Pitch + Slice(start, duration)
```

当前 legacy BMS 通常只有 `SampleKey`，但类型应预留 pitch 和 slice，避免 BMSON 或扩展命令加入后再次重构。

- 来源类型不属于终止域；BGM 和 keysound 可以互相终止。
- 两个 BGM 使用同一终止域时，后者终止前者。
- 两个 `#WAVxx` 即使解析到同一文件，只要 SampleKey 不同，就属于不同终止域。
- PCM 数据可以按文件身份共享，但逻辑终止域不得按文件路径合并。
- 相同输出 frame 内的同域请求按稳定提交顺序折叠，最后一个请求决定音量和 offset。
- 不同终止域的请求不得因同列、同文件名或相同 PCM 内容而互相终止。

### 终止包络

- 默认同域终止淡出为 2 ms，最终值必须通过听测和自动分析确认。
- 新 voice 在目标 frame 立即开始，不等待旧 voice 淡出。
- 已处于 Draining 的旧 voice 再次被终止时，从当前实际 gain 继续淡出，不跳回 1。
- 自然结束前应用短尾部包络，防止源文件以非零采样结束。
- `StopAll`、暂停和 seek 使用独立的总线淡出策略，不伪装成普通同域 retrigger。

### 固定 rate

本局 rate 在 `BmsDrawableRuleset` 创建 `BmsSamplePlayback` 时确定，之后保持不可变：

```text
tempo = rate
pitch = 1.0
processedDuration = originalDuration / rate
```

如果运行时检测到 rate 发生变化，首期实现必须记录不支持状态，不得静默让 PCM 与 gameplay clock 漂移。

## 总体架构

```text
BmsAudioResourceStore
        |
        v
BmsFixedRatePcmProcessor -- BASS decode -> BASS_FX tempo -> 44.1 kHz float stereo
        |
        v
BmsPcmAssetCache -- immutable PCM chunks + usage/lifetime + memory budget
        |
        v
BmsSamplePlayback facade -- QueueLivePlay / SubmitLivePlayBatch / Play / StopAll
        |
        v
BmsPcmVoiceMixer -- command queue -> voice state -> fade -> sum -> limiter
        |
        v
BmsBassMixerBridge -- one custom float stream attached to framework BMS mixer
        |
        v
framework final mixer and selected audio device
```

### 保留的 framework 职责

- 音频设备初始化、选择与切换。
- 最终输出 mixer 和系统缓冲。
- 全局 master volume bindable。
- ruleset 生命周期、gameplay clock 和依赖注入。

### 移除的 framework 职责

- BMS sample `Track` 创建和池化。
- BMS voice 的 Start、Stop、Seek、Restart。
- 同域终止选择。
- BMS sample 的包络与 limiter。
- BMS sample 播放状态的 managed Track 修正。

## 组件设计

### `BmsFixedRatePcmProcessor`

职责：把一个已解析资源转换为连续、统一格式的 PCM chunk 流。

处理顺序：

1. 从 `BmsAudioResourceStore` 获取 stream 或 bytes。
2. 创建 BASS float decode stream。
3. rate 不为 1 时创建 BASS_FX tempo decode stream。
4. 只设置 Tempo，不设置 Frequency。
5. 通过离线 BASS mixer 统一到 44100 Hz、双声道、float32。
6. 连续调用 `ChannelGetData()` 生成固定 frame 数的 chunk。
7. 到 EOF 后刷新算法尾部，记录最终 frame 数和时长。
8. 正常结束、取消和失败都必须释放全部 native handle。

建议沿用 framework 当前的 BASS_FX 参数作为首个基线：quick algorithm、30 ms sequence、4 ms overlap。参数属于
处理管线版本，修改参数必须使缓存失效并重新执行音质、相关性和 onset 验收。

### 单一连续处理算法

所有音频必须使用同一处理算法和同一种 chunk 表示，不按长短选择两套算法。

- 每个资源只有一个连续的 decode/tempo 状态。
- 生成一个 chunk 后可以暂停任务，但不能为了下一个 chunk 重建 BASS_FX 状态。
- 短音频只是更早到 EOF；长音频只是生成更多 chunk。
- chunk 是存储和调度边界，不是 time-stretch 边界。
- rate 为 1 时 tempo stage 可以执行 identity 快路，但输出格式、chunking 和验证路径保持一致。

### `BmsPcmAsset`

建议字段：

```text
AssetId
ResolvedResourceIdentity
RateKey
Chunks
ReadyUntilFrame
TotalFrameCount (unknown until EOF)
OriginalDuration
ProcessedDuration
State: Planned / Preparing / Ready / Complete / Failed / Disposed
ReferenceCount
LastRequiredChartTime
```

PCM chunk 在发布后不可变。音频回调只能读取已发布 chunk，不得观察处理中的可变缓冲。

### `BmsPcmAssetCache`

职责：资源去重、预取、处理优先级、引用计数、内存预算和释放。

去重键使用解析后的资源身份、rate 和处理管线版本。多个 SampleKey 可以共享同一 asset，但每个播放请求仍携带自己的
终止域。

处理优先级从高到低：

1. 活跃 voice 即将读取的后续 chunk。
2. 当前可触发 keysound candidate 的启动安全缓冲。
3. 当前时间附近即将触发的 BGM。
4. 下一 keysound candidate。
5. 更远的 chart usage。

不得以文件时长作为算法选择条件。调度器只根据 deadline、ready-ahead、活跃引用和内存压力决定下一步处理哪个 asset。

### `BmsPcmVoiceMixer`

voice 状态：

```text
Free -> Active -> Draining -> Free
```

voice 至少包含：

```text
AssetId
TerminationDomain
CurrentFrame
EndFrame
BaseGain
CurrentEnvelopeGain
FadeStartFrame
FadeEndFrame
SourceOffsetFrame
NextVoiceInDomain
```

使用预分配 voice 数组、free list 和按 SampleKey 索引的 domain heads。不得在 render callback 中使用会扩容的
`List`、`Dictionary`、LINQ、Task 或闭包。

### `BmsVoiceCommandQueue`

使用 update thread 单生产者、audio callback 单消费者的分段 SPSC ring buffer。每段固定容量并可循环复用；只有 producer
积压超过当前段剩余容量时才创建新段。新段及完整 batch 在 producer 线程填充完成后一次性发布，audio callback 只读取和切段，
不得分配、复制或等待。

命令至少包括：

```text
PlayBatch
Pause
Resume
SeekReset
StopAll
SetMasterGain
ReplaceEpoch
```

命令包含绝对目标 frame 和 epoch。seek 或重新建立时间线时递增 epoch，callback 必须忽略旧 epoch 命令。

命令不得因初始段容量不足而丢弃。段扩展次数和队列高水位必须可诊断；无法分配新段属于会话级致命错误，不能静默继续播放。

### `BmsBassMixerBridge`

职责：创建一个自定义 BASS float decode stream，将 `BmsPcmVoiceMixer.Render()` 的结果交给专用 framework BMS mixer。

- delegate 必须保持强引用，直到 native stream 完全释放。
- stream handle 创建、挂载、移除和释放必须有明确所有者。
- mixer 重建或设备切换后重新挂载，不能重复挂载同一 handle。
- bridge 安装时验证所需 framework 成员；任何成员缺失都必须明确失败。
- Harmony 只负责 mixer handle/重建通知和 float mixer，不再访问 `TrackBass` 私有播放状态。

## PCM Chunk 与预取

首期建议用固定 4096 frames 的 chunk 作为基线，测试还必须覆盖 1024、8192 和随机读取大小。

启动安全水位不能凭经验写死为最终值。初始实现可以使用保守常量，随后通过两平台测量以下数据确定：

- 单 chunk 处理耗时的 p50、p95、p99。
- 活跃资源数增加时的处理吞吐。
- BASS_FX rate 0.5、0.75、1、1.5、2 下的最差吞吐。
- 磁盘冷缓存与热缓存差异。

正式水位应满足最差验证机器上处理线程短暂抖动后仍不会追上播放 cursor。任何时候都不得让 audio callback 等待 chunk。

### 初始加载门槛

进入 gameplay 前只等待：

- 从 0 时刻开始可能触发的每列首个 candidate 的启动安全缓冲。
- 开头 BGM 的启动安全缓冲。
- 必需的 mixer bridge 初始化。

不等待所有 sample 处理完成。后续 candidate 使用现有 `BmsSampleUsage.CandidateStartTime/CandidateEndTime` 规划 deadline。

### 内存策略

- 初始 soft budget 建议为 512 MiB，仅作为测量起点，不作为未经验证的永久常量。
- asset 被 Active/Draining voice 引用时不得释放相关 chunk。
- candidate 仍允许从头反复触发时，必须保留其起始 chunk。
- 一次性 BGM 越过的旧 chunk，在无 seek/cache 需求和无引用时可以释放。
- 达到 soft budget 时优先释放无引用且最远未来才需要的 chunk。
- 达到 hard limit 时不得在 callback 中回收或触发 GC；由 update/worker 线程执行受控释放。
- 记录 resident bytes、chunk count、asset count、eviction count 和 regeneration count。

## 播放与批处理

`BmsSamplePlayback` 继续作为调用 facade，保留：

```text
QueueLivePlay
SubmitLivePlayBatch
Play
SetPlaybackBlocked
StopAll
```

`BmsBackgroundAudioPlayer` 和 `BmsColumnKeySound` 不直接操作 asset 或 voice。

一次 `SubmitLivePlayBatch()` 的处理：

1. 收集该 ruleset update 内的 BGM、实际判定音、空按音、LN 尾音和地雷音。
2. 计算共享目标输出 frame。
3. 按稳定提交顺序折叠同 frame、同终止域请求。
4. 将一个 `PlayBatch` 命令写入 ring buffer。
5. callback 在目标 frame 开始所有不同终止域 voice。
6. callback 同时将同域旧 voice 标记为 Draining。

BGM 可以提前准备 PCM，但首期不得提前提交到未来输出缓冲。到达事件的 ruleset update 时与 live keysound 一起提交，避免
BGM 已经生成而同时间的玩家输入无法进入相同 batch。

## 时间映射

PCM 已按固定 rate 改变持续时间。chart time 到输出 frame 的基线映射为：

```text
outputFrame = originOutputFrame
            + (chartTime - originChartTime) * sampleRate / (1000 * rate)
```

BGM seek 的 source offset 映射为：

```text
processedOffsetFrame = originalChartOffsetMs * sampleRate / (1000 * rate)
```

提交目标不得早于 mixer 已生成位置：

```text
targetFrame = max(mappedFrame, renderedFrame)
```

需要分别记录 generated/rendered position 与 audible/device position。不得把 callback 已生成位置当作用户已经听到的位置。所有
映射假设必须通过 loopback capture 对齐验证，不以代码推导代替实测。

## Pause、Resume 与 Seek

### Pause

- 提交总线短淡出。
- callback 进入 paused 后输出静音且不推进 voice cursor。
- 处理 worker 可以继续补充安全缓冲，但不得无限预处理远期资源。
- Resume 时重设时间映射，并进行短淡入。

### Seek

1. update thread 增加 epoch。
2. callback 丢弃旧 epoch 的未来命令。
3. 旧总线短淡出，释放所有 keysound voice。
4. 重建 chart/output frame origin。
5. 选择当前时刻仍应发声的 BGM。
6. 将 chart offset 转换为 processed PCM offset。
7. 目标 chunk 不存在时，从目标之前的 preroll 建立相同 BASS_FX 状态，丢弃 preroll 输出直到目标位置。
8. 新 BGM 准备完成后淡入。

Seek 绝不能对 Active/Draining voice 原地修改 cursor，也不能复用旧 voice。

## 音量路由

每个 voice 的基础增益：

```text
eventVolume / 100 * AudioManager.AggregateVolume
```

- 不绑定 `AudioManager.VolumeSample`，因为它对应全局 effect volume。
- 不让 BGM 和 keysound 因来源类型进入不同全局音量链。
- preview 自身的 fade/restore gain 在进入 voice mixer 前作为额外总线 gain。
- 所有 gain 必须检查 NaN、Infinity 和负值。
- limiter 只处理最终混音峰值，不改变 BMS 事件音量语义。

## Limiter

沿用当前 float mixer limiter 作为基线，但移动到自有 mixer 输出末端。

- 输入为所有 voice 和 envelope 处理后的 float sum。
- attack 必须足够快以避免输出越界。
- release 必须跨 callback 保持状态，不能随 block 重置。
- 不使用会增加明显 lookahead latency 的算法，除非另行评审。
- limiter 命中次数、输入 peak、输出 peak 和 gain reduction 必须可诊断。
- limiter 不能被当作终止爆音修复；关闭同域硬切后仍应单独验证它的必要性。

## 线程与实时安全

### Update thread

- 收集请求、折叠 batch、更新 clock mapping、提交命令。
- 管理 candidate 状态和 asset 使用计划。
- 读取完成通知并发布不可变 asset/chunk 元数据。

### Processing workers

- 文件 IO、格式解码、BASS_FX、重采样、chunk 生成。
- 每个 asset 同一时刻只能有一个 processor 操作其 BASS state。
- 并发数必须有限，避免与 audio/game thread 争抢 CPU。
- 支持取消，退出 gameplay 后不得继续持有文件或 native handle。

### Audio callback

- 只消费命令、读取不可变 chunk、推进 voice、计算 envelope、混音和 limiter。
- 禁止 `lock`、Monitor、Semaphore、Task、await、文件 IO、日志格式化和托管分配。
- callback 内只写预分配的 completion/diagnostic ring，不直接写日志。
- 任何异常不得穿过 native callback 边界。

## 失败策略

| 失败 | 行为 |
|---|---|
| 文件缺失或无法解码 | 标记 SampleKey unavailable，保持静音并记录路径 |
| PCM 启动缓冲未准备好 | 不阻塞；记录 preload underflow，测试视为失败 |
| Active voice 后续 chunk underflow | 对该 voice 快速淡出，记录高优先级诊断，不硬切 |
| command segment 扩展 | producer 创建并完整填充新段后原子发布；callback 无分配地切段 |
| command segment 无法分配 | 终止音频会话并报告致命错误，不静默丢弃命令 |
| voice pool 满 | 不抢占任意其他终止域；记录容量错误，测试视为失败 |
| bridge 安装失败 | 开发阶段回退旧 Track 后端并明确记录原因 |
| mixer/device 重建 | 重建或重新挂载 bridge，保留可恢复的逻辑状态 |
| rate 不合法或运行时变化 | 拒绝新配置或重建会话，不静默漂移 |
| Dispose 期间 callback 仍在运行 | 先停止挂载和回调，再释放 assets、delegates 和 handles |

## 诊断与可观测性

至少暴露以下结构化计数，不在核心组件中拼接面向控制台的字符串：

```text
RenderedFrames
ActiveVoices / DrainingVoices / PeakVoices
QueuedCommands / QueueHighWater / QueueExpansions
VoicePoolExpansions / VoicePoolOverflows
LoadedAssets / PreparingAssets / FailedAssets
ResidentPcmBytes / PeakResidentPcmBytes
ReadyAheadFrames
PreloadUnderflows / PlaybackUnderflows
ProcessorFramesPerSecond
RenderCallbackP50 / P95 / P99 / Max
InputPeak / OutputPeak / LimiterGain / LimitedFrames
DeviceReattachCount
```

测试和 headless runner 负责把结构化状态输出为 JSON。生产代码只提供快照。

## 建议文件布局

```text
Media/Audio/Native/BmsBassMixerBridge.cs
Media/Audio/Native/BmsBassMixerBridgePatcher.cs
Media/Audio/Processing/BmsFixedRatePcmProcessor.cs
Media/Audio/Processing/BmsPcmProcessingScheduler.cs
Media/Audio/Samples/BmsPcmAsset.cs
Media/Audio/Samples/BmsPcmAssetCache.cs
Media/Audio/Mixing/BmsPcmVoice.cs
Media/Audio/Mixing/BmsPcmVoiceMixer.cs
Media/Audio/Mixing/BmsVoiceCommand.cs
Media/Audio/Mixing/BmsVoiceCommandQueue.cs
Media/Audio/Mixing/BmsPlaybackClockMapper.cs
Media/Audio/Mixing/BmsAudioDiagnostics.cs
```

不要让 `BmsSamplePlayback` 承担解码、缓存、实时混音、线程队列和诊断格式化。它只协调 gameplay 调用与 Component 生命周期；后端组装和所有权由 `BmsPcmPlaybackSession` 负责。

## 开发阶段与验收门

### 阶段 0：冻结基线

工作内容：

- 保存当前 Alice、SOLROS 完整原始采集、离线渲染、分析 JSON 和相关率曲线。
- 固定 19 秒、22 秒、26 秒及 2 分钟后的已知问题区间。
- 记录当前 Windows/Linux 配置、设备、buffer、sample rate 和 rate。
- 为旧 Track 后端建立内部 backend interface，避免迁移期间调用方同时变化。

退出条件：

- 基线文件可重复生成。
- analyzer 能在已知有问题版本中报告目标区间异常。
- 当前工作树中的用户变更均得到保留。

### 阶段 1：PCM 处理管线

工作内容：

- 实现 BASS decode、BASS_FX 固定 tempo、统一格式和连续 chunk 输出。
- 实现取消、失败清理、资源身份去重和 pipeline version。
- 支持 WAV、FLAC、OGG、MP3，以及 mono、stereo 和不同 sample rate。

退出条件：

- 440 Hz 在 0.5、0.75、1、1.5、2 rate 下保持在验收误差内。
- 输出时长与 `original / rate` 的误差在既定范围内。
- 全量读取、固定 chunk 和随机 chunk 的输出一致。
- impulse onset 延迟已测量并得到补偿或明确记录。
- 取消和失败路径无 native handle、stream 或文件句柄泄漏。

### 阶段 2：纯内存 Voice Mixer

工作内容：

- 实现 command ring、voice pool、domain 索引、2 ms fade、尾部包络和 limiter。
- 实现离线 render API，不依赖音频设备或 framework mixer。
- 实现 epoch、Pause、Resume、StopAll 和 seek reset。

退出条件：

- callback 稳态托管分配为零。
- 同域 retrigger 不产生超出干净 reference 的不连续点。
- 不同域和弦在同一 frame 开始。
- 同 frame 同域请求稳定折叠。
- 七键不同 sample、七键同 sample、layered sample 均符合语义。
- 命令分块方式不改变最终渲染结果。

### 阶段 3：Asset Cache 与调度

工作内容：

- 接入 `BmsSampleUsage`、candidate 时间、ready-ahead 和内存预算。
- 实现 chunk 发布、引用计数、优先级调度、释放和重新生成。
- 实现 underflow/queue/voice capacity 诊断。

退出条件：

- 长资源不要求完整处理后才能开始 gameplay。
- 密集段和多个长 BGM 下 playback underflow 为零。
- candidate 退休后可释放不再需要的起始数据。
- 内存达到 soft budget 时释放顺序可预测且不影响活跃 voice。
- 长时间运行后 resident memory 和 native handle 数量达到平台期。

### 阶段 4：Native Mixer Bridge

工作内容：

- 创建单一 float stream 并挂载到专用 framework BMS mixer。
- 处理 global mixer 模式、普通模式和设备重建。
- 将 callback 异常转成安全静音和延迟诊断。
- 保持 aggregate master volume 路由。

退出条件：

- Windows 与 Linux 均能后台运行且无需测试窗口。
- 输出不是空白或全静音。
- 切换设备、切换 WASAPI 模式、暂停和退出无崩溃或泄漏。
- effect volume 改变不影响 BGM/keysound，master volume 正常生效。
- bridge 成员验证失败时不会进入部分安装状态。

### 阶段 5：Gameplay 接入

工作内容：

- `BmsSamplePlayback` 切换到 PCM backend。
- 保持 `BmsColumnKeySound`、`BmsBackgroundAudioPlayer` 和 `BmsDrawableRuleset` 的 facade 调用稳定。
- BGM 与 live keysound 进入同一个 update batch。
- 接入 landmine、LN tail、empty press、autoplay/replay 禁音和 Background Keysound mod。

退出条件：

- 所有 gameplay audio 定向测试通过。
- 首音、长间隔后的 candidate、连续空按和实际判定音不丢失。
- BGM 不早于同一 update 的 keysound。
- 同域 BGM/keysound 互相淡出终止。
- 游戏 update/render thread 无同步文件 IO 和音频等待。

### 阶段 6：Pause、Seek 与 Preview

工作内容：

- 完成 pause/resume cursor 冻结与时间线重建。
- 完成 BGM seek reconstruction 和 preroll。
- 将事件合成式 preview 切换到同一 PCM engine。
- dedicated preview file 暂时保留单 Track 路径。

退出条件：

- 多次 seek、暂停、继续后 BGM 内容位置正确。
- seek 前旧命令不会在新 epoch 播放。
- preview 完整播放无同域硬切杂音。
- preview/gameplay 切换无双重播放和资源泄漏。

### 阶段 7：跨平台全曲验收

工作内容：

- Windows 使用非默认、非主输出设备进行后台 loopback 采集。
- Linux/WSL 使用 headless 输出与离线 reference 对比。
- 对 Alice、SOLROS 和合成压力谱面录制完整曲目，不只截取低压力区间。
- 输出相关率、RMS ratio、异常事件和 callback/underflow 曲线。

退出条件：

- Alice 19 秒、22 秒、26 秒及 2 分钟后无已知滋滋声或卡顿。
- Windows 与 Linux 的自动分析均通过同一判定规则。
- `PreloadUnderflows`、`PlaybackUnderflows`、`VoicePoolOverflows` 和非预期 command loss 为零；`QueueExpansions` 与 `VoicePoolExpansions` 仅作容量诊断。
- 无 keysound 丢失；同 sample 同 frame 的语义折叠除外。
- 用户在目标环境完成听测确认。

### 阶段 8：删除旧后端

工作内容：

- 删除 `BmsSampleTrackRegistry`、Track pool、Track command queue 和 Track restart/seek fallback。
- 删除 `BmsKeysoundMixerPatcher` 与 `BmsTrackAudioPatcher`；bridge/float mixer 由独立 PCM native 组件负责。
- 删除 transitional feature switch、无效诊断和旧测试。
- 更新开发文档和音频架构说明。

退出条件：

- 搜索确认 gameplay/event preview 不再为 sample 创建 framework Track。
- 所有保留测试通过。
- `git diff --check` 通过。
- 定向 build 无 warning/error。
- Windows/Linux 全曲验收在删除旧后端后重新通过。

## 自动化测试矩阵

### PCM Processor

- rate：0.5、0.75、1、1.5、2。
- 输入：impulse、正弦、白噪声、非零结尾、极短 sample、mono/stereo、多 sample rate。
- 格式：WAV、FLAC、OGG、MP3。
- chunk：一次性、1024、4096、8192、随机大小。
- 行为：取消、损坏文件、空文件、缺失文件、EOF flush、重复路径去重。

### Voice Mixer

- 单 voice 自然结束。
- 同域 retrigger 的 2 ms fade。
- fade 中再次 retrigger。
- 同 frame 同域折叠。
- 同 frame 不同域七押。
- BGM/keysound 跨来源终止。
- 相同文件不同 SampleKey 不终止。
- 非零 source offset。
- Pause/Resume、StopAll、epoch replacement。
- limiter attack/release、NaN/Infinity 防护。
- voice/command 容量边界。

### Cache 与生命周期

- 首 candidate 从 chart 0 起可触发。
- 前 note 判定后下一 candidate 立即可用。
- 很长 candidate lead。
- 重复空按不触发新 IO。
- active voice 保持 chunk 引用。
- candidate 退休释放起始 chunk。
- soft budget eviction。
- 多次 seek 后 regeneration 不泄漏。

### Gameplay 与 Preview

- BGM only、keysound only、两者混合。
- 实际命中、空按、BAD/E-POOR、LN tail、landmine。
- autoplay、replay、Background Keysound mod。
- seek 到长 BGM 中段。
- preview seek、preview/gameplay 切换。
- master volume 与 effect volume 路由。

## 验收指标

首期指标如下；阶段 1 的测量如果证明某项不符合 BASS_FX 的客观输出，应在实现前更新本文并说明原因，不能在测试中静默放宽。

| 指标 | 目标 |
|---|---|
| 440 Hz pitch | 0.5–2 rate 下误差不超过 0.5 Hz |
| 处理后时长 | 与 `original / rate` 的误差不超过 `max(5 ms, 0.1%)` |
| chunk 一致性 | 最大绝对差不超过 `1e-6`，或确认 BASS 行为后使用等价的高相关阈值 |
| onset 对齐 | 补偿后不超过 2 ms，且不同 chunk 大小结果一致 |
| render callback 分配 | 稳态 0 bytes/callback |
| callback 阻塞 | 0 次 lock wait、0 次文件 IO、0 次 Task wait |
| playback underflow | 完整验收曲目为 0 |
| command loss | 0 |
| 非预期 voice drop | 0 |
| 输出范围 | limiter 后有限值且不超过目标峰值 |
| 内存 | 完整播放后回落并达到稳定平台，无随时间单调增长 |
| native handles | gameplay/preview 退出后回到基线 |

性能基准测试属于 benchmark，按仓库规则必须在执行前获得明确许可。普通定向单元测试、headless render 和已有完整曲目诊断不视为 benchmark，但仍应限制范围。

## 开发验证命令

始终使用测试过滤器，不运行无过滤的完整测试套件：

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset.Tests\osu.Game.Rulesets.BmsRuleset.Tests.csproj --no-restore
dotnet test osu.Game.Rulesets.BmsRuleset.Tests\osu.Game.Rulesets.BmsRuleset.Tests.csproj --no-build --filter "FullyQualifiedName~BmsFixedRatePcmProcessorTest"
dotnet test osu.Game.Rulesets.BmsRuleset.Tests\osu.Game.Rulesets.BmsRuleset.Tests.csproj --no-build --filter "FullyQualifiedName~BmsPcmVoiceMixerTest"
dotnet test osu.Game.Rulesets.BmsRuleset.Tests\osu.Game.Rulesets.BmsRuleset.Tests.csproj --no-build --filter "BmsAudioArtifactAnalyzerTest|TestBmsAudioVolumeRouting"
git diff --check
```

新增测试类后应更新 filter 名称，不使用宽泛到等同全套测试的表达式。

## 端到端验收流程

1. 构建 Release 或与实际游玩一致的配置。
2. 用引擎自身离线 render 生成完整 reference WAV。
3. 后台启动实时播放，不创建交互窗口。
4. Windows 枚举设备并显式选择非默认、非主设备及对应 loopback；不得硬编码易变化的设备编号。
5. Linux/WSL 使用独立 headless sink，不占用用户主音频设备。
6. 从歌曲开头录制到全部 voice 自然结束，保存未裁剪原始采集。
7. 使用同步脉冲或可靠瞬态完成对齐，避免纯正弦的周期歧义。
8. 生成整曲分窗相关率、RMS ratio、peak、样本差分和 artifact event 曲线。
9. 单独标记 Alice 19、22、26 秒和 2 分钟后的区间。
10. 检查 engine diagnostics，确认 underflow、command loss、voice overflow、voice drop、callback over-budget 均为零，并记录 queue expansion。
11. 自动分析通过后再进行人工听测；人工听测不能替代自动门禁。
12. 保存 JSON、完整 WAV、曲线和构建 commit，保证结果可追溯。

外部 BMS 包不得提交到仓库。自动化单元测试使用生成的合成 PCM 和小型可分发 fixture；Alice、SOLROS 仅作为本地端到端验收素材。

## Code Review 检查表

### 实时安全

- callback 是否出现分配、锁、LINQ、Task、日志格式化或文件访问。
- 跨线程发布的 chunk 是否在发布后不可变。
- native callback delegate 是否被错误回收。
- Dispose 是否先停止 native callback，再释放托管状态。

### 音频正确性

- 是否意外按文件路径合并终止域。
- BGM 与 keysound 是否确实进入同一 domain index。
- 同 frame 折叠是否保持稳定顺序和最终音量。
- fade 是否从当前 gain 开始。
- offset、duration 和 chart/output frame 是否正确除以 rate。
- limiter 是否位于所有 voice 求和之后。

### 生命周期

- candidate 退休是否只禁止新触发，不提前销毁 Active/Draining voice。
- asset eviction 是否尊重 voice 引用。
- seek epoch 是否使旧命令失效。
- 失败、取消、设备切换和退出是否释放所有 BASS handles。

### 性能与数据结构

- callback 是否只消费固定容量 segment，并且扩段仅发生在 producer。
- domain 查找是否避免每 frame 扫描全部历史 voice。
- chunk 调度是否根据 deadline，而非简单 FIFO。
- 处理 worker 并发是否有上限。
- 诊断是否结构化并避免生产组件承担展示格式化。

### 兼容与维护

- Harmony 是否集中在 bridge，不散布到业务组件。
- 安装时是否验证所有私有成员和 patch 数量。
- Windows/Linux 是否运行相同业务路径。
- 新增用户可见文本是否使用 `BmsStrings` 并同步全部 resx。
- 是否只修改本仓库，没有修改 sibling reference checkout。

## 风险与应对

| 风险 | 应对 |
|---|---|
| BASS_FX 首部延迟或尾部 padding | impulse/长度测试，记录并补偿 processor delay |
| 多个长 asset 同时追赶导致 underflow | deadline scheduler、有限 worker、ready-ahead 高水位和完整压力测试 |
| PCM 内存过高 | chunk eviction、共享 asset、soft budget、结构化内存诊断 |
| framework mixer 私有 API 变化 | 集中 bridge、严格成员验证、安装失败降级、每次 osu 升级运行 E2E |
| Windows global mixer 与 Linux普通 mixer 行为不同 | bridge 两模式集成测试，业务 mixer 保持同一代码 |
| callback 异常导致 native 崩溃 | callback 边界 catch、安全静音、延迟错误上报 |
| 播放位置映射使用了错误的 generated/audible 时间 | loopback 同步脉冲、整曲相关曲线和 rate/seek 测试 |
| 初始 command segment 容量估计不足 | producer 分段扩展、batch 原子发布、高水位与扩段次数诊断 |
| 迁移期间旧新后端行为混合 | backend interface、单会话只选择一个后端、分阶段删除旧代码 |

## 回滚策略

- 阶段 7 完成前曾保留旧 Track backend；阶段 8 后不再包含运行时 backend 选择器。
- bridge 安装或首批关键 asset 准备失败时保持 sample 不可用并记录错误，不能静默切换播放语义。
- 每个阶段独立提交，提交边界与上述退出条件一致。
- 删除旧 backend 保持为独立提交；发现回归时可单独 revert，而不撤销 PCM processor 和测试基础设施。
- 不使用永久的隐藏环境变量作为发布行为开关。迁移完成后删除 transitional switch。

## 完成定义

只有同时满足以下条件，固定 rate PCM 引擎才算完成：

- gameplay 和事件合成 preview 不再使用 framework Track 播放 sample。
- 固定 rate 音高、时长、onset、seek 和 volume routing 自动测试通过。
- 同域终止完全由新 mixer 的短淡出实现，不存在 Active Track 硬停止或复用。
- Windows 与 Linux 完整曲目自动验收通过，且无 underflow、overflow、非预期丢音或已知滋滋声。
- Alice 已知问题位置和长曲后段通过自动分析与人工听测。
- callback 实时安全、内存与 native handle 生命周期通过专项 review。
- 旧 Track pool、restart/seek patch 和 transitional fallback 已删除。
- 文档、诊断入口和过滤测试命令已更新。
