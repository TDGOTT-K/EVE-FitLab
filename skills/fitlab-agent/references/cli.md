# CLI

Read [connection.json](connection.json) for the CLI/config locations (relative paths are relative to that JSON file). The repository entry is `fitlab.py`; Python 3.9+ and the configured standalone engine are required. Connection settings are `engine`, `state`, `mcp_dll`, `baseline` in agent-config.json alongside the entry, or an explicit `--config FILE`. Environment overrides are FITLAB_NENGINE_ROOT, FITLAB_NENGINE_STATE, FITLAB_AGENT_MCP_DLL and FITLAB_AGENT_BASELINE. Baseline/version validation is retained. Inspect a configured file without exposing credentials; FitLab's local connection configuration contains paths, not tokens.

Examples:
```
python fitlab.py catalog describe --name "狞獾级"
python fitlab.py catalog search --input search.json
python fitlab.py fitting preview --input changes.json
python fitlab.py fitting curves --input curve.json --out curve-result.json
python fitlab.py battle run --input experiment.json --out report.json
python fitlab.py jobs list
python fitlab.py help operation --domain fitting --operation edit
```

A request file contains operation arguments only, without domain/action. `--json` accepts a JSON object; do not concatenate unescaped user text into shell commands. Structured arrays/objects/booleans passed as individual flags are JSON. `--input -` reads stdin. Help is offline. Normal operation stdout is one JSON envelope; progress is on stderr. Exit 2 indicates an input or failed operation. MCP mode is `python fitlab.py mcp` and uses the same registry/dispatcher.

CLI run/start/resume stays alive until terminal; Ctrl-C requests cooperative cancellation. Other reads can inspect a running job owned by another host. Closing a process that owns a running job is not a valid detached execution mechanism. No daemon install is required.
