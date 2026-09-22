const avatarKey='fitlab-pilot-avatars';
const shapes=[
 ['combat','战斗','M7 25 25 7m-4 0h4v4M10 20l6 6M7 23l6 6M8 8l20 20M8 8v5m0-5h5'],
 ['shield','护盾','M18 4 30 9v9c0 8-12 14-12 14S6 26 6 18V9ZM18 10v15M12 17h12'],
 ['repair','后勤','M13 6h10v7h7v10h-7v7H13v-7H6V13h7Z'],
 ['explore','探索','M18 4 31 18 18 32 5 18ZM22 12l-3 9-6 3 3-9Z'],
 ['mining','采矿','M6 11c8-7 16-7 24 0M18 8v22M12 29h12M6 11l-2 5m26-5 2 5'],
 ['industry','工业','M5 30V16l9-5v7l9-5v7h8v10ZM7 16V6h5v8M10 25h3m5 0h3m5 0h3'],
 ['trade','贸易','M6 12h23l-6-6M30 24H7l6 6M8 7v10m20 2v10'],
 ['haul','运输','M5 11h26v20H5ZM5 11l7-6h12l7 6M18 11v20M12 5l6 6 6-6'],
 ['command','指挥','M5 10l7 6 6-10 6 10 7-6-4 17H9ZM10 31h16'],
 ['science','科研','M13 4h10M15 4v10L6 28q-1 4 4 4h16q5 0 4-4l-9-14V4M11 22h14M15 27h1m5-2h1']
];
export const builtinAvatars=[...['0','I','II','III','IV','V'].map((name,i)=>({id:'level-'+i,name:'技能 '+name})),...shapes.map(([id,name])=>({id,name}))];
export function avatarMarkup(id){
 const level=/^level-([0-5])$/.exec(id);
 if(level){const n=Number(level[1]);return `<svg viewBox="0 0 36 36" role="img" aria-label="技能 ${['0','I','II','III','IV','V'][n]}"><path d="M5 3h26v23l-13 7L5 26Z" fill="currentColor" fill-opacity=".08" stroke="currentColor"/><text x="18" y="19" text-anchor="middle" font-size="15" fill="currentColor">${['0','I','II','III','IV','V'][n]}</text>${[0,1,2,3,4].map(i=>`<rect x="${8+i*4}" y="23" width="3" height="4" fill="currentColor" fill-opacity="${i<n?1:.15}"/>`).join('')}</svg>`;}
 const entry=shapes.find(s=>s[0]===id);return entry?`<svg viewBox="0 0 36 36" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round" stroke-linecap="round" role="img" aria-label="${entry[1]}"><path d="${entry[2]}"/></svg>`:'';
}
const identity=c=>String(c.id||'draft:'+c.name);
export function savePilotAvatar(c,value){let prefs={};try{prefs=JSON.parse(localStorage.getItem(avatarKey))||{}}catch{}if(value)prefs[identity(c)]=value;else delete prefs[identity(c)];localStorage.setItem(avatarKey,JSON.stringify(prefs));window.dispatchEvent(new Event('pilot-avatar-changed'));}
export function copyDraftAvatar(from,to){if(from.id)return;try{const v=JSON.parse(localStorage.getItem(avatarKey))?.[identity(from)];if(v){savePilotAvatar(to,v);savePilotAvatar(from,null);}}catch{}}
export function pilotPortrait(character={}){
 let custom;try{custom=JSON.parse(localStorage.getItem(avatarKey))?.[identity(character)]}catch{}
 if(typeof custom==='string'){
  if(/^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(custom))return `<img src="${custom}" alt="自定义头像" width="36" height="36">`;
  const svg=avatarMarkup(custom);if(svg)return svg;
 }

 const id=Number(character.eveCharacterId);
 if(Number.isSafeInteger(id)&&id>0)return `<img src="https://images.evetech.net/characters/${id}/portrait?size=128" alt="游戏角色头像" width="36" height="36">`;
 const full=character.id==='all5'||character.name==='全技能 V · 模拟角色',empty=character.id==='none'||character.name==='无技能 · 基础对照'||!character.name;
 const roman=character.name?.match(/^(?:全)?技能 (I|II|III|IV) · 对比角色$/)?.[1];
 const level=full?5:roman?['','I','II','III','IV'].indexOf(roman):0;
 if(full||empty||roman)return `<svg viewBox="0 0 36 36" role="img" aria-label="${roman?'技能 '+roman:full?'全技能五级':'无技能'}"><path d="M5 3h26v23l-13 7L5 26Z" fill="currentColor" fill-opacity=".08" stroke="currentColor"/><text x="18" y="19" text-anchor="middle" font-size="15" font-family="Segoe UI" fill="currentColor">${roman|| (full?'V':'0')}</text>${[0,1,2,3,4].map(i=>`<rect x="${8+i*4}" y="23" width="3" height="4" fill="currentColor" fill-opacity="${i<level?1:.15}"/>`).join('')}</svg>`;
 return '<svg viewBox="0 0 36 36" fill="none" stroke="currentColor" role="img" aria-label="自定义角色"><circle cx="18" cy="12" r="6"/><path d="M6 32v-4c0-12 24-12 24 0v4"/></svg>';
}
