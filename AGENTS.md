## Local References

- Use sibling checkouts for API/source inspection: `..\osu`, `..\osu-framework`, `..\rulesets`.
- If missing, clone `https://github.com/ppy/osu.git` and `https://github.com/ppy/osu-framework.git`; community rulesets are linked from `https://github.com/ppy/osu/discussions/13096`.
- Treat sibling checkouts as read-only references. Never edit, format, stage, commit, or otherwise modify files in `..\osu`, `..\osu-framework`, or `..\rulesets`.
- Make all implementation changes in this repository. Even when the root cause is upstream, use a ruleset-side solution or report the limitation instead of patching sibling source code.

## Ruleset Rules

- Background Sample and KeySound volumes should NOT be affected by the effect volume of global volume settings.

## Code Style

- use `var` for local variables when possible
- avoid unnecessary testing and over-defensive coding

## Implementation Design

- Prefer the smallest change that completes the feature through existing models and lifecycles. Minimise the number of mechanisms, states, and contracts that must be maintained; fewer changed files or lines alone is not evidence of a simpler design.
- Before adding a mechanism, trace the real production flow from entry and parsing through conversion, configuration, consumers, cloning, seek/reset, invalidation, and disposal as applicable. Implement one coherent lifecycle change rather than independent patches at each consumer.
- Give each value one authoritative owner and compute derived data once at the appropriate layer. Reuse existing metadata and completed parse results instead of adding mirrored fields, parallel representations, or repeated reconstruction across layers.
- Keep source-format and version dispatch at import/parse boundaries when downstream systems only need normalised data. Represent required runtime behaviour directly; do not propagate format flags or infer one capability from an unrelated field.
- Validate and normalise external input at the boundary that owns its contract. Internal consumers should rely on established guarantees instead of repeating checks or inventing defaults. Represent meaningful distinctions such as unspecified and explicit zero in a single clear contract.
- Add fields, parameters, interface members, wrappers, and forwarding methods only for a demonstrated production requirement or a real ownership/lifecycle boundary. Do not introduce configurable policies, per-item state, compatibility aliases, or abstractions for hypothetical future uses.
- Share data that is immutable after publication. Retain copies, snapshots, caches, cancellation, and resource cleanup where mutation, concurrency, or lifetime requirements justify them; verify those requirements before removing them.
- Every compatibility path or fallback must address a concrete supported version, external contract, or reachable production state. Check writers, readers, serialisation, framework callbacks, and history before changing it. Preserve historical scores, replays, and stored data; an uncommitted intermediate implementation does not by itself create a compatibility requirement.
- Preserve observable ordering and lifecycle semantics, including same-time events, source precedence, audio retriggers and tails, seek recovery, and asynchronous readiness. Matching sort keys alone does not prove that sorting, filtering, or deduplication steps can be merged.
- Before finishing, review the complete feature diff and remove redundant state, parameters, branches, conversions, and test scaffolding introduced during implementation. Keep unresolved items with an explicit evidence boundary instead of replacing uncertainty with another fallback.

## Localisation

- All user-facing text must use the `BmsStrings` localisation system; do not hard-code labels, abbreviations, formatted values, or units in UI code.
- Keep the English base resource and all supported `.resx` translations in sync when adding or changing user-facing text.

## Documentation and In-Game Descriptions

- Do not edit README files for internal refactors, replay storage changes, or test changes unless explicitly requested.
- Update documentation for settings, mods, or gameplay behaviour only when players need new usage instructions or an existing documented description becomes inaccurate.
- Do not add documentation for routine bug fixes or minor visual corrections unless explicitly requested.
- Write documentation for players: explain how to use features and their effects on gameplay, without implementation details.
- Keep documentation brief and focused on essential usage and effects. Cover edge cases, mod combinations, and detailed behaviour only when explicitly requested.
- In documentation and in-game descriptions, describe what a feature does. Avoid describing what it does not do.

## Test Rules

- Do NOT run benchmark tests without asking. Benchmarks can take minutes and consume significant resources.
- Always run tests with `--filter` arguments — never run the full unfiltered test suite.
- Tests must supply valid inputs under the production contract. Artificially incomplete objects, arbitrary internal call orders, or reflection-injected state do not justify production fallback behaviour. Update fixtures to establish the real contract while retaining their player-behaviour assertions.
- After implementation, build as needed and run the smallest relevant filtered tests. Do not rebuild read-only reference checkouts; use `--no-restore -p:BuildProjectReferences=false` with their existing build outputs where applicable.
- Verify affected behaviour such as timing boundaries, event priority, audio bounds, seek/backtracking, readiness, clone isolation, and final state. Stop repeating tests once they pass unless new changes or evidence justify another run. Report actual pass/skip counts and unverified boundaries; do not claim a performance improvement without measurement.

## Git Style

- When the user asks to commit, run the relevant tests before committing and fix failures where possible so CI is likely to pass. Follow the test filters and benchmark restrictions above.
- A request to commit does not authorise pushing. Push only when the user explicitly requests pushing or publication.
- Match the existing commit subject style: `type(scope): summary` when a focused scope helps, or `type: summary` when it does not.

## Comment Style

- Write **why**-type comments (the rationale, intent, or non-obvious trade-off), not **what**-type comments (restating what the code already says).
