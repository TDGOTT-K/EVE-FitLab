"""Bake EVE hull shading into portable glTF PBR textures for an asset probe.

Static hull only: decals, banners, shield-impact meshes and animated effects
are deliberately excluded and recorded. Original authored scene is retained.
"""
import json
import os
import sys
from pathlib import Path
import time

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
PROBE = ROOT / "output/ship-assets-probe"
sys.path.insert(0, str(PROBE / "tools/python"))
import numpy as np
import xatlas
ship_id = os.environ.get("SHIP_PROBE_ID", "crow")
target = PROBE / "ships" / ship_id
out = PROBE / "runtime" / ship_id
out.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(target / "source.blend"))
scene = bpy.context.scene
scene.frame_set(1)

def surface_group(material):
    if not material or not material.use_nodes:
        return None
    return next((n for n in material.node_tree.nodes if n.type == "GROUP"
                 and all(k in n.outputs for k in ("Albedo", "Roughness", "Emission", "Normal"))), None)

hulls = [o for o in bpy.data.objects if o.type == "MESH" and "gr2_mesh_index" in o
         and any(surface_group(s.material) for s in o.material_slots)]
assert hulls, "No supported hull geometry; do not export a placeholder"
# Evaluate the source pose once. Hundreds of sprite objects and their drivers
# otherwise make each edit-mode transition unnecessarily expensive.
depsgraph = bpy.context.evaluated_depsgraph_get()
for hull in hulls:
    frozen = bpy.data.meshes.new_from_object(hull.evaluated_get(depsgraph), depsgraph=depsgraph)
    matrix = hull.matrix_world.copy()
    hull.modifiers.clear()
    hull.parent = None
    hull.matrix_world = matrix
    hull.data = frozen
for o in list(bpy.data.objects):
    if o not in hulls:
        bpy.data.objects.remove(o, do_unlink=True)
for o in hulls:
    o.hide_render = False
    o.hide_set(False)
bpy.context.view_layer.update()
points = [o.matrix_world @ Vector(c) for o in hulls for c in o.bound_box]
lo = Vector(tuple(min(p[a] for p in points) for a in range(3)))
hi = Vector(tuple(max(p[a] for p in points) for a in range(3)))
center = (lo + hi) / 2
span = max(hi-lo)
camera_data = bpy.data.cameras.new("Probe camera")
camera = bpy.data.objects.new("Probe camera", camera_data)
scene.collection.objects.link(camera)
camera.location = center + Vector((1.25, -1.6, 1.15)).normalized() * span * 2.4
camera.rotation_euler = (center-camera.location).to_track_quat("-Z", "Y").to_euler()
camera_data.type = "ORTHO"
camera_data.ortho_scale = span * 1.5
camera_data.clip_end = span * 30
scene.camera = camera
for name, offset, energy, color in [
    ("Key", (1, -1, 2), 4, (0.8, 0.88, 1.0)),
    ("Fill", (-1, -0.4, 0.4), 2, (0.55, 0.7, 1.0)),
    ("Rim", (0, 1, 1), 3, (1.0, 0.8, 0.6)),
]:
    data = bpy.data.lights.new(name, "SUN")
    data.energy = energy
    data.angle = 0.2
    data.color = color
    lamp = bpy.data.objects.new(name, data)
    scene.collection.objects.link(lamp)
    lamp.rotation_euler = (-Vector(offset)).to_track_quat("-Z", "Y").to_euler()
scene.world = bpy.data.worlds.new("Probe space")
scene.world.use_nodes = True
scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.025, 0.035, 0.055, 1)
scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.3
scene.view_settings.view_transform = "AgX"
scene.render.resolution_x = 1000
scene.render.resolution_y = 750
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.engine = "CYCLES"
scene.cycles.samples = 16
scene.cycles.use_denoising = True
cycles = bpy.context.preferences.addons['cycles'].preferences
cycles.compute_device_type = 'OPTIX'
cycles.get_devices()
for device in cycles.devices:
    device.use = device.type == 'OPTIX'
scene.cycles.device = 'GPU'
scene.render.filepath = str(target / "source-preview.png")
bpy.ops.render.render(write_still=True)

