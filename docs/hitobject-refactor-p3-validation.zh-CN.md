# HitObject 重构 P3 验证记录

[返回重构计划](hitobject-refactor-plan.zh-CN.md)

日期：2026-09-30。状态：长按控制器与历史迁移已实现，定向行为及有序轨迹对照通过，性能待验证。

## 对照环境

- 生产代码基线：`cebe24e55da539d2fec0d5261c4a37054cd273fb`（P2）。新增生命周期测试先在该版本执行并保存轨迹，再修改生产代码。
- Windows x64，.NET SDK `10.0.109`，`net10.0`，Release。
- osu! 源码引用：`ebaf7e9910ef3755308dec2c7950d916aabc545b`；framework 包：`2026.918.0`。
- 构建使用现有引用程序集，带 `--no-restore -p:BuildProjectReferences=false`；实现与构建输出均限于本仓库。

## 迁移结果

- `BmsLongNoteJudgementController`、`BmsHellChargeBodyTracker` 和 `BmsHellChargeHistory` 移入 `UI/Gameplay/Judgement`。引擎的列上下文按谱面对象身份创建并持有控制器，身份与时间、列号分离。
- 控制器保存谱面对象、窗口、模式、释放历史和身体历史。每次操作借用 `IBmsLongNoteJudgementSink`，不保存 sink、Drawable 或 entry；HCN 使用列上下文缓存的回调。
- 上下文只登记当前 Drawable。主结果仍同步写入端点并调用 `ApplyBmsResult`；合成端点按源谱面对象直接进入原 playfield 计分/血量链，移除 `IBmsLnScoring` 的 Drawable 注入路径。
- `IBmsLongNoteDrawable` 仅同步固定头、身体可见性和退场。已移除 `IBmsLongNoteHooks`、entry 的 `LongNoteJudgementController` 字段和逐 Drawable 的 HCN 身体判定入口。
- 普通倒退在池化激活之前恢复已创建控制器，再恢复视觉及重建索引。暂停结算沿用原顺序，恢复引导期间冻结状态。回池前保留最后一次被动判定所需的表示，在 `OnFree` 解除映射和 Drawable 的控制器引用；候选及有序长按缓存也移除已回池表示。
- 重按、松开、身体和暂停结算由上下文直接调用控制器。框架的被动检查、收尾边界与 Mod 输入保留薄转发，维持原逐对象顺序；普通 LN 主结果及 CN/HCN 合成尾仍先提交，再写入 `TailJudged`。

## 行为验证

| 批次 | 通过 | 内容 |
| --- | ---: | --- |
| P2 既有场景基线 | 458 | 判定、长按、Scratch、暂停、裁剪、列判定及跨列帧顺序 |
| P2 新生命周期基线 | 7 | 六种模式/混合列组合及测试场景初始化 |
| P3 首轮迁移回归 | 469 | 既有场景、生命周期及活跃索引检查 |
| P3 状态归属与同步顺序 | 73 | 控制器单测、回池解绑、同列同时间对象及尾结果通知时的状态 |
| P3 最终定向回归 | 755 | 上述检查，加 PMS、回放倒退、Invisible Note、Mod、AutoScratch、短 LN Autoplay、Invert、keysound cursor |

最终批次 **755 项全部通过，无失败、无跳过**。规则集和测试项目的 Release 构建均为零错误、零警告。所有测试命令均带 `--filter`。

扩展回归有一批因名称前缀可能匹配性能场景而立即中止，不计入验收；最终批次改用明确场景名称并排除 `.Performance.`。本阶段未取得性能验收数据，也不作性能持平或提升的结论。

### 有序轨迹

与 P2 对照的 **23 个场景、95 个检查点全部一致**：

- 原有 17 个列判定及 HCN/地雷场景，共 47 个检查点。
- 新增 LN/CN/HCN 各含纯长按列和地雷混合列的六个场景，共 48 个检查点：头判定、身体与地雷、提前尾判定、成功尾后的身体、回池后另一对象的头、倒退进入原身体、重判尾及再次退场。

轨迹包含谱面对象编号、主结果和时间、判定事件与端点、EX 进度、总分、连击及每次血量变化。新增基线保存在 [long_note_p2_trace.json](../osu.Game.Rulesets.BmsRuleset.Tests/reference_fixtures/long_note_p2_trace.json)，由 [TestSceneBmsLongNoteLifecycle](../osu.Game.Rulesets.BmsRuleset.Tests/Visualize/TestSceneBmsLongNoteLifecycle.cs) 自动核对。旧预期值未因迁移而修改。

生命周期测试另验证：回池后原谱面对象没有表示可供结果出口使用；倒退再激活仍使用原控制器；相同列和开始时间的两个对象状态独立。控制器测试验证固定头先于主结果、尾提交先于状态写入、HCN 头 POOR 的索引通知先于两个合成端点。

原始 TRX、构建日志、对照脚本和摘要保存在本地 `artifacts/hitobject-p3/`，版本库保留基线夹具和本记录。

## 复验

已有匹配的引用程序集及依赖还原结果时：

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj -c Release --no-restore -p:BuildProjectReferences=false
dotnet build osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false
dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~BmsLongNoteJudgementControllerTest|FullyQualifiedName~TestSceneBmsLongNoteLifecycle|FullyQualifiedName~TestSceneBmsColumnJudgement|FullyQualifiedName~TestSceneBmsBeatorajaHcnFrameOrder|FullyQualifiedName~TestSceneBmsBeatorajaHcnLandmineFrameOrder|FullyQualifiedName~TestSceneBmsJudgementIndexes)&FullyQualifiedName!~.Performance.'
```

完整定向范围记录在本地 `final.trx`。真实 Player 性能、分配、保留内存及完整音效/视觉反馈轨迹仍是最终集成前的验收项；运行 benchmark 前需征得同意。

本记录反映 P3 完成时的实现及验证，当时安排的下一阶段为 P4。后续设计已增加最小集成验证、规则拆分及绘制契约迁移；当前执行顺序与下一项任务统一见[主计划实施入口](hitobject-refactor-plan.zh-CN.md#implementation-start)。本记录不作为新增设计的通过证据。P0/P1 剩余验收和最终非快进双父 merge commit 的要求保持不变。
