# Agent catalog queries v4 / NUI-113

Engine candidate: 0.212.1-ui.catalog1, r61-ui-catalog1, 39 native tools. Facade v4 keeps eight tools. This is a query/discovery update, not a new game mechanic. Frozen agent v1-v3 and engine r60 remain unchanged.

## Task-oriented search

`fitlab_search {query:{groupId:40,filters:[{attribute:"cpu",max:100}],attributes:["cpu","power"],sortBy:"power",limit:3}}` performs one native catalog_search call. Filters are AND, inclusive, explicit base values in native source units. IDs and exact internal names take precedence over display aliases. Unknown/ambiguous names fail with discovery instructions. `query:{attributeSearch:"CPU"}` discovers IDs, names and units with pagination. Group/category/meta-group/official variation family, text and published-only filters are exposed. Attribute filtering/sorting is implemented in EveCatalog and reused by the native CLI, not Python formulas.

Missing display attributes remain null with state=missing. Items missing filter/sort attributes are excluded and counted explicitly; no implicit defaults. Sorting ties use typeId. Cursor includes all query options except limit plus source. Changing filters requires restarting without cursor. Returned nextCall is executable as-is, including from legacy batched name lookup. Each query returns provenance. Group facets above 20 have groupsNextCall; no discarded tail.

`fitlab_item {typeIds:[399,400],names:["cpu","power"]}` returns compact comparison rows, full detail references and per-item errors. Up to 10 items. Explicit result pagination now works for small values too and includes a nextCall. Attribute-name identity precedence is shared by existing item/fitted attribute resolution. Tool schema budget is 9,500 characters (actual 8,523); no extra top-level tool introduced.

## Validation and limits

Nine native catalog tests and three frozen/live contract tests pass. Python search and fitting/research regression tests pass (optional historical job test skipped this turn). Installed DSH MCP plugin is tested through isJsonValue and model-facing output.render. Native catalog CLI equals native MCP; facade CLI equals facade MCP. Evidence: output/catalog-verification.json and output/catalog-dsh-verification.json. A 93-item multi-filter query traverses every page, sorted without duplicate/missing items, in one native call per page. Warm search about 10 ms; first search about 4.4 seconds excluding process initialization, DSH about 5.8 seconds including startup. These timings are local samples, not guarantees. Three-item comparison is inline (~2.8k characters).

Search does not rank fitted DPS, price, sustainable tank or automatically certify a module fits a particular session. Those still require the appropriate official fitted/preview/valuation interfaces. No missing game mechanisms are simulated. Broad cold catalog loading remains a performance limitation.

## Isolated deployment

Build: artifacts/catalog-build-001/bin/NEngine.Mcp/debug/NEngine.Mcp.dll in the authorized engine copy. Explicit UI-AGENT-BASELINE.json binds agent to r61; NEngineBridge now accepts an optional explicit baseline path with the same validation. DSH profile adds --baseline and uses this DLL; existing DSH state remains unchanged. The webpage/default build stays on its r60 baseline. No package, push, session migration or battle checkpoint resume is performed. Unit tests use FITLAB_AGENT_BASELINE in addition to FITLAB_NENGINE_ROOT/FITLAB_AGENT_MCP_DLL. Native game version changed, so historical resumable battle checkpoints are not claimed compatible.
