import {fighterOutputOption,toggleFighterOutput,defaultFighterOutput} from './fighter-output-selection.js';
// Native fighter loadout presenter; server validates every change through the pinned engine.
export const fighterHull = ship => Number.isFinite(ship?.attrs?.[2055])&&ship.attrs[2055]>0;
let catalogError='';
const types=await fetch('./api/fighters').then(r=>{if(!r.ok)throw Error('舰载机目录暂不可用');return r.json()}).then(d=>d.items.map(t=>({...t,kind:({light:'轻型',heavy:'重型',support:'支援'})[t.class]}))).catch(e=>{catalogError=e.message;return []});
const icon=t=>`<img class="fighter-type-icon" src="https://images.evetech.net/types/${t.id}/icon?size=64" alt="" width="32" height="32" draggable="false" loading="lazy">`;
const type=id=>types.find(t=>t.id===Number(id));
let drag=null,selected=null,owner=null;
function clearFighterDrag(){
 drag=null;
 document.querySelectorAll('.fighter-drop').forEach(el=>el.classList.remove('fighter-drop'));
}
function fighterIssueMessage(issue,resources=[]){
 const label=({'fighterClass.light':'轻型','fighterClass.support':'支援','fighterClass.heavy':'重型'})[issue.message];
 if(!label)return issue.message;
 const resource=resources?.find(r=>r.id===issue.message);
 return Number.isFinite(resource?.capacity)?label+'舰载机已部署中队超过上限（'+resource.used+'/'+resource.capacity+'）':label+'舰载机部署超限';
}
function menu(event,origin,item,entries,header=null){
 event.preventDefault();event.stopPropagation();
 origin=origin.querySelector('.fighter-pick')||origin;
 document.dispatchEvent(new CustomEvent('fitlab-loadout-menu',{detail:{event,origin,item,entries,header,showDetails:false}}));
}
function bindMenu(element,open){
 element.oncontextmenu=open;
 element.addEventListener('keydown',e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')open(e)});
}
export function mountFighters(root,{ship,fit,report,mutate,browse,say,validate,onInfo}){
 root.querySelector('#fighter-config')?.remove();if(owner!==fit){owner=fit;selected=null;}
 if(!fighterHull(ship))return;
 const bay=report?.native?.fighterBay;const count=bay?.maximumSquadrons||0;
 const state=fit.fighterLoadout||{tubes:Array(count).fill(null),reserve:[]};
 const change=async (fn,newSquadronId=null)=>{if(host._busy)return;host._busy=true;const next=structuredClone(state);fn(next);say('正在校验舰载机配置…');try{const result=await validate({...fit,fighterLoadout:next});if(!host.isConnected)return;const blocking=result.issues.filter(e=>e.code==='STATIC_COVERAGE_INCOMPLETE'||e.code.startsWith('FIGHTER_')||e.code.startsWith('EVE_FIGHTER'));if(blocking.length){say('无法装载：'+blocking.map(e=>fighterIssueMessage(e,result.native?.resources)).join('；'));return}const defaults=newSquadronId?defaultFighterOutput(next,result.native?.outputContributions?.items||[],newSquadronId):null;mutate(()=>{fit.fighterLoadout=defaults?.loadout||next;if(defaults)fit.outputMetric=defaults.metric},'舰载机已更新 · N 号引擎校验')}catch(e){say(e.message)}finally{host._busy=false}};
 const host=document.createElement('section');host.id='fighter-config';root.prepend(host);host._catalogMenu=(e,el,t)=>menu(e,el,t,[['装入发射管',()=>{},{disabled:true,title:'等待可用的装配计算结果'}],['加入备用机库',()=>{},{disabled:true,title:'等待可用的装配计算结果'}],['详细信息',()=>onInfo(t)]]);if(!bay||!types.length){host.innerHTML='<div class="slot-heading">铁骑舰载机</div><p class="profile-note">'+(catalogError||'等待引擎返回发射管与机库参数…')+'</p>';return;}
 const row=(entry,index,reserve=false)=>{
  const t=entry&&type(entry.typeId),key=reserve?'reserve':'tubes';
  return `<div class="fighter-row ${entry?'filled':''}" data-fighter-index="${index}" data-fighter-list="${key}" ${entry?'draggable="true"':''}>
  <span class="fighter-tube">${reserve?'备':String(index+1).padStart(2,'0')}</span>
  <button class="fighter-pick" aria-label="${entry?t.name:'选择发射管 '+(index+1)+' 的舰载机'}">${t?icon(t):'<span class="fighter-empty">＋</span>'}<span>${t?t.name:'空发射管'}<small>${t?t.kind:'选择或拖入中队'}</small></span></button>
  ${entry?`<div class="fighter-number"><button data-delta="-1" aria-label="减少中队数量">−</button><b>${entry.quantity}<small> / ${t.max}</small></b><button data-delta="1" aria-label="增加中队数量">＋</button></div>${!reserve?`<button class="fighter-active ${entry.active?'on':''}" data-active aria-pressed="${entry.active}">${entry.active?'参战':'待命'}</button>`:''}`:''}</div>`;
 };
 const quotas=Object.entries(bay.classLimits).map(([kind,limit])=>{
  const resource=report.native.resources?.find(r=>r.id==='fighterClass.'+kind);
  const used=resource?.used,capacity=resource?.capacity??limit;
  const known=Number.isFinite(used)&&Number.isFinite(capacity);
  const color=known?(used>capacity?'limit-over':used===capacity?'limit-full':'limit-free'):'';
  return `<span class="fighter-class-quota" title="已部署中队 / 上限；待命和备用不计入">${({light:'轻型',support:'支援',heavy:'重型'})[kind]} <b class="${color}">${Number.isFinite(used)?used:'—'}</b>/${Number.isFinite(capacity)?capacity:'—'}</span>`;
 }).join('');
 host.innerHTML=`<div class="slot-heading"><button class="bay-filter" data-browse>铁骑舰载机</button><span class="fighter-mock">N 引擎</span><span>${state.tubes.filter(Boolean).length} / ${count}</span></div><div class="fighter-quotas">${quotas}<span class="fighter-bay-capacity">机库 ${report.native.resources.find(r=>r.id==="fighterBay")?.used.toLocaleString()} / ${bay.capacityCubicMeters.toLocaleString()} m³</span></div><div class="fighter-tubes">${Array.from({length:count},(_,i)=>row(state.tubes[i],i)).join('')}</div><details class="fighter-reserve" open><summary>备用机库 <span>${state.reserve.length} 中队</span></summary><div class="fighter-reserve-drop">${state.reserve.map((e,i)=>row(e,i,true)).join('')}<button class="fighter-reserve-add">＋ 添加备用中队</button></div></details>`;
 const paintSelection=()=>host.querySelectorAll('.fighter-row').forEach(el=>{
  const active=selected?.list===el.dataset.fighterList&&selected?.index===Number(el.dataset.fighterIndex);
  el.classList.toggle('fighter-selected',active);el.querySelector('.fighter-pick').setAttribute('aria-pressed',String(active));
 });
 const select=(list,index)=>{clearFighterDrag();selected=selected?.list===list&&selected?.index===index?null:{list,index};paintSelection();browse(!selected)};
 host._clearSelection=()=>{selected=null;paintSelection();browse(true)};
 paintSelection();
 host.querySelector('[data-browse]').onclick=()=>select('tubes',state.tubes.findIndex(x=>!x));
 host.querySelector('.fighter-reserve-add').onclick=()=>select('reserve',state.reserve.length);

 const drop=(target,index)=>{
  if(!drag)return;
  const sourceDrag=drag;clearFighterDrag();
  if(host._busy){say('舰载机配置正在校验，请稍后重试');return;}
  const next=structuredClone(state);
  if(sourceDrag.list){const source=next[sourceDrag.list][sourceDrag.index];if(!source)return;const old=next[target][index]||null;next[target][index]=source;next[sourceDrag.list][sourceDrag.index]=old;if(sourceDrag.list==='reserve')next.reserve=next.reserve.filter(Boolean)}
  else next[target][index]={id:'squadron-'+crypto.randomUUID(),typeId:sourceDrag.type,quantity:type(sourceDrag.type).max,active:true};
  change(s=>Object.assign(s,next),sourceDrag.list?null:next[target][index].id);
 };
 function weaponHeader(t,entry,list,index){
  const ident=entry.id||'fighter-'+list+'-'+index,projection=report?.native?.fighterEntities?.[ident];
  const abilities=projection?.abilityMetadata?.abilities?.filter(a=>[2233,2182,2401].includes(a.duration?.source?.attributeId))||[];
  if(!abilities.length)return null;
  const header=document.createElement('div');header.className='menu-title fighter-weapon-header';
  const caption=document.createElement('div');caption.className='fighter-weapon-caption';
  const name=document.createElement('span');name.textContent=t.name;const label=document.createElement('small');label.textContent='计入已选输出';caption.append(name,label);header.append(caption);
  const bar=document.createElement('div');bar.className='fighter-weapon-bar';header.append(bar);
  for(const a of abilities){
   const metric=fit.outputMetric||'nominalCycleDps';
   const contribution=report.native.outputContributions?.items.find(item=>item.source.squadronId===ident&&item.source.officialAbilityId===a.abilityId),reading=contribution?.metrics[metric],primary=['fighter_primary','fighter_missile_primary'].includes(contribution?.kind);
   const enabled=primary?!(entry.excludedAbilities||[]).includes(a.abilityId):(entry.includedSecondaryAbilities||[]).includes(a.abilityId),{available,finite}=fighterOutputOption(contribution,metric);
   const button=document.createElement('button');button.type='button';button.role='menuitemcheckbox';button.disabled=(!available&&!enabled)||host._busy;button.setAttribute('aria-checked',String(enabled));button.setAttribute('aria-label',(a.displayName.zh||a.displayName.en)+'计入DPS');
   button.title=available?((finite?'选择此武器会按有限弹量周期口径汇总已选输出；假定可发射，不代表可无限持续输出。':'只改变显示选择，不改变部署或消耗弹药。')+(list==='reserve'||!entry.active?'当前中队未参战，参战后计入。':'')):reading?.reason||'此能力没有可用的周期输出';
   const glyph=document.createElement('span');glyph.className='fighter-weapon-symbol';glyph.textContent=primary?'◎':a.duration.source.attributeId===2401?'✹':'↗';
   const text=document.createElement('span');text.className='fighter-weapon-name';text.textContent=a.displayName.zh||a.displayName.en;
   const stateLabel=document.createElement('small');stateLabel.textContent=!available?'当前不可计入':enabled?(finite?'已计入 · 有限弹量':'已计入'):(finite?'点击计入 · 有限弹量':'不计入');if(available&&(list==='reserve'||!entry.active)&&enabled)stateLabel.textContent='已选 · 待命';
   button.append(glyph,text,stateLabel);
   button.onclick=()=>{document.querySelector('#menu').hidden=true;const next=toggleFighterOutput(state,report.native.outputContributions?.items||[],list,index,a.abilityId,primary);mutate(()=>{fit.fighterLoadout=next.loadout;fit.outputMetric=next.metric},next.metric==='loadedCycleDps'?'已选输出按有限弹量周期计算 · 不代表持续输出':'已选输出恢复名义周期口径')};bar.append(button);
  }
  return header;
 }
 host.querySelectorAll('.fighter-row').forEach(el=>{
  const list=el.dataset.fighterList,index=Number(el.dataset.fighterIndex),entry=state[list][index];
  el.querySelector('.fighter-pick').onclick=()=>select(list,index);
  bindMenu(el,e=>{
   if(!entry){menu(e,el,{name:'发射管 '+(index+1)},[['选择舰载机',()=>select(list,index)]]);return;}
   const t=type(entry.typeId),empty=Array.from({length:count},(_,i)=>i).find(i=>!state.tubes[i]);
   const actions=list==='tubes'?[
    [entry.active?'设为待命':'设为参战',()=>change(s=>s.tubes[index].active=!entry.active),{disabled:host._busy}],
    ['移入备用机库',()=>change(s=>{s.reserve.push({...s.tubes[index],active:false});s.tubes[index]=null}),{disabled:host._busy}],
   ]:[['装入空发射管',()=>change(s=>{s.tubes[empty]={...s.reserve[index],active:true};s.reserve.splice(index,1)}),{disabled:host._busy||empty===undefined,title:empty===undefined?'没有空发射管':''}]];
   menu(e,el,t,[...actions,['在浏览器中定位',()=>{select(list,index);document.querySelector('#search').value=t.name;document.querySelector('#search').dispatchEvent(new Event('input',{bubbles:true}))}],['卸下',()=>change(s=>{if(list==='reserve')s.reserve.splice(index,1);else s.tubes[index]=null}),{disabled:host._busy}],['详细信息',()=>onInfo(t,'fighter.'+(entry.id||'fighter-'+list+'-'+index),(list==='tubes'?'发射管 '+(index+1):'备用中队 '+(index+1))+' · 单架属性 · 中队 '+entry.quantity+' 架')]],weaponHeader(t,entry,list,index));
  });
  el.querySelectorAll('[data-delta]').forEach(b=>b.onclick=()=>change(s=>{s[list][index].quantity=Math.max(1,Math.min(type(entry.typeId).max,entry.quantity+Number(b.dataset.delta)))}));
  const active=el.querySelector('[data-active]');if(active)active.onclick=()=>change(s=>s[list][index].active=!entry.active);
  el.ondragstart=e=>{drag={list,index};e.dataTransfer.setData('application/x-fitlab-fighter','1');e.dataTransfer.effectAllowed='move'};
  el.ondragend=clearFighterDrag;
  el.ondragover=e=>{if(!drag||host._busy)return;e.preventDefault();e.stopPropagation();document.querySelectorAll('.fighter-drop').forEach(x=>{if(x!==el)x.classList.remove('fighter-drop')});el.classList.add('fighter-drop')};
  el.ondragleave=()=>el.classList.remove('fighter-drop');
  el.ondrop=e=>{if(!drag)return;e.preventDefault();e.stopPropagation();drop(list,index);drag=null};
 });
 const reserve=host.querySelector('.fighter-reserve-drop');
 reserve.ondragover=e=>{if(!drag)return;e.preventDefault();reserve.classList.add('fighter-drop')};reserve.ondragleave=()=>reserve.classList.remove('fighter-drop');
 reserve.ondrop=e=>{if(!drag)return;e.preventDefault();e.stopPropagation();drop('reserve',state.reserve.length);drag=null};
 // Capture only fighter drags; equipment browser retains its ordinary handlers.
 const browser=document.querySelector('.browser');
 browser.onfighterremove=()=>{if(drag?.list)change(s=>{if(drag.list==='reserve')s.reserve.splice(drag.index,1);else s.tubes[drag.index]=null});drag=null};
 host._install=t=>{drag={type:t.id};let destination=selected||{list:'tubes',index:state.tubes.findIndex(x=>!x)};if(destination.index<0){say('发射管已满，请选择替换位置或备用机库');drag=null;return}drop(destination.list,destination.index);drag=null};
 host._catalogMenu=(e,el,t)=>{
  const index=selected?.list==='tubes'?selected.index:Array.from({length:count},(_,i)=>i).find(i=>!state.tubes[i]);
  const available=Number.isInteger(index)&&index>=0&&index<count;
  const install=(list,i)=>{drag={type:t.id};drop(list,i);drag=null};
  menu(e,el,t,[[available?(state.tubes[index]?'替换第 ':'装入第 ')+(index+1)+' 发射管':'装入发射管',()=>install('tubes',index),{disabled:host._busy||!available,title:available?'':'没有空发射管，请先选择替换位置'}],['加入备用机库',()=>install('reserve',state.reserve.length),{disabled:host._busy}],['详细信息',()=>onInfo(t)]]);
 };
}
// Navigation only: SDE 3248221 marketGroups 157 -> 2236 -> 2410 -> 840/2239/1310.
// All 53 pinned fighter type IDs were checked against that market tree.
const fighterMarketRoot=['无人机','铁骑舰载机','航母铁骑舰载机'];
const fighterMarketLeaves={light:'轻型铁骑舰载机',support:'后勤铁骑舰载机',heavy:'重型铁骑舰载机'};
export const fighterMarketIcons=Object.fromEntries([
 ...fighterMarketRoot.map((_,i)=>fighterMarketRoot.slice(0,i+1)),
 ...Object.values(fighterMarketLeaves).map(leaf=>[...fighterMarketRoot,leaf])
].map(path=>['/'+path.join('/'),'assets/market-1084.png']));
export function fighterBrowserItems(catalog){
 const metaNames=new Map(catalog.filter(t=>t.metaGroupId&&t.meta).map(t=>[t.metaGroupId,t.meta]));
 return [...types].sort((a,b)=>['light','support','heavy'].indexOf(a.class)-['light','support','heavy'].indexOf(b.class)||(a.meta??99)-(b.meta??99)||a.name.localeCompare(b.name,'zh-CN')).map(t=>({...t,kind:'fighter',attrs:{},effects:[],metaGroupId:t.meta,
  meta:metaNames.get(t.meta)||({1:'一级科技',2:'二级科技',4:'势力'})[t.meta]||'未标注科技分类',
  path:[...fighterMarketRoot,fighterMarketLeaves[t.class]],
  navigationSource:'SDE-3248221-marketGroups',marketGroupId:({light:840,support:2239,heavy:1310})[t.class]}));
}
export function bindFighterBrowserItem(el,item,onInfo){
 const t=type(item.id);
 el.ondblclick=()=>document.querySelector('#fighter-config')?._install?.(t);
 el.onkeydown=e=>{if(e.key==='Enter')el.ondblclick()};
 bindMenu(el,e=>{
  const host=document.querySelector('#fighter-config');
  if(host?._catalogMenu)host._catalogMenu(e,el,t);
  else menu(e,el,t,[['装入发射管',()=>{},{disabled:true,title:'当前舰船没有舰载机发射管'}],['详细信息',()=>onInfo(item)]]);
 });
 el.ondragstart=e=>{if(!document.querySelector('#fighter-config')?._install){e.preventDefault();return;}drag={type:t.id};e.dataTransfer.setData('application/x-fitlab-fighter','1');e.dataTransfer.effectAllowed='copy'};
 el.ondragend=clearFighterDrag;
}
document.addEventListener('dragover',e=>{if(drag?.list&&e.target.closest('.browser')){e.preventDefault();e.dataTransfer.dropEffect='move'}},true);
document.addEventListener('drop',e=>{if(drag?.list&&e.target.closest('.browser')){e.preventDefault();e.stopImmediatePropagation();document.querySelector('.browser').onfighterremove?.()}},true);
document.addEventListener('dragend',clearFighterDrag,true);
document.addEventListener('drop',clearFighterDrag);
window.addEventListener('blur',clearFighterDrag);
document.addEventListener('keydown',e=>{if(e.key==='Escape'){clearFighterDrag();if(selected)document.querySelector('#fighter-config')?._clearSelection?.()}});
document.addEventListener('contextmenu',e=>{if(drag){e.preventDefault();e.stopImmediatePropagation();clearFighterDrag()}},true);
