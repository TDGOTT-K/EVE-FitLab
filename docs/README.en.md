# EVE FitLab — current development source

FitLab combines a browser UI, Python local service and Electron desktop shell. Its only calculation backend is the independent NEngine MCP host: **0.211.0-ui.workbench1, contract r59, 38 tools, SDE 3503375, static rules v59**. The old bundled Dogma engine and .NET 9 host have been removed.

Place `EVE-FitLab/` and `NEngine/` next to each other. Prepare NEngine's pinned .NET 10.0.401 SDK, SDE index and MCP/CLI builds following its README. Then run:

```powershell
npm ci
powershell -NoProfile -File scripts/start.ps1
```

Open http://127.0.0.1:5208/. The SSO callback uses port 5207. The launcher keeps all user state under ignored `state/`. Override the engine path with `-EngineRoot` and the UI port with `-Port`.

The UI includes fitting libraries, skills, abyssal items, implants/boosters, fighter/drone inventories and image sharing. Availability and partial results follow the engine's explicit diagnostics. Seven locale resource sets exist; this is not a claim that every translation has been manually reviewed.

Run `npm run check:ui`, `npm run check:locales`, `npm run test:i18n`, `npm run test:share-code`. Install `requirements-dev.txt` before running `python scripts/check-phase1.py`, which additionally requires the engine and fixed data and uses temporary state. These checks do not certify complete game mechanics or a release installer.

See [documentation](README.md), [desktop packaging](../desktop/README.md), [cleanup scope](workspace-cleanup.md) and [contributing](../CONTRIBUTING.md). The website's historical downloads and the package version are not a release announcement for this development snapshot.
