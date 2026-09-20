"""Prepare an isolated Blender asset probe; never accept third-party terms.

Run with Blender --background --python and isolated BLENDER_USER_CONFIG /
BLENDER_USER_SCRIPTS directories (see the probe's launch script).
"""
import hashlib
import json
from pathlib import Path

import bpy


ROOT = Path(__file__).resolve().parents[1]
PROBE = ROOT / "output" / "ship-assets-probe"
ARCHIVE = PROBE / "tools" / "carbon_eve_resources-0.8.0.zip"
EXPECTED_SHA256 = "e2bc06fc2c0a2b780e0fc79d2153d632abad249ad7f739af82b8518108c419dd"

assert hashlib.sha256(ARCHIVE.read_bytes()).hexdigest() == EXPECTED_SHA256
bpy.ops.preferences.addon_install(filepath=str(ARCHIVE))
bpy.ops.preferences.addon_enable(module="carbon_eve_resources")
prefs = bpy.context.preferences.addons["carbon_eve_resources"].preferences
prefs.auto_load = False
prefs.cache_directory = str(PROBE / "source-cache")
bpy.ops.wm.save_userpref()

from carbon_eve_resources.addon import CREATOR_TERMS_ACCEPTANCE_ID

request = json.loads((ROOT / "data/sandbox-preview/request.json").read_text(encoding="utf-8"))
ships = [{"id": p["id"], "typeId": p["fit"]["shipTypeId"],
          "status": "pending", "dna": None, "files": [], "bytes": None}
         for p in request["participants"]]
report = {
    "status": "ready" if prefs.creator_terms_revision == CREATOR_TERMS_ACCEPTANCE_ID
              else "awaiting_terms_acceptance",
    "blender": bpy.app.version_string,
    "addon": {"version": "0.8.0", "zipBytes": ARCHIVE.stat().st_size,
              "sha256": EXPECTED_SHA256,
              "source": "https://github.com/carbonenginejs/tools-blender/releases/tag/v0.8.0"},
    "budget": {"hardLimitBytes": 1_000_000_000, "productionTargetBytes": 700_000_000,
               "sixShipTargetBytes": 80_000_000, "scope": "art assets, not application or tools"},
    "termsAccepted": prefs.creator_terms_revision == CREATOR_TERMS_ACCEPTANCE_ID,
    "downloadedModelBytes": 0,
    "note": "Preparation only. No asset acquisition or conversion attempted.",
    "ships": ships,
}
target = PROBE / "preflight.json"
target.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report, ensure_ascii=True, indent=2))
