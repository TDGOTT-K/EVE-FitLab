import {encodeFitCodes,encodeLegacyFitCodes,decodeFitCodes,parseCode} from '../../fit-image-code.js';
import assert from 'node:assert/strict';
const root='http://127.0.0.1:5207';const fit=(await fetch(root+'/api/library').then(r=>r.json())).find(f=>f.name==='刷洞pld');const catalog=await fetch(root+'/data/full-catalog.json').then(r=>r.json());
const options={notes:true,pilot:true};const old=await encodeLegacyFitCodes(fit,options),next=await encodeFitCodes(fit,options);
assert.deepEqual(await decodeFitCodes(next,catalog),await decodeFitCodes(old,catalog));
console.log(JSON.stringify({legacyCodes:old.length,binaryCodes:next.length,legacyQrBytes:old.reduce((n,s)=>n+new TextEncoder().encode(s).length,0),binaryQrBytes:next.reduce((n,s)=>n+Buffer.from(s.slice(5),'base64').length,0),skills:fit.skills.length,slots:fit.slots.length}));
