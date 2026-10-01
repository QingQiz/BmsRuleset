# HitObject 重构 P3a-0 验证记录

[返回重构计划](hitobject-refactor-plan.zh-CN.md#implementation-start)

> 历史记录说明（2026-10-01）：本文件保留原实施时的代码事实、轨迹、测试结果和命令。原任务卡、下一卡、独立验收和阶段交接安排已由[完整实现方案](hitobject-refactor-plan.zh-CN.md#implementation-order)替代，不再作为执行指令。新的执行顺序为保存并 squash 现有工作、移除过渡层并完整实现核心、使用 Git 旧版对照适配，最后再处理 Mod；本说明不改变下文的历史证据或将未验收工作标为完成。

日期：2026-10-01（Asia/Shanghai）。P3a-0.1 至 P3a-0.6 已完成，主 agent 技术验收通过。本记录区分旧路径观测、新旧执行对照和生产计划生成对照；受限 Note/LN/CN/HCN 执行对照通过不代表完整新架构或计划生成已经实现。

<a id="p3a0-1"></a>

## P3a-0.1：固定旧路径检查点基线

### 基线、环境与实现范围

- 设计依据及实施起点：`3f0dc9c87004d8f3463c083daceb58db575a9eb2`，开始时工作区干净。生产判定代码仍为 P3 的 `3a5beceb66611ced25328fa846e40ff4b6bbb55c`。
- Windows x64，.NET SDK `10.0.109`，`net10.0`，Release，NUnit headless Player。
- osu! 源码引用：`ebaf7e9910ef3755308dec2c7950d916aabc545b`；实际 framework 包：`2026.918.0`。本机 framework 源码参考为 `f02756c5aa5032e6d04729922702b8d56c4bc2eb`，不将源码 checkout 当成实际加载程序集。
- 构建使用已还原依赖和引用程序集，始终传入 `--no-restore -p:BuildProjectReferences=false`。未修改或构建只读 sibling checkouts。
- 交付为 [测试场景](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/TestSceneBmsJudgementCheckpoints.cs)、[测试期记录器](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/BmsJudgementCheckpointRecorder.cs)、[旧路径 fixture](../osu.Game.Rulesets.BmsRuleset.Tests/reference_fixtures/judgement_p3_checkpoints.json) 及测试资源登记。本阶段没有修改生产业务路径，也没有新增试验判定分支。
- 记录器复用现有 `ScopedMethodProbe`，只在非并行测试的观测范围内安装探针和事件订阅，结束时解除。生产程序集没有新增日志、逐帧分配或双路径开关。记录器属于迁移验证工具，后续由新边界对照测试消费；核心职责稳定后在 P5b 清理核心旧私有方法探针，AS 专用迁移观察待 P6 所需证据完成后再清理，保留已冻结的历史轨迹。

### 可复现输入与观测来源

测试谱面为 BME 7K、RANK 2、TOTAL 24、Argon 测试皮肤及 AutoScratch：

| 稳定对象编号 | 对象 | 列 | 时间（ms） | sample 请求 |
| --- | --- | ---: | --- | --- |
| 0 | 普通 scratch | 0 | 3000 | key 11，volume 80 |
| 1 | 普通按键 | 1 | 3000 | key 12，volume 70 |
| 2 | LN / CN / HCN scratch | 0 | 3001–3301 | 头 key 13 / volume 60；尾 key 14 / volume 50 |
| 3 | 被动漏判或后续输入目标 | 1 | 3010 | 默认 |
| 4 | 保持 Player 生命周期的远期普通 Note | 2 | 30000 | 默认 |

固定时钟场景先用真实 Player 加载并 seek 到 2998，再将 Playfield、ScoreProcessor、HealthProcessor 接到同一个速率为 1 的手动帧时钟。由测试推进一次时钟，关闭 Playfield 对该自定义时钟的重复推进，保持地雷、身体、输入和子树检查点读取相同 elapsed。

普通/延迟对照使用完全相同的模拟时间：`2999, 3000, 3001, 3002, 3003, 3202, 3301, 3605, 2999, 3000, 3001, 3301`。在 3000 按下 Key1，在 3001 松开。普通配置逐步完整遍历；延迟配置把前五步放在同一个 `BeginGameplayFrame` / `EndGameplayFrame` 内，余下步骤相同。测试只选择现有入口和分帧方式，没有替代规则或预注入判定结果。

另有独立场景覆盖：

- LN/CN/HCN 已开始后真实调用 `HitObject.ApplyDefaults()`，随后前进、回退和重判。
- 未判普通 Note 原地重应用，检查原 entry、Result 和候选身份。
- 父级输入在 `BeginGameplayFrame` 中按 3000 的原时间消费；3001 的三次访问各处理一个独立批次。
- 保留真实 Player / FrameStabilityContainer 的回放追赶：从 2998 seek 到 3004，回放在 3000/3001/3002/3003 分别按下/松开 Key1。此场景不替换时钟，也不手动调用逻辑阶段。

fixture 保存 **11 个固定场景、3,487 个有序检查点**。JSON 的 `fields` 给出每行以 `|` 分隔的字段顺序：

| 字段 | 实际来源与含义 |
| --- | --- |
| 执行序号、frame、simulationStep、inputBatch | 分别按记录行、`BeginGameplayFrame`、`UpdateSubTree`、`ProcessFrame` 的真实调用递增；batch 包含空输入的阶段处理。未调用 Begin 的固定普通遍历 frame 为 0。 |
| 来源/检查点、进入/退出 | 被探测方法及调用栈；`>` / `<` 为方法前后，`=` 为真实结果事件或测试标记。最终完整遍历来自 `frame.end/playfield.update`，不伪装成新模拟步骤。 |
| 模拟时间、elapsed、输入时间 | 前两项读 Playfield 时钟；输入时间取 `QueueInput` 参数，保留在 arguments 中。 |
| 对象身份、绑定代次、次序 | 身份取谱面对象实例的固定编号；观测开始时已绑定表示记为第 1 代，实际 `OnApply(entry)` 增加代次。代次是审计计数，当前生产代码尚未实现新设计的代次契约。次序分别读取 `AliveEntries.Values` 和 `AliveObjects`。 |
| 端点/自动持有/主结果状态 | 查询当前 Drawable 和 Controller；AutoScratch 每次调用前后保存 scratch 遍历次序及状态，包括没有产生结果的调用。 |
| 结果及参数 | 保存主结果 Type、TimeAbsolute、RawTime、offset、rate、SuppressPenalty；长按通知及合成端点保存 Kind、结果、事件时间、应用时间、rate。 |
| 候选、活跃长按、计分/血量 | 查询被测的 0/1 列索引，及每个检查点的事件数、EX、combo、总分、当前血量。同步回调内的中间值也保留，不仅比较最后统计。 |

列阶段记录聚焦上述 0/1 列，跨列地雷与血量裁剪继续由既有 `frames` 回归和 P3 fixture 覆盖。keysound 观测到 `BmsColumn.PlaySample` 的 key/volume 请求顺序，不代表已完成实际混音、音量路由或完整视觉反馈验收。

### 观察结论与兼容性处理

**AutoScratch 存在随遍历方式变化的可观察差异。** 在相同固定模拟序列下：

| 观察项 | 普通遍历 | 前五步延迟到最终遍历 |
| --- | --- | --- |
| 3000 scratch 的实际判定时间 | 3000 | 3003 |
| 3001 scratch 长按头的实际事件时间 | 3001 | 3003 |
| 自动按住何时变为 true | 3001 的 Mod 调用后 | 3003 的最终 Mod 调用后 |
| HCN 在 3202 的身体 tick | 一次恢复，血量 0.32 → 0.34 | 没有 tick，血量仍为 0.32 |

三个模式均保存普通和延迟轨迹。真实回放追赶也观察到延迟及最终完整遍历：本次复验的四次按键事件保持原时间，AutoScratch 在 3004 处理两个 scratch 头，随后同时间戳的另一次 Mod 调用没有新增结果。追赶按实际宿主帧预算分段，因此真实回放用例断言输入序列和生产入口关系，并输出 `P3A0_REPLAY` 原始观察；不将某台机器的全部宿主帧数量固化成确定性 fixture。

记录还固定了两类音效顺序：普通 scratch 先请求 keysound，再提交主结果；CN/HCN 长按头先发布头状态及主结果，再请求头 keysound。身体阶段读取该批处理时已经存在的自动按住状态，后续 Mod 写入不会反向改变此前采样。

**2026-10-01 范围决策：AS-1 停止且未实施。** P3a-0.1 至 P3a-0.6 的已完成证据保留。AutoScratch/Mod 属于核心完成后的独立适配，不再是 P3a-0.7 或后续核心任务的前置。旧 AS 轨迹、自动持有和身体采样观察原样保留，作为 P6 决定采样语义、适配边界及行为修复的证据；不得用迁移后输出改写旧预期，也不得要求核心为匹配旧调度保留专用时序或耦合。

**原地重应用是重新绑定，且存在需要保留证据的旧状态变化。** 真实链为 `ApplyDefaults → onDefaultsApplied → OnFree(entry) → OnApply(entry) → DefaultsApplied`。同 entry、同 Result、同 Controller 继续使用，未触发 `OnKilled` 或移除候选，活跃长按仍只有一份；观测到的绑定代次增加。

- Controller 的头状态保留，但 `IsAutomaticallyHeld` 从 true 变为 false。
- 框架将 `Result.TimeOffset` 设为 0。CN/HCN 已提交头的 RawTime / TimeAbsolute 因而从 3001 变为对象尾时间 3301，已有计分事件数量不变；LN 尚未提交主结果时也可被写入 RawTime。
- 上述 **RA-1** 是旧路径证据，不作为新契约已通过的依据。P3a-0.6 必须在真实框架适配上分别检查自动持有、框架结果时间与适用历史；若要改变旧重应用行为，应另立修复及预期，不能借结构迁移偷偷恢复自动持有或重写主结果权威。

**撤销有两个不同观察时点。** Playfield 和仍绑定的 Drawable 的真实撤销回调内可查询旧主结果；`Playfield.revertResult()` 返回时 `Reset()` 已完成，Type 为 None、RawTime 为 null。随后自定义历史恢复、索引重建；在下一次输入之前候选正确，所有列回退完成后的血量恢复到 0.2。记录器观测 `Reset()` 的调用方返回，避免小方法被 JIT 内联造成漏记。回池对象也保留 Playfield 级撤销证据。

普通 Note、LN/CN/HCN 的回池/再激活、被动检查与结果撤销顺序存于同一 fixture。每个表示移除期间保留原检查点，初始代次与再激活代次分开记录。

### 验证结果

规则集和测试项目 Release 构建均为 **0 警告、0 错误**。所有测试均传入过滤器并排除 `.Performance.`，未运行 benchmark。

| 批次 | 通过 | 失败 / 跳过 | 证据 |
| --- | ---: | --- | --- |
| 新 checkpoint 场景，独立复验 | 13 | 0 / 0 | `verification/checkpoint.trx` |
| note | 70 | 0 / 0 | `regression/note.trx` |
| long | 106 | 0 / 0 | `regression/long.trx` |
| frames | 10 | 0 / 0 | `regression/frames.trx` |
| lifecycle | 28 | 0 / 0 | `regression/lifecycle.trx` |
| automation | 22 | 0 / 0 | `regression/automation.trx` |
| 新旧场景同进程合跑（去重） | 238 | 0 / 0 | `combined2/combined.trx` |

13 项包括 12 个行为场景和 1 个场景构造测试；11 个固定场景逐行核对 fixture，另一个行为场景观测真实回放入口。五组既有回归共 236 次执行、225 个不同测试，重叠主要来自回放组。同进程合跑和独立运行均核对相同的 3,487 行，验证调用顺序不依赖其他场景是否已预热。

测试工具开发中修正了固定时钟与 JIT 内联造成的观测问题：时钟同时绑定计分/血量处理器；重按探针直接观测 `Controller.TryRepress`，避免重编译薄转发时内联叶方法、影响其他场景的计数；安装探针后保留原方法体、重新生成具体长按的两个框架转发调用边界，使先前已预热的进程也能观察到被动和收尾调用。未改变业务输入、结果或调用顺序，也未删掉漏记的检查点来让对照通过。探索日志 `capture*` / `isolation*` / `combined` 不计入完成证据。

### 复验命令与证据保存

完整日志和 TRX 位于本地 `artifacts/hitobject-p3a0/p3a0-1/`。下列命令从仓库根目录执行；fixture、测试输入和断言随版本库提供，不依赖这些本机日志。

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj -c Release --no-restore -p:BuildProjectReferences=false
if ($LASTEXITCODE -ne 0) { throw 'Ruleset build failed.' }
dotnet build osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }

$checkpointFilters = [ordered]@{
    checkpoint = 'FullyQualifiedName~TestSceneBmsJudgementCheckpoints'
    note = 'FullyQualifiedName~BmsJudgementSelectorTest|FullyQualifiedName~TestSceneBmsColumnJudgement|FullyQualifiedName~TestSceneBmsBeatorajaPmsConsumers|FullyQualifiedName~TestSceneBmsVisualCulling'
    long = 'FullyQualifiedName~BmsLongNoteJudgementControllerTest|FullyQualifiedName~TestSceneBmsLongNoteJudgement|FullyQualifiedName~TestSceneBmsBeatorajaScratchInput'
    frames = 'FullyQualifiedName~TestSceneBmsBeatorajaHcnFrameOrder|FullyQualifiedName~TestSceneBmsBeatorajaHcnLandmineFrameOrder'
    lifecycle = 'FullyQualifiedName~TestSceneBmsLongNoteLifecycle|FullyQualifiedName~TestSceneBmsJudgementIndexes|FullyQualifiedName~TestSceneBmsPauseRewindLongNote|FullyQualifiedName~TestSceneBmsReplayRewindTiming'
    automation = 'FullyQualifiedName~TestSceneBmsMods.TestAutoScratch|FullyQualifiedName~TestSceneBmsReplayRewind'
}
$checkpointRunId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
foreach ($checkpointGroup in $checkpointFilters.Keys) {
    $checkpointFilter = '(' + $checkpointFilters[$checkpointGroup] + ')&FullyQualifiedName!~.Performance.'
    dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter $checkpointFilter --logger "trx;LogFileName=$checkpointGroup.trx" --results-directory "artifacts/hitobject-p3a0/p3a0-1/$checkpointRunId"
    if ($LASTEXITCODE -ne 0) { throw "Test group failed: $checkpointGroup" }
}

# 同进程合跑验证探针解除后不影响其他观测测试。
$combinedCheckpointFilter = '(' + ($checkpointFilters.Values -join '|') + ')&FullyQualifiedName!~.Performance.'
dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter $combinedCheckpointFilter --logger 'trx;LogFileName=combined.trx' --results-directory "artifacts/hitobject-p3a0/p3a0-1/$checkpointRunId"
```

每个固定场景输出 `P3A0_CHECKPOINTS {"scenario":...,"rows":[...]}`，可从 TRX 的 `StdOut` 提取，与已提交 fixture 的同名数组逐行比较。fixture 采自上述未修改的 P3 业务路径；复验不写 fixture，后续迁移不得用新输出覆盖旧预期。真实追赶另输出 `P3A0_REPLAY`，保留实际宿主分帧信息。

### 验收边界与交接

- **旧路径可重复性：** 固定场景精确对照，无浮点容差放宽；血量恢复的独立语义断言沿用既有精度。
- **新旧执行等价：** 尚未开始。本任务没有实现新输出、操作绑定或执行上下文；从 P3a-0.2 开始比较两类输出。
- **新计划生成等价：** 尚未开始。已有真实旧 Player 追赶观测及受控生产遍历基线，不能据此宣布新调度器会生成正确计划。
- **剩余验收：** RA-1 按上文保留旧行为；PMS 延迟应用时间、新表示代次失效、暂停恢复的新边界及无 Drawable 输出仍由后续任务验证。AS-1 停止且未实施，P6 再处理；完整音效/视觉反馈及性能、分配、保留内存验收保留至后续阶段。
- **下一项（历史记录）：** 本节记录时的下一项曾为 P3a-0.2；当前权威入口已更新为计划中的 P3a-0.7 核心真实入口计划生成。历史已完成阶段事实不因范围修订抹去。

<a id="p3a0-2"></a>

## P3a-0.2：普通 Note 的共享操作、内部协调与输出

### 基线、实现范围与所有权

- 实施日期：2026-09-30。设计依据为 `3f0dc9c87004d8f3463c083daceb58db575a9eb2` 的交接契约；代码从干净的 `5a59d66e`（P3a-0.1）开始。产出为包含本节的 `refactor(judgement): connect ordinary note execution outputs` 提交。运行环境、SDK 和只读参考依赖与 P3a-0.1 相同；未修改或构建 sibling checkouts。
- [列上下文 Notes 分部](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsColumnJudgementContext.Notes.cs) 持有谱面身份到槽位的映射、值类型登记/窗口数组、活跃 handle 次序及待判索引；普通 Note 没有新增逐对象可变堆状态或逐对象委托。每列共享一组按下/被动调用，Rule 无逐对象可变状态。旧的 `IBmsPressJudgementRule` / `IBmsPassiveJudgementRule<T>` 无接口消费者，已删除。
- [内部执行上下文](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsJudgementExecutionContext.cs) 在调用期间借用列上下文和 [共用输出](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/IBmsJudgementOutput.cs)。生产输出查询原框架 `BmsJudgementResult`，没有并行 `Judged` 标志；[记录输出](../osu.Game.Rulesets.BmsRuleset.Tests/Normal/Gameplay/Judgement/BmsRecordingJudgementOutput.cs) 保存测试主结果，并复现同步通知前后位置。两者不持有候选索引，也不复制索引算法。
- [真实框架输出](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsDrawableJudgementOutput.cs) 只管理当前表示映射、框架结果引用及结果/反馈适配。提交前验证应用时间和速率与框架时钟一致，再调用原 `ApplyResult`；不事后修改结果时间。主结果先经过原列/Playfield 通知，再在原 Drawable 结果回调位置调用内部索引检查点。
- `BmsPlayfield.EnableNoteExecution(column)` 只有新集成测试消费；拒绝含长按计分对象的列。普通游玩、其他列和未覆盖类型继续完整旧路径。每个普通 Note 在所选组合中只有一个结果写入者；地雷/Invisible Note 的原有路径没有迁移。
- 生产激活/移除边界从真实 `AliveEntries.Values` 保存活跃 handle 次序，输入时复用它；不假定 Dictionary 移除后再登记等于简单追加。待判 exact-time 次序仍使用登记序号。表示在容器移除候选之后保留至 `OnKilled` 末次检查返回；随后通过反向绑定映射解绑，不依赖回池后可能清空的 `HitObject`。

### 执行对照与断言

[TestSceneBmsNoteExecution](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/TestSceneBmsNoteExecution.cs) 每次先运行默认旧生产路径，再重新加载相同谱面运行受限新生产路径；两次各自与相同逻辑计划驱动的记录输出比较。记录输出宿主不创建 Drawable、Column 或皮肤树，通过数据构造的 `BmsJudgementEngine.GetColumn()` 调用同一个列上下文；规则、选择器、按键历史及索引协调均来自生产代码。

| 场景 | 固定计划及覆盖 |
| --- | --- |
| 同时间两个普通头 | 同时间连续输入、两个独立主结果；第一个选择时间为 2997、应用时间为 3000，速率为 1.5，主结果仍为 offset 0 / rate 1.5。 |
| PMS BAD 后命中 | 2850 的非消费 BAD、2860 再次输入、3000 命中；只记录一次 BAD，命中前主结果为 None、仍可选中。 |
| PMS BAD 后被动 POOR | 同样连续输入，BAD 截止时间不漏判，下一毫秒产生一次 `SuppressPenalty=true` 的主结果；再次被动检查不重复提交。 |
| 移除末次检查 | 真实 `container.Remove(entry)` 先移除候选，再在 `OnKilled` 提交 POOR；结果回调内及返回后均不会重新成为候选，输出最后解绑。 |
| 移除后再激活 | 未判重复头经真实 Remove/Add 重新激活，保存 Dictionary 槽位回用后的真实次序；慢选择和 exact-time 查询继续选择同一对象。 |

五类场景分别组合可批处理/含远期地雷的不可批处理列，以及可见/裁剪配置，共 20 项。额外四项在再激活场景验证 Duration、Lowest、Score 及旧回放 null 算法，连同前述 Combo 覆盖五种选择算法。加上场景构造及四项 [BmsNoteExecutionTest](../osu.Game.Rulesets.BmsRuleset.Tests/Normal/Gameplay/Judgement/BmsNoteExecutionTest.cs)，新增测试共 29 项。

- 每个 `Playfield.NewResult` 同步回调和索引维护后的 Drawable 回调均记录所有主结果（Type、TimeAbsolute、TimeOffset、GameplayRate、SuppressPenalty）、候选顺序及首待判查询；不是只读方法返回后的快照。可批处理列的首待判索引在前一观察点仍可指向刚提交对象，但可选择候选已按框架结果排除它，后一观察点完成索引更新；不可批处理列同时对照其慢查询。
- 有序记录还包含 BAD、keysound/sample 请求和每个计划操作后的查询。真实旧/新生产输出另精确比较完整 `JudgementEvents`、combo、总分和血量历史；记录输出不复制计分或血量算法。
- 所有对照逐行相等，没有浮点容差放宽或用新输出更新旧 fixture。24 个行为场景各输出旧/新两份 `P3A0_NOTE`，共 **48 份真实宿主轨迹、464 行观察**；每份也与对应无 Drawable 轨迹相等。
- 结构断言用 1,024 个登记核对同列委托实例共享、槽位/身份对应、窗口不含引用、不同列绑定独立；这是结构检查，没有计时或分配 benchmark。另验证多个被动截止时间在消费/移除后推进，以及移除中的对象最多提交一次主结果。

### 验证结果、命令与证据

最终规则集及测试项目 Release 构建均为 **0 警告、0 错误**。所有测试有 `--filter` 并排除 `.Performance.`，未运行 benchmark。

| 批次 | 通过 | 失败 / 跳过 | 本地证据（相对 `artifacts/hitobject-p3a0/p3a0-2/`） |
| --- | ---: | --- | --- |
| 首次测试探索 | 9 | 5 / 0 | `initial/note-execution.trx`；5 项为 NUnit TestCase 与 Values 混用造成的参数数量错误，未进入场景。改成完整参数组合后复验。 |
| 新增执行/结构测试独立运行 | 29 | 0 / 0 | `iteration2/note-execution.trx` |
| note 组 + 旧 checkpoint + 新增测试同进程合跑 | 112 | 0 / 0 | `verification/note-checkpoint-execution.trx`；分别为 70 + 13 + 29 项。 |
| 主 agent 独立复验新增测试 | 29 | 0 / 0 | `supervisor/supervisor-note-execution.trx`；相同新增过滤器，静态审查通过。 |

构建探索中补齐命名空间、保留框架 GameplayRate 的可空类型，并修复测试 entry 可空标注；没有修改行为断言来通过编译或测试。实现检查发现被动截止时间需要保留旧的“置脏或 now 超过旧截止时间”刷新条件，已修复并用多个截止时间断言覆盖。同进程合跑核对 P3a-0.1 的 **11 场景、3,487 个检查点**，原 `judgement_p3_checkpoints.json` 无 diff。

可从仓库根目录复验：

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj -c Release --no-restore -p:BuildProjectReferences=false
if ($LASTEXITCODE -ne 0) { throw 'Ruleset build failed.' }
dotnet build osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsNoteExecutionTest|FullyQualifiedName~TestSceneBmsNoteExecution)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=note-execution.trx' --results-directory artifacts/hitobject-p3a0/p3a0-2/iteration2
if ($LASTEXITCODE -ne 0) { throw 'Note execution tests failed.' }

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsJudgementSelectorTest|FullyQualifiedName~TestSceneBmsColumnJudgement|FullyQualifiedName~TestSceneBmsBeatorajaPmsConsumers|FullyQualifiedName~TestSceneBmsVisualCulling|FullyQualifiedName~TestSceneBmsJudgementCheckpoints|FullyQualifiedName~BmsNoteExecutionTest|FullyQualifiedName~TestSceneBmsNoteExecution)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=note-checkpoint-execution.trx' --results-directory artifacts/hitobject-p3a0/p3a0-2/verification
if ($LASTEXITCODE -ne 0) { throw 'Combined note and checkpoint tests failed.' }
```

测试输入和逐项断言随版本库提供。`P3A0_NOTE` 从 TRX `StdOut` 按行提取；仅结果日志保留在本地，不依赖本机日志重建期望。生产反馈探针仅在非并行测试作用域安装/解除，无生产日志或逐帧比较开关。

### 临时桥接、删除里程碑与未验收项

| 当前桥接/消费者 | 去向 |
| --- | --- |
| `EnableNoteExecution`、新旧列选择分支；消费者仅新集成测试 | P3a-0.3～0.6 扩展最小类型/生命周期验证；P5a 收敛正式输入与完整调度后删除双路径组合，普通游玩届时再切换。 |
| Drawable Note 的 TryHit、UpdateSubTree 和 OnKilled 转发；旧路径及新受限路径消费 | P5a 迁移核心被动计划及通用生命周期薄桥后删除逐对象核心业务转发；Mod 定向适配留待 P6，不能为此保留核心 Drawable 输入接口。 |
| `FirstPendingHead` 将内部身份查询映回 Drawable，生产边界从 AliveEntries 保存 handle 次序 | P5a 将现有容器/调度消费者迁至身份与显式计划；本步保留原入口和顺序。 |
| 生产表示/结果映射只覆盖当前普通 Note 的绑定、移除及未判再激活 | P3a-0.6 验证 Reapply 代次、旧绑定失效、撤销两个时点与恢复；本步未实现完整代次协议。 |
| 记录输出的索引检查点前后通知模拟和测试反馈探针 | 继续供后续输出执行对照；P5b 清理依赖旧私有入口的迁移观测工具。 |

- **执行等价：** 本步固定普通 Note 操作计划、主结果同步观察及内部索引已对照；未把旧 baseline 通过替代新输出验收。
- **生成等价：** 尚未验收。数据宿主当前通过 `GetColumn()` 执行提供的计划；`Engine.ProcessFrame` 仍是旧真实 Playfield 调度，尚未验证无 Drawable 的整体调度或生产入口自行生成的新计划。激活次序由宿主计划/真实边界提供，本步不是确定性激活生成器。
- **明确未覆盖：** 新路径的 LN/CN/HCN、地雷规则、完整混合被动计划、Reapply 代次及 RA-1、撤销双时点和暂停恢复；E-POOR 专用计分适配仍由旧调用方负责。AS-1 没有修复，普通模式及 AutoScratch 原轨迹保留；完整音频混音/视觉反馈、性能和保留内存尚未验收。
- **交接：** 本步实现和定向验证已完成，提交供主 agent 独立验收；验收通过后再由新的实施 agent 进入 P3a-0.3。本 agent 未开始下一卡、未 push、未 merge。

<a id="p3a0-3"></a>

## P3a-0.3：LN 无主结果的状态发布与 PMS 延迟提交

### 基线、实现与所有权

- 实施日期：2026-09-30。设计依据为 `3f0dc9c87004d8f3463c083daceb58db575a9eb2` 的交接契约和当前任务卡。代码从干净的 `4570df8ed9b51e14bce49f6c35dc9b10f355ebea`（P3a-0.2）开始；其 29 项新增、112 项合跑及主 agent 的 29 项独立复验已经通过。产出为包含本节的 `refactor(judgement): connect bounded long note execution` 提交。
- Windows x64、.NET SDK `10.0.109`、`net10.0`、Release、NUnit headless Player；沿用 P3a-0.1 的已还原引用与 Argon 测试皮肤。构建不遍历项目引用，未修改或构建 `..\osu`、`..\osu-framework`、`..\rulesets`。
- [共享 LN 操作绑定](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsLongNoteOperationBinding.cs) 复用现有 Controller 的计算与历史，通过每列共享按下/被动委托和类型化值登记接入 P3a-0.2 的相同内部协调。Controller 仍是本步运行状态的唯一写入者；[BmsLongNoteState](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsLongNoteState.cs) 为只读值快照，没有另建生产 `Judged`。完整三种 Rule 拆分仍在 P3a。
- [执行上下文](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsJudgementExecutionContext.cs) 在状态改变后同步调用只读输出，再刷新列内对象身份索引和截止时间。主结果仍由原框架 `ApplyResult` 写入，并在原 `OnNewResult` 位置维护内部索引。输出不保存索引、不复制候选算法。
- [列操作](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsColumnJudgementContext.Notes.cs) 扩展至普通 Note/LN 混合候选：未提交主结果的已判 LN 头保留在慢候选中，带正确的 `HeadJudged` 与尾时间；重叠持有仍使 exact-time 快路径失效。重按/释放按保存的真实活跃次序访问 LN 槽位，空持有时不扫描普通 Note，释放热路径不创建 LINQ 迭代器。普通 Note 列不分配 LN 登记数组或 LN 绑定。
- [过渡表现映射](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsLongNotePresentation.cs) 只接受先前/当前只读状态，在真实输出侧调用已有 Drawable 表现。公共 `IBmsJudgementOutput` 只新增只读状态发布、带端点的主结果与尾 sample 请求，没有固定头、清除身体或退场命令。表现快照限当前表示，解绑清除；恢复边界重建快照，PMS 回退重结算仍会立即清除身体。被动头 POOR 的完整状态发布不会新增旧链没有的固定头请求。
- `BmsPlayfield.EnableLongNoteExecution(column)` 仅由本步集成测试启用；普通游玩保持旧路径，CN/HCN 在该组合入口被拒绝。既有 Note 测试入口仍限制普通 Note。每个对象只有一个主结果写入者。

### 三方轨迹与明确时间断言

[TestSceneBmsLongNoteExecution](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/TestSceneBmsLongNoteExecution.cs) 每个测试先执行默认旧生产路径，再重新加载相同谱面执行新生产路径；两份轨迹分别与相同固定计划驱动的记录输出逐行比较。数据宿主不创建 Drawable、Column、Clock 或皮肤树。

| 场景 | 固定计划及关键断言 |
| --- | --- |
| 普通释放 | 头输入 3000、模拟应用 3003；尾输入 3990、应用 3997。尾端点 offset 为 -10，框架主结果 offset 为 -3；头/尾速率分别为 1.25/1.5。 |
| 持续按住自动完成 | 4000 被动检查完成，沿用头 offset 的尾事件时间为 4003，应用时间仍为 4000。 |
| PMS 宽限到期、撤销和重结算 | 输入释放 3400、模拟释放 3407；3606 无主结果，3607 到期结算。恰好回到 3607 保留主结果，3606 才撤销；恢复 pending release 后在 3607 再结算，身体再次立即清除。 |
| PMS 宽限内重按 | 3606 处理时间为 3600 的重按，取消待结算释放；3607 不提交，4000 正常释放完成。 |
| 被动头 POOR | 截止时间仍可等待，下一毫秒一次性提交；回调中的头状态已写入、`TailJudged` 仍为 false；无多余固定头表现。 |
| LN 头后重叠普通 Note / 另一 LN | 已处理头但未提交主结果时仍是慢候选；持有索引先消费重按，首待判快查询被抑制。释放后继续判后一个头。Combo、Lowest 和旧回放 null 算法分别对照。 |

七类场景各组合可批处理/含远期地雷的不可批处理列、可见/裁剪配置，共 28 项；重叠两类再各测 Lowest/null，共 4 项，加场景构造为 33 项。[BmsLongNoteExecutionTest](../osu.Game.Rulesets.BmsRuleset.Tests/Normal/Gameplay/Judgement/BmsLongNoteExecutionTest.cs) 的 6 项检查状态发布前后的索引边界、尾结果回调时尚未写入尾状态、共享绑定及独立状态、同应用时间的逆提交撤销顺序、移除末次检查不重加索引和未覆盖模式拒绝。新增合计 **39 项**。

PMS 到期结果逐项固定如下，不能把输入时间、端点事件时间与框架写入时间相互替换：

| 字段 | 值及来源 |
| --- | --- |
| 输入释放时间 | 3400，旧列计算初次 release offset 的输入 |
| 待结算释放 / 尾 `EventTime` | 3407，旧 Controller 在进入宽限时保存的模拟时间 |
| 尾 `ApplicationTime` / 框架 `RawTime` | 3607，宽限到期并同步调用 `ApplyResult` 的时刻 |
| 尾 `ExpectedTime` / `TimeOffset` | 4000 / -593 |
| 框架 `TimeAbsolute` / `TimeOffset` | 3607 / -393，沿用框架主结果语义 |
| 速率 | 头端点 1.25；释放时 1.5；到期尾端点及框架主结果 0.75 |
| 撤销边界 | 3607 仍保留；3606 触发真实框架撤销。外部和 Drawable 撤销通知仍能读取旧主结果；随后 Reset 清 Type/RawTime，但保留 GameplayRate 及端点数组；历史/索引恢复后再次输入或结算使用恢复状态。 |

生产撤销调用实际 `BmsColumn.Update` 的框架主结果栈消费，再按原恢复阶段执行 Controller/Drawable/索引恢复；这验证了 PMS 主结果应用时刻与撤销观察顺序，未宣称完整生命周期/遍历计划生成已经验收。记录输出用主结果提交栈复现相同顺序，时间相同的多个对象仍逆提交顺序撤销，不按时间稳定排序近似。

64 份真实旧/新宿主轨迹包含 **708 行同步观察**，每份均与无 Drawable 输出逐行一致；另精确对照 **264 条即时表现进入/退出记录**，包含 Alpha、LifetimeEnd、端点语义状态和当时主结果。观察点包括只读头表现已处理而内部索引尚未更新、框架外部/Drawable 结果回调、框架撤销/Reset 返回、操作后候选和持有查询。结果回调均保留 `TailJudged=false`，不是只在操作结束后比较状态。

两条真实生产路径还精确比较完整 `JudgementEvents`、combo、总分和血量历史；记录输出不复制计分/血量算法。sample key/volume 请求顺序加入执行轨迹。没有放宽浮点容差、修改旧 fixture 或用新实现输出覆盖旧预期。

### 验证结果与复验命令

最终规则集与测试项目 Release 构建均为 **0 警告、0 错误**。所有测试始终使用 `--filter` 并排除 `.Performance.`，未运行 benchmark。

| 批次 | 通过 | 失败 / 跳过 | 本地证据（相对 `artifacts/hitobject-p3a0/p3a0-3/`） |
| --- | ---: | --- | --- |
| 首轮观测探索 | 1 | 32 / 0 | `initial/ln-execution.trx`；记录输出错误假定初始 RawTime 为尾时间，且将旧链的直接持有状态查询与新链的索引查询混用。真实旧生产为初始 null、头发布前持有索引未更新；修正记录器和对应查询来源。 |
| 修正后的集成场景 | 33 | 0 / 0 | `iteration2/ln-execution.trx` |
| 新增集成与结构/同步测试 | 39 | 0 / 0 | `verification/ln-execution.trx` |
| long + note + .1/.2/.3 同进程合跑 | 257 | 0 / 0 | `combined/long-note-checkpoint-execution.trx`；分别为 106 + 70 + 13 + 29 + 39 项。 |
| 主 agent 独立复验新增测试 | 39 | 0 / 0 | `supervisor/supervisor-ln-execution.trx`；最终静态审查通过，原 fixture SHA256 仍为 `D0F84AFD210466CD4462969BACA785CB5B0EDCD6258402398BCB765FC15E316F`，osu/osu-framework 工作区干净。 |

构建探索仅补齐测试使用的对象生命周期/输入结果命名空间。静态审查还修正了表现快照的解绑/回退重建、被动 POOR 的固定头映射、普通 Note 输入前的多余扫描以及记录输出同时间撤销的逆序；上述最终场景均覆盖对应语义。合跑再次核对 P3a-0.1 的 **11 场景、3,487 检查点**，`judgement_p3_checkpoints.json` 无 diff。

从仓库根目录执行：

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj -c Release --no-restore -p:BuildProjectReferences=false
if ($LASTEXITCODE -ne 0) { throw 'Ruleset build failed.' }
dotnet build osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsLongNoteExecutionTest|FullyQualifiedName~TestSceneBmsLongNoteExecution)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=ln-execution.trx' --results-directory artifacts/hitobject-p3a0/p3a0-3/verification
if ($LASTEXITCODE -ne 0) { throw 'LN execution tests failed.' }

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsLongNoteJudgementControllerTest|FullyQualifiedName~TestSceneBmsLongNoteJudgement|FullyQualifiedName~TestSceneBmsBeatorajaScratchInput|FullyQualifiedName~BmsJudgementSelectorTest|FullyQualifiedName~TestSceneBmsColumnJudgement|FullyQualifiedName~TestSceneBmsBeatorajaPmsConsumers|FullyQualifiedName~TestSceneBmsVisualCulling|FullyQualifiedName~TestSceneBmsJudgementCheckpoints|FullyQualifiedName~BmsNoteExecutionTest|FullyQualifiedName~TestSceneBmsNoteExecution|FullyQualifiedName~BmsLongNoteExecutionTest|FullyQualifiedName~TestSceneBmsLongNoteExecution)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=long-note-checkpoint-execution.trx' --results-directory artifacts/hitobject-p3a0/p3a0-3/combined
if ($LASTEXITCODE -ne 0) { throw 'Combined long/note/checkpoint tests failed.' }
```

实际测试命令的 stdout/stderr 另保存为 `iteration2.log`、`verification.log`、`combined.log`。每份 TRX 的 `StdOut` 输出 `P3A0_LN { ... }`，其中 `rows`、`recordedRows`、`visuals`、`score` 分别是生产观察、记录输出观察、真实表现观察及计分/血量快照；合跑的这些 JSON 行另提取为 `combined/ln-traces.jsonl`。输入、预期与断言均在版本库中的测试代码，不依赖本机日志复现。

### 临时桥接、验收边界与交接

| 当前桥接/消费者 | 删除或收敛阶段 |
| --- | --- |
| `BmsLongNoteOperationBinding` 内部实现旧 `IBmsLongNoteJudgementSink` 以借用 Controller 计算；公共出口只消费结果/只读状态 | P3a 提取完整 LN/CN/HCN Rule 时一并删除旧 Controller/sink，不能把本步共享桥接当作完整规则提取。 |
| `BmsLongNotePresentation` 从只读状态调用旧 `IBmsLongNoteDrawable`；Drawable 仍直接读取 Controller 完成其他绘制 | P3b 迁至完整表现数据，删除旧视觉命令接口及直接判定状态依赖。 |
| 容器延迟绘制仍消费原 Drawable 活跃列表；新判定选择使用对象槽位索引 | P3b/P5a 迁移视觉与调度消费者，当前仅在生产绑定存在时维护表现列表。 |
| `EnableLongNoteExecution` 和 Note/LN 双路径测试组合、Drawable 转发及宿主时钟适配 | P5a 完成所有模式和独立调度后删除；默认游玩本步未切换。 |
| 表现快照恢复、真实框架 PMS 撤销及数据宿主提供的固定计划 | P3a-0.6 扩展代次、Reapply、末次检查与完整回退/暂停；P3a-0.7/P5a 验证生产计划生成。 |
| 测试期反射探针及显式被动/框架恢复入口 | 用于最小边界对照；P5b 在正式调度/表现边界稳定后清理旧私有入口观测。 |

- **执行等价：** 普通 LN 头尾、自动完成、PMS 延迟/救回/撤销重结算、被动头 POOR、重叠候选/重按以及即时表现检查点在本步固定计划上通过三方对照。普通 Note 原受限路径及历史检查点同时回归通过。
- **生成等价：** 尚未验收。数据构造的 Engine 仍通过 `GetColumn()` 消费测试计划，`ProcessFrame` 仍依赖旧 Playfield；主动安排的被动检查和恢复顺序不是新独立调度器生成的计划。
- **剩余项：** CN/HCN 新路径、完整 Rule 与表现分离、地雷、混合阶段、Reapply/RA-1、绑定代次、完整暂停和生命周期恢复仍按后续任务卡推进。AS-1 停止且未实施，P6 再处理；完整图形、实际音频混音、性能/分配/保留内存未验收。没有新玩家文案或玩法改动。
- **交接：** P3a-0.3 实现、定向验证、主 agent 静态审查及 39 项独立复验已完成。提交后停止，由主 agent 确认提交并安排全新实施 agent 进入 P3a-0.4；本 agent 未开始下一卡、未 push、未 merge。

<a id="p3a0-4"></a>

## P3a-0.4：CN 头结果后的独立尾成员与同步输出

### 基线、实现与职责

- 实施日期：2026-09-30；从干净的 `9c76d2cc733045e16fc1c72da52ff80c5401312b`（P3a-0.3）开始，前置 39 项独立复验和 257 项组合回归已通过。产出为包含本节的 `refactor(judgement): preserve pending charge note tails` 提交。
- 沿用 Windows x64、.NET SDK `10.0.109`、`net10.0`、Release、NUnit headless Player、Argon 测试皮肤及 P3a-0.1 已还原引用。构建始终使用 `--no-restore -p:BuildProjectReferences=false`，不修改或构建 sibling checkouts。
- [只读长按状态](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsLongNoteState.cs) 增加 `HasPendingTail`；[列执行上下文](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsColumnJudgementContext.Notes.cs) 的待判尾槽位由端点状态维护，独立于主结果和真实 Drawable 的视觉活跃列表。已完成 POOR 尾仍可等待视觉退场，但不再留在待判尾索引；移除先退出索引，末次结果通知不能重新登记。
- CN 头仍通过框架 `ApplyResult` 一次性提交，尾通过新增 `IBmsJudgementOutput.ApplySyntheticEndpoint` 同步送入原 Playfield 计分/血量路径。出口只负责表示及结果适配；`PublishState` 仍先通知只读表现，再更新引擎内部索引，合成尾返回前不写 `TailJudged`。
- 共享 `BmsLongNoteOperationBinding` 继续借用 Controller 中已经分开的 charge 计算分支。主结果已经存在时，待判尾调用旧 `UpdatePostResult` 入口，不能重新运行可能再次提交头结果的头漏判分支。普通游玩仍走完整旧路径；只有测试显式启用 `EnableLongNoteExecution`。HCN 仍在组合入口被拒绝；完整 LN/CN/HCN Rule 拆分留给 P3a。
- 反向 scratch 重按结束持有后，受限列路径现在请求与旧路径相同的尾 sample。`DrawableBmsLongNote.UpdateHitStateTransforms` 的头后寿命延长和 `BmsHitObjectLifetimeEntry` 的窗口余量原本已支持 CN，本步用真实框架证明其有效，没有改写这些寿命算法。
- 记录出口新增合成端点和真实提交时刻栈，栈时间取操作 `frame.SimulationTime`。它不持有候选或尾索引，也不复制计分/血量算法。主结果存储、状态和合成尾观察仍分别来自同一内部协调及原结果协议。

### 三方场景、实际表示存续与中间观察

[TestSceneBmsChargeNoteExecution](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/TestSceneBmsChargeNoteExecution.cs) 每例先运行真实旧生产，再重载运行受限新生产；两份轨迹各与不创建 Drawable、Column、Clock 或皮肤树的记录出口逐行比较。

| 固定计划 | 实测断言 |
| --- | --- |
| 正常键盘尾 | 头输入 3000、模拟提交 3003、速率 1.25；尾输入 3990、应用 3997、速率 1.5。主结果及其头端点保持不变，独立尾端点 offset 为 -10。 |
| 提前释放 | 输入 3400、应用 3407，CN 尾为 POOR（`Meh`）；没有使用普通 LN 提前释放的 BAD 规则。尾只结算一次，视觉可以继续存续。 |
| 被动尾 | 4000 和尾被动截止时刻仍待判；下一毫秒提交一次 POOR 尾，重复遍历不再结算。 |
| 被动头 POOR | 在原头截止后同步提交主头及合成尾，两次回调中的尾仍未完成；不增加成功头分支才有的 `PrepareHeadPin`。 |
| PMS 宽限到期 | 释放输入 3400、模拟 3407，3606 保留待判尾，3607 到期；端点事件时间 3407、应用 3607、offset -593，原主头仍是 3003/1.25。 |
| PMS 宽限内重按 | 在 3606 处理输入时间 3600 的重按，取消延迟释放；3607 不结算，4000 释放只产生一次尾。 |
| Scratch 反向尾 | 同方向在尾窗口内释放后仍保留尾，反向按下才结算并请求尾 sample；输入/模拟时间分离的 ST-1 证据见下。 |

7 个计划分别覆盖可批处理/含远期地雷的不可批处理列、可见/真实裁剪，共 **28 个行为场景**，加构造测试为 29 项。另有 [5 项纯记录测试](../osu.Game.Rulesets.BmsRuleset.Tests/Normal/Gameplay/Judgement/BmsChargeNoteExecutionTest.cs)：头状态发布先于索引、主结果后独立待判尾、提前/被动尾各一次、被动头 POOR 的顺序、末次检查不重加索引、scratch 反向及合成提交栈边界；新增总计 **34 项**。

成功头后实际执行 `Playfield.UpdateSubTree()` 到 3004 和 3200，再从容器 `AliveEntries` 重新查找当前表示，断言与原表示相同，entry 与 Drawable 的寿命均超过 4000；随后才执行尾输入或后续遍历。被动尾还在 4000 和截止时刻重新确认表示与尾目标存在。测试不是保存一个已回池 Drawable 再直接调尾方法。所有场景结束前再重复当前时刻并推进至 4400，确认真实容器回收，尾计数仍为一。

裁剪组临时扩展并恢复该测试绑定的视觉偏移上限，使用正向 1,000,000 的视觉偏移，让实际布局持续离屏；真实合成端点调用内部及每次存续遍历都断言 `VisualsSuppressed`。只修改测试设置，不修改生产范围、默认值或判定时间。试验中直接设置超过原范围的值曾被钳制，修正前的失败保留为探索记录，不能计作裁剪已通过。

记录计划复用相同输入和模拟时刻；生产每次执行真实遍历，记录端调用已登记 passive/post-result 操作。**移除边界由该次真实容器观测提供**，记录端先移除成员，再做末次 passive，不在表示已回池后补跑 post-result。此处验证同一计划的执行和边界通知，不声称数据引擎已经独立生成框架生命周期计划。头后到尾前的记录待判成员只依赖端点状态。

最终 **56 份真实旧/新宿主轨迹、832 行同步观察**分别与无 Drawable 输出逐行相等；两条真实生产路径另精确比较 **160 条即时表现进入/退出记录、800 个完整计分/血量快照**。快照包括头通知、合成尾前、尾计分后且血量未更新、尾血量后、尾返回及每个操作结束。显式断言计分事件由 1 增至 2 时血量历史仍为 1，随后才增至 2。合成尾进入和退出时 `TailJudged` 都为 false。没有放宽容差或更新旧 fixture。

<a id="st-1"></a>

### ST-1：旧 CN scratch 端点时间与真实合成提交时刻

本步三方实测确认，旧 `TryRepress` 使用输入时间调用 charge 尾计算，端点中名为 `ApplicationTime` 的值并非这一路径的实际模拟提交时刻。受限适配保留旧字段及血量事件时间，同时独立携带真实操作帧；没有为了满足目标时间契约重写旧结果。

| 检查点 | 固定值或观察 |
| --- | --- |
| 反向输入 / 实际模拟提交 | 3990 / 3997 |
| 尾端点 `EventTime` / 旧 `ApplicationTime` | 3990 / 3990 |
| 尾预期时间 / offset / rate | 4000 / -10 / 1.5 |
| Controller `TailTime` | 3990，沿用旧反向重按时间 |
| 生产 `syntheticResults` / 记录出口的提交栈 | 均为 3997；记录出口不按旧端点字段排序或撤销 |
| 尾计分后、血量更新前 | 2 个计分事件、combo 2、总分 666667；血量历史仍只有头事件 3003，血量记录为 0.28 |
| 尾血量更新后 | 新历史事件时间为 3990，值为 0.36000000000000004；精确与旧链相等 |
| 回到 3997 | 实际调用 Playfield 的补充结果栈消费者，生产/记录均保留尾 |
| 回到 3996 | 两者均撤销尾，真实 score 恢复为 1 个事件、combo 1、总分 333333；框架主头仍存在 |

上述撤销仅验证原 Playfield `Update` 中的补充栈消费。Controller、表示再激活、血量历史清理及后续输入的完整恢复尚未执行；此时血量历史仍含 3990 事件，不能把该检查点当成完整回退完成。**处理里程碑：** P3a-0.6 在同一例外上验证所有恢复阶段；P3a 完整 CN Rule 迁移前单独决定保留兼容或修复旧 `ApplicationTime`/血量时间，并保留修复前后证据。HCN 的对应新路径仍未验证，不将 CN 证据推广到 HCN。

### 构建、测试与证据

最终规则集及测试项目 Release 构建均为 **0 警告、0 错误**。每次测试有 `--filter` 且排除 `.Performance.`，未运行 benchmark。

| 批次 | 通过 | 失败 / 跳过 | 本地证据（相对 `artifacts/hitobject-p3a0/p3a0-4/`） |
| --- | ---: | --- | --- |
| 首轮观测探索 | 21 | 13 / 0 | `initial/cn-execution.trx`：新路径漏 scratch 尾 sample、记录端在回池后仍跑 post-result、首例 gauge 探针受内联影响。 |
| 修正同步/移除/提交时刻后 | 20 | 14 / 0 | `iteration2/cn-execution.trx`：失败仅为裁剪设置被钳制，实际未离屏。 |
| 裁剪配置继续定位（仅场景组） | 15 | 14 / 0 | `iteration3/cn-execution.trx`：正向偏移仍受绑定范围限制。 |
| 新增测试独立运行 | 34 | 0 / 0 | `verification/cn-execution.trx`，包括实际裁剪与 ST-1。 |
| 主 agent 独立复验新增组 | 34 | 0 / 0 | `supervisor/supervisor-cn-execution.trx`。 |
| 首轮全部相关组合 | 305 | 13 / 0 | `combined/long-lifecycle-execution.trx`：1 项旧 CN 拒绝样例失效；12 项旧 checkpoint 受前置真实遍历及探针预热影响丢观察行，未改 fixture。 |
| 叶检查点调用者重建后最小组合 | 47 | 0 / 0 | `probe-rebuild/cn-checkpoints.trx`；后续独立复验仍暴露真实回放外层调用者缺口，不能以本次通过代替修复。 |
| 第二轮全部相关组合 | 317 | 1 / 0 | `combined2/long-lifecycle-execution.trx`：仅回放收尾 `frame.end/playfield.update>` 观测缺失。 |
| 回放外层诊断最小组合 | 46 | 1 / 0 | `replay-diagnostics/cn-checkpoints.trx`；原断言前新增有序 rows，确认尾部遍历发生但 `frame.end` 被外层内联隐藏。 |
| 真实回放外层调用者重建后 | 47 | 0 / 0 | `replay-caller/cn-checkpoints.trx`，旧 fixture 和真实回放断言均保留。 |
| 最终 long + lifecycle + note + .1/.2/.3/.4 同进程组合 | 318 | 0 / 0 | `combined3/long-lifecycle-execution.trx`；分别为 106 + 28 + 70 + 13 + 29 + 38 + 34 项。 |
| 主 agent 最终独立 CN + checkpoint 复验 | 47 | 0 / 0 | `supervisor/supervisor-final2-cn-checkpoints.trx`；最终静态审查及技术验收通过。此前 `supervisor-final-cn-checkpoints.trx` 的回放来源观测失败保留为修复前证据，不计入最终验收。 |

P3a-0.3 新增组现在为 **38 项**：移除“CN 必须被拒绝”的旧参数用例，由本步 CN 正向测试覆盖；HCN 拒绝仍保留。P3a-0.2 的同一 unsupported 测试改用仍未支持的 HCN，组数量仍为 29。旧路径算法和 fixture 没有更改。

最终组合再次核对 P3a-0.1 的 **11 个固定场景、3,487 个检查点**。旧 `judgement_p3_checkpoints.json` SHA256 仍为 `D0F84AFD210466CD4462969BACA785CB5B0EDCD6258402398BCB765FC15E316F`；osu/osu-framework 工作区干净，没有对只读 sibling 的写入。构建输出见 `ruleset-build.log` 与 `tests-build.log`。

新增场景的端点观测按 health、score、Playfield 的顺序安装，再重建调用者，保证首例也能看见中间血量边界。组合运行还暴露旧 checkpoint recorder 的预热内联问题；其修正只重建测试期观察调用者，不改生产方法体、不删预期行或弱化同步检查。回放失败前的 `P3A0_REPLAY_ROWS` 诊断显示 12 对 deferred 调用、2 对真实 Playfield 更新，却没有 `frame.end` 行；据此在叶探针安装后重建 `BmsDrawableRuleset.Update/UpdateAfterChildren`，恢复真实外层来源。主 agent 的冷启动 13 项检查点单独通过，也佐证该差异来自同进程观测预热。

主要复验命令（仓库根目录；本地 `commands.ps1` 保留对应命令，stdout/stderr 另存同名 `.log`）：

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj -c Release --no-restore -p:BuildProjectReferences=false
if ($LASTEXITCODE -ne 0) { throw 'Ruleset build failed.' }
dotnet build osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsChargeNoteExecutionTest|FullyQualifiedName~TestSceneBmsChargeNoteExecution)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=cn-execution.trx' --results-directory artifacts/hitobject-p3a0/p3a0-4/verification
if ($LASTEXITCODE -ne 0) { throw 'CN execution tests failed.' }

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsChargeNoteExecutionTest|FullyQualifiedName~TestSceneBmsChargeNoteExecution|FullyQualifiedName~TestSceneBmsJudgementCheckpoints)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=cn-checkpoints.trx' --results-directory artifacts/hitobject-p3a0/p3a0-4/replay-caller
if ($LASTEXITCODE -ne 0) { throw 'CN and checkpoint tests failed.' }

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsLongNoteJudgementControllerTest|FullyQualifiedName~TestSceneBmsLongNoteJudgement|FullyQualifiedName~TestSceneBmsBeatorajaScratchInput|FullyQualifiedName~TestSceneBmsLongNoteLifecycle|FullyQualifiedName~TestSceneBmsJudgementIndexes|FullyQualifiedName~TestSceneBmsPauseRewindLongNote|FullyQualifiedName~TestSceneBmsReplayRewindTiming|FullyQualifiedName~BmsJudgementSelectorTest|FullyQualifiedName~TestSceneBmsColumnJudgement|FullyQualifiedName~TestSceneBmsBeatorajaPmsConsumers|FullyQualifiedName~TestSceneBmsVisualCulling|FullyQualifiedName~TestSceneBmsJudgementCheckpoints|FullyQualifiedName~BmsNoteExecutionTest|FullyQualifiedName~TestSceneBmsNoteExecution|FullyQualifiedName~BmsLongNoteExecutionTest|FullyQualifiedName~TestSceneBmsLongNoteExecution|FullyQualifiedName~BmsChargeNoteExecutionTest|FullyQualifiedName~TestSceneBmsChargeNoteExecution)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=long-lifecycle-execution.trx' --results-directory artifacts/hitobject-p3a0/p3a0-4/combined3
if ($LASTEXITCODE -ne 0) { throw 'Combined long/lifecycle/note/execution tests failed.' }
```

