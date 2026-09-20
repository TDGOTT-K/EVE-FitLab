# Agent platform v6 — NUI-115

## Findings and design

Three real v5 DSH tasks exposed domain-union schemas hiding required fields (four missing sessionId create calls), missing fighter roster editing, preset rejection causing a refit, and repeated confusion between last-damage HP and opponent destruction time. This release fixes those shared interaction paths without changing numerical engine rules.

MCP now advertises 35 distinct operation tools, e.g. fitlab_catalog_describe, fitlab_fitting_create, fitlab_fitting_preview, fitlab_fitting_edit, fitlab_fitting_roster, fitlab_battle_policies and fitlab_battle_run. No action argument. Each operation has its real required fields and rejects unrelated ones; name/typeId are exclusive. Edit command branches declare their own required fields. This trades a larger discovery schema (~29k characters) for accurate pre-call guidance. CLI domain/action syntax is unchanged. Shared registry powers both. Frozen v5 is preserved; v6 is a deliberate MCP naming change, requiring a new client session.

## Objects and capability routing

Typed collection setters expose existing native setFighters, setDrones, setSubsystems, setImplants, setBoosters. Each replaces its entire named collection, preserving other fitting fields. Fighter member IDs, deployed/location/tubeIndex are explicit; actual class/size limits come from engine validation. fitting roster returns concise current rosters, fitted bay and primary-DPS projections, errors and a full evidence reference. Diagnostics mark needs_correction; a primary DPS does not include all abilities or certify combat equivalence. catalog describe now includes object capabilities. Category mismatch directs to roster/setters, not generic preview help. Global incomplete coverage is not feature absence.

## Battle policy and evidence

battle policies optionally inspects a draft and identifies exact unsupported ship/ability/mechanism. Default stationary-weapons-v1 remains unchanged. Explicit stationary-weapons-defense-v1 fires conventional guns/missiles and activates native admitted conventional hardeners on self. Hardener identity is checked against authoritative assembly bindings, not merely selfEffect (which could also be propulsion). Capacitor and paid cycles remain native. Propulsion, local sensors, repairs, EWAR, fighters and movement are not silently enabled; custom policies remain available. Incompatibility returns POLICY_CAPABILITY_MISMATCH, never changes the fitting.

Reports add citableFacts with destruction or last-damage timestamp/evidence and unavailableClaims for exact simultaneous HP, measured sustained DPS, counterfactual kill time and universal ship equivalence. No HP percentages or guessed damage formulas were added. Skill/prompt explain those distinctions and forbid refitting simply to satisfy a preset. These changes reduce ambiguity but do not guarantee model adherence.

## Validation and deployment

Engine remains 0.213.0-ui.agent1/r62/42 native tools. No new build, formulas or checkpoint versions. Reuse existing v5 state and deployed DLL/baseline; preserve all fits/jobs and webpage runtime.

- Shared API regression: four weapon families, preview/retry, unrelated fight, provenance, native/task CLI parity and lifecycle.
- v6 regression: exact schemas reject missing command fields; Thanatos with three six-member Firbolg II squadrons previews/installs without errors; primary DPS and selected fighter curves are available; conventional hardener policy produces positive native activation count without editing away the hardener; unsupported preset rejects and points to policy discovery.
- Actual installed DSH plugin discovery/model-visible JSON: all 35 tools, ordinary fit/curve/new fight and carrier fitting/roster; same CLI description equals MCP. Evidence: output/agent-v6-regression.json, agent-v6-acceptance.json, agent-v6-dsh-verification.json.
- Skill validated and synchronized to Codex and the DSH FitLab workspace. Docs and prompt updated; no autonomous DeepSeek run or universal correctness claim.

No package/push, no historical state deletion or migration. Cold data startup and unimplemented game mechanisms remain separate limitations.
