"""Summarize measured files and produce a six-hull contact sheet."""
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
PROBE = ROOT / "output/ship-assets-probe"
pack = PROBE / "pack"
manifest = json.loads((pack / "manifest.json").read_text(encoding="utf-8"))
names = {"crow": "黑鸦 / CROW", "phoenix": "凤凰 / PHOENIX", "typhoon": "台风 / TYPHOON",
         "bhaalgorn": "巴戈龙 / BHAALGORN", "keres": "克勒斯 / KERES", "scythe": "镰刀 / SCYTHE"}
unique_sources = {}
rows = []
for ship in manifest["ships"]:
    report = json.loads((PROBE / "ships" / ship["id"] / "report.json").read_text(encoding="utf-8"))
    for file in report["files"]:
        unique_sources[file["sha256"]] = file["bytes"]
    for variant in ship["variants"]:
        data = (pack / variant["file"]).read_bytes()
        assert hashlib.sha256(data).hexdigest() == variant["sha256"]
        assert len(data) == variant["bytes"]
    rows.append({"id": ship["id"], "sourceBytes": ship["sourceBytes"],
                 "lod0Bytes": ship["variants"][0]["bytes"], "lod1Bytes": ship["variants"][1]["bytes"],
                 "lod0Triangles": ship["variants"][0]["triangles"], "lod1Triangles": ship["variants"][1]["triangles"],
                 "resourceProblems": ship["resourceProblems"], "validation": ship["baseValidation"]})
(pack / "NOTICE.txt").write_text(manifest["attribution"] + "\n\n" + "\n".join(manifest["limitations"]) +
    "\n\nSource: EVE Online build 3503375; fetched with CarbonEngineJS Blender Tools 0.8.0.\n" +
    "CCP terms: https://support.eveonline.com/hc/en-us/articles/8563917741084-EVE-Online-Content-Creation-Terms-of-Use\n",
    encoding="utf-8")
pack_bytes = sum(p.stat().st_size for p in pack.rglob("*") if p.is_file())
assert pack_bytes <= 1_000_000_000
storage = {p.name: sum(f.stat().st_size for f in p.rglob("*") if f.is_file())
           for p in PROBE.iterdir() if p.is_dir()}
summary = {"ships": rows, "uniqueSourcePayloadBytes": sum(unique_sources.values()),
           "sourceCacheBytes": storage["source-cache"], "packBytes": pack_bytes,
           "artBudgetBytes": 1_000_000_000, "budgetPassed": True, "directories": storage,
           "limitations": manifest["limitations"]}
(PROBE / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
canvas = Image.new("RGB", (1800, 1130), "#0a1018")
draw = ImageDraw.Draw(canvas)
font_path = "C:/Windows/Fonts/msyh.ttc"
title = ImageFont.truetype(font_path, 34)
normal = ImageFont.truetype(font_path, 22)
small = ImageFont.truetype(font_path, 17)
draw.text((35, 24), "FITLAB  /  EVE 六舰素材样本", font=title, fill="#e8f0f8")
draw.text((35, 76), f"2K 烘焙贴图 · GLB + KTX2 + Meshopt · 两档模型合计 {pack_bytes/1e6:.2f} MB / 1,000 MB", font=normal, fill="#8fe6c6")
for i, ship in enumerate(manifest["ships"]):
    x, y = (i % 3) * 600, 128 + (i // 3) * 465
    picture = Image.open(PROBE / "ships" / ship["id"] / "pbr-preview.png").convert("RGB")
    picture = picture.resize((590, 442), Image.Resampling.LANCZOS)
    canvas.paste(picture, (x + 5, y))
    draw.text((x + 24, y + 12), names[ship["id"]], font=normal, fill="#e8f0f8")
    lod0 = ship["variants"][0]
    draw.text((x + 24, y + 407), f"LOD0  {lod0['bytes']/1e6:.2f} MB  /  {lod0['triangles']:,} tris", font=small, fill="#9eb2c5")
draw.text((35, 1080), "静态舰体材质预览；独立贴花、炮塔、尾焰及战斗特效未接入。EVE 素材版权归 CCP hf.。", font=small, fill="#91a4b8")
canvas.save(PROBE / "six-ships-preview.jpg", quality=94)
print(json.dumps(summary, ensure_ascii=True, indent=2))