每份新增场景 TRX 的 `P3A0_CN` 行包含 `rows`、`recordedRows`、`visuals`、`scores`；JSON 轨迹另提取至对应目录的 `cn-traces.jsonl`。测试输入和断言随仓库提供，日志仅作本地证据，不需要本机日志来重建期望。

### 临时适配与未验收范围

| 临时适配/边界 | 后续去向 |
| --- | --- |
| LN/CN 共享 binding 借用旧 Controller 和 legacy sink；三种规则尚未拆分 | P3a 完整 Rule 迁移一并删除；CN 不通过 LN 完整流程实现规则。 |
| Drawable/entry 的既有寿命及 `UpdateColumnFrame` → post-result 调度桥 | P3b/P5a 完成表现和调度迁移时收敛；本步只证明 CN 主头结果后真实表示仍可用。 |
| 只读状态映射调用旧图形接口，判定待判尾与视觉活跃列表分别维护 | P3b 删除剩余绘制命令接口及 Drawable 直接 Controller 查询。 |
| 测试显式开启 Note/LN/CN 受限路径，固定输入/帧加真实移除边界重放 | P3a-0.7/P5a 验证计划生成，全部模式完成前默认游玩继续旧路径。 |
| ST-1 的旧端点字段、真实提交栈及仅补充栈的撤销证据 | P3a-0.6 补完整恢复；完整 CN Rule 迁移前单独决定兼容修订。 |
| 测试反射探针及调用者重建 | P5b 在正式调度与表现边界稳定后清理迁移观测工具。 |

