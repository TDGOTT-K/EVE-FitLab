// Only complete, unambiguous six-piece sets qualify for bulk installation.
export function buildImplantSets(catalog){
 const candidates=new Map();
 for(const t of catalog){const match=/^(Low|Mid|High)-grade (.+) (Alpha|Beta|Gamma|Delta|Epsilon|Omega)$/.exec(t.en||'');if(!match)continue;const key=match[1]+'-grade '+match[2];if(!candidates.has(key))candidates.set(key,[]);candidates.get(key).push(t);}
 const sets=new Map(),byItem=new Map();
 for(const [key,items] of candidates){if(items.length!==6||new Set(items.map(t=>t.slot)).size!==6||items.some(t=>t.slot<1||t.slot>6))continue;items.sort((a,b)=>a.slot-b.slot);const alpha=items.find(t=>t.en.endsWith(' Alpha'));const name=alpha.name.replace(/\s*[-－—]?\s*阿尔法(?:型)?\s*$/,'').trim();const set={key,name,items};sets.set(key,set);for(const t of items)byItem.set(t.id,set);}
 return {sets,byItem};
}
export function implantSetChanges(current,items,fillOnly=false){return items.map(t=>({item:t,previous:current.find(x=>x.slot===t.slot)})).filter(x=>fillOnly?!x.previous:x.previous?.typeId!==x.item.id);}
export function applyImplantSet(current,items,fillOnly=false){const changes=implantSetChanges(current,items,fillOnly),slots=new Set(changes.map(x=>x.item.slot));return [...current.filter(x=>!slots.has(x.slot)),...changes.map(x=>({typeId:x.item.id,slot:x.item.slot}))].sort((a,b)=>a.slot-b.slot);}
export function implantSetPreview(current,set,catalog,fillOnly=false){
 const changes=implantSetChanges(current,set.items,fillOnly);
 if(!changes.length)return fillOnly?'槽位 1–6 均已占用，无需补齐':'此套装已完整安装';
 return changes.map(({item,previous})=>'槽 '+item.slot+'：'+(previous?'替换 '+(catalog.find(t=>t.id===previous.typeId)?.name||'已有脑插')+' → ':'装入 ')+item.name).join('\n');
}
