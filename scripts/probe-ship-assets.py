"""Fetch/build one EVE sample using the pinned local CarbonEngineJS add-on.

Requires prior user acceptance through the original add-on operator.
SHIP_PROBE_ID selects one of the six existing sandbox participants.
"""
import hashlib
import json
import os
from pathlib import Path
import time

import bpy
from carbon_eve_resources import addon, service_access
from carbon_eve_resources.core import sof_fetch, sof_lookup

ROOT = Path(__file__).resolve().parents[1]
PROBE = ROOT / "output/ship-assets-probe"
ship_id = os.environ.get("SHIP_PROBE_ID", "crow")
request = json.loads((ROOT / "data/sandbox-preview/request.json").read_text(encoding="utf-8"))
participant = next(p for p in request["participants"] if p["id"] == ship_id)
target = PROBE / "ships" / ship_id
target.mkdir(parents=True, exist_ok=True)

assert addon._context_terms_accepted(bpy.context), "Accept the official terms through the add-on first"
prefs = bpy.context.preferences.addons["carbon_eve_resources"].preferences
client = service_access.client()
source = service_access.source()
started = time.time()
dna = sof_lookup.dna_for(participant["fit"]["shipTypeId"], client=client, **source.sde())
assert dna, f"No verified DNA for {ship_id}"
print("FETCH", ship_id, dna, source.to_dict(), flush=True)
document, resources, problems = sof_fetch.fetch_ship(
    dna, client, prefs.cache_directory, **source.resources(), source=source,
    prepare=addon._prepare_texture,
    progress=lambda s: print("RESOURCE", s, flush=True),
)
(target / "document.json").write_text(json.dumps(document), encoding="utf-8")
(target / "manifest.json").write_text(json.dumps(resources, indent=2), encoding="utf-8")
files = []
for logical, local in resources.items():
    path = Path(local)
    if not path.is_file():
        problems.append(f"Missing local resource: {logical}")
        continue
    files.append({"logical": logical, "path": str(path), "bytes": path.stat().st_size,
                  "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
report = {"id": ship_id, "typeId": participant["fit"]["shipTypeId"], "dna": dna,
          "source": source.to_dict(), "files": files, "problems": problems,
          "sourceBytes": sum(f["bytes"] for f in {f["sha256"]: f for f in files}.values()),
          "status": "downloaded", "elapsedSeconds": time.time() - started}
(target / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
assert report["sourceBytes"] < 1_000_000_000, "Sample exceeds entire art budget"
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
summary = addon._build_fetched_ship(document, resources, problems)
print("BUILD", summary, flush=True)
if summary.startswith("Error:"):
    raise RuntimeError(summary)
report["buildSummary"] = summary
report["meshObjects"] = len([o for o in bpy.data.objects if o.type == "MESH"])
report["triangles"] = sum(sum(len(p.vertices)-2 for p in o.data.polygons)
                          for o in bpy.data.objects if o.type == "MESH")
report["images"] = [{"name": i.name, "size": list(i.size), "path": i.filepath}
                    for i in bpy.data.images if i.source == "FILE"]
bpy.ops.wm.save_as_mainfile(filepath=str(target / "source.blend"))
report["status"] = "assembled"
report["elapsedSeconds"] = time.time() - started
(target / "report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("RESULT", json.dumps({k: v for k, v in report.items() if k not in ("files", "images")}), flush=True)