- **执行等价：** CN 头后尾存续、键盘提前/正常/被动尾、PMS 宽限及救回、scratch 反向、同步端点和中间计分/血量在本步计划上通过三方对照。
- **生成等价：** 尚未验收。记录端通过 `GetColumn()` 消费计划；引擎 `ProcessFrame` 仍使用旧 Playfield 调度，真实移除来源也未替换为独立数据计划生成器。
- **其余未验收：** HCN 新路径、地雷规则、完整 Rule/表现分离、Reapply/RA-1、代次及全生命周期/暂停恢复、AS-1、完整音频混音/视觉反馈与性能/分配/保留内存。没有新增玩家文案或玩法变更。
- **交接：** P3a-0.4 实现、318 项最终组合、主 agent 静态审查及 47 项最终独立复验已完成。主 agent 确认本步提交后，由全新实施 agent 进入 P3a-0.5；本 agent 只完成本卡，未开始下一卡、未 push、未 merge。

<a id="p3a0-5"></a>

## P3a-0.5：HCN 独立身体成员、逐对象累计与语义 tick

状态：**已完成，主 agent 技术验收通过**。本卡从干净的 P3a-0.4 HEAD `b9a80e4d520cb65dd787b12a9e1fae439d00fa78` 开始；实现方与主 agent 的最终同版 374 项组合均通过，未进入 P3a-0.6。产出为包含本节的 `refactor(judgement): preserve hell charge body execution` 独立提交。

