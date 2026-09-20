import bpy,pathlib,json
p=pathlib.Path(__file__).resolve().parents[3] / "output/ship-assets-probe"
bpy.ops.wm.open_mainfile(filepath=str(p/'ships/launcher/textured-rigged.blend'))
obj=next(o for o in bpy.data.objects if o.type=='MESH');rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
with bpy.data.libraries.load(str(p/'ships/launcher/baked.blend'),link=False) as (src,dst):
 dst.meshes=[n for n in src.meshes if n.startswith('Citadel')]
 dst.materials=[n for n in src.materials if n.startswith('launcher-hull')]
baked=next(m for m in dst.meshes if m and m.uv_layers.get('FITLAB_ATLAS'))
assert len(baked.loops)==len(obj.data.loops)
uv=[0.0]*(len(baked.loops)*2);baked.uv_layers['FITLAB_ATLAS'].data.foreach_get('uv',uv)
for layer in list(obj.data.uv_layers):obj.data.uv_layers.remove(layer)
layer=obj.data.uv_layers.new(name='FITLAB_ATLAS');layer.data.foreach_set('uv',uv);layer.active_render=True
obj.data.materials.clear();obj.data.materials.append(dst.materials[0])
for polygon in obj.data.polygons:polygon.material_index=0
for action in bpy.data.actions:action.use_fake_user=True
bpy.context.scene.render.fps=30
bpy.context.scene.frame_set(0)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=str(p/'chain/launcher-textured.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIONS',export_skins=True,export_apply=False,export_tangents=True,export_force_sampling=True)
bpy.ops.wm.save_as_mainfile(filepath=str(p/'ships/launcher/final-rigged.blend'))
print('EXPORTED_RIG',flush=True)
