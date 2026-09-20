// Package the six measured hulls with KTX2, meshopt and a separate far LOD.
import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath, pathToFileURL} from 'node:url';
import {spawnSync} from 'node:child_process';
import {createHash} from 'node:crypto';
const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const probe = path.join(repo, 'output/ship-assets-probe');
const modules = path.join(probe, 'tools/node/node_modules');
const load = async p => import(pathToFileURL(path.join(modules, p)).href);
const {NodeIO} = await load('@gltf-transform/core/dist/index.js');
const {ALL_EXTENSIONS} = await load('@gltf-transform/extensions/dist/index.js');
const {dedup, prune, flatten, weld, meshopt, simplify, cloneDocument, getBounds} = await load('@gltf-transform/functions/dist/index.js');
const {MeshoptEncoder, MeshoptDecoder, MeshoptSimplifier} = await load('meshoptimizer/index.js');
const validator = (await load('gltf-validator/index.js')).default;
await Promise.all([MeshoptEncoder.ready, MeshoptDecoder.ready, MeshoptSimplifier.ready]);
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS).registerDependencies({'meshopt.decoder':MeshoptDecoder,'meshopt.encoder':MeshoptEncoder});
const pack = path.join(probe, 'pack');
await fs.mkdir(pack,{recursive:true});
const manifest = {version:1, sourceBuild:'3503375', bytesLimit:1_000_000_000,
  textureSize:2048, textures:'KTX2 UASTC + mipmaps + Zstd', geometry:'EXT_meshopt_compression',
  units:'meters, glTF Y-up', ships:[],
  limitations:['Static hulls; no separate decals, banners, sprites, shield impact, engine flames, or turret attachments.',
    'Baked approximation of Carbon materials. Full game shader/animation parity has not been established.',
    'Local development sample; redistribution clearance is not asserted.'],
  attribution:'EVE Online and related assets are the property of CCP hf. This material is used with limited permission of CCP Games. No official affiliation or endorsement by CCP Games is stated or implied.'};
for (const id of ['crow','phoenix','typhoon','bhaalgorn','keres','scythe']) {
  const raw = path.join(probe,'runtime',id,`${id}.glb`);
  const clean = await io.read(raw);
  let repairedTangents = 0;
  // Blender can emit a zero tangent at a degenerate UV corner. Supply a
  // deterministic unit tangent perpendicular to that vertex's normal.
  for (const mesh of clean.getRoot().listMeshes()) for(const primitive of mesh.listPrimitives()) {
    const t=primitive.getAttribute('TANGENT'),n=primitive.getAttribute('NORMAL');
    if(!t||!n)continue;
    for(let i=0;i<t.getCount();i++) {
      const v=t.getElement(i,[]),length=Math.hypot(...v.slice(0,3));
      if(!Number.isFinite(length)) throw new Error(`${id}: non-finite tangent`);
      if(length<1e-8) {
        const normal=n.getElement(i,[]);
        const axis=Math.abs(normal[1])<.9?[0,1,0]:[1,0,0];
        const cross=[axis[1]*normal[2]-axis[2]*normal[1],axis[2]*normal[0]-axis[0]*normal[2],axis[0]*normal[1]-axis[1]*normal[0]];
        const size=Math.hypot(...cross);
        if(size<1e-8)throw new Error(`${id}: degenerate normal`);
        t.setElement(i,[...cross.map(x=>x/size),v[3]<0?-1:1]);repairedTangents++;
      }
    }
  }
  const source=path.join(probe,'ships',id,`${id}-sanitized.glb`);
  await io.write(source,clean);
  const input = await fs.readFile(source);
  const validation = await validator.validateBytes(new Uint8Array(input), {uri:`${id}.glb`, maxIssues:100});
  await fs.writeFile(path.join(probe,'ships',id,'gltf-validation.json'),JSON.stringify(validation,null,2));
  if(validation.issues.numErrors) throw new Error(`${id}: ${validation.issues.numErrors} glTF errors`);
  const ktx = path.join(probe,'ships',id,`${id}-ktx.glb`);
  const result = spawnSync(process.execPath,[path.join(modules,'@gltf-transform/cli/bin/cli.js'),
    'uastc',source,ktx,'--level','2','--rdo','--rdo-lambda','0.75','--jobs','2'],
    {encoding:'utf8',env:{...process.env,PATH:path.join(probe,'tools/ktx-portable/bin')+path.delimiter+process.env.PATH}});
  process.stdout.write(result.stdout||'');
  if(result.status!==0) throw new Error(result.stderr||`KTX failed: ${id}`);
  const doc = await io.read(ktx);
  await doc.transform(dedup(),prune(),flatten(),weld());
  const lod = cloneDocument(doc);
  await lod.transform(simplify({simplifier:MeshoptSimplifier,ratio:0.25,error:0.003}),prune());
  const sourceReport=JSON.parse(await fs.readFile(path.join(probe,'ships',id,'report.json'),'utf8'));
  const entry={id,typeId:sourceReport.typeId,dna:sourceReport.dna,sourceBytes:sourceReport.sourceBytes,
    resourceProblems:sourceReport.problems,repairedTangents,baseValidation:{errors:validation.issues.numErrors,warnings:validation.issues.numWarnings},
    bounds:getBounds(doc.getRoot().listScenes()[0]),variants:[]};
  for(const [label,document] of [['lod0',doc],['lod1',lod]]) {
    const triangles=document.getRoot().listMeshes().reduce((sum,m)=>sum+m.listPrimitives().reduce((n,p)=>n+(p.getIndices()?.getCount()||p.getAttribute('POSITION').getCount())/3,0),0);
    await document.transform(meshopt({encoder:MeshoptEncoder,level:'high'}));
    const file=`${id}-${label}.glb`;
    await io.write(path.join(pack,file),document);
    const data=await fs.readFile(path.join(pack,file));
    // Round-trip the actual compressed bytes through the decoder.
    const reread=await io.readBinary(data);
    if(!reread.getRoot().listMeshes().length) throw new Error(`${file}: no decoded mesh`);
    entry.variants.push({lod:label,file,bytes:data.length,sha256:createHash('sha256').update(data).digest('hex'),triangles});
  }
  manifest.ships.push(entry);
  console.log('PACKED',id,JSON.stringify(entry.variants));
}
manifest.modelBytes=manifest.ships.flatMap(s=>s.variants).reduce((sum,v)=>sum+v.bytes,0);
if(manifest.modelBytes>manifest.bytesLimit) throw new Error('Art budget exceeded');
await fs.writeFile(path.join(pack,'manifest.json'),JSON.stringify(manifest,null,2));
console.log('TOTAL_MODEL_BYTES',manifest.modelBytes);