### 实现与临时边界

- 列上下文增加按对象槽位维护的 `activeBodySlots`，与待判尾、主结果、Drawable 视觉活跃列表分别维护。`BmsLongNoteState.HasActiveBody` 只读投影现有 Controller 身体状态；尾已完成仍可登记身体，末端更新或开始移除后退出身体成员。移除当前身体时继续访问后一个槽位。
- 同质可批处理列沿用旧头登记顺序；混合列沿用实际 `AliveEntries` 顺序。身体阶段从当前框架表示读取原模拟时间、elapsed、最终物理按住状态和已有自动持有值。记录宿主可按相同计划直接执行 `UpdateRegisteredBodies`。
- `IBmsJudgementOutput.ApplyBodyTick` 以对象身份、按住效果、scale 和真实操作帧传递身体血量效果，生产适配调用原 Playfield/Health 路径。公共出口没有增加 `OnHellChargeHeadPoor`、`KeepBodyAlive` 等模式或绘制命令；输出不拥有身体索引。
- HCN 头 POOR 在旧 Controller 的通知位置进入同一个 `PublishState`：只读表现先完成旧 `KeepBodyAlive` 映射，内部索引随后发布，然后依次提交合成头、合成尾；两个端点回调返回前都能观察到 `TailJudged == false`。头 POOR 保留旧路径的“没有框架主结果”语义。
- 提前成功尾后只读表现仍保持身体，至原结束时点退场。旧 Controller、`BmsHellChargeBodyTracker`、`BmsHellChargeHistory` 继续作为本阶段计算桥；每对象 Controller 独立持有累计器与历史。本卡未改变累计、窗口、历史算法，没有 catch-up tick 循环，也没有引入 HCN 继承 CN 的 Rule 流程。完整 LN/CN/HCN Rule 拆分、旧 sink 删除及表现/调度迁移仍按 P3a/P3b/P5a 推进。
- 只有测试显式开启 `EnableLongNoteExecution`；普通生产默认仍走旧路径。地雷继续全部走旧经过路径。P3a-0.2/0.3 的 HCN 拒绝用例已由 HCN 正向测试替代，保留未知长按模式的拒绝检查，两个原执行组的实际用例数量仍为 29/38。

