import bpy,json,pathlib
from carbon_eve_resources import addon,service_access,ship
from carbon_eve_resources.core import sof_fetch
p=pathlib.Path(__file__).resolve().parents[3] / "output/ship-assets-probe"
j=json.loads((p/'chain/launcher-source.json').read_text())
assert addon._context_terms_accepted(bpy.context)
c=service_access.client();s=service_access.source();prefs=bpy.context.preferences.addons['carbon_eve_resources'].preferences
f=sof_fetch.fetch_resource(j['weapon']['resPath'],c,prefs.cache_directory,**s.resources())
(p/'chain/weapon.black').write_bytes(pathlib.Path(f).read_bytes())
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
ship.import_geometry(j['resources'][j['document']['geometryResPath']],'launcher',logical_path=j['document']['geometryResPath'])
for o in bpy.data.objects:
 if o.type=='ARMATURE':print('BONES',[(b.name,list(b.head_local),list(b.tail_local)) for b in o.data.bones],flush=True)
 if o.type=='MESH':print('MESH',o.name,'normals',o.data.has_custom_normals,'attrs',[(a.name,a.data_type) for a in o.data.attributes],flush=True)
print('ACTIONS',[(a.name,list(a.frame_range),[s.identifier for s in a.slots]) for a in bpy.data.actions],flush=True)
bpy.ops.wm.save_as_mainfile(filepath=str(p/'ships/launcher/rigged.blend'))
