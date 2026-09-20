// Bounded, process-local detail loaders. DOM carries only an identity, never a full fit.
const entries=new Map();
export function deferDetail(key,load){
 if(!entries.has(key)){entries.set(key,{load,pending:null});while(entries.size>2048)entries.delete(entries.keys().next().value);}
 return key;
}
export function loadDeferredDetail(key){
 const entry=entries.get(key);if(!entry)return Promise.reject(Error('装配已变化，请重新打开详情'));
 if(!entry.pending)entry.pending=Promise.resolve().then(entry.load).catch(error=>{entry.pending=null;throw error});
 return entry.pending;
}