### 真实生产、记录输出和中间观察

[TestSceneBmsHellChargeNoteExecution](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/TestSceneBmsHellChargeNoteExecution.cs) 的每例先运行真实旧生产，再重新加载受限新生产；各自与无 Drawable 的记录上下文比较同步轨迹。真实路径使用 Player、容器及 `Playfield.UpdateSubTree`，记录端消费同一手动供给计划和观测到的移除边界，**没有声称独立调度或生产计划生成已完成**。

| 场景 | 关键断言 |
| --- | --- |
| 被动头 POOR 后身体 | 从实际 `NextPassiveJudgementTime + 1` 取得 3211；头状态发布先于身体索引，两合成端点先于尾完成；3211 重复遍历无累计，3412 一次身体扣血，重按后按原有符号累计于 3614 恢复。主结果始终未提交。 |
| 提前成功键盘尾 | 3980 成功尾后无持有/待判尾成员，身体仍登记；物理释放后 3998 继续恢复。真实 entry/Drawable 和寿命持续可用，4000 身体停止，4400 实际回池。 |
| 大 elapsed | 3977 一次 977ms 更新只产生一次 tick；重复 3977 不追加；3978、3979 各释放一次旧积压，成功尾后 3998 再产生一次。模拟切片及 tick 时间保持不变。 |
| Scratch 成功尾 | 独立实测旧 HCN 的输入 3990 / 模拟 3997：尾 `EventTime`、旧 `ApplicationTime`、血量事件时间为 3990，真实提交栈为 3997；新生产和记录输出一致。真实本例速率为 1.25；纯记录边界另覆盖 1.5。HCN 完整撤销及血量/Controller 恢复留待 P3a-0.6。 |
| 同列同时间双对象 | 先判谱面对象 1，再判对象 0；3201 仅对象 1 tick、3301 仅对象 0 tick，3502 batch 按头登记顺序、mixed 按真实 alive 顺序；4000 对象 0 结束后对象 1 仍得到本次更新。两个真实 Controller 与记录状态分别相等，输出携带各自对象身份。 |

前四个计划分别覆盖可批处理/混合列、可见/真实离屏，共 16 个行为场景；双对象覆盖两种列顺序，共 18 个行为场景，加构造测试为 **19 项**。32 份单对象旧/新生产轨迹包含 **632 行同步观察**，逐行等于记录出口；另外精确比较 **128 条表现进入/退出记录、704 个中间计分/血量快照**。同列双对象另输出 4 份旧/新生产轨迹。端点计分后的回调显式断言计分事件已增加、血量历史尚未增加，随后健康处理器回调才增加该端点的血量记录；身体 tick 前后也保存完整血量历史。

[BmsHellChargeNoteExecutionTest](../osu.Game.Rulesets.BmsRuleset.Tests/Normal/Gameplay/Judgement/BmsHellChargeNoteExecutionTest.cs) 的 **13 项**纯执行测试覆盖：按下/被动头 POOR 的同步索引；同时间新头零累计；同时间成功尾不把此前 elapsed 变成恢复；重复 timestamp 和 post-result 重访不重复累计；正负 601ms 大步与每更新至多一 tick；双对象独立累计与回退到各自最后历史帧；反转头登记顺序；更新中移除第一个身体不跳过下一对象；scratch 的事件/提交时间与记录端合成栈边界。记录端没有 Drawable、Column 或 Container，索引维护仍由同一执行上下文完成。正常历史单元组另有 6 项回归，不以本卡这些局部回退检查冒充 P3a-0.6 的完整生命周期验收。

两套既有跨列场景保留原预期并加入旧/新路径参数，实际共 **18 项**（16 个行为 + 2 个构造）：

- [HCN/地雷顺序](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/TestSceneBmsBeatorajaHcnLandmineFrameOrder.cs) 继续核对全部地雷先于身体，含同列/跨列、饱和与未饱和血量、最终物理释放状态及原回放回退检查点。例：未饱和地雷/身体中间血量 `[0.14, 0.16]`；上限顺序 `[0.9, 1]`。
- [HCN/其他列输入顺序](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/TestSceneBmsBeatorajaHcnFrameOrder.cs) 核对身体先于后续列输入，保留 `[1, 0.97]` 和释放身体后的 `[0.925, 1]` 等中间轨迹，以及严格 `>200ms` 的单 tick 边界。

### 构建、定向回归与证据

环境沿用 Windows x64、.NET SDK `10.0.109`、`net10.0`、framework `2026.918.0`、Release、headless Player。生产及测试项目构建均为 **0 警告、0 错误**，始终使用 `--no-restore -p:BuildProjectReferences=false`。本地证据根目录为 `artifacts/hitobject-p3a0/p3a0-5/implementation/`。

| 批次 | 通过 | 失败 / 跳过 | 证据 |
| --- | ---: | --- | --- |
| 第一版 HCN/frames | 40 | 8 / 0 | `hcn-first.trx`、`.log`；失败保留，不计完成证据。 |
| 调整输入计划后 HCN/frames | 48 | 0 / 0 | `hcn-second.trx`；尚未加入真实双对象。 |
| 最终 HCN/frames | 50 | 0 / 0 | `hcn-third.trx`：HCN 32 项 + frames 18 项。 |
| 首次全组合 | 365 | 9 / 0 | `combined-first.trx`、`.log`；旧 checkpoint 输入观测丢行，保留修复前证据。 |
| 输入 caller 探针修复后真实 HCN/frames/checkpoint | 50 | 0 / 0 | `hcn-checkpoints.trx`：真实 HCN 19 + frames 18 + checkpoint 13。 |
| 输入 caller 修复后全组合 | 364 | 10 / 0 | `combined-final.trx`；普通 Note 被动观察丢行，保留修复前证据。 |
| 普通 Note caller 修复后全组合 | 363 | 11 / 0 | `combined-final2.trx`；长按 post-result 观察丢行，保留修复前证据。 |
| 主 agent 首次独立 HCN/frames/checkpoint | 63 | 0 / 0 | `../supervisor/supervisor-hcn-frames-checkpoints.trx`；小组合通过不能代替全组合。 |
| 容器 caller 链补全后实施方全组合 | 364 | 10 / 0 | `combined-final4.trx`；框架长按被动观察仍不稳定。 |
| 同一二进制主 agent 独立全组合 | 374 | 0 / 0 | `../supervisor/supervisor-combined-final.trx`；与上一行一过一失，不能作为稳定性完成证据。 |
| 最终实施方全组合 | 374 | 0 / 0 | `combined-final5.trx`，`tests-build7.log` 对应最终二进制。 |
| 最终同版主 agent 独立全组合 | 374 | 0 / 0 | `../supervisor/supervisor-combined-final2.trx`；静态审查、fixture、只读 sibling 检查及技术验收通过。 |

第一版八项失败都发生在真实旧生产执行阶段：头 POOR 测试给定 3201，尚未越过真实 3210 截止；大 elapsed 测试在 3603 已耗完积压，跳到尾输入后剩余 18ms 不足再 tick。修正供给计划为实际截止后 1ms，并把大 elapsed 置于 3977，保留同一组严格重复时间/单 tick/尾后 tick 断言。首版源码 `TestSceneBmsHellChargeNoteExecution.first.cs` 和 `production-first.patch` 与失败日志一并保存；没有为这些失败修改生产算法。

各次全组合失败分别定位并保存，不能用其间小组合或同二进制的单次通过代替完整验收：

- `combined-first` 九项旧 checkpoint 缺少按下输入的 `input.queue` 进入/退出行，release 行及后续结果仍存在。此前真实帧测试预热了 `BmsPlayfield.OnPressed`，将 `QueueInput` 内联到调用者，真实 replay 因而只观察到 2/4 个入队。记录器在安装叶探针后重建 `OnPressed/OnReleased` 两个原 caller。
- `combined-final` 十项 checkpoint 缺普通 `note.passive`。普通 `DrawableBmsNote<TCol>` 实际从 `UpdateSubTree` 和 `OnKilled` 调用被动检查，原记录器仅重建了长按适配；补齐实际已绑定普通 Note 具体泛型类型的这两个 caller。
- `combined-final2` 十一项 checkpoint 缺 `column.layout/long.finish` 及其嵌套身体观察。逐行去序号对齐后，所有剩余实际行都与 fixture 匹配，没有未匹配的实际行。完整链为具体长按 post-result → `DrawableBmsHitObject.UpdateColumnFrame` → 具体长按 deferred 入口 / 容器 layout/deferred → Engine / Playfield → DrawableRuleset；最终记录器按叶到调用者顺序重建这条链。保留 `.checkpoint-diff.json` 及生成脚本，证明修复针对观测边界。
- `combined-final4` 十项 checkpoint 缺长按 `long.passive`，前述三类检查点已恢复。相同二进制的主 agent 全组合通过，显示遗漏取决于早期分层编译/虚调用内联。只读框架源码确认 `DrawableHitObject.UpdateResult` 调用具体 `CheckForResult`，且由框架 `UpdateAfterChildren/OnKilled` 与 BMS 子树/列帧调用；记录器进一步按叶到调用者顺序重建该中间层，未编辑 sibling 源码。

