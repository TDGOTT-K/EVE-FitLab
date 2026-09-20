# FitLab Agent Platform v5 — NUI-114

## Design objective

A general task API for EVE facts, discovery, fitting, static research and combat experiments. The Caracal audits are failure evidence, not the product's task taxonomy. A simple ship-bonus question must not require a simulated fight or native schema browsing. A faction weapon survey must be able to combine source descriptions with condition-bound output curves without pretending each faction has one universal curve.

Methods used:

- Task analysis / jobs-to-be-done: group by user outcome, not C# class or transport.
- Progressive disclosure: eight domain tools, 33 typed operations; native advanced tools remain discoverable on demand. No scenario graph in normal tool schemas.
- Explicit state machines: command success is separate from fitting readiness and battle completion.
- Error-recovery design: missing/ambiguous input, stale revision, unavailable mechanism, running job, failed job, low storage and version mismatch have distinct results and next actions.
- Contract-first shared implementation: one operation registry powers MCP discovery, CLI help and argument validation; one dispatcher powers both transports.
- Evidence provenance: source and fit identity on calculations, immutable request and scenario identity on experiments, time and meaning on each report measurement.
- Scenario-based acceptance: simple lookup, discovery/comparison, preview/edit retry, several weapon families, experiment lifecycle, storage recovery and both transport projections.

## Task map and acceptance criteria

| User task | Entry | Observable acceptance |
| --- | --- | --- |
| 查询狞獾级舰船加成 | catalog describe by name | One visible call; official per-level traits, source, no battle |
| 找CPU受限的护盾装备 | catalog search | Filter/sort in engine; executable next preserves cursor |
| 比较多个装备属性/找到脚本 | catalog compare/charges | Batch base values; charge groups followed by native preview |
| 修改并检查配装 | fitting create/preview/edit/read | Typed changes, stable revision/requestId; preview leaves revision unchanged |
| 四族武器及输出曲线研究 | catalog describe + fitting outputs/curves | Separate qualitative synthesis from calculations; explicit hull/ammo/skills/target/selection; multiple families |
| 距离/信号/角速度敏感性 | fitting curves | Native points, source/fitHash, null/completeness and exact target |
| 实际作战研究 | battle run or prepare/start | Fixed revisions, finite supplies, explicit policy, report bound to accepted request |
| 等待/失败/容量管理 | battle wait + jobs | No start retry loops; inspect/list/preview compaction without shell |
| 更高级已支持机制 | help native + raw | Discover exact native contract; no invented mechanism |

Eight tools: catalog, fitting, battle, jobs, help, status, raw, result, each prefixed fitlab_. 33 operations and about 10.6k characters of tool discovery (rather than native graph schemas). Top-level properties advertise the union of each domain's arguments; help operation provides the exact per-action schema, and runtime validates that schema before doing work. All old frozen facade contracts v1-v4 are retained; v5 uses fitlab.py, not the legacy agent_mcp.py transport.

## Unified response

`{apiVersion, ok, state, data, issues, next}`. A successful command can produce needs_selection, needs_correction, preview, prepared, pending, partial, unavailable or failed rather than a completed task. Only complete represents a completed battle horizon. Each next entry contains a tool and executable arguments, never an implicit permission for destructive work. Raw/detail payloads retain references when too large; task summaries are shaped for direct reading. Missing numerical values are not replaced by zero.

Fitting edits preserve engine revision/requestId receipt semantics. Battle starts preserve jobId/request hashing. Default skills are untrained, all5 is explicit. Default battle policy is explicitly stationary-weapons-v1: no movement/repair/EWAR/drone behavior is implied. Default curve selection is online ship-weapon potential output; excluded IDs are returned, other contributions require an explicit selection. Static curves are not sustained DPS or measured combat averages.

## Experiment and storage design

New task-API starts save facade-owned provenance only after the native acceptance receipt. It binds the requestHash to draftId, exact fit snapshots, source, conditions and policy, with an integrity checksum. Numerical results still come only from public native results/events. Reports cross-check state identity during reading. expectedDraftId rejects an unrelated experiment. Historical jobs without this binding are marked historical_unbound; their original geometry can be read through native battle_manifest but fit correspondence is not fabricated. Old numerical jobs still require matching original binaries.

