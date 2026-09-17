let current=null;
export function closeScenarioQuickMenu(){current?.();}
export function openScenarioQuickMenu(anchor,{state,onSelect,onEdit}){
 if(current){current();return;}
 const menu=document.createElement('div');menu.className='scenario-quick-menu';menu.id='scenario-quick-menu';menu.setAttribute('popover','auto');menu.setAttribute('role','menu');menu.setAttribute('aria-label','快速切换情景');
 const close=()=>{if(!menu.isConnected)return;menu.hidePopover();menu.remove();anchor.setAttribute('aria-expanded','false');document.removeEventListener('fitlab-calculation-invalidated',close);window.removeEventListener('resize',close);current=null;};
 current=close;anchor.setAttribute('aria-expanded','true');
 const title=document.createElement('div');title.className='scenario-quick-title';title.textContent='切换情景';menu.append(title);
 const entries=[{id:null,name:'不应用情景'},...state.scenarios];
 const error=document.createElement('p');error.className='scenario-quick-error';error.setAttribute('role','alert');error.hidden=true;
 for(const entry of entries){
  const button=document.createElement('button');button.type='button';button.setAttribute('role','menuitemradio');button.setAttribute('aria-label',entry.name);button.setAttribute('aria-checked',String(entry.id===state.activeScenarioId));
  const mark=document.createElement('span');mark.className='scenario-quick-check';mark.textContent=entry.id===state.activeScenarioId?'✓':'';
  const name=document.createElement('span');name.textContent=entry.name;button.append(mark,name);menu.append(button);
  button.onclick=async()=>{
   if(entry.id===state.activeScenarioId){close();anchor.focus();return;}
   menu.querySelectorAll('button').forEach(b=>b.disabled=true);menu.setAttribute('aria-busy','true');error.hidden=true;
   try{await onSelect(entry.id);close();anchor.focus();}
   catch(e){if(!menu.isConnected)return;error.textContent=e.message;error.hidden=false;menu.querySelectorAll('button').forEach(b=>b.disabled=false);menu.removeAttribute('aria-busy');button.focus();}
  };
 }
 const edit=document.createElement('button');edit.type='button';edit.className='scenario-quick-edit';edit.setAttribute('role','menuitem');edit.textContent=state.scenarios.length?'编辑情景…':'创建情景…';edit.onclick=()=>{close();onEdit();};menu.append(edit,error);
 document.body.append(menu);menu.showPopover();
 const r=anchor.getBoundingClientRect();menu.style.left=Math.max(8,Math.min(r.right-menu.offsetWidth,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(r.bottom+6,innerHeight-menu.offsetHeight-8))+'px';
 menu.addEventListener('toggle',e=>{if(e.newState==='closed')close();});
 menu.onkeydown=e=>{
  const buttons=[...menu.querySelectorAll('button:not(:disabled)')],i=buttons.indexOf(document.activeElement);
  if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();buttons[(i+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length]?.focus();}
  if(e.key==='Home'||e.key==='End'){e.preventDefault();buttons[e.key==='Home'?0:buttons.length-1]?.focus();}
  if(e.key==='Escape'){e.preventDefault();e.stopPropagation();close();anchor.focus();}
  if(e.key==='Tab')close();
 };
 document.addEventListener('fitlab-calculation-invalidated',close);window.addEventListener('resize',close);
 menu.querySelector('[aria-checked="true"]')?.focus();
}
