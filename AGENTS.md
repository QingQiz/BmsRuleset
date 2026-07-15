## Local References

- Use sibling checkouts for API/source inspection: `..\osu`, `..\osu-framework`, `..\rulesets`.
- If missing, clone `https://github.com/ppy/osu.git` and `https://github.com/ppy/osu-framework.git`; community rulesets are linked from `https://github.com/ppy/osu/discussions/13096`.

## Ruleset Rules

- Background Sample and KeySound volumes should NOT be affected by the effect volume of global volume settings.

## Code Style

- use `var` for local variables when possible
- use collection expressions when possible (e.g. `string[] vowels = ["a", "e", "i", "o", "u"]`)

## Localisation

- All user-facing text must use the `BmsStrings` localisation system; do not hard-code labels, abbreviations, formatted values, or units in UI code.
- Keep the English base resource and all supported `.resx` translations in sync when adding or changing user-facing text.

## Test Rules

- Do NOT run benchmark tests without asking. Benchmarks can take minutes and consume significant resources.
- Always run tests with `--filter` arguments — never run the full unfiltered test suite.

## Git Style

- Match the existing commit subject style: `type(scope): summary` when a focused scope helps, or `type: summary` when it does not.

## Comment Style

- Write **why**-type comments (the rationale, intent, or non-obvious trade-off), not **what**-type comments (restating what the code already says).
