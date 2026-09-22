const key='fitlab-pilot-folders';
export function readPilotFolders(){
 let p;try{p=JSON.parse(localStorage.getItem(key))}catch{}
 if(!p||!Array.isArray(p.folders))return {folders:[],assignment:{}};
 return {order:Array.isArray(p.order)?p.order:[],initialized:Array.isArray(p.initialized)?p.initialized:[],folders:p.folders.filter(f=>typeof f.id==='string'&&typeof f.name==='string'),assignment:p.assignment&&typeof p.assignment==='object'?p.assignment:{}};
}
export function writePilotFolders(p){localStorage.setItem(key,JSON.stringify(p));window.dispatchEvent(new Event('pilot-folders-changed'))}
export function onPilotFoldersChanged(fn){window.addEventListener('pilot-folders-changed',fn);window.addEventListener('storage',e=>{if(e.key===key)fn()})}

export function initializePilotFolders(p,characters){
 let changed=false;p.initialized??=[];
 for(const c of characters){
  if(!(c.source==='内置'||/^(?:全)?技能 (I|II|III|IV) · 对比角色$/.test(c.name))||p.initialized.includes(String(c.id)))continue;
  p.initialized.push(String(c.id));changed=true;
  if(Object.hasOwn(p.assignment,String(c.id)))continue;
  let folder=p.folders.find(f=>f.name==='默认');
  if(!folder){folder={id:crypto.randomUUID(),name:'默认',open:true};p.folders.push(folder);}
  p.assignment[String(c.id)]=folder.id;
 }
 if(changed)writePilotFolders(p);
 return p;
}
export function orderedPilots(characters,p,points){
 const order=p.order||[],rank=new Map(order.map((id,i)=>[String(id),i]));
 return [...characters].sort((a,b)=>{
  if(rank.has(String(a.id))||rank.has(String(b.id)))return (rank.get(String(a.id))??Infinity)-(rank.get(String(b.id))??Infinity);
  return (points.value(a)??Infinity)-(points.value(b)??Infinity)||String(a.id).localeCompare(String(b.id));
 });
}
export function reorderPilot(p,characters,id,target,after,folder){
 const ids=characters.map(c=>String(c.id)).filter(x=>x!==String(id));
 const index=ids.indexOf(String(target));if(index<0||String(id)===String(target))return;
 ids.splice(index+(after?1:0),0,String(id));p.order=ids;p.assignment[String(id)]=folder;
 writePilotFolders(p);
}
