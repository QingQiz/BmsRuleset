## Local References

- Use sibling checkouts for API/source inspection: `..\osu`, `..\osu-framework`, `..\rulesets`.
- If missing, clone `https://github.com/ppy/osu.git` and `https://github.com/ppy/osu-framework.git`; community rulesets are linked from `https://github.com/ppy/osu/discussions/13096`.

## Ruleset Rules

- Background Sample and KeySound volumes should NOT be affected by the effect volume of global volume settings.

## Dev Rules

- Do NOT run benchmark tests without asking. Benchmarks can take minutes and consume significant resources.
- Always run tests with `--filter` arguments — never run the full unfiltered test suite.

## Comment Style

- Write **why**-type comments (the rationale, intent, or non-obvious trade-off), not **what**-type comments (restating what the code already says).

