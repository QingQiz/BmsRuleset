# BMS icon font generator

Generates the monochrome 64×64 glyphs used by `BmsIcons`. This is the maintained version of the temporary `.codex-temp/BmsIconFontGenerator` tool and its `create-bms-icons` skill copy. Use this repository version to retain the complete glyph set, including No Mine.

[Back to development guide](../../docs/development.md#mod-icons)

## Regeneration

From the repository root:

```powershell
dotnet run --project scripts/bms-icon-font-generator/BmsIconFontGenerator.csproj -- osu.Game.Rulesets.BmsRuleset/Resources/Fonts/bmsIcons_0.png osu.Game.Rulesets.BmsRuleset/Resources/Fonts/bmsIcons.fnt
```

The outputs are a horizontal RGBA sprite sheet and BMFont binary v3 metadata. Each glyph occupies a 64×64 cell; the seven current glyphs produce a 448×64 page. Visible pixels are white, with antialiased alpha; the game supplies the display colour.

## Integration

Append new draw functions to `glyphs` in [Program.cs](Program.cs) to preserve existing codepoints. Do not reorder or reuse entries.

- Declare the glyph in [BmsIcons.cs](../../osu.Game.Rulesets.BmsRuleset/UI/Icons/BmsIcons.cs) and assign it to the mod's `Icon` property.
- Keep the family name `bmsIcons` and asset names `bmsIcons.fnt` / `bmsIcons_0.png`.
- Add the mod to [TestSceneBmsModIcons.cs](../../osu.Game.Rulesets.BmsRuleset.Tests/Components/TestSceneBmsModIcons.cs).

No Mine uses a filled bomb with a deletion stroke matching Hide Scratch's direction and weight. A transparent gap separates the stroke from the bomb body at small sizes.

| Codepoint | Glyph |
| --- | --- |
| U+E000 | Scratch |
| U+E001 | Hide Scratch |
| U+E002 | Auto Scratch |
| U+E003 | Auto Gauge |
| U+E004 | No Good |
| U+E005 | No Great |
| U+E006 | No Mine |

## Verification

Inspect the sprite at its original size and preview the glyph at 16–80 px. Confirm that existing cells are pixel-identical after appending a glyph.

After building the test project in Debug, run the filtered icon-loading test:

```powershell
dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~TestSceneBmsModIcons' -- NUnit.NumberOfTestWorkers=1
```

Build the ruleset in Release and verify that both font resources are embedded in the final DLL after ILRepack.
