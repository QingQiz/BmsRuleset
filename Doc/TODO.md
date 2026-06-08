# TODO

## FIXME

- [ ] on changing ruleset from bms to any or from any to bms

  ```
  2026-06-01 15:13:14 [verbose]: This error has been automatically reported to the dev team.
  2026-06-01 15:13:14 [error]: An unobserved error has occurred.
  2026-06-01 15:13:14 [error]: osu.Game.Rulesets.UI.BeatmapInvalidForRulesetException: Beatmap can not be converted for the ruleset (ruleset: osu.Game.Rulesets.Mania.ManiaRuleset, osu.Game.Rulesets.Mania, converter: osu.Game.Rulesets.Mania.Beatmaps.ManiaBeatmapConverter).
  2026-06-01 15:13:14 [error]: at osu.Game.Beatmaps.WorkingBeatmap.GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList`1 mods, CancellationToken token)
  2026-06-01 15:13:14 [error]: at osu.Game.Beatmaps.WorkingBeatmap.GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList`1 mods)
  2026-06-01 15:13:14 [error]: at osu.Game.Screens.Select.BeatmapTitleWedge.DifficultyDisplay.<>c__DisplayClass36_0.<updateCountStatistics>b__0()
  2026-06-01 15:13:14 [error]: at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
  2026-06-01 15:13:14 [error]: --- End of stack trace from previous location ---
  2026-06-01 15:13:14 [error]: at System.Threading.ExecutionContext.RunFromThreadPoolDispatchLoop(Thread threadPoolThread, ExecutionContext executionContext, ContextCallback callback, Object state)
  2026-06-01 15:13:14 [error]: at System.Threading.Tasks.Task.ExecuteWithThreadLocal(Task& currentTaskSlot, Thread threadPoolThread)

  ```

## TODO

- parser 未实现 command（参考 https://hitkey.nekokan.dyndns.info/cmds.htm）
    - [ ] `#DEFEXRANK` / `#EXRANKxx` / channel `A0` — 判定 rank
    - [ ] `#EXBPMxx` — `#BPMxx` 的别名（规避 BMSC 解析 bug）
    - [ ] `#STP` — 绝对 STOP 序列
    - [ ] `#VOLWAV` — 全局音量
    - [ ] `#WAVCMD` — MacBeat 音高/音量/时间
    - [ ] `#EXWAVxx` — 扩展音频定义（pan/volume/freq）
    - [ ] `#PATH_WAV` — 资源路径前缀（WAV/BMP 共用）
    - [ ] `#BGAxx` / `#POORBGA` / `#SWBGAxx` / `#@BGAxx` / `#ARGBxx` — BGA 定义
    - [ ] `#BMPxx` / `#EXBMPxx` — 图像定义（非资源扫描）
    - [ ] `#VIDEOFILE` / `#VIDEOf/s` / `#VIDEOCOLORS` / `#VIDEODLY` / `#MOVIE` / `#SEEKxx` — 视频
    - [ ] `#STAGEFILE` / `#BANNER` / `#BACKBMP` — 界面图像（非资源扫描）
    - [ ] `#CHARFILE` / `#ExtChr` — 角色/皮肤
    - [ ] `#CHANGEOPTIONxx` / channel `A6` — 动态 option
    - [ ] `#OPTION` — 强制 option
    - [ ] `#OCT/FP` — 八度/踏板
    - [ ] `#CDDA` / `#MIDIFILE` — CD / MIDI
    - [ ] `#MATERIALS` / `#MATERIALSWAV` / `#MATERIALSBMP` / `#DIVIDEPROP` — 资源组
    - [ ] ~~`#CHARSET` — 编码声明~~
    - [ ] channel `04` / `06` / `07` / `0A`–`0E` — BGA 层
    - [ ] channel `17` / `27` — free-zone 键
    - [ ] channel `31`–`49` — 隐形音符
    - [ ] channel `97` — 动态 BGM 音量
    - [ ] channel `98` — 动态 KEY 音量（channel `97` 的对应）

- skin
    - [ ] column start : value or enum(leftN, rightN, center)
    - [ ] bga position/size
    - [ ] bms skin in none-legacy way, full configurable via skin editor
    - [ ] hitGreat -> hitGreatLate/hitGreatEarly, ... (`HitGreat: imgearly,imglate` or
      `HitGreatLate: imglate\nHitGreatEarly:imgearly`)
    - [ ] e-poor image

- mod
    - [ ] different health bar
    - [ ] random
    - [ ] remember the last used mod combination
    - [ ] BG: make keysound to background samples. so the hit result will not effect the music
    - [ ] CS: remove the SV. add options for SV multiplier, default by 0, 1 without the mod

- importer
    - [ ] use a reference/symbolic link to the original bms file instead of copying it to the realm, to speed up the
      import.

- [ ] result screen

- [ ] bga

- audio
    - [ ] #WAVCMD (MacBeat) — Sets pitch (00), volume (01), or playback time (02) per WAV slot. Format:
      #WAVCMD <commandID> <WAV-index> <value>. Default pitch=60 (C6), volume=100%.
    - [ ] #EXWAVxx (nanasi) — Defines a WAV file with pan (-10000 to 10000), volume (-10000 to 0), and frequency/pitch (
      100–100000 Hz). Format: #EXWAVxx <flags> <pan> <volume> <freq> <filename>.
    - [ ] #VOLWAV n (BM98) — Global volume scalar for all sounds as a percentage. #VOLWAV 100 = original, #VOLWAV 200 =
      200%.
    - [ ] #xxx97 (fgt) — Dynamic BGM volume change channel. Range [01-FF] (hex), e.g. #00197:003C sets volume to 60 at
      measure 1.

- [ ] Exrank

- [ ] 结算时不同判定的文字颜色
- [ ] beatmap statisitc 展示更多信息，比如 random 分支数
- [ ] combo 显示
- [ ] 挡板，挡板皮肤， 挡板移动
- [ ] mania 7k 转谱
- [ ] reply not available
- [ ] 调整判定偏移的能力
- [x] 同样的速度下 bms 下落比 mania 快

- [ ] rewrite 血条，红黄绿三色渐变，不改变整体颜色，去掉边框
- [ ] LN 的 头判