这些修改只重建测试期 caller，不改变原方法体、旧 fixture 或任何同步断言。`combined-final3.log` 来自一次在构建尚未完成时提前启动、随后立即中断的无效测试调用，没有 TRX，不计作验证结果；后续运行只在对应日志确认有效构建后开始。

最终两套 374 项均按实际 TRX 统计：long 106、frames 18、lifecycle 28、note 70、旧 checkpoint 13、Note/LN/CN/HCN 执行 29/38/34/32、身体历史 6，共 23 个测试类。最终再次逐行核对 **11 个固定场景、3,487 个旧检查点**；HCN 三方统计仍为 632/632 行、128 条表现记录、704 个计分/血量快照和 4 份双对象轨迹。最终 `*.summary.json`、`combined-final5.checkpoint-diff.json` 与 `checkpoint-summary.json` 保存核对结果；旧 fixture 未改变，所有只读 sibling 工作区保持干净。

命令可从仓库根目录运行本地 `commands.ps1` 重现构建、HCN 和组合回归；`extract.ps1` 只从各 `UnitTestResult.Output.StdOut` 提取本地 stdout，避免 TRX 全局 stdout 重复统计。单对象和双对象轨迹分别为 `*.hcn-traces.jsonl` / `*.hcn-multi.jsonl`，统计为 `*.summary.json`。可复现输入、断言和测试探针随源代码提供，本地日志不是唯一验证依据。

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj -c Release --no-restore -p:BuildProjectReferences=false
dotnet build osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsHellChargeNoteExecutionTest|FullyQualifiedName~TestSceneBmsHellChargeNoteExecution|FullyQualifiedName~TestSceneBmsBeatorajaHcnFrameOrder|FullyQualifiedName~TestSceneBmsBeatorajaHcnLandmineFrameOrder)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=hcn.trx' --results-directory artifacts/hitobject-p3a0/p3a0-5/reproduce

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~TestSceneBmsHellChargeNoteExecution|FullyQualifiedName~TestSceneBmsBeatorajaHcnFrameOrder|FullyQualifiedName~TestSceneBmsBeatorajaHcnLandmineFrameOrder|FullyQualifiedName~TestSceneBmsJudgementCheckpoints)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=hcn-checkpoints.trx' --results-directory artifacts/hitobject-p3a0/p3a0-5/reproduce

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsLongNoteJudgementControllerTest|FullyQualifiedName~TestSceneBmsLongNoteJudgement|FullyQualifiedName~TestSceneBmsBeatorajaScratchInput|FullyQualifiedName~TestSceneBmsLongNoteLifecycle|FullyQualifiedName~TestSceneBmsJudgementIndexes|FullyQualifiedName~TestSceneBmsPauseRewindLongNote|FullyQualifiedName~TestSceneBmsReplayRewindTiming|FullyQualifiedName~BmsJudgementSelectorTest|FullyQualifiedName~TestSceneBmsColumnJudgement|FullyQualifiedName~TestSceneBmsBeatorajaPmsConsumers|FullyQualifiedName~TestSceneBmsVisualCulling|FullyQualifiedName~TestSceneBmsJudgementCheckpoints|FullyQualifiedName~BmsNoteExecutionTest|FullyQualifiedName~TestSceneBmsNoteExecution|FullyQualifiedName~BmsLongNoteExecutionTest|FullyQualifiedName~TestSceneBmsLongNoteExecution|FullyQualifiedName~BmsChargeNoteExecutionTest|FullyQualifiedName~TestSceneBmsChargeNoteExecution|FullyQualifiedName~BmsHellChargeNoteExecutionTest|FullyQualifiedName~TestSceneBmsHellChargeNoteExecution|FullyQualifiedName~TestSceneBmsBeatorajaHcnFrameOrder|FullyQualifiedName~TestSceneBmsBeatorajaHcnLandmineFrameOrder|FullyQualifiedName~BmsHellChargeHistoryTest)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=combined.trx' --results-directory artifacts/hitobject-p3a0/p3a0-5/reproduce
```

所有测试均带过滤器且排除 `.Performance.`，未运行 benchmark。没有新增玩家文案或玩法变更。旧 checkpoint fixture 的 SHA256 保持 `D0F84AFD210466CD4462969BACA785CB5B0EDCD6258402398BCB765FC15E316F`。

- **执行等价：** 本卡供给计划中的 HCN 身体存续、身份/索引、累计边界、顺序和同步输出通过三方对照；最终 374 项实施组合及主 agent 独立同版 374 项组合均通过。
- **生成等价：** 未验收。核心阶段计划生成由 P3a-0.7/P5a 验收；AS/Mod 适配和采样语义移至 P6，不把更频繁执行 Mod 作为本卡的等价依据。
- **剩余项：** 完整 Rule/表现分离、地雷迁移、Reapply/RA-1、代次、ST-1/HCN 完整撤销恢复、暂停恢复、实际音频混音/视觉反馈与性能/分配/保留内存仍待后续里程碑。P3a-0.6 特别需要真实验证 HCN `CompleteAtPause` 与表现映射：旧暂停会清 `LongNoteStarted` 而不调用 ClearVisual sink，当前只读映射在尾完成/停止时的 ClearBody 投影尚未验收；本卡没有宣称暂停等价。
- **交接：** P3a-0.5 实现、最终两套同版 374 项组合和主 agent 技术验收已完成。主 agent 确认本卡提交后，由全新实施 agent 进入 P3a-0.6；本 agent 只完成本卡，未开始下一卡、未 push、未 merge。

<a id="p3a0-6"></a>

## P3a-0.6：真实生命周期、原地重应用与撤销双时点

状态：**已完成，主 agent 技术验收通过并已提交**。本卡从干净 HEAD `5d843c3a0b4947a4b1e94ebade25765ac3b1e623` 开始；实施方与主 agent 的两套最终同版 417 项组合均通过，产出提交为 `9f7f3ad043647b567744f3dbda32d009ec89c7b9`。未进入 AS-1 或 P3a-0.7。默认生产开关仍走旧路径，新执行只由测试显式启用。

### 范围与最小适配

- `DrawableBmsHitObject` 每次真实 `OnApply` 增加表示代次，`OnFree` 只解除该代次的输出关联。`BmsDrawableJudgementOutput` 的具体绑定保存对象身份、Drawable 和代次；解绑解除结果订阅与表现快照，旧回调/旧代次解除通知在原地重应用和跨对象池复用后失效。框架 Result 引用仍是唯一主结果权威。
- Note/LN 的具体 `OnApply` 在窗口或 Controller 准备后刷新当前表示。活跃 entry 的原地重应用不再依赖不存在的 `AddDrawable` 回调；阶段登记、同时间位置和适用历史保留。共享 `RefreshNoteConfiguration` 同时刷新记录侧与生产侧的配置/索引；缓存旧头时间以移除旧排序键，配置重排使用稳定顺序。
- 真正移除仍由原容器先解除索引，再进入原 `OnKilled` 的停止样本、末次同步判定，最后回池/解绑。没有改框架主结果撤销通知、`Reset()` 或历史恢复的原顺序，没有引入通用生命周期事务框架。
- 暂停最终冻结状态发布使用 `BmsFrameContext.IsPauseCompletion`。Controller 内原同步端点与清体回调保持原位置；仅返回后的最终冻结快照不额外推导清体/退场，输出仍保存新快照并由执行上下文维护索引。下一次普通操作 `begin` 重新借用其自身帧，不沿用暂停标记。公共输出没有增加模式绘制命令或长按接口。

### 真实旧/新生产与无 Drawable 记录对照

新增 [TestSceneBmsJudgementLifecycleExecution](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/TestSceneBmsJudgementLifecycleExecution.cs) 共 **43 项**（42 个行为用例与 1 个构造测试）。真实路径来自已加载 Player、实际 entry/容器/Drawable，原地重应用调用 `HitObject.ApplyDefaults`，移除和再激活调用 `Playfield.UpdateSubTree`，暂停调用 `Player.Pause/Resume`。记录侧消费同一输入、帧和实际观测到的激活/移除顺序；这里验证执行等价，不宣称独立计划生成。

| 场景 | 行为用例 | 新增严格边界 |
| --- | ---: | --- |
| Note/LN/CN/HCN 原地重应用，batch/mixed | 8 | 实际 `OnFree→OnApply`、无 `OnKilled`；entry/Result/Controller 身份保留，代次 +1，RA-1 原行为分别断言，几何/模拟缓存失效，逻辑/表现映射各一份，原位置不变；旧回调与旧代次解除失效。 |
| Note/LN/CN/HCN 真实移除及同 Drawable 池复用，batch/mixed | 8 | `OnKilled` 前已退出候选/尾/身体；停止样本早于末次结果，末次检查的表示仍可借用；同步结果回调不重新加入索引；Note/LN 各 1 个计分事件，CN/HCN 各 2 个端点，重复遍历不追加。实际同一 Drawable 随后绑定 9000 的另一个对象。 |
| Note/LN/CN/HCN 改变排序键 | 4 | 实际 ApplyDefaults 后旧 3000 键删除，新 2990 键及相邻同时间对象各一份；记录侧直接调用同一配置刷新后索引相等；改回同时间仍保留 tie order。 |
| 四类主结果普通撤销，batch/mixed | 8 | `Playfield.RevertResult → entry 通知进入 → Drawable.OnRevertResult → entry 通知退出 → Result.Reset 后` 共五个检查点；前四个仍读取旧框架结果与状态。Reset/Controller/血量恢复完毕、下一输入前候选正确，并重新选中同一头。 |
| 非空 PMS HCN 历史 Reapply | 1 | 回放配置明确开启身体历史；先真实累计身体并于 3300 释放、3400 保留 pending release，再重应用。Controller、累计器、身体历史和释放列表身份/内容保留；3350/3299 普通回退逐层比较记录侧累计及历史，随后重新前进。RA-1 的主结果时间改写仍保留。 |
| LN/CN/HCN 回池后普通回退再激活 | 3 | 在实际 Register 进入时 Controller 已恢复头/尾状态，退出时表示与阶段索引可用；真实池复用同一 Drawable、新代次绑定原逻辑对象，再次释放只提交应有尾结果。 |
| 真实 Player 暂停/恢复 | 8 | LN/CN/HCN 各覆盖 3600/3990 暂停，另有 HCN 头 POOR、提前成功尾。反复经过恢复目标与暂停前一刻，结果/血量/Controller 冻结；跨恢复结束时间后首批输入、身体及端点计数继续与旧路径和记录输出相等。 |
| CN/HCN scratch ST-1 完整分层恢复 | 2 | 输入 3990 / 真实提交 3997，速率 1.5；3997 保留，3996 撤销合成/血量而控制器按旧事件时间保留尾，3989 才恢复 pending；HCN 独立检查身体成员与累计。 |

最终组合提取到 **76 份旧/新生产轨迹、246 个逻辑检查点及 246 个计分/三层血量快照**。末次检查为 **36/36** 个真实/记录同步观察，主结果撤销为 **80/80** 个同步观察；暂停另有 **16 份轨迹、80 个表现快照**。生产状态、结果、pending/holding/body 索引及 HCN 累计直接与记录侧比较；old/new 的计分和全部血量历史逐检查点相等。16 份 Reapply 和 16 份移除记录单独保留。

### RA-1、ST-1 与暂停表现的兼容结论

**RA-1 不作隐性玩法修复。** Reapply 保留逻辑 Controller、结果对象及适用历史，但 `IsAutomaticallyHeld` 仍为 true→false。框架无条件写 `TimeOffset=0`，所以未判 Note/LN 的 `RawTime` 也会变成对象结束时间；CN/HCN 已提交头的 `RawTime/TimeAbsolute` 从 3003 变为 4000，头 endpoint 与旧计分事件仍为 3003。记录适配完整模拟这次 setter，包括未判结果。后续普通回退继续遵循被改写的主结果时间，不覆写主结果来伪装满足目标契约。是否改变旧重应用玩法保留为独立修复决策。

**ST-1 完整恢复是按层恢复，不是把所有层强行改为同一时间。** 真实 CN/HCN 在 3996 都撤销 3997 的合成提交；Health 的私有回退历史使用实际提交时间，因此 Normal、Hard、ExHard 三层与公开 GaugeHistory 恢复到尾前。Controller 则仍保存旧 `TailTime=3990` 与 `TailJudged=true`，pending tail 为空；HCN 身体仍活跃。3989 才清除尾状态、恢复 pending tail。旧、新和记录输出均复现这一不一致；监督已同意以此窄兼容例外界定本卡验收。完整 CN Rule 迁移前仍须单独决定修复或保留 ST-1，并保留前后证据。

**暂停修的是适配投影。** `pause-third.trx` 保留三处真实差异：CN 3600 提前 POOR、HCN 3600 POOR、HCN 3990 成功暂停。逻辑、结果、血量和恢复后行为已相等，但旧暂停表现为 Alpha 1、寿命 4310；新适配额外 `Retire/ClearBody` 变为 Alpha 0、寿命暂停时刻。最终冻结帧标记只去掉这次额外投影，其他原反馈保持。最终 8 个暂停用例均通过。HCN 已经头 POOR 或提前成功尾时，旧 `CompleteAtPause` 不再结算该尾；身体状态在恢复期间冻结，恢复结束后沿用原身体语义。本卡没有扩张为“暂停所有 HCN 一律清身体”的玩法修改。

### 构建与可重现证据

环境为 Windows x64、SDK `10.0.109`、`net10.0`、framework `2026.918.0`、Release/headless Player。最终两项目构建都是 **0 警告、0 错误**。生产与测试构建始终带 `--no-restore -p:BuildProjectReferences=false`；后期额外关闭节点复用/共享编译以避免复用节点的长等待，未写入 sibling 项目。

| 批次 | 通过 / 失败 | 证据与解释 |
| --- | --- | --- |
| ST-1 首个真实三方复现 | 3 / 0 | `implementation/st1-first.trx`，两个 CN/HCN 行为与构造；旧/新均保留控制器 3990 边界。 |
| 生命周期初稿 | 3 / 16，6 / 13 | `lifecycle-first.trx` / `lifecycle-second.trx`；新测试探针反射命中了重载/继承方法，修正为声明方法并确保部分安装失败时清理。 |
| 首批生命周期/重应用 | 19 / 0 | `implementation/lifecycle-third.trx`。 |
| 加入排序键及撤销双时点 | 31 / 0 | `implementation/lifecycle-fourth.trx`。 |
| 暂停入口调试 | 0 / 8，0 / 8 | `pause-first.trx` 为 non-replay 时钟未推进；`pause-second.trx` 为探针读取已回池表示。未改生产寿命；改用 entry 权威结果及实际活跃表示查询。second 的同名重跑日志保留覆盖警告，不能作为两个独立证据计数。 |
| 暂停真实差异 | 5 / 3 | `implementation/pause-third.trx`，三处额外表现投影的修复前旧/新轨迹保留。 |
| 完整新增组历史校正前 | 42 / 1 | `implementation/lifecycle-fifth.trx`；纯记录计划错误地固定为未按住，真实列已按住。改为同帧实际物理按住值，增加所有 HCN 检查点的累计直接对照。 |
| 完整新增组 | 43 / 0 | `implementation/lifecycle-sixth.trx`。 |
| 最终共享配置收口后新增组 | 43 / 0 | `final/lifecycle.trx`，最终两份 build 日志对应此二进制。 |
| 最终实施方同进程完整组合 | **417 / 0** | `final/combined.trx`，全部 417 项成功，0 跳过。 |
| 主 agent 同版独立完整组合 | **417 / 0** | `supervisor/combined.trx`、`supervisor/summary.json`；前后 DLL 哈希与冻结版本相等，旧 fixture、diff check、只读 sibling 状态均通过，技术验收通过。 |

证据根目录为 `artifacts/hitobject-p3a0/p3a0-6/`。`implementation/commands.ps1` 提供构建与最终过滤器；`implementation/extract.ps1` 仅统计 `UnitTestResult.Output.StdOut`。`final/combined.summary.json`、各 `*.jsonl` 和 `combined.checkpoint-summary.json` 保存匹配数量。417 项 = P3a-0.5 的 374 项 + 本卡 43 项，共 24 个测试类。最终再次精确核对 **11 个旧固定场景、3487 行、0 差异**；旧 fixture SHA256 保持 `D0F84AFD210466CD4462969BACA785CB5B0EDCD6258402398BCB765FC15E316F`。

可从仓库根目录执行（重跑应选择新的结果目录）：

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj -c Release --no-restore -p:BuildProjectReferences=false -nr:false -p:UseSharedCompilation=false
dotnet build osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false -nr:false -p:UseSharedCompilation=false

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~TestSceneBmsJudgementLifecycleExecution&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=lifecycle.trx' --results-directory artifacts/hitobject-p3a0/p3a0-6/reproduce

dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsLongNoteJudgementControllerTest|FullyQualifiedName~TestSceneBmsJudgementLifecycleExecution|FullyQualifiedName~TestSceneBmsLongNoteJudgement|FullyQualifiedName~TestSceneBmsBeatorajaScratchInput|FullyQualifiedName~TestSceneBmsLongNoteLifecycle|FullyQualifiedName~TestSceneBmsJudgementIndexes|FullyQualifiedName~TestSceneBmsPauseRewindLongNote|FullyQualifiedName~TestSceneBmsReplayRewindTiming|FullyQualifiedName~BmsJudgementSelectorTest|FullyQualifiedName~TestSceneBmsColumnJudgement|FullyQualifiedName~TestSceneBmsBeatorajaPmsConsumers|FullyQualifiedName~TestSceneBmsVisualCulling|FullyQualifiedName~TestSceneBmsJudgementCheckpoints|FullyQualifiedName~BmsNoteExecutionTest|FullyQualifiedName~TestSceneBmsNoteExecution|FullyQualifiedName~BmsLongNoteExecutionTest|FullyQualifiedName~TestSceneBmsLongNoteExecution|FullyQualifiedName~BmsChargeNoteExecutionTest|FullyQualifiedName~TestSceneBmsChargeNoteExecution|FullyQualifiedName~BmsHellChargeNoteExecutionTest|FullyQualifiedName~TestSceneBmsHellChargeNoteExecution|FullyQualifiedName~TestSceneBmsBeatorajaHcnFrameOrder|FullyQualifiedName~TestSceneBmsBeatorajaHcnLandmineFrameOrder|FullyQualifiedName~BmsHellChargeHistoryTest)&FullyQualifiedName!~.Performance.' --logger 'trx;LogFileName=combined.trx' --results-directory artifacts/hitobject-p3a0/p3a0-6/reproduce
```

