// UI prototype mapping from NEngine native-tactical-modes.md (SDE 3503375).
// Selection only: the legacy calculator does not apply these mode effects.
export const tacticalModes={
 34317:[34319,34323,34321],
 34562:[34564,34566,34570],
 34828:[35676,35677,35678],
 35683:[35686,35687,35688],
};
const modeIcons=[
 '<path d="m10 2 6 2.5v5c0 3.8-3 6.7-6 8.5-3-1.8-6-4.7-6-8.5v-5Z"/><path d="M10 5v9"/>',
 '<path d="m7 3 9 7-9 7 3-7Z"/><path d="M3 6h2M2 10h4M3 14h2"/>',
 '<circle cx="10" cy="10" r="5.5"/><circle cx="10" cy="10" r="1"/><path d="M10 1v5m0 8v5M1 10h5m8 0h5"/>'
];
let dismissModeMenu=null;
const modeNames=['防御模式','推进模式','精确模式'];
const modeSvg=i=>'<svg viewBox="0 0 20 20" aria-hidden="true">'+(modeIcons[i]||'<path d="m10 2 7 4v8l-7 4-7-4V6Z"/>')+'</svg>';
export function mountTacticalMode(root,fit,onChange){
 dismissModeMenu?.();root.querySelector('.tactical-mode-row')?.remove();
 const modes=tacticalModes[fit.shipId];if(!modes)return;
 const selected=modes.indexOf(fit.tacticalModeTypeId);
 const row=document.createElement('div');row.className='tactical-mode-row';
 row.innerHTML='<span class="tactical-mode-label">'+modeSvg(-1)+'舰体模式</span><div class="tactical-mode-choice"><button type="button" id="tactical-mode-select" aria-label="舰体模式" aria-haspopup="listbox" aria-expanded="false">'+modeSvg(selected)+'<span>'+ (modeNames[selected]||'请选择模式')+'</span><span class="mode-arrow" aria-hidden="true">⌄</span></button></div><small title="本轮仅预览模式选择交互；当前计算尚未应用模式加成">加成待接入</small>';
 const trigger=row.querySelector('button');
 function open(){
  if(dismissModeMenu){dismissModeMenu();return;}
  const menu=document.createElement('div');menu.className='tactical-mode-menu';menu.id='tactical-mode-menu';menu.setAttribute('popover','auto');menu.setAttribute('role','listbox');menu.setAttribute('aria-label','选择舰体模式');
  const close=()=>{if(!menu.isConnected)return;menu.hidePopover();menu.remove();trigger.setAttribute('aria-expanded','false');window.removeEventListener('resize',close);document.removeEventListener('scroll',onScroll,true);dismissModeMenu=null;};
  const onScroll=e=>{if(!menu.contains(e.target))close();};
  dismissModeMenu=close;
  for(const i of [-1,0,1,2]){
   const button=document.createElement('button');button.type='button';button.setAttribute('role','option');button.setAttribute('aria-selected',String(i===selected));button.setAttribute('aria-label',modeNames[i]||'请选择模式');
   button.innerHTML=modeSvg(i)+'<span>'+(modeNames[i]||'请选择模式')+'</span><span class="mode-check" aria-hidden="true">'+(i===selected?'✓':'')+'</span>';
   button.onclick=()=>{close();if(i!==selected)onChange(i<0?null:modes[i]);root.querySelector('#tactical-mode-select')?.focus({preventScroll:true});};menu.append(button);
  }
  document.body.append(menu);menu.showPopover();trigger.setAttribute('aria-expanded','true');trigger.setAttribute('aria-controls',menu.id);
  const r=trigger.getBoundingClientRect();menu.style.width=Math.max(170,r.width)+'px';menu.style.left=Math.max(8,Math.min(r.left,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,r.bottom+menu.offsetHeight+5<innerHeight?r.bottom+5:r.top-menu.offsetHeight-5)+'px';
  menu.addEventListener('toggle',e=>{if(e.newState==='closed')close();});
  menu.onkeydown=e=>{
   const buttons=[...menu.querySelectorAll('button')],i=buttons.indexOf(document.activeElement);
   if(['ArrowDown','ArrowUp','Home','End'].includes(e.key)){e.preventDefault();buttons[e.key==='Home'?0:e.key==='End'?buttons.length-1:(i+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length].focus();}
   if(e.key==='Escape'){e.preventDefault();e.stopPropagation();close();trigger.focus({preventScroll:true});}
   if(e.key==='Tab')close();
  };
  window.addEventListener('resize',close);document.addEventListener('scroll',onScroll,true);
  menu.querySelector('[aria-selected="true"]').focus({preventScroll:true});
 }
 trigger.onclick=open;trigger.onkeydown=e=>{if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();open();}};
 root.prepend(row);
}
