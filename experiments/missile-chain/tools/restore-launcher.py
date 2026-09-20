import bpy,json,pathlib,struct
from carbon_eve_resources import addon,service_access,turrets,ship
from carbon_eve_resources.core import turret_materials,sof_materials
p=pathlib.Path(__file__).resolve().parents[3] / "output/ship-assets-probe";out=p/'chain'
assert addon._context_terms_accepted(bpy.context)
j=json.loads((out/'launcher-source.json').read_text());doc=j['document'];res=j['resources']
b=(out/'weapon.black').read_bytes();count=struct.unpack_from('<H',b,12)[0]
strings=[];offset=14
for i in range(count):
 end=b.index(0,offset);strings.append(b[offset:end].decode());offset=end+1
packed=doc['turretEffect']['constParameters'];assert packed['structureSize']==24
data=bytes(packed['bytes']);params=[]
for i in range(packed['count']):
 idx,padding,*value=struct.unpack_from('<II4f',data,i*24)
 assert idx<len(strings) and padding==0
 params.append({'name':strings[idx],'value':value})
doc['turretEffect']['constParameters']=params
c=service_access.client();source=service_access.source();dna='cdn1_t1:caldaribase:caldari'
generic=c.request_json('GET',f'/{source.target}/{source.resource_build}/res/dx9/model/spaceobjectfactory/generic.black?format=json')['object']
faction=sof_materials.faction('caldaribase',c,**source.resources());race=addon._race_record(dna,source);hull=addon._hull_record(dna,source)
values=turret_materials.resolve(doc['turretEffect'],generic,faction,lambda name:sof_materials.material(name,c,**source.resources()),race=race,dna=dna,sof6=bool(hull.get('sof6',False)))
doc=turret_materials.apply(doc,values)
(out/'decoded-material.json').write_text(json.dumps({'strings':strings,'parameters':params,'factionOverrides':values,'document':doc},indent=2))
print('PARAMETERS',params,'OVERRIDES',values,flush=True)
bpy.ops.wm.open_mainfile(filepath=str(p/'ships/launcher/rigged.blend'))
mat=turrets._turret_material(doc,res,'XL torpedo original maps','eve');assert mat
meshes=[o for o in bpy.data.objects if o.type=='MESH']
for obj in meshes:
 obj.data.materials.clear();obj.data.materials.append(mat)
 for poly in obj.data.polygons:poly.material_index=0
ship.apply_ship_globals(meshes)
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
actions=list(bpy.data.actions)
for action in actions:action.name=action.name.rsplit('.',1)[-1]
active=next(a for a in actions if a.name=='Active');rig.animation_data.action=active;rig.animation_data.action_slot=active.slots[0]
bpy.context.scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(p/'ships/launcher/textured-rigged.blend'))
# Texture bake uses the same source material but retains this rigged source.
bpy.ops.wm.save_as_mainfile(filepath=str(p/'ships/launcher/source.blend'))
print('TEXTURED_READY',flush=True)