供监督同版复验的冻结 SHA256 见 `final/binary-hashes.json`：生产 DLL `6005061B43A9F3E7C6C6AEA62D5F1C0CC4BED55F8DDF50EB3FF9AD13402E13DD`；测试 DLL `0736EFCEED29353ECCFA7E64526C49F05E675C26AB55F6B4825E9A3CC8AD67F3`。最终通过后停止源码修改与构建，只整理文档/证据。

- **执行等价：** 本卡供给计划中的真实生命周期、RA-1、ST-1 分层恢复及暂停输出通过旧/新/记录对照；实施方与主 agent 两套最终同版 417 项组合均通过。监督独立计数同为新生命周期 43 项、76 份逻辑轨迹及 Reapply/Removal/Revert/Pause 各 16 份记录。
- **未覆盖：** 完整 Rule/表现拆分、独立纯视觉观察登记和核心生命周期/阶段计划生成、地雷迁移、真实音频混音、全面视觉渲染、性能/分配/保留内存仍按 P3a-0.7/P3a/P3b/P4/P5 推进，不能由本卡推断完成。AS-1 与 Mod 专项适配、采样修复及验收仅在 P5b 核心完成后的 P6 处理。
- **范围约束：** 测试均带过滤器并排除 `.Performance.`；未运行 benchmark。没有玩家玩法变更或新文案；`osu`、`osu-framework` 及 `rulesets` 仅作只读引用，未 push、未 merge。
- **交接（2026-10-01 修订）：** P3a-0.6 已完成并由主 agent 技术验收；其产出提交为 `9f7f3ad043647b567744f3dbda32d009ec89c7b9`，两套同版 417 项组合通过。AS-1 停止且未实施，不再阻塞 P3a-0.7。主 agent 审查计划修订后，由 `fork_turns:none` 的全新实施 agent 串行开始 P3a-0.7 核心真实入口计划生成；每卡经主 agent 独立验收后再进入下一卡。P6 在 P5b 核心验收后处理 AS/Mod 适配与最终集成。本 agent 不进入后续工作。

<a id="p3a0-7"></a>

## P3a-0.7：由真实核心入口生成受限 Note/HCN 计划

状态：**进行中，实施结果待主 agent 最终独立验收，未提交。** 实施日期为 2026-10-01，从干净 HEAD `329018cd3497b26499cc556ab18e751796273cce` 开始。设计依据是该 HEAD 的核心/Mod 分阶段计划；AS-1 已停止，所有新增生成场景均使用普通玩家或普通回放。先冻结无 AS 旧核心基线，主 agent 独立通过基线后才开始生产改动。

### 改动前基线与真实输入

新增不可变 [judgement_p3a07_core.json](../osu.Game.Rulesets.BmsRuleset.Tests/reference_fixtures/judgement_p3a07_core.json) 保存 **8 场景、10,619 条完整原始行**，SHA256 为 `CF0F8984C46B31E574CEA362FAF061D96666CB2341B91CDD1419843A3A143B38`。捕获时生产 diff 为空，旧生产 DLL 为 `6005061B43A9F3E7C6C6AEA62D5F1C0CC4BED55F8DDF50EB3FF9AD13402E13DD`。实施方 `baseline/frozen-second.trx` 和主 agent `supervisor-baseline/core-baseline.trx` 均 **10/10、零失败/跳过**；基线测试 DLL 为 `F4E1EDA2A4B57968587FEEBF10EBD6D30DC0812DE6541312BDD372C5A203D477`。这项审批仅指改动前基线，不代表最终实现验收。

历史 [judgement_p3_checkpoints.json](../osu.Game.Rulesets.BmsRuleset.Tests/reference_fixtures/judgement_p3_checkpoints.json) 仍为 11 场景、3,487 行，SHA256 保持 `D0F84AFD210466CD4462969BACA785CB5B0EDCD6258402398BCB765FC15E316F`。它包含历史 AS 场景，保留给 P6；本卡没有使用新生产输出重写任一 fixture，也没有将该历史 AS 测试组列入核心组合门槛。

[BmsCoreSchedulingRecorder](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/BmsCoreSchedulingRecorder.cs) 是独立测试记录器。每条原始行保留 frame/step/batch、模拟时间和 elapsed、调用来源、对象身份、观察代次、真实绑定代次、alive-map/AliveObjects/实际 `IndexOf` 三种位置、输入时间和方向、物理按住方向、Controller 状态和身体累计、结果/端点、候选与活跃索引、计分及中间血量。

- 普通键盘场景使用真实 `BmsInputManager`，由硬件形态 `InputHandler` 提供 `KeyboardKeyInput`，经过实际输入分发和 Player/Playfield 更新；覆盖正反 scratch、普通键、HCN、正常与 deferred，以及地雷仍走旧阶段的混合列。
- 父级输入场景先执行真实 `KeyboardKeyInput.Apply(CurrentState, manager)`，再由实际 `BeginGameplayFrame` 在 3000 冲刷父级输入；两个后续独立批次在 3001 处理。测试只供给物理输入和时钟，不向新调度器预注入期望阶段。
- 回放场景运行真实 `DrawableRuleset.UpdateSubTree`、`FrameStabilityContainer` 和 `BmsFramedReplayInputHandler`，向前追赶至 4003，再回到 2998。为使外层分组不受机器速度影响，完整 fixture 的测试探针只重置当前 Player 的 CPU 预算 stopwatch；每个模拟切片和回放输入仍由原框架生成。另一个 smoke 使用未修改的真实预算，逐项比较全部输入/效果/结果轨迹。

### 实现范围与有序逻辑投影

新增 [BmsCoreJudgementSchedule](../osu.Game.Rulesets.BmsRuleset/UI/Gameplay/Judgement/BmsCoreJudgementSchedule.cs) 由列上下文持有，以对象身份和每次登记独立颁发的唯一 binding token 保存可复用计划。宿主登记维护 Drawable 加局部代次到逻辑 handle 的映射，核心 handle 只包含逻辑对象和 token，没有 Drawable 引用；原 `BindingGeneration` 观测保持不变。引擎提供 epoch/step/batch；容器在原生命周期边界提供布局顺序、实际子树顺序和反向活跃长按 deferred 顺序。普通 Note/HCN 的被动检查和 post-result 由该计划执行，当前项移除不压缩游标，旧绑定失效后不能借用新表示；同步新批次、嵌套计划或回退会使外层余下工作失效。单项重绑只使旧 handle 失效，其余有效项继续执行。阶段去重键包含 epoch、batch 和绑定变更；回退历史恢复另以 epoch/step 去重，负 elapsed 的纯表现重复不再次恢复或判定。

这是**容器生命周期边界驱动的 Note/HCN 受限计划**。默认生产仍走旧路径，新路径由测试显式启用；框架 `CheckForResult/OnKilled` 仍提供移除时末次检查桥，末次检查继续位于原停止样本之后、表示解绑之前。Controller、框架 Result 和既有同步出口仍承担各自原职责。本卡没有完成全类型独立调度或完整绘制分离。

布局缓存分别保存 alive-map 与真实 `IndexOf`，没有把同时间对象的两种次序混用。Add/Remove、重应用和直接 StartTime 变化使顺序失效；容器拥有绑定副本订阅，移除与 Dispose 解除，重复原生 Remove/Add 保持恰好一份订阅。身体阶段在旧边界清理已过期表现成员，保留采样、输入和地雷的原相对顺序。

新路径与冻结旧轨迹比较 **7,240 条有序逻辑行**。投影选择 input/batch、地雷/身体、被动/post、keysound、端点/结果/撤销、生命周期、历史和索引检查点；移除行号，并将旧/新适配方法来源归一为 layout/subtree/deferred/removal/batch。post-result 参数只保留两者共同的 `holding`，elapsed 仍在独立时间字段保留。其余所选字段逐项比较；适配包装行、原 `head.publish` 包装和非逻辑遍历行不参与该投影，原完整 fixture 仍由旧路径逐行断言。投影不是期望计划的执行输入。

| 场景 | 完整旧原始行 | 旧/新逻辑行 |
| --- | ---: | ---: |
| 键盘，普通列、正常更新 | 491 | 376 |
| 键盘，普通列、deferred | 423 | 314 |
| 键盘，混合阶段、正常更新 | 499 | 382 |
| 键盘，混合阶段、deferred | 497 | 374 |
| 键盘，同时间批次、重应用、回池及回退 | 397 | 308 |
| 父级输入与两个模拟批次 | 98 | 68 |
| 普通回放，追赶及回退 | 4,075 | 2,684 |
| 混合阶段回放，追赶及回退 | 4,139 | 2,734 |
| 合计 | **10,619** | **7,240** |

### 固定计划结构测试与真实生成测试分别计数

