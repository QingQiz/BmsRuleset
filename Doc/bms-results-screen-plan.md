# BMS Play Result Statistics Screen — Design Plan

## Goal

Replace the generic `SoloResultsScreen` with a `BmsResultsScreen` that presents play statistics in the BMS-native style rather than osu!'s accuracy-centric layout.

---

## 1. Framework Entry Points

### 1.1 Current state

`SoloSongSelect` instantiates `SoloPlayer` directly; there is no ruleset-level hook to substitute a custom Player class at runtime. Until the framework provides `Ruleset.CreatePlayer()`, `BmsPlayer.CreateResults()` is only exercised in test harness contexts.

Tracking issue: wire `BmsPlayer` into the game's song select flow once a framework hook exists.

### 1.2 Required class

```
BmsResultsScreen : ResultsScreen          (osu.Game.Screens.Ranking)
```

`ResultsScreen` is `abstract partial class ResultsScreen : ScreenWithBeatmapBackground`. The required overrides are:

| Method | Purpose |
|--------|---------|
| `FetchScores(CancellationToken)` | Returns local scores to populate the leaderboard panel. |
| (optional) override layout panels | Add/remove content panels in `CreatePanels()` equivalent. |

---

## 2. Screen Layout

BMS result screens traditionally follow a vertical layout with distinct zones:

```
┌─────────────────────────────────────────────┐
│  Song title / artist / chart name / BPM     │  header
├─────────────────────────────────────────────┤
│                                             │
│   CLEAR / FAILED                            │  clear lamp (large, centred)
│   ██████████████████░░░░░  gauge %          │  gauge bar
│                                             │
├─────────────────────────────────────────────┤
│  EX-SCORE   12345     DJ LEVEL   AA         │  score summary
│  MAX COMBO  543 / 721                        │
│  RANK       ████████ (EX-score ratio bar)    │
├─────────────────────────────────────────────┤
│  PGREAT  │ GREAT │ GOOD │ BAD │ POOR │ MISS │  judgement breakdown
│   412    │  108  │   22 │   7 │   5  │   3  │
├─────────────────────────────────────────────┤
│  Fast / Slow timing histogram (optional)    │  timing detail
├─────────────────────────────────────────────┤
│  Local leaderboard (EX-score descending)    │  score history
│  — includes failed scores                   │
└─────────────────────────────────────────────┘
```

---

## 3. Data Sources

| UI element | Source |
|------------|--------|
| Clear lamp (CLEAR/FAILED) | `ScoreInfo.Rank == F` → FAILED; else CLEAR. Augment with `BmsHealthProcessor.Health` at end if possible. |
| Gauge % at end | Store in `ScoreInfo.Statistics` using a sentinel key (e.g. `HitResult.LegacyComboIncrease` repurposed, or a custom metadata field — see §5). |
| EX-score | `ScoreInfo.TotalScore` (already 0–1,000,000 mapped from EX ratio). |
| DJ LEVEL | `ScoreInfo.Rank` (X/S/A/B/C/D from `BmsScoreProcessor.RankFromScore`). |
| Max combo | `ScoreInfo.MaxCombo`. |
| Judgement counts | `ScoreInfo.Statistics[HitResult.*]` for all six BMS results. |
| Fast / slow histogram | `ScoreInfo.HitEvents` time-offset distribution (requires `TrackHitEvents = true`). |
| Local leaderboard scores | `ScoreManager.GetAllUsableScores()` filtered to the same beatmap hash, sorted descending by `TotalScore`. |

---

## 4. Clear Lamp Colours

Match LR2/beatoraja convention:

| State | Label | Colour |
|-------|-------|--------|
| Failed (gauge < 80 %) | FAILED | Red (#E02020) |
| Normal clear (gauge ≥ 80 %) | CLEAR | Blue (#2080E0) |
| Hard clear (hard gauge ≥ 80 %) | HARD CLEAR | Gold (#E0A020) |
| Full combo | FULL COMBO | Green (#20C040) |
| Perfect (all PGREAT) | PERFECT | Cyan (#20E0E0) |

In the initial implementation only FAILED / CLEAR are required; the others are planned.

---

## 5. Storing Gauge-at-End in the Score

The osu! `ScoreInfo` has no BMS-specific field for the final gauge value. Options:

### Option A — Custom metadata key in `AdditionalData` dictionary

`ScoreInfo` has `public Dictionary<string,object> AdditionalData` (Realm-persisted JSON). Store:
```json
{ "bmsGauge": 0.843, "bmsClearType": "NormalClear" }
```
Read back with `scoreInfo.AdditionalData.TryGetValue("bmsGauge", ...)`.

### Option B — Overload an unused `HitResult` statistic bucket

Repurpose `HitResult.LegacyComboIncrease` (not used in BMS) as a fixed-point gauge slot:
`Statistics[HitResult.LegacyComboIncrease] = (int)(health * 10000)`.
This is fragile and not recommended.

### Option C — Subclass `ScoreInfo` (not feasible)

`ScoreInfo` is a Realm-managed class; subclassing breaks the ORM.

**Recommendation**: Option A. Implement a `BmsScoreMetadata` helper class that reads/writes `AdditionalData` for type safety. Key names: `bms_gauge_end`, `bms_clear_type`.

---

## 6. Local Leaderboard (Failed Scores)

`BmsPlayer.ConcludeFailedScore` already imports failed scores to the Realm database. The leaderboard query:

```csharp
scoreManager.GetAllUsableScores()
    .Where(s => s.BeatmapInfo.MD5Hash == currentBeatmap.MD5Hash
             && s.Ruleset.ShortName == "bms")
    .OrderByDescending(s => s.TotalScore)
```

Failed scores have `Rank == F`; display them with a red "FAILED" badge inline in the leaderboard table rather than hiding them.

---

## 7. Implementation Steps

| Step | Work item | File(s) |
|------|-----------|---------|
| 1 | Create `BmsResultsScreen : ResultsScreen` skeleton | `Screens/BmsResultsScreen.cs` |
| 2 | Implement `FetchScores` to query local leaderboard | same |
| 3 | Add clear lamp panel (`BmsClearLampPanel`) | `Screens/BmsClearLampPanel.cs` |
| 4 | Add gauge bar widget (`BmsGaugeResultWidget`) | `Screens/BmsGaugeResultWidget.cs` |
| 5 | Add judgement breakdown panel (`BmsJudgementBreakdown`) | `Screens/BmsJudgementBreakdown.cs` |
| 6 | Add EX-score / DJ LEVEL summary panel | `Screens/BmsScoreSummaryPanel.cs` |
| 7 | Wire `BmsPlayer.CreateResults` to `BmsResultsScreen` | `UI/BmsPlayer.cs` |
| 8 | Add `BmsScoreMetadata` helper for AdditionalData fields | `Scoring/BmsScoreMetadata.cs` |
| 9 | Store gauge-at-end in `BmsHealthProcessor` via metadata | `Scoring/BmsHealthProcessor.cs` |
| 10 | Visual test scene `TestSceneBmsResultsScreen` | `Tests/TestSceneBmsResultsScreen.cs` |

---

## 8. Out of Scope for Initial Version

- Online leaderboard integration (no server-side BMS ruleset support).
- Beatoraja/LR2 import of external score databases.
- Graph overlays (density curve, offset scatter plot).
- Replay viewer button (works via base `ResultsScreen.AllowWatchingReplay`).
- Multi-lamp history bar (lamp trend over last N plays).

---

## 9. Related Files

- `osu.Game.Rulesets.BmsRuleset/UI/BmsPlayer.cs` — `CreateResults` entry point.
- `osu.Game.Rulesets.BmsRuleset/Scoring/BmsScoreProcessor.cs` — `RankFromScore` (DJ LEVEL).
- `osu.Game.Rulesets.BmsRuleset/Scoring/BmsHealthProcessor.cs` — final gauge value source.
- `osu-ref/osu.Game/Screens/Ranking/ResultsScreen.cs` — base class API.
- `osu-ref/osu.Game/Screens/Ranking/SoloResultsScreen.cs` — reference implementation.
- `osu-ref/osu.Game/Scoring/ScoreInfo.cs` — `AdditionalData`, `Statistics`, `HitEvents`.
- `Doc/native-gap-todo.md` — tracks all open gameplay gaps including results screen.
