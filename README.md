
## FIXME

- [ ] 游戏在autoplay时，切到后台，hitsound 的播放会乱掉。切到后台再切到前台游戏会卡死
- [ ] 游戏在 autoplay 暂停时， background sample 会继续播放

- [ ] test 步骤里的 import real bms 步骤总是会失败
    ```
      [runtime] 2026-05-27 18:19:42 [error]: Step "import real bms" SingleStepButton triggered error
      [runtime] 2026-05-27 18:19:42 [error]: System.InvalidOperationException: TestContext.WorkDirectory must not be accessed before DefaultTestAssemblyBuilder.Build runs.
      [runtime] 2026-05-27 18:19:42 [error]: at NUnit.Framework.TestContext.get_WorkDirectory()
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.candidateTestSongRoots() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 43
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.get_testSongsRoot() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 31
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.<TestImportedRealBmsAutoplayWithBeatmapSkin>b__13_0() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 90
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Framework.Testing.Drawables.Steps.SingleStepButton.clickAction()
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Framework.Testing.Drawables.Steps.StepButton.PerformStep(Boolean userTriggered)
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Framework.Testing.TestScene.runNextStep(Action onCompletion, Action`2 onError, Func`2 stopCondition)
      [runtime] 2026-05-27 18:19:44 [error]: Step "import real bms" SingleStepButton triggered an error
      [runtime] 2026-05-27 18:19:44 [error]: System.InvalidOperationException: TestContext.WorkDirectory must not be accessed before DefaultTestAssemblyBuilder.Build runs.
      [runtime] 2026-05-27 18:19:44 [error]: at NUnit.Framework.TestContext.get_WorkDirectory()
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.candidateTestSongRoots() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 43
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.get_testSongsRoot() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 31
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.<TestImportedRealBmsAutoplayWithBeatmapSkin>b__13_0() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 90
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Framework.Testing.Drawables.Steps.SingleStepButton.clickAction()
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Framework.Testing.Drawables.Steps.StepButton.PerformStep(Boolean userTriggered)
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Framework.Testing.Drawables.Steps.StepButton.OnClick(ClickEvent e)
    ```
- [ ] the empty poor seems not reduce the hp


## TODO

- [ ] Mine
- [ ] health bar overflow
- [ ] timing sys
- [ ] measure line
- [ ] correct metadata display (title, artist, etc, rank, hp, ...)

- skin
  - [ ] column start : value or enum(leftN, rightN, center)
  - [ ] bga position/size
  - [ ] bms skin in none-legacy way, full configurable via skin editor

- mod
  - [ ] auto scratch
  - [ ] hide scratch
  - [ ] mirror

- ask
  - [ ] ask how to impl a new HUD element (e.g. combo, score, ...), and how to customize their position/size/skin