[BmsCoreJudgementScheduleTest](../osu.Game.Rulesets.BmsRuleset.Tests/Normal/Gameplay/Judgement/BmsCoreJudgementScheduleTest.cs) 的 **7 项**无 Drawable 测试使用显式供给计划。原 5 项验证结构：移除当前对象仍访问下一对象；重新绑定使旧计划和旧解绑无效；同 batch 编号的新 epoch 嵌套计划不能恢复旧游标；历史回退中止余下项；移除末次检查只对当前绑定执行一次。另 2 项验证相同对象替代绑定的 token 独立性，覆盖旧移除/末次检查、带或不带迟到旧解绑，同时要求第三个有效计划项继续执行、下一批次仍能执行替代绑定。它们不是实际 Player 生成的证据。

[TestSceneBmsCoreSchedulingGeneration](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/TestSceneBmsCoreSchedulingGeneration.cs) 的 **28 项**包含构造测试、8 个旧 fixture 场景及其 8 个新生成对应场景、真实预算 smoke，以及以下独立严格边界：

- 固定同一逻辑步骤，分别在正常前进和负 elapsed 回退后重复容器/Drawable 子树、masking、几何和高度样式刷新；epoch/step/batch、计分/血量不变，零新增 passive/post/body/history/revert 操作。
- 旧/新各自直接修改 StartTime，不调用 ApplyDefaults，实际 passive 顺序严格等于原生容器 `IndexOf`。
- 原生 Remove/Add 三次，每次移除释放一份排序订阅、重新加入恢复一份。
- 真正同步 `Playfield.NewResult` 回调移除当前 entry 或重应用下一对象：不跳过后继，旧计划不执行下一对象旧代次，新批次执行新代次。
- 旧/新结果回调立即回池仍保留原对象身份，且计分只提交一次。

另 1 项真实换池测试先对原 Drawable 重应用到局部代次 2，再原生移除两个表示并重新加入原 entry，使另一个 Drawable 以同样的局部代次 2 表示同一对象。该测试使用显式计划验证旧 handle、旧 `BeginRemoval/CompleteRemoval` 和旧解绑均无效；迟到的真实 `UnbindNoteRepresentation(originalDrawable, oldLocalGeneration)` 保留替代 token 和输出绑定；其余有效项继续执行，下一批次能正常提交替代对象。这个案例属于真实生命周期接合加固定计划验证，单独计证，不增加上述 8 个真实生成场景。

最后一项发现两个窄宿主缺陷：`BmsColumn.onColumnNewResult` 在外部结果回调后改用稳定 `result.HitObject` 记录和反馈；旧 `removeJudgedHead` 回调遇到已移除或重绑表示时忽略过期捕获。两处修复也适用于默认旧路径，保留原正常计分/反馈并避免同步回池后的空身份访问；对应旧/新场景均保留。没有因此修改 Controller 规则、auto-held 算法或 Mod 采样。

RA-1、ST-1 和暂停冻结约定保持：重应用保留 entry/Result/Controller/历史，自动持有重置 false，结果时间仍由框架 setter 改写；速率 1.5 的 scratch 输入 3990/提交 3997，在 3996 只撤销合成/血量，Controller 尾到 3989 才恢复；`IsPauseCompletion` 只发布最终冻结状态，不额外退场。

### 构建、失败记录与组合回归

环境沿用 Windows x64、.NET SDK `10.0.109`、`net10.0`、framework `2026.918.0`、Release、headless Player。证据根目录是本地 `artifacts/hitobject-p3a0/p3a0-7/`。测试均显式使用 `--filter` 并排除 `.Performance.`，没有运行 benchmark。基线捕获的初始观察/提取错误及修正见 `baseline/README.md`：回池后查询 IndexOf、错误 prefix 长度造成空 draft，以及跨重应用错误预期只执行一次 post；最终 fixture 冻结后未再改写。

| 批次 | 通过 | 失败 / 跳过 | 结果与定位 |
| --- | ---: | --- | --- |
| `baseline/frozen-second` | 10 | 0 / 0 | 生产改动前完整旧路径复跑。 |
| `supervisor-baseline/core-baseline` | 10 | 0 / 0 | 主 agent 独立批准旧基线。 |
| `implementation/generation-first` | 15 | 3 / 0 | 末次检查/退场时点及 OnKilled 观测边界。 |
| `implementation/generation-second` | 16 | 2 / 0 | 回退时多余 HCN passive，恢复原框架条件。 |
| `implementation/generation-third` | 25 | 1 / 0 | 固定计划仅访问两个对象，第三对象仍为候选；修正错误的空集合断言。 |
| `implementation/generation-fourth` | 27 | 0 / 0 | 当时新增组通过。 |
| `implementation/generation-fifth` | 29 | 1 / 0 | 真结果回调回池暴露列反馈使用已释放表示身份。 |
| `implementation/generation-sixth` | 30 | 2 / 0 | 旧头回调使用过期表示；另一个断言误对值类型 source 使用引用相等。 |
| `implementation/generation-seventh` | 32 | 0 / 0 | 新增完整组通过。 |
| `final/core` | 32 | 0 / 0 | 加入 epoch 去重后完整组通过。 |
| `final/combined` | 419 | 17 / 0 | 12 项 .6 OnFree 观测缺失，5 项回放 poll 探针为零。 |
| `probe-diagnostic/combined` | 419 | 17 / 0 | 相同组合先验证真实效果，再复现相同探针失败。 |
| `final-second/core` | 11 | 21 / 0 | 新刷新目标用了继承反射 MethodInfo，Harmony 拒绝安装；保留失败，组合未启动。 |
| `final-third/core` | 32 | 0 / 0 | 声明方法刷新后新增组通过；后续静态审查仍发现绑定碰撞。 |
| `final-third/combined` | 436 | 0 / 0 | token 修复前完整核心组合通过，1 分 55 秒；保留为历史证据。 |
| `binding-collision/before` | 5 | 2 / 0 | 修复前新增纯核心用例准确复现旧 handle 误执行、旧解绑误删替代绑定。 |
| `binding-collision/native-before` | 0 | 1 / 0 | 修复前真实不同 Drawable、相同局部代次 2 复现旧 handle 误执行。 |
| `binding-collision/after` | 8 | 0 / 0 | token 修复后 7 项纯结构加 1 项真实换池验证通过。 |

诊断组合确认四个确定性回放均真实到达 4003，十个输入时间严格为 `3000,3000,3001,3002,3201,3202,3990,3991,4001,4002`，跨多个模拟步骤，stability 计数为 1，但 replay poll 为 0。真实预算 smoke 的完整效果先匹配 fixture，随后才在 poll 断言失败。六个 defaults 场景先通过代次 1→2、原 Result 保留，六个移除场景先通过末次结果/移除断言并记录 `freed=True`，随后才在 OnFree 进入/退出记录上失败。证据保存为 `probe-diagnostic/failures.json`，证明不是实际输入或生命周期未执行。

仅测试作用域按叶到调用者刷新真实方法体：回放使用实际封闭声明类型 `FramedReplayInputHandler<BmsReplayFrame>.CollectPendingInputs`；生命周期使用具体长按 `OnFree` 的声明方法，再刷新框架 `OnFree(entry)`。它们避免前序分层编译/探针恢复后内联隐藏当前叶探针；保留全部原断言、原方法体、fixture 和组合范围。未用分跑取代组合，也未为探针失败修改生产。

主 agent 在 `final-third` 静态审查中发现独立阻断项：`BindingGeneration` 由每个 Drawable 自己递增，不同实例可能对同一对象出现相同数字；仅用 `(Object, Generation)` 的旧计划会误认为替代绑定仍有效，旧解绑也可能移除替代绑定。先添加上述两个纯核心及一个真实换池失败用例，再改为调度登记颁发唯一 token，宿主只转发真实登记 handle。单个重绑没有通过全局取消计划掩盖错误，后继有效项仍须通过。两份 fixture 未改，原 436/436 不作为这一修复的验收证据。

`final-third/combined.summary.json` 按各测试 stdout 提取 8 份旧轨迹、8 份生成轨迹和 8 份投影，旧原始行合计 10,619，新旧投影合计 7,240/7,240 且逐行相等；四个确定性回放均观察到 68 次 poll 和 1 次外层 stability 更新。修复前组合 436 项的范围在生产实施前已确定，没有因失败删除测试；token 修复增加 3 个回归用例，最终范围应为新增组 35 项、组合 439 项，等待当前完整运行确认。

token 修复前生产 DLL SHA256 为 `21FAE46738FAD5B5810DFE66ABA21EB8A4C3D1879EF8C5DC0192A37C5A8CD8B0`，测试 DLL 为 `36F4443E070AFF600F5D5E4DAE994C5E1B863333261C21E1AED965403A328EE5`，保留于 `final-third/binary-hashes.json`。新的最终冻结与主 agent 同版独立验收仍待进行。

以下为本卡实际使用的构建及过滤范围；本地 `commands.ps1` 将 core 和 combined 分别写入独立 TRX，`extract.ps1` 只读各 `UnitTestResult.Output.StdOut`，不会重复统计全局 stdout，也不会写入 fixture。

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj -c Release --no-restore -p:BuildProjectReferences=false -nr:false -p:UseSharedCompilation=false
dotnet build osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false -nr:false -p:UseSharedCompilation=false

$coreFilter = '(FullyQualifiedName~TestSceneBmsCoreSchedulingGeneration|FullyQualifiedName~BmsCoreJudgementScheduleTest)&FullyQualifiedName!~.Performance.'
$combinedFilter = '(FullyQualifiedName~TestSceneBmsCoreSchedulingGeneration|FullyQualifiedName~BmsCoreJudgementScheduleTest|FullyQualifiedName~BmsLongNoteJudgementControllerTest|FullyQualifiedName~TestSceneBmsJudgementLifecycleExecution|FullyQualifiedName~TestSceneBmsLongNoteJudgement|FullyQualifiedName~TestSceneBmsBeatorajaScratchInput|FullyQualifiedName~TestSceneBmsLongNoteLifecycle|FullyQualifiedName~TestSceneBmsJudgementIndexes|FullyQualifiedName~TestSceneBmsPauseRewindLongNote|FullyQualifiedName~TestSceneBmsReplayRewindTiming|FullyQualifiedName~BmsJudgementSelectorTest|FullyQualifiedName~TestSceneBmsColumnJudgement|FullyQualifiedName~TestSceneBmsBeatorajaPmsConsumers|FullyQualifiedName~TestSceneBmsVisualCulling|FullyQualifiedName~BmsNoteExecutionTest|FullyQualifiedName~TestSceneBmsNoteExecution|FullyQualifiedName~BmsLongNoteExecutionTest|FullyQualifiedName~TestSceneBmsLongNoteExecution|FullyQualifiedName~BmsChargeNoteExecutionTest|FullyQualifiedName~TestSceneBmsChargeNoteExecution|FullyQualifiedName~BmsHellChargeNoteExecutionTest|FullyQualifiedName~TestSceneBmsHellChargeNoteExecution|FullyQualifiedName~TestSceneBmsBeatorajaHcnFrameOrder|FullyQualifiedName~TestSceneBmsBeatorajaHcnLandmineFrameOrder|FullyQualifiedName~BmsHellChargeHistoryTest)&FullyQualifiedName!~.Performance.'
dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter $coreFilter --logger 'trx;LogFileName=core.trx' --results-directory artifacts/hitobject-p3a0/p3a0-7/reproduce
dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter $combinedFilter --logger 'trx;LogFileName=combined.trx' --results-directory artifacts/hitobject-p3a0/p3a0-7/reproduce
```

### 临时适配、剩余范围与交接

| 当前消费者/边界 | 当前责任和限制 | 删除或替换里程碑 |
| --- | --- | --- |
| Playfield/Engine 的显式旧新开关 | 默认旧路径；测试仅启用受限 Note/HCN 路径。 | P5a 扩展所有核心入口；P5b 核心验收前去除对比开关。 |
| 容器生命周期、排序、deferred 适配 | 报告真实表示可用性和原次序；调度仍借用这些边界。 | P5a 独立调度，容器只保留生命周期/表示通知。 |
| `CheckForResult/OnKilled` 末次检查桥 | 借原框架停止样本、最后结果和解绑边界。 | P5a 仅保留框架生命周期薄桥，业务检查由核心计划拥有。 |
| Controller 与 `BmsLongNoteOperationBinding` | HCN 仍借既有规则/状态；LN/CN 未由本卡迁移。 | P3a-1..4 依次提取完整 LN/CN/HCN 并删除总 Controller/sink。 |
| 长按视觉 timestamp、头固定及只读表现映射 | Drawable 仍拥有视觉脉冲时间和绘制刷新。 | P3b 完整表现分离与通用长按 Drawable。 |
| 地雷旧阶段 | 混合场景验证其与身体/输入的顺序；未迁移地雷规则。 | P4 规则迁移、P5a 全阶段调度。 |
| 现有 Mod 消费者、auto-held 字段 | 算法和字段保持现状；本卡没有新 AS 桥或 Mod 专用调度。 | P6 在 P5b 后按核心稳定身份/入口适配并替换临时外层消费。 |

- **执行等价：** 本卡 7 项固定计划结构断言、1 项真实换池接合及既有 Note/LN/CN/HCN 执行/生命周期回归分别核对；RA-1/ST-1 的兼容事实继续保留。
- **生成等价：** 限于上述 8 个真实玩家/回放 Note/HCN 场景的 7,240 条逻辑投影；完整旧路径仍比较 10,619 条原始行。没有宣称 LN/CN/所有模式、所有类型或独立全帧调度已完成。
- **未验证：** 完整 Rule/表现分离、全部核心独立调度、AS/Mod 集成、完整渲染/实际音频混音与性能/分配/保留内存。没有新增玩家文案或有意玩法改动。所有 sibling 仅作只读参考。
- **交接：** 最终独立验收前保持本卡进行中，不提交、不进入下一卡。下一卡已细化为 P3a-1 LN 完整规则，其后依次 P3a-2 CN（先明确 ST-1 决策）、P3a-3 HCN、P3a-4 绑定与 Controller 清理；P6 的 AS 边界保持。
