import assert from 'node:assert/strict';
let resolveFetch;
globalThis.fetch=()=>new Promise(resolve=>{resolveFetch=resolve});
globalThis.document={addEventListener(){}};
globalThis.window={addEventListener(){}};
let timeout;
const module=await Promise.race([
 import('./fighter-ui.js'),
 new Promise((_,reject)=>{timeout=setTimeout(()=>reject(Error('Fighter fetch blocked module initialization')),2000)})
]);
clearTimeout(timeout);
assert.deepEqual(module.fighterBrowserItems([]),[]);
resolveFetch({ok:true,json:async()=>({items:[{id:1,name:'test',en:'test',class:'light',max:9,meta:2}]})});
await module.fighterCatalogReady;
const rows=module.fighterBrowserItems([]);
assert.equal(rows.length,1);assert.equal(rows[0].kind,'fighter');assert.equal(rows[0].metaGroupId,2);
console.log('Delayed fighter metadata does not block module initialization; results populate when ready');
