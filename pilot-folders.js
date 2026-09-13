const key='fitlab-pilot-folders';
export function readPilotFolders(){
 let p;try{p=JSON.parse(localStorage.getItem(key))}catch{}
 if(!p||!Array.isArray(p.folders))return {folders:[],assignment:{}};
 return {folders:p.folders.filter(f=>typeof f.id==='string'&&typeof f.name==='string'),assignment:p.assignment&&typeof p.assignment==='object'?p.assignment:{}};
}
export function writePilotFolders(p){localStorage.setItem(key,JSON.stringify(p));window.dispatchEvent(new Event('pilot-folders-changed'))}
export function onPilotFoldersChanged(fn){window.addEventListener('pilot-folders-changed',fn);window.addEventListener('storage',e=>{if(e.key===key)fn()})}