started = time.time()
scene.cycles.samples = 1
scene.render.bake.use_clear = True
scene.render.bake.margin = 3
scene.render.bake.use_selected_to_active = False
size = 2048
stats = []
for index, hull in enumerate(hulls):
    bpy.ops.object.select_all(action="DESELECT")
    hull.select_set(True)
    bpy.context.view_layer.objects.active = hull
    assert hull.data.uv_layers, f"Missing UVs: {hull.name}"
    print("ATLAS", ship_id, flush=True)
    # EVE areas may overlap in UV0 or tile beyond [0,1]. Bake to a new,
    # non-overlapping atlas while original shading continues to read UV0.
    source_uv_name = hull.data.uv_layers.active.name
    hull.data.uv_layers[source_uv_name].active_render = True
    assert all(len(p.vertices) == 3 for p in hull.data.polygons), "Expected triangle mesh"
    vertices = np.empty(len(hull.data.vertices) * 3, dtype=np.float32)
    hull.data.vertices.foreach_get("co", vertices)
    faces = np.array([tuple(p.vertices) for p in hull.data.polygons], dtype=np.uint32)
    uv_atlas = xatlas.Atlas()
    uv_atlas.add_mesh(vertices.reshape(-1, 3), faces)
    options = xatlas.PackOptions()
    options.resolution = size
    options.padding = 4
    print("UNWRAP", ship_id, len(faces), flush=True)
    uv_atlas.generate(pack_options=options)
    mapping, indices, uv = uv_atlas[0]
    assert np.array_equal(mapping[indices], faces), "Atlas changed face order"
    atlas = hull.data.uv_layers.new(name="FITLAB_ATLAS")
    atlas.data.foreach_set("uv", uv[indices.reshape(-1)].reshape(-1))
    hull.data.uv_layers.active = atlas
    hull.data.uv_layers[source_uv_name].active_render = True
    materials = [s.material for s in hull.material_slots]
    assert all(surface_group(m) for m in materials), f"Unsupported material on {hull.name}"
    baked = {}
    for channel in ("Albedo", "Roughness", "Emission", "Normal"):
        name = f"{ship_id}-{index}-{channel.lower()}"
        image = bpy.data.images.new(name, width=size, height=size, alpha=False)
        image.colorspace_settings.name = "sRGB" if channel in ("Albedo", "Emission") else "Non-Color"
        changes = []
        for mat in materials:
            nodes, links = mat.node_tree.nodes, mat.node_tree.links
            output = next(n for n in nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output)
            original = output.inputs["Surface"].links[0].from_socket
            texture = nodes.new("ShaderNodeTexImage")
            texture.image = image
            nodes.active = texture
            emission = None
            if channel != "Normal":
                emission = nodes.new("ShaderNodeEmission")
                links.new(surface_group(mat).outputs[channel], emission.inputs["Color"])
                links.new(emission.outputs[0], output.inputs["Surface"])
            changes.append((mat, output, original, texture, emission))
        print("BAKE", ship_id, index, channel, flush=True)
        bpy.ops.object.bake(type="NORMAL" if channel == "Normal" else "EMIT", uv_layer="FITLAB_ATLAS")
        image.filepath_raw = str(target / f"{name}.png")
        image.file_format = "PNG"
        image.save()
        for mat, output, original, texture, emission in changes:
            mat.node_tree.links.new(original, output.inputs["Surface"])
            mat.node_tree.nodes.remove(texture)
            if emission:
                mat.node_tree.nodes.remove(emission)
        baked[channel] = image
    mat = bpy.data.materials.new(f"{ship_id}-hull-{index}-PBR")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    principled = nodes.get("Principled BSDF")
    principled.inputs["Metallic"].default_value = 0.0
    for channel, socket in (("Albedo", "Base Color"), ("Roughness", "Roughness"), ("Emission", "Emission Color")):
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = baked[channel]
        links.new(tex.outputs["Color"], principled.inputs[socket])
    principled.inputs["Emission Strength"].default_value = 1.0
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = baked["Normal"]
    normal = nodes.new("ShaderNodeNormalMap")
    links.new(tex.outputs["Color"], normal.inputs["Color"])
    links.new(normal.outputs["Normal"], principled.inputs["Normal"])
    hull.data.materials.clear()
    hull.data.materials.append(mat)
    for uv in list(hull.data.uv_layers):
        if uv.name != "FITLAB_ATLAS":
            hull.data.uv_layers.remove(uv)
    hull.data.uv_layers.active_index = 0
    hull.data.uv_layers[0].active_render = True
    for polygon in hull.data.polygons:
        polygon.material_index = 0
    stats.append({"mesh": hull.name, "triangles": sum(len(p.vertices)-2 for p in hull.data.polygons), "textureSize": size})
bpy.ops.object.select_all(action="DESELECT")
for hull in hulls:
    hull.select_set(True)
bpy.context.view_layer.objects.active = hulls[0]
if ship_id == "launcher":
    bpy.ops.wm.save_as_mainfile(filepath=str(target / "baked.blend"))
glb = out / f"{ship_id}.glb"
bpy.ops.export_scene.gltf(filepath=str(glb), export_format="GLB", use_selection=True,
                          export_apply=True, export_animations=False, export_skins=False,
                          export_cameras=False, export_lights=False, export_extras=False,
                          export_tangents=True)
scene.cycles.samples = 16
scene.render.filepath = str(target / "pbr-preview.png")
bpy.ops.render.render(write_still=True)
report = {"id": ship_id, "glbBytes": glb.stat().st_size, "meshes": stats,
          "elapsedSeconds": time.time()-started,
          "limitations": ["Static hull only; no decals, banners, sprites, shield impact, engine flame or turret attachments",
                          "PBR approximation: no exact Carbon fresnel response or animated heat shader",
                          "LOD and KTX2 not yet generated"]}
(target / "export-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("EXPORTED", json.dumps(report), flush=True)
