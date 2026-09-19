export function pilotPortrait(character={}){
 const id=Number(character.eveCharacterId);
 if(Number.isSafeInteger(id)&&id>0)return `<img src="https://images.evetech.net/characters/${id}/portrait?size=128" alt="游戏角色头像" width="36" height="36">`;
 const full=character.id==='all5'||character.name==='全技能 V · 模拟角色',empty=character.id==='none'||character.name==='无技能 · 基础对照'||!character.name;
 if(full||empty)return `<svg viewBox="0 0 36 36" role="img" aria-label="${full?'全技能五级':'无技能'}"><path d="M5 3h26v23l-13 7L5 26Z" fill="currentColor" fill-opacity=".08" stroke="currentColor"/><text x="18" y="19" text-anchor="middle" font-size="15" font-family="Segoe UI" fill="currentColor">${full?'V':'0'}</text>${[0,1,2,3,4].map(i=>`<rect x="${8+i*4}" y="23" width="3" height="4" fill="currentColor" fill-opacity="${full?1:.15}"/>`).join('')}</svg>`;
 return '<svg viewBox="0 0 36 36" fill="none" stroke="currentColor" role="img" aria-label="自定义角色"><circle cx="18" cy="12" r="6"/><path d="M6 32v-4c0-12 24-12 24 0v4"/></svg>';
}
