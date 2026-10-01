# HitObject 重构 P2 验证记录

[返回重构计划](hitobject-refactor-plan.zh-CN.md)

日期：2026-09-30。状态：列逻辑与普通 Note 迁移已实现，定向行为验证通过，性能待验证。

## 对照环境

- 生产代码基线：`6524d324494f852d4cc53e0955af35fda567bb92`（P1）。先在该实现上加入新测试并保存轨迹，再迁移生产代码。
- Windows x64，.NET SDK `10.0.109`，`net10.0`，Release。
- osu! 源码引用：`ebaf7e9910ef3755308dec2c7950d916aabc545b`；framework 包：`2026.918.0`。
- 使用已有引用程序集，构建带 `--no-restore -p:BuildProjectReferences=false`，未构建或修改 sibling checkouts。
- 测试使用固定谱面、回放及 seek 检查点。新增同时间输入场景还直接调用列输入入口，覆盖五种选择算法。

## 本阶段迁移

- 引擎创建每列 `BmsColumnJudgementContext`，持有按键方向、按键历史、候选缓冲、待判头与活跃长按索引、被动截止时间。
- 列输入选择、exact-time 快速路径、重按、松开及 PMS mistake 处理移入上下文，保留原候选遍历、相同时间对象顺序和同步音效调用点。
- 普通 Note 的窗口缓存改为无可变判定状态的值类型 `BmsNoteJudgementRule`，分别实现按下与被动判定能力。上下文直接调用具体规则，不创建逐对象服务或逐 tick 委托。
- 普通 Note 移除 `CheckForResult` 实现及旧裁剪补偿，改为在每个 Drawable 的子树遍历末尾和 `OnKilled` 边界转发给上下文。结果仍同步调用原 `ApplyBmsResult` / 框架 `ApplyResult`。
- 容器继续负责池化、定位和延迟绘制；登记、撤销、回退索引重建仍在原通知点执行。长按控制器、entry 自定义状态和地雷规则留待 P3/P4。

## 行为结果

| 批次 | 通过 | 内容 |
| --- | ---: | --- |
| P1 基线 | 30 | 新列判定场景、裁剪、HCN/地雷帧顺序 |
| P2 同组对照 | 30 | 与 P1 使用相同输入和检查点 |
| P2 判定回归 | 416 | 判定单元测试、算法、索引、PMS、Scratch、长按、暂停恢复、地雷回退、Invisible Note、身体着色 |
| P2 回放与自动化 | 224 | 新轨迹夹具断言、普通 Note 暂停回退、回放、短 LN Autoplay、Mod 与 keysound cursor |
| 夹具整理与恢复输入 | 27 | 去重后的夹具、暂停恢复输入、恢复后的完成状态 |

P2 各批次合计覆盖 **674 个不同测试用例**，均通过，无跳过。规则集与测试项目的 Release 构建均为零错误、零警告。所有测试命令均带 `--filter`，没有运行 benchmark 或微测量。

### 有序轨迹

P1/P2 的 **17 个场景、47 个检查点**逐行对照一致：

- 新增列判定场景：9 个参数组合、13 个检查点，覆盖同列同时间不同对象、五种算法、包含地雷的非批处理列、可见/裁剪普通 Note、严格窗口边界和回池后的回退再激活。
- 现有 HCN/地雷帧顺序场景：8 个场景、34 个检查点，覆盖血量饱和、同帧松开、重复时间戳及回退。

新轨迹记录谱面稳定对象编号、框架结果及时间、判定事件与端点、计分标志、EX 进度、当前总分和连击、每次变化后的 gauge history。基线保存在 [column_judgement_p1_trace.json](../osu.Game.Rulesets.BmsRuleset.Tests/reference_fixtures/column_judgement_p1_trace.json)，测试会自动核对；相同轨迹的参数组合共用一份记录，夹具保留全部参数名称。

原始 TRX、构建日志和对照摘要保存在本地 `artifacts/hitobject-p2/`；版本库保留基线夹具和本记录。没有修改旧实现的预期值以接受迁移差异。

## 复验

在已有匹配引用程序集和依赖还原结果的环境中构建：

```powershell
dotnet build osu.Game.Rulesets.BmsRuleset/osu.Game.Rulesets.BmsRuleset.csproj -c Release --no-restore -p:BuildProjectReferences=false
dotnet build osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-restore -p:BuildProjectReferences=false
```

重跑基线夹具、裁剪及跨列有序轨迹：

```powershell
dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~TestSceneBmsColumnJudgement|FullyQualifiedName~TestSceneBmsVisualCulling|FullyQualifiedName~TestSceneBmsBeatorajaHcnFrameOrder|FullyQualifiedName~TestSceneBmsBeatorajaHcnLandmineFrameOrder'
```

其他回归批次使用对应 fixture 的 `FullyQualifiedName` 过滤器，详见本地 `regression.trx`、`final.trx` 和 `fixture.trx`。

## 后续验收

P2 的输入与被动路径性能、稳态分配、保留内存和真实 Player 对照仍待验证。完整音效/视觉反馈轨迹及 P0/P1 剩余行为覆盖也仍是合并前的验收项。本记录只确认本阶段实现和上述定向行为结果，不代表整个重构计划已完成。

下一阶段为 P3：迁移长按控制器及历史状态，移除 `IBmsLongNoteHooks` 和 entry 的长按控制器字段。最终集成仍按计划使用非快进的双父 merge commit。