Report fields separate destruction events, last-damage snapshots and final/partial snapshots. Exact opponent-death HP stays unavailable if absent. Missile output is named damageApplicationMultiplierBeforeResistance, with explicit “not hit probability”. A first sample is not an average. There is no built-in generic ship power multiplier.

Native r62 adds battle_list, battle_manifest and battle_compact. New jobs retain current plus previous recovery generations only after committing the new pointer. Explicit compaction defaults to dryRun, requires an inactive job and expectedRevision, and never deletes job identity or final results. Completed/cancelled results validate final hashes before replacing recovery; failed jobs retain request/state/failure; interrupted jobs retain latest committed points and newer orphan evidence; prior attempts are untouched. Live writer leases, quota lock, path bounds and link checks protect maintenance. This is compaction, not arbitrary job deletion, quota inflation, or automatic sweeping of old data.

Storage is surfaced in status/jobs. Starts preflight a conservative 16 MiB available-payload floor; this is a refusal threshold, not a promise a run fits. Capacity is still enforced throughout execution. Failed jobs are not combat losses.

## CLI and configuration

Copy agent-config.example.json to ignored agent-config.json and set absolute engine, state, mcp_dll and baseline paths. The local deployment does this. `fitlab.cmd` is a Windows convenience entry; Python 3.9+ can call fitlab.py directly. Global --config/--engine/--state/--mcp-dll/--baseline precede domain/action. Environment overrides are documented in the bundled skill.

```
python fitlab.py catalog describe --name "狞獾级"
python fitlab.py fitting curves --input curve-request.json --out curves.json
python fitlab.py battle run --input experiment.json --out report.json
python fitlab.py jobs list
python fitlab.py help operation --domain fitting --operation edit
python fitlab.py mcp
```

JSON request files contain operation arguments, not domain/action. --input - reads stdin. Command help and task routing work without engine startup. CLI run/start/resume retains the worker host until terminal; progress is stderr, final JSON stdout/file. Ctrl-C requests cooperative cancellation. Do not use a short-lived legacy --call start as detached execution.

## Skills, prompts and deployment

Canonical distributable skill: skills/fitlab-agent; entry plus conditional references for weapon research, combat and CLI. Host integration prompt: prompts/fitlab-agent-system.md. Skill validation passes. Installed copies target Codex user skills and DSH's FitLab workspace .agents/skills; no unrelated global persona is replaced.

Engine 0.213.0-ui.agent1/r62, 42 native tools, isolated artifacts/agent-v5-build-001/bin/NEngine.Mcp/debug and UI-AGENT-V5-BASELINE.json. DSH/CLI uses a fresh v5 state namespace to avoid incompatible old checkpoints and preserve existing historical evidence. The old r59 state, r61 agent baseline, and webpage r60 runtime remain intact; no automatic migration or old-job deletion. Old job management is still inspectable via an explicit --state override; numerical replay needs its original runtime. No package or GitHub push.

## Validation evidence

- 2 management, 7 recovery, 9 quota/storage, 10 job lifecycle and 3 public frozen/live contract tests passed.
- test_agent_api.py exercises one-call ship traits, ambiguity/errors, four weapon families (laser/hybrid/projectile/missile), preview immutability, edit replay, native curve equality, unrelated frigate battle, duplicate run, bound/mismatched reports, compaction preservation, native management CLI parity, task CLI parity and CLI worker lifetime.
- test_agent_platform_dsh.mjs exercises actual installed DSH plugin, isJsonValue and model-facing output.render: traits, paginated search, fitting and curve, job listing; task CLI result equals MCP result.
- Evidence: output/agent-v5-acceptance.json and output/agent-v5-dsh-verification.json. Tests use disposable/fresh state; no user fits or historical results are removed.
- DSH samples: cold first lookup ~5.8s, warm next search ~9ms, fit edit ~158ms, small curve ~311ms. Local observations, not latency guarantees. Cold SDE startup remains a limitation.

No claim that DeepSeek autonomously follows every instruction has been made: this validates interfaces, executable workflows and client projections. A follow-up real-model conversation remains useful behavioral evaluation. Full combat-mechanism regression was not rerun because numerical rules are unchanged; affected job/recovery/contract suites were run.
