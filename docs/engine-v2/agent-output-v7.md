# Agent output research v7 / NUI-116

The latest DSH carrier study successfully installed fighters but received only a detail reference for outputs, mixed finite rockets with nominal primary output, and stopped after INCOMPLETE_OUTPUT_SELECTION. This release addresses those generic output/recovery paths, not a special carrier answer template.

## Changes

- `fitting outputs` returns a compact paged list: identity/kind, native per-metric state/value/unit/reason/aggregationKey, source deployment, and scoped native totals. Default/max page size 12, offset plus expectedFitHash; changes invalidate later pages. Full application/trace metadata remains by reference. Nine carrier outputs fit inline without the old all-or-reference collapse.
- Fit create/read/edit/save and preview expose native weapon+drone nominal and deployed fighter-primary totals from the SAME receipt analysis; no extra native inspection or arithmetic. They stay separate with scope and diagnostics; primary is not rockets/utility/full combat, unavailable is not zero, erroneous fits are not ready.
- Every curve contains per-selected-contribution baseline diagnostics and the native baseline (including exclusions). Unavailable curves have a top-level issue and next action. Recovery plans partition eligible selected contributions by available native baseline metric/aggregation key; finite outputs may offer an explicit loaded-cycle alternative. Plans retain target/intervals and bind fitHash, list omitted IDs and disclaim sustained output. Original curve is never silently repaired or relabeled ready. Actual native follow-up still validates target/application.
- Generic bound/paging navigation is promoted to envelope next rather than hidden only under data. Future consumers can follow one consistent location.

No new game formulas or engine code changes. Engine stays 0.213.0-ui.agent1/r62, 42 native tools. MCP retains the same 35 operation names; v7 adds outputs pagination/fitHash arguments and curve fitHash preconditions. Frozen v6 remains unchanged. Current DSH state, fits, jobs and webpage runtime remain unchanged.

## Validation

`test_agent_outputs.py` uses a disposable Thanatos with three six-member Firbolg II squads. It verifies same-receipt totals equal native analysis, all nine outputs and per-metric values remain visible and native-identical, pagination is complete, original mixed nominal curve stays unavailable, rockets report FINITE_ABILITY_USE_LOADED_CYCLE_BASIS, both offered subset/loaded curves actually become ready, target is preserved, unknown IDs stay explicit, changed fitHash rejects stale pages/plans, generic bounded navigation is top-level. No user fits mutated.

Shared task and v6 defense/roster tests retained. Actual installed DSH client executes output discovery, unavailable curve and BOTH recovery calls; CLI failed-curve response is identical to MCP. Skills/prompt updated and validated. Evidence: output/agent-v7-output-verification.json, agent-v7-acceptance.json, agent-v7-dsh-verification.json. Model adherence is not proven by interface tests; no autonomous DeepSeek run claimed.

Limitations: nominal/loaded-cycle values remain static; no actual sustained DPS, universal ship-equivalence or inferred spool-up. Recovery suggestions based on available base metrics are not guaranteed complete application coverage. Source startup latency and unsupported mechanics are unchanged.
