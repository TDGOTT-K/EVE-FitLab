import {transferSlots} from './slot-transfer.js';
import {installationLimitReason} from './installation-limits.js';
import {openScenarioQuickMenu} from './scenario-quick-menu.js';
import {numberAttributes,scenarioDetail,explanationAttributes,metricText} from './scenario-display.js';
import {openScenarioEditor} from './scenario-editor.js';
import {scenarioPresets,scenarioFields,withoutScenario} from './scenario-presets.js';
import {initI18n,setLocale,getLocale,matchesName,t,languages} from './i18n.js';
import {showImageImport} from './image-import.js';
import {showShareImage} from './share-image.js';
import {installCharacterManager} from './character-manager.js';
import {splitDroneStacks,maximumDroneQuantity} from './drone-stacks.js';
import {createLibraryTree} from './library-tree.js';
import {installWorkspaceResize} from './workspace-resize.js';
import {installPilotPicker} from './pilot-picker.js';
import {damageTenths,redistributeDamage,splitDamage,damageSplits,damageNames} from './defense-profile.js';
import {selectSlotMetrics} from './slot-metrics.js';
import {requestCache} from './request-cache.js';
import {renderItemInfo} from './item-info.js?v=ship-parameters-1';
import {installExplanations} from './explanations.js';
import {mountEngineStats,attributeDetail} from './engine-view.js?v=calculation-graphs-1';
await initI18n();
const catalog=await fetch('./data/full-catalog.json').then(r=>r.json());
const marketIcons=await fetch('./data/market-icons.json').then(r=>r.json());

const tLabel=t;
const $=s=>document.querySelector(s), typeIndex=new Map(catalog.map(t=>[t.id,t])), byId=id=>typeIndex.get(Number(id));
let ship=byId(587),fitRecord={name:'裂谷级 · 我的装配',shipId:587,skills:[],characterName:'无技能 · 基础对照'},report=null,reportVersion=-1,analysisVersion=0,analysisTimer,analysisState='pending';
const cachedItem=requestCache(id=>api('items/'+id),128);
const cachedCalculation=requestCache(fit=>api('analyze',fit),4);
let attackMode='dps';try{attackMode=localStorage.getItem('fitlab-attack-mode')==='edps'?'edps':'dps'}catch{}
function getCalculation(fit){fit={...fit,attackMode};if(fit.activeScenarioId||Object.keys(fit.scenario||{}).length)return api('analyze',fit);return cachedCalculation(JSON.stringify({shipId:fit.shipId,slots:fit.slots,skills:fit.skills,drones:fit.drones,cargo:fit.cargo,scenario:fit.scenario,attackMode}),fit)}
const treeOpen=new Set(),searchCollapsed=new Set();
let treeSearchQuery="";
const labels={subsystem:'子系统',high:'高槽',mid:'中槽',low:'低槽',rig:'改装件'}, counts={subsystem:ship.group===963?4:0,high:ship.attrs['14']||0,mid:ship.attrs['13']||0,low:ship.attrs['12']||0,rig:ship.attrs['1137']||0};
const fresh=()=>Object.entries(counts).flatMap(([kind,n])=>Array.from({length:n},(_,i)=>({key:`${kind}-${i}`,kind,item:null,ammo:null,online:true})));
const selectedSlots=new Set();let selectionAnchor=null,previewTimer=null,previewToken=0,previewKey=null,previewRestore=null;
let slots=fresh(),filter=null,dragged=null,slotDrag=null,history=[],redoHistory=[],menuOrigin=null;
try{const saved=JSON.parse(localStorage.getItem('fitlab-prototype-v1'));if(Array.isArray(saved))slots=slots.map(s=>{const old=saved.find(v=>v.key===s.key&&v.kind===s.kind);return old&&(!old.item||byId(old.item))?{...s,...old}:s})}catch{}
const img=t=>`<img src="https://images.evetech.net/types/${t.id}/icon?size=64" alt="" loading="lazy">`;
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const acceptsAmmo=(mod,ammo)=>mod&&ammo?.kind==='ammo'&&[604,605,606,609,610].some(a=>mod.attrs[a]===ammo.group)&&(!mod.attrs['128']||mod.attrs['128']===ammo.attrs['128']);
const needsAmmo=mod=>mod&&[604,605,606,609,610].some(a=>mod.attrs[a]);
function say(text){$('#message').textContent=text}
function currentFit(){return {...fitRecord,shipId:ship.id,slots:structuredClone(slots)}}
function save(){try{localStorage.setItem('fitlab-working-draft',JSON.stringify(currentFit()));$('#save').textContent='草稿已保留 · 待保存到装配库'}catch{$('#save').textContent='草稿存储失败，请保存装配'}scheduleAnalysis()}
function mutate(fn,msg){cancelInstallPreview();redoHistory=[];$('#redo').disabled=true;history.push(editSnapshot());if(history.length>30)history.shift();fn();if(filter?.ammo&&!needsAmmo(byId(slots.find(s=>s.key===filter.key)?.item)))filter=null;save();renderSlots();renderResources();renderShipStats();renderTree();$('#undo').disabled=false;say(msg)}
function undo(){cancelInstallPreview();if(!history.length)return;redoHistory.push(editSnapshot());applyEditSnapshot(history.pop());$('#redo').disabled=false;filter=null;save();renderSlots();renderResources();renderShipStats();renderTree();$('#undo').disabled=!history.length;say('已撤销最近一次操作')}
function redo(){cancelInstallPreview();if(!redoHistory.length)return;history.push(editSnapshot());applyEditSnapshot(redoHistory.pop());filter=null;save();renderSlots();renderResources();renderShipStats();renderTree();$('#undo').disabled=false;$('#redo').disabled=!redoHistory.length;say('已重做最近一次操作')}
$('#redo').onclick=redo;

$('#undo').onclick=undo;
function toggleFilter(key,ammo=false){cancelInstallPreview();filter=filter?.key===key&&filter.ammo===ammo?null:{key,ammo};renderSlots();renderTree()}
const stateLabels={Offline:'离线',Online:'关闭',Active:'启用',Overload:'超载'};
function moduleState(s){return s.state||(s.online===false?'Offline':byId(s.item)?.canActivate?'Active':'Online')}
function changeModuleState(key,state){const s=slots.find(s=>s.key===key);mutate(()=>{s.state=state;s.online=state!=='Offline'},'装备状态：'+stateLabels[state])}
function slotMetrics(s,group='details',displayReport=report){
 const siblings=slots.filter(x=>x.kind===s.kind&&x.item===s.item),index=siblings.findIndex(x=>x.key===s.key);
 const m=reportVersion===analysisVersion?(displayReport?.snapshot.modules.find(m=>m.workspaceSlotKey===s.key)||displayReport?.snapshot.modules.filter(m=>m.slotKind.toLowerCase()===s.kind&&m.dogmaTypeId===s.item)[index]):null;
 return selectSlotMetrics(byId(s.item),m,moduleState(s),group).map(({id,label,unit,title,value,baseline,conditions})=>`<span ${conditions?explanationAttributes(scenarioDetail(title,metricText(value,unit),value,baseline,[],conditions)):''} class="slot-metric" data-metric="${id}" title="${title}">${group==='resources'?`<svg class="slot-resource-icon" viewBox="0 0 24 24" aria-hidden="true">${id==='cpu'?'<rect x="6" y="6" width="12" height="12" rx="2"/><path d="M9 2v4m6-4v4M9 18v4m6-4v4M2 9h4m-4 6h4m12-6h4m-4 6h4"/>':'<path d="m13 2-8 12h6l-1 8 9-13h-6Z"/>'}</svg>`:`<span class="slot-metric-label">${label}</span>`}<b ${numberAttributes(value,baseline)}>${value==null?'—':new Intl.NumberFormat('zh-CN',{maximumFractionDigits:2}).format(value)}</b>${unit?`<span class="slot-metric-unit">${unit}</span>`:''}</span>`).join('');
}
function refreshSlotMetrics(){document.querySelectorAll('[data-module-metrics]').forEach(el=>{const s=slots.find(s=>s.key===el.dataset.moduleMetrics);if(s)el.innerHTML=slotMetrics(s,el.dataset.metricGroup||'details')})}
function renderSlots(){renderBayConfig();const root=$('#slots'),scroll=$('.fitting').scrollTop;root.innerHTML=Object.keys(counts).filter(kind=>counts[kind]||slots.some(s=>s.kind===kind&&s.item)).map(kind=>`<div class="slot-heading"><span>${labels[kind]}</span><span class="slot-limits">${kind==='high'?hardpoints():kind==='rig'?`<span title="校准值：已使用 / 上限">校准 ${limit(slots.filter(s=>s.kind==='rig'&&s.item).reduce((n,s)=>n+(byId(s.item).attrs[1153]||0),0),ship.attrs[1132])}</span>`:''}<span title="已用槽位 / 总槽位">${limit(slots.filter(s=>s.kind===kind&&s.item).length,counts[kind])}</span></span></div>`+slots.filter(s=>s.kind===kind).map(s=>{const t=byId(s.item),a=byId(s.ammo);return `<div class="slot ${t?'filled':''} ${filter?.key===s.key&&!filter.ammo?'selected':''} ${moduleState(s)==='Offline'?'offline':''} state-${moduleState(s).toLowerCase()}" data-key="${s.key}"><div class="module" tabindex="0" role="button" aria-label="${labels[kind]} ${Number(s.key.split('-')[1])+1} ${t?esc(t.name):'空槽'}">${t?img(t):'<span class="empty-symbol">＋</span>'}<span class="name ${t?'':'empty-text'}"><span class="module-heading"><span class="module-identity">${t?esc(t.name):kind==='subsystem'?['核心子系统','防御子系统','攻击子系统','推进子系统'][Number(s.key.split('-')[1])]:labels[kind]}${t?`<small>${kind==='rig'?`校准 ${t.attrs[1153]||0} · 尺寸 ${t.attrs[1547]}`:`${moduleState(s)==='Online'&&!t.canActivate?'在线 · 被动':stateLabels[moduleState(s)]}`}</small>`:''}</span>${t&&kind!=='rig'?`<span class="slot-resources" data-module-metrics="${s.key}" data-metric-group="resources">${slotMetrics(s,'resources')}</span>`:''}</span>${t&&kind!=='rig'?`<span class="slot-metrics" data-module-metrics="${s.key}">${slotMetrics(s)}</span>`:''}</span></div>${needsAmmo(t)?`<div class="ammo ${filter?.key===s.key&&filter.ammo?'selected':''}" tabindex="0" role="button" aria-label="${esc(t.name)}的弹药槽">${a?img(a):'<span>↳</span>'}<span>${a?esc(a.name):'弹药槽 · 点击筛选 / 拖入装填'}</span>${a?`<span class="ammo-count" title="满弹夹容量：模块容量 ÷ 单发弹药体积">X ${magazine(t,a)}</span>`:''}</div>`:''}</div>`}).join('')).join('');$('.fitting').scrollTop=scroll;
 root.querySelectorAll('.slot').forEach(el=>{const key=el.dataset.key;el.querySelector('.module').onclick=e=>{selectSlot(e,key)};el.querySelector('.module').onkeydown=e=>{if(e.target!==e.currentTarget)return;if(e.key==='Enter'||e.key===' '){e.preventDefault();selectSlot(e,key)}};el.oncontextmenu=e=>{if(selectedSlots.has(key)&&selectedSlots.size>1&&!e.target.closest('.ammo'))openBatchMenu(e,[...selectedSlots],'已选 '+selectedSlots.size+' 个槽位');else openMenu(e,key,e.target.closest('.ammo')?'ammo':'slot')};el.ondragover=e=>{e.preventDefault();if(slotDrag){const valid=!!slotTransfer(key)&&(!e.target.closest('.ammo')||slotDrag.ammo);e.dataTransfer.dropEffect=valid?'move':'none';el.classList.toggle('preview-target',valid);return;}const valid=canInstall(byId(dragged),key)&&(!e.target.closest('.ammo')||byId(dragged)?.kind==='ammo');e.dataTransfer.dropEffect=valid?'copy':'none';if(valid)queueInstallPreview(key);else cancelInstallPreview()};el.ondragleave=e=>{if(slotDrag&&!el.contains(e.relatedTarget))el.classList.remove('preview-target')};el.ondrop=e=>{e.preventDefault();e.stopPropagation();if(slotDrag){if(e.target.closest('.ammo')&&!slotDrag.ammo)say('装备不能放入弹药子槽');else moveInstalled(key);clearDrag();return;}const dropped=byId(e.dataTransfer.getData('text/plain'));if(e.target.closest('.ammo')&&dropped?.kind!=='ammo')say('弹药子槽只接受兼容弹药');else install(dropped,key);clearDrag()};const ammo=el.querySelector('.ammo');if(ammo){ammo.onclick=e=>toggleFilter(key,true);ammo.onkeydown=e=>{if(e.target===e.currentTarget&&(e.key==='Enter'||e.key===' ')){e.preventDefault();toggleFilter(key,true)}}}const source=slots.find(s=>s.key===key);if(source.item)bindInstalledDrag(el.querySelector('.module'),key,false);if(source.ammo&&ammo)bindInstalledDrag(ammo,key,true);});installRackTools();
}
function remove(key){const s=slots.find(s=>s.key===key);mutate(()=>{s.item=null;s.ammo=null;s.online=true;delete s.state},'已卸载装备及其弹药 · 可撤销')}
function unload(key){mutate(()=>slots.find(s=>s.key===key).ammo=null,'已卸载弹药，装备保留')}
function installRackTools(){
 for(const key of selectedSlots)if(!slots.some(s=>s.key===key))selectedSlots.delete(key);
 const headings=[...$('#slots').querySelectorAll('.slot-heading')];
 const kinds=Object.keys(counts).filter(kind=>counts[kind]||slots.some(s=>s.kind===kind&&s.item));
 headings.forEach((h,i)=>{const kind=kinds[i];if(!['high','mid','low'].includes(kind))return;
 const b=document.createElement('button');b.className='rack-operations';b.setAttribute('aria-label',labels[kind]+'批量操作');b.textContent='整组 ▾';
 b.onclick=e=>openBatchMenu(e,slots.filter(s=>s.kind===kind).map(s=>s.key),labels[kind]+'整组');h.firstElementChild.after(b);
 });
 $('#slots').querySelectorAll('.slot').forEach(el=>el.classList.toggle('multi-selected',selectedSlots.has(el.dataset.key)));
}
function canInstall(t,key){const s=slots.find(s=>s.key===key);return !!(t&&s&&(t.kind==='ammo'?acceptsAmmo(byId(s.item),t):t.kind===s.kind&&(t.kind!=='rig'||t.attrs[1547]===ship.attrs[1547])&&(t.kind!=='subsystem'||t.attrs[1380]===ship.id&&t.attrs[1366]===125+Number(s.key.split('-')[1]))))}
function installTarget(t){
 if(filter?.bay||t?.kind==='drone')return null;
 if(selectedSlots.size>1)return null;
 const explicit=filter?.key||(selectedSlots.size===1?[...selectedSlots][0]:null);
 if(explicit)return canInstall(t,explicit)?explicit:null;
 return slots.find(s=>t?.kind==='ammo'?acceptsAmmo(byId(s.item),t):canInstall(t,s.key)&&!s.item)?.key;
}
function applyCandidate(slot,item){
 if(item.kind==='ammo'){slot.ammo=item.id;return;}
 const ammo=byId(slot.ammo),state=moduleState(slot);
 const replacing=!!slot.item;
 slot.item=item.id;slot.ammo=ammo&&acceptsAmmo(item,ammo)?ammo.id:null;
 slot.state=item.kind==='subsystem'?'Online':replacing&&state==='Offline'?'Offline':replacing&&state==='Online'?'Online':item.canActivate?(state==='Overload'&&item.canOverload?'Overload':'Active'):'Online';
 slot.online=slot.state!=='Offline';
}
function installLimit(t,key){
 if(t?.kind==='ammo')return '';
 if(reportVersion!==analysisVersion||analysisState!=='complete')return '装配计算中，请完成后再安装';
 return installationLimitReason(t,key,slots,byId,report?.attributes);
}
function shipInstallTarget(t){return slots.find(s=>!s.item&&canInstall(t,s.key))?.key;}
function install(t,key){
 if(!t)return;
 if(!key&&(filter?.bay||t.kind==='drone')){addToBay(t,filter?.bay||'drones');return;}
 key=key||installTarget(t);
 if(!key||!canInstall(t,key)){say(selectedSlots.size>1?'请先选择一个目标槽位，或拖到指定槽位':'无法安装：请选择兼容槽位，或检查弹药组、尺寸及空槽');return;}
 const limitReason=installLimit(t,key);if(limitReason){say('无法安装：'+limitReason);return;}
 mutate(()=>applyCandidate(slots.find(s=>s.key===key),t),`已${t.kind==='ammo'?'装填':'安装'} ${t.name}`);
}
function loadAll(t){const target=slots.filter(s=>acceptsAmmo(byId(s.item),t));if(!target.length){say('没有可使用这种弹药的已装备模块');return}mutate(()=>target.forEach(s=>s.ammo=t.id),`已为 ${target.length} 件兼容装备装填 ${t.name}`)}
function renderTree(){cancelInstallPreview();const root=$('#tree'),scroll=root.scrollTop,closed=new Set();const selected=filter&&slots.find(s=>s.key===filter.key);$('#filter').innerHTML=selected?`<button aria-label="清除装备筛选">${filter.ammo?'兼容弹药 · '+esc(byId(selected.item)?.name||''):labels[selected.kind]+' · 槽位 '+(Number(selected.key.split('-')[1])+1)}　×</button>`:'';if(selected)$('#filter button').onclick=()=>{filter=null;renderSlots();renderTree()};if(filter?.bay){$('#filter').innerHTML='<button aria-label="清除装备筛选">'+(filter.bay==='drones'?'无人机库':'货舱')+' ×</button>';$('#filter button').onclick=()=>{filter=null;renderSlots();renderTree()}}const q=$('#search').value.trim().toLowerCase();if(q!==treeSearchQuery){searchCollapsed.clear();treeSearchQuery=q}const items=catalog.filter(t=>!['ship','skill'].includes(t.kind)&&(!filter?.bay||acceptsBay(t,filter.bay))&&(t.kind!=='subsystem'||t.attrs[1380]===ship.id)&&(!selected||(filter.ammo?acceptsAmmo(byId(selected.item),t):canInstall(t,selected.key)))&&(!q||(matchesName(t,q)||t.path.some(p=>tLabel(p).toLowerCase().includes(q))||t.path.join(' ').toLowerCase().includes(q))));$('#count').textContent=`${items.length} 件`;const tree={};for(const t of [...items].sort((a,b)=>(a.kind==='ammo')-(b.kind==='ammo'))){let n=tree;for(const p of [...t.path,...(t.kind==='ammo'?[]:[t.meta||'科技 I'])]){n[p]??={};n=n[p]}(n._items??=[]).push(t)}
 const branch=(n,path='')=>Object.entries(n).sort(([a],[b])=>(a==='未列入市场')-(b==='未列入市场')).map(([k,v])=>k==='_items'?v.map(t=>`<div class="item" draggable="true" tabindex="0" role="button" data-id="${t.id}" aria-label="${esc(t.name)}">${img(t)}<span>${esc(t.name)}</span><em>${t.en.endsWith(' II')?'II':''}</em></div>`).join(''):(()=>{const key=path+'/'+k,open=q?!searchCollapsed.has(key):treeOpen.has(key);return `<details data-path="${esc(key)}" ${open?'open':''}><summary>${marketIcons[key]?`<img class="tree-icon" src="${marketIcons[key]}" alt="">`:''}${esc(k)}</summary>${open?branch(v,key):''}</details>`})()).join('');root.innerHTML=items.length?branch(tree):'<div class="hint">没有匹配物品。试试清除搜索或筛选标签。</div>';root.scrollTop=scroll;
 root.querySelectorAll('summary').forEach(summary=>summary.onclick=e=>{e.preventDefault();const key=summary.parentElement.dataset.path;if(q){if(searchCollapsed.has(key))searchCollapsed.delete(key);else searchCollapsed.add(key)}else{if(treeOpen.has(key))treeOpen.delete(key);else treeOpen.add(key)}renderTree()});
 root.querySelectorAll('.item').forEach(el=>{const t=byId(el.dataset.id);el.onpointerenter=()=>{if(!dragged&&!filter?.bay&&t.kind!=='drone')queueInstallPreview(installTarget(t),t.id,'hover')};el.onpointerleave=()=>{if(!dragged)cancelInstallPreview()};el.onfocus=()=>{if(!dragged&&!filter?.bay&&t.kind!=='drone')queueInstallPreview(installTarget(t),t.id,'hover')};el.onblur=()=>{if(!dragged)cancelInstallPreview()};el.ondblclick=()=>install(t);el.onkeydown=e=>{if(e.key==='Enter')install(t)};el.oncontextmenu=e=>openMenu(e,t.id,'item');el.ondragstart=e=>{clearDrag();dragged=t.id;e.dataTransfer.setData('text/plain',String(t.id));e.dataTransfer.effectAllowed='copy';document.querySelectorAll('.slot').forEach(slot=>slot.classList.add(canInstall(t,slot.dataset.key)?'compatible':'incompatible'));const shipKey=shipInstallTarget(t);if(shipKey&&!installLimit(t,shipKey))$('#ship').classList.add('compatible');const n=slots.filter(s=>acceptsAmmo(byId(s.item),t)).length;if(n){$('#ship').classList.add('compatible');say(`拖到舰船图像，为 ${n} 件兼容装备装填`)}else say('拖入槽位替换，或拖到舰船图片安装到空槽')};el.ondragend=clearDrag});}
function clearDrag(){cancelInstallPreview();dragged=null;slotDrag=null;$('.browser')?.classList.remove('unload-drop-target');document.querySelectorAll('.installed-drag-source').forEach(el=>el.classList.remove('installed-drag-source'));document.querySelectorAll('.compatible,.incompatible').forEach(el=>el.classList.remove('compatible','incompatible'))}
function slotTransfer(key){
 if(!slotDrag||slotDrag.version!==analysisVersion)return null;
 return transferSlots(slots,slotDrag.key,key,slotDrag.ammo,(id,target)=>canInstall(byId(id),target));
}
function moveInstalled(key){
 const next=slotTransfer(key);if(!next){if(key!==slotDrag?.key)say('无法移动：双方槽位或弹药不兼容');return;}
 const source=slotDrag.key,ammo=slotDrag.ammo,target=slots.find(s=>s.key===key),exchange=ammo?!!target.ammo:!!target.item;
 mutate(()=>{slots=next;filter=null;selectedSlots.clear();selectedSlots.add(key);selectionAnchor=key;},(exchange?'已交换':'已移动')+(ammo?'弹药':'装备')+' · 可撤销');
}
function bindInstalledDrag(element,key,ammo){
 element.draggable=true;
 element.querySelectorAll('img').forEach(img=>img.draggable=false);
 element.ondragstart=e=>{
  e.stopPropagation();clearDrag();const source=slots.find(s=>s.key===key),id=ammo?source?.ammo:source?.item;if(!id){e.preventDefault();return;}
  slotDrag={key,ammo,version:analysisVersion};dragged=id;e.dataTransfer.setData('text/plain',String(id));e.dataTransfer.setData('application/x-fitlab-slot',key);e.dataTransfer.effectAllowed='move';element.classList.add('installed-drag-source');
  $('#slots').querySelectorAll('.slot').forEach(el=>el.classList.add(slotTransfer(el.dataset.key)?'compatible':'incompatible'));
  $('.browser').classList.add('unload-drop-target');say('拖回装备浏览器卸载 · 拖到空槽移动 · 拖到已占用槽位交换');
 };
 element.ondragend=clearDrag;
}
$('.browser').addEventListener('dragover',e=>{if(!slotDrag)return;e.preventDefault();e.dataTransfer.dropEffect='move';});
$('.browser').addEventListener('drop',e=>{
 if(!slotDrag)return;e.preventDefault();e.stopPropagation();
 const {key,ammo,version}=slotDrag;
 if(version===analysisVersion){if(ammo)unload(key);else remove(key);}
 clearDrag();
});
function magazine(t,a){return t.capacity!=null&&a.volume>0?Math.floor(t.capacity/a.volume+1e-8):'—'}
function renderResources(displayReport=report,host=$('#resources')){if(!displayReport){host.innerHTML='<p class="profile-note">等待计算服务…</p>';return}const a=displayReport.attributes;
 host.innerHTML=[['CPU',a.cpuUsed,a.cpuAvailable,'tf','cpuOutput','cpuUsage'],['能量栅格',a.powergridUsed,a.powergridAvailable,'MW','powerOutput','powergridUsage']].map(([name,value,max,unit,attr,field])=>{
 const contributions=displayReport.snapshot.modules.filter(m=>m.state!=='Offline'&&m[field]>0).map(m=>[byId(m.dogmaTypeId)?.name||m.name,`+ ${m[field].toFixed(2)} ${unit}`]);
 const sum=displayReport.snapshot.modules.filter(m=>m.state!=='Offline').reduce((n,m)=>n+(m[field]||0),0);
 const usage={title:name+' · 占用',result:`${value.toFixed(2)} ${unit}`,terms:Math.abs(sum-value)<0.01?(contributions.length?contributions:[['已安装在线模块','0']]):[['模块明细','当前接口未提供可核对的完整分解']],conditions:[['来源','Dogma 装配结果'],['离线模块','不占用资源']]};
 const capacity=attributeDetail(name+' · 上限',max,unit,attr,a,catalog,fitRecord.characterName),capacityChanged=Math.abs(max-(ship.attrs[attr==='cpuOutput'?48:11]||0))>0.001;
 const remaining=max-value;
 const detail={title:name+' · 剩余',result:`${remaining.toFixed(1)} / ${max} ${unit}`,terms:[['已用',`${value.toFixed(2)} ${unit}`,null,value?usage:null],['上限',`${max} ${unit}`,null,capacityChanged?capacity:null],['剩余',`${(max-value).toFixed(2)} ${unit}`]],conditions:[['角色',fitRecord.characterName||'无技能']]};
 return `<div class="meter ${value>max?'over':''}" ${value||capacityChanged?`tabindex="0" data-explain="${esc(JSON.stringify(detail))}"`:''}><label>${name} 剩余<span>${remaining.toFixed(1)} / ${max} ${unit}</span></label><progress aria-label="${name}剩余" value="${Math.max(0,Math.min(remaining,max))}" max="${max||1}"></progress></div>`}).join('')}

function hardpoints(){return [['turret','炮塔',42,102],['launcher','导弹发射器',40,101]].map(([icon,name,effect,attr])=>{const used=slots.filter(s=>byId(s.item)?.effects?.includes(effect)).length,total=report?.attributes?.[icon==='turret'?'turretHardpointsAvailable':'launcherHardpointsAvailable']??ship.attrs[attr]??0;return `<span class="hardpoint ${used>total?'over':''}" tabindex="0" title="${name}挂点：${used} / ${total}" aria-label="${name}挂点：${used} / ${total}"><img src="assets/${icon}.png" alt="" width="26" height="26">${limit(used,total)}</span>`}).join('')}
function limit(used,max){return `<b class="limit-${used<max?'free':used===max?'full':'over'}">${used}</b> / ${max}`}
function renderShipStats(){if(report){renderEngineStats();return}$('#ship-stats').innerHTML='<div class="stat-block profile-note">等待装配计算…</div>';renderScenario()}
let infoOrigin=null;
function closeInfo(){const panel=$('#info-window');if(panel.hidden)return;panel.hidden=true;if(infoOrigin?.isConnected)infoOrigin.focus({preventScroll:true})}
let infoRequest=0;
async function showInfo(t,slotKey=null,droneIndex=null){infoOrigin=document.activeElement;const request=++infoRequest,panel=$('#info-window'),captured=currentFit(),selected=slotKey?captured.slots.find(s=>s.key===slotKey):null;$('#info-title').textContent=t.path.join(' › ');$('#info-title').title='在装备浏览器中定位此物品';$('#info-title').onclick=e=>{e.preventDefault();locateItem(t)};$('#info-content').innerHTML='<p class="profile-note">正在读取物品属性…</p>';panel.hidden=false;if(!panel.style.left){panel.style.left=Math.max(8,(innerWidth-panel.offsetWidth)/2)+'px';panel.style.top='100px'}clampInfo();$('#info-close').focus({preventScroll:true});
 const calculationRequest=selected||droneIndex!==null||t.kind==='ship'?getCalculation(captured):Promise.resolve(null);
 // Attach rejection handling immediately, even while item metadata is in flight.
 const settledCalculation=calculationRequest.then(value=>({value}),error=>({error}));
 try{const data=await cachedItem(t.id,t.id);if(request!==infoRequest||panel.hidden)return;
 renderItemInfo($('#info-content'),t,data,null,catalog,'');clampInfo();
 if(!selected&&droneIndex===null&&t.kind!=='ship'){say('已读取物品详情');return}
 const status=document.createElement('p');status.className='profile-note';status.textContent='装配参数计算中 · 可先查看基础属性';$('#info-content').append(status);
 let touched=false;const remember=()=>{touched=true};$('#info-content').querySelector('.info-tabs').addEventListener('click',remember,{once:true});
 const outcome=await settledCalculation;if(request!==infoRequest||panel.hidden)return;
 if(outcome.error){status.textContent='装配参数暂不可用：'+outcome.error.message;return}
 const calculation=outcome.value,group=t.kind==='ship'?[]:droneIndex!==null?calculation.snapshot.droneBay.drones:selected.kind==='rig'?calculation.snapshot.rigs:calculation.snapshot.modules;
 const siblings=selected?captured.slots.filter(s=>s.kind===selected.kind&&s.item===selected.item):[],index=siblings.findIndex(s=>s.key===selected?.key);
 let computed=t.kind==='ship'?{attributeSnapshot:{...calculation.attributes.attributeSnapshot,cpuLoad:calculation.attributes.cpuUsed,powerLoad:calculation.attributes.powergridUsed,baseWarpSpeed:calculation.attributes.attributeSnapshot.warpSpeedMultiplier},attributeTraces:calculation.attributes.attributeTraces}:droneIndex!==null?group[droneIndex]:(group.find(m=>m.workspaceSlotKey===selected.key)||group.filter(m=>m.dogmaTypeId===selected.item)[index]);if(t.kind==='ammo')computed=computed?.charge||null;
 const selectedTab=touched?$('#info-content [aria-pressed="true"]')?.dataset.infoTab:null,scroll=panel.scrollTop;
 renderItemInfo($('#info-content'),t,data,computed,catalog,`${captured.characterName||'无技能'} · ${t.kind==='ship'?'当前船体 · 加成后属性':droneIndex!==null?'单架无人机 · 加成后能力':stateLabels[moduleState(selected)]||'被动'}`);
 if(selectedTab)$('#info-content').querySelector(`[data-info-tab="${selectedTab}"]`)?.click();clampInfo();if(touched)panel.scrollTop=scroll;say('已读取物品详情');
 }catch(e){if(request===infoRequest&&!panel.hidden)$('#info-content').innerHTML=`<p class="notice">${esc(e.message)}</p>`}
}

function locateItem(t){
 if(['ship','skill'].includes(t.kind)){say('此物品不属于装备浏览器');return}
 $('#search').value='';filter=null;let path='';for(const p of [...t.path,...(t.kind==='ammo'?[]:[t.meta||'科技 I'])]){path+='/'+p;treeOpen.add(path)}renderSlots();renderTree();const item=$(`#tree [data-id="${t.id}"]`);if(item){item.classList.add('located');item.scrollIntoView({block:'center'});item.focus({preventScroll:true});say('已定位：'+t.name)}
}
function clampInfo(){const p=$('#info-window');if(p.hidden)return;const r=p.getBoundingClientRect();p.style.left=Math.max(8,Math.min(r.left,innerWidth-r.width-8))+'px';p.style.top=Math.max(8,Math.min(r.top,innerHeight-r.height-8))+'px'}
$('#info-close').onclick=closeInfo;
$('.info-titlebar').onpointerdown=e=>{if(e.target.closest('button,a')||e.button!==0)return;e.preventDefault();const bar=e.currentTarget,p=$('#info-window'),r=p.getBoundingClientRect(),dx=e.clientX-r.left,dy=e.clientY-r.top;bar.setPointerCapture(e.pointerId);bar.onpointermove=ev=>{p.style.left=ev.clientX-dx+'px';p.style.top=ev.clientY-dy+'px';clampInfo()};bar.onpointerup=bar.onpointercancel=()=>{bar.onpointermove=null;bar.onpointerup=null;bar.onpointercancel=null}};
window.addEventListener('resize',clampInfo);
function closeMenu(restore=true){$('#menu').hidden=true;if(restore&&menuOrigin?.isConnected)menuOrigin.focus()}
const menuIconPaths={
 active:'<path d="m8 5 10 7-10 7Z"/>',
 stop:'<rect x="6" y="6" width="12" height="12" rx="1"/>',
 offline:'<path d="M12 3v8M6.4 5.8a8 8 0 1 0 11.2 0"/>',
 overload:'<path d="m13 2-8 12h6l-1 8 9-13h-6Z"/>',
 remove:'<path d="M10 5H5v14h5M9 12h12m-4-4 4 4-4 4"/>',
 info:'<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7v.5"/>',
 install:'<path d="M4 15v5h16v-5M12 3v12m-4-4 4 4 4-4"/>'
};
function menuIcon(label){const key=label==='启用'?'active':label==='关闭'?'stop':label==='离线'?'offline':label==='超载'?'overload':label==='查看信息'?'info':label.includes('卸载')?'remove':label==='在线 · 被动'?'offline':'install';return `<svg class="menu-action-icon" viewBox="0 0 24 24" aria-hidden="true">${menuIconPaths[key]}</svg>`}
function openMenu(e,id,type){e.preventDefault();e.stopPropagation();menuOrigin=e.target.closest('[tabindex],button')||e.target;const s=type==='slot'||type==='ammo'?slots.find(s=>s.key===id):null,t=type==='item'?byId(id):s?byId(type==='ammo'?s.ammo:s.item):ship;let entries=[];if(type==='item'){entries=t.kind==='ammo'?[['装填到选中 / 首个兼容装备',()=>install(t),slots.some(s=>acceptsAmmo(byId(s.item),t))],[`装填全部兼容装备（${slots.filter(s=>acceptsAmmo(byId(s.item),t)).length}）`,()=>loadAll(t),slots.some(s=>acceptsAmmo(byId(s.item),t))]]:[['安装到选中 / 首个空槽',()=>install(t),!!(filter&&canInstall(t,filter.key))||slots.some(s=>canInstall(t,s.key)&&!s.item)]]}
 if(type==='slot'){if(t&&!['rig','subsystem'].includes(s.kind)){for(const [state,label] of [...(t.canActivate?[['Active','启用']]:[]),['Online',t.canActivate?'关闭':'在线 · 被动'],['Offline','离线'],...(t.canOverload&&t.canActivate?[['Overload','超载']]:[])])entries.push([(moduleState(s)===state?'✓ ':'')+label,()=>changeModuleState(id,state),moduleState(s)!==state]);entries.push(['卸载装备',()=>remove(id),true,'danger'])}if(t&&s.kind==='subsystem')entries.push(['卸载子系统',()=>remove(id),true,'danger']);if(t&&s.kind==='rig')entries.push(['卸载改装件',()=>remove(id),true,'danger']);}
 if(type==='ammo'){if(t)entries.push(['同种弹药装填全部兼容装备',()=>loadAll(t)],['卸载此处弹药',()=>unload(id),true,'danger']);}
 if(type==='ship')entries.push(['卸载所有弹药',()=>mutate(()=>slots.forEach(s=>s.ammo=null),'已卸载所有弹药'),slots.some(s=>s.ammo),'danger']);
 if(t)entries.push(['查看信息',()=>showInfo(t,s?.key)]);const menu=$('#menu');if(!entries.length){closeMenu(false);return}menu.innerHTML=`<div class="menu-title">${esc(t?.name||(type==='ammo'?'空弹药槽':labels[s?.kind]+' · 空槽'))}</div>`;for(const [label,fn,enabled=true,cls=''] of entries){const b=document.createElement('button');b.type='button';b.role='menuitem';const checked=label.startsWith('✓ '),text=checked?label.slice(2):label;b.setAttribute('aria-label',text);b.innerHTML=menuIcon(text)+`<span class="menu-action-label">${esc(text)}</span>${checked?'<svg class="menu-selected-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6"/></svg>':''}`;if(checked)b.setAttribute('aria-current','true');b.disabled=!enabled;b.className=cls;b.onclick=()=>{closeMenu();fn()};menu.append(b)}menu.hidden=false;const r=menuOrigin.getBoundingClientRect();const x=e.type==='keydown'?r.left:e.clientX,y=e.type==='keydown'?r.bottom:e.clientY;menu.style.left=Math.max(8,Math.min(x,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(y,innerHeight-menu.offsetHeight-8))+'px';menu.querySelector('button:not(:disabled)')?.focus({preventScroll:true});}
$('#menu').onkeydown=e=>{const buttons=[...$('#menu').querySelectorAll('button:not(:disabled)')];let i=buttons.indexOf(document.activeElement);if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();buttons[(i+(e.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length].focus()}if(e.key==='Home'){e.preventDefault();buttons[0].focus()}if(e.key==='End'){e.preventDefault();buttons.at(-1).focus()}if(e.key==='Tab')closeMenu()};
document.addEventListener('pointerdown',e=>{if(!e.target.closest('#menu'))closeMenu(false)});window.addEventListener('resize',()=>closeMenu(false));document.addEventListener('wheel',e=>{if(!e.target.closest('#menu'))closeMenu(false)},{passive:true});
document.addEventListener('keydown',e=>{if(e.key==='Escape'){if(!$('#menu').hidden)closeMenu();else closeInfo();clearDrag()}if((e.ctrlKey||e.metaKey)&&['z','y'].includes(e.key.toLowerCase())&&!e.target.closest('input,textarea,[contenteditable=true]')&&!$('#editor-page').hidden){e.preventDefault();if(e.key.toLowerCase()==='y'||e.shiftKey)redo();else undo()}if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10'){const item=e.target.closest('.item'),slot=e.target.closest('.slot');if(item)openMenu(e,item.dataset.id,'item');else if(slot){if(selectedSlots.has(slot.dataset.key)&&selectedSlots.size>1&&!e.target.closest('.ammo'))openBatchMenu(e,[...selectedSlots],'已选 '+selectedSlots.size+' 个槽位');else openMenu(e,slot.dataset.key,e.target.closest('.ammo')?'ammo':'slot')}else if(e.target.id==='ship')openMenu(e,null,'ship')}});
$('#ship').oncontextmenu=e=>openMenu(e,null,'ship');
$('#ship').ondragover=e=>{
 e.preventDefault();if(slotDrag){e.dataTransfer.dropEffect='none';return;}const item=byId(dragged),key=shipInstallTarget(item);
 const allowed=item?.kind==='ammo'?slots.some(s=>acceptsAmmo(byId(s.item),item)):!!key&&!installLimit(item,key);
 e.dataTransfer.dropEffect=allowed?'copy':'none';$('#ship').classList.toggle('compatible',allowed);
};
$('#ship').ondrop=e=>{
 e.preventDefault();e.stopPropagation();if(slotDrag){clearDrag();return;}const item=byId(e.dataTransfer.getData('text/plain'));
 if(item?.kind==='ammo')loadAll(item);
 else if(item){const key=shipInstallTarget(item);if(key)install(item,key);else say('无法安装：没有兼容空槽，拖到具体槽位可替换装备');}
 clearDrag();
};
$('#search').oninput=renderTree;$('#theme').onclick=()=>{document.body.classList.toggle('light');$('#theme').textContent=document.body.classList.contains('light')?'☾ 夜间':'☼ 日间'};
renderSlots();renderTree();renderResources();renderShipStats();

installExplanations();

async function api(path,body){const response=await fetch('/api/'+path,body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const data=await response.json();if(!response.ok)throw Error(data.error||'请求失败');return data}
function updateShip(){ship=byId(fitRecord.shipId);counts.subsystem=ship.group===963?4:0;for(const [kind,id] of Object.entries({high:14,mid:13,low:12,rig:1137}))counts[kind]=ship.attrs[id]||0;$('.title h1').textContent=fitRecord.name;renderFitTags();$('#ship .ship-label').textContent=ship.en;renderShipBadge();$('#ship img').src=`https://images.evetech.net/types/${ship.id}/render?size=512`;$('#ship img').alt=ship.name;$('#ship .ship-caption').innerHTML=`${esc(ship.name)}<small>拖入装备安装 · 拖入弹药批量装填</small>`;renderPilot();}
function restoreFit(record){editorFitDeleted=false;cancelInstallPreview();selectedSlots.clear();selectionAnchor=null;analysisVersion++;report=null;fitRecord={...structuredClone(record),...scenarioFields(scenarioPresets(record))};fitRecord.drones=splitDroneStacks(fitRecord.drones||[]);updateShip();slots=fresh().map(s=>record.slots?.find(x=>x.key===s.key)||s);for(const s of record.slots||[])if(s.item&&!slots.some(x=>x.key===s.key))slots.push(structuredClone(s));filter=ship.group===963?{key:'subsystem-0',ammo:false}:null;history=[];redoHistory=[];$('#undo').disabled=true;$('#redo').disabled=true;$('#search').value='';treeOpen.clear();renderSlots();renderTree();renderResources();renderShipStats();save()}
let saving=Promise.resolve();
function persistFit(){const task=saving.catch(()=>{}).then(writeFit);saving=task;return task}
async function writeFit(){const input=currentFit(),saved=await api('save',input);fitRecord={...fitRecord,id:saved.id,revision:saved.revision,updatedAt:saved.updatedAt};localStorage.setItem('fitlab-working-draft',JSON.stringify(currentFit()));$('#save').textContent=JSON.stringify(input.slots)===JSON.stringify(slots)&&JSON.stringify(input.skills)===JSON.stringify(fitRecord.skills)?'已保存 · '+new Date(saved.updatedAt).toLocaleTimeString():'已保存上一版本 · 当前改动待保存';return saved}
document.addEventListener('fitlab-attack-mode',e=>{attackMode=e.detail==='edps'?'edps':'dps';try{localStorage.setItem('fitlab-attack-mode',attackMode)}catch{}scheduleAnalysis()});
function scheduleAnalysis(){document.dispatchEvent(new CustomEvent('fitlab-calculation-invalidated'));if(!$('#info-window').hidden)closeInfo();analysisState='pending';updateScenarioStatus();cancelInstallPreview();$('.inspector').classList.add('calculating');analysisVersion++;refreshSlotMetrics();clearTimeout(analysisTimer);$('#engine-status').textContent='计算中…';analysisTimer=setTimeout(runAnalysis,250)}
async function runAnalysis(){const version=analysisVersion;try{const result=await getCalculation(currentFit());if(version!==analysisVersion)return;report=result;reportVersion=version;analysisState='complete';updateScenarioStatus();syncEngineSlots();refreshSlotMetrics();$('.inspector').classList.remove('calculating');$('#engine-status').textContent=`Dogma · ${result.skillCount} 项技能`;$('.notice').textContent=result.isValid?'装配校验通过':`校验提示：${result.issues.map(x=>x.message).join('；')}`;renderResources();renderShipStats()}catch(e){if(version!==analysisVersion)return;$('.inspector').classList.remove('calculating');analysisState='failed';updateScenarioStatus();$('#engine-status').textContent='计算失败 · 当前数据未更新';$('.notice').textContent=e.message;say('装配已保留，计算失败：'+e.message)}}
const flow=document.createElement('dialog');flow.id='flow-dialog';document.body.append(flow);
function openFlow(title,body){flow.innerHTML=`<div class="flow-head"><b>${esc(title)}</b><button aria-label="关闭">×</button></div><div class="flow-body">${body}</div><p id="flow-error"></p>`;flow.querySelector('.flow-head button').onclick=()=>flow.close();flow.showModal()}
function guarded(fn){return async()=>{try{await fn()}catch(e){say(e.message);if(flow.open)$('#flow-error').textContent=e.message}}}
$('#save-fit').onclick=guarded(persistFit);
function exportFitImage(fit){return showShareImage(withoutScenario(fit),{calculate:getCalculation,catalog,getPrice:async f=>{marketPricePromise??=api('prices').catch(e=>{marketPricePromise=null;throw e});return estimateFitPrice(f,await marketPricePromise)}})}
$('#share-fit').onclick=()=>exportFitImage(currentFit());
$('#import-fit-image').onclick=()=>showImageImport({catalog,calculate:getCalculation,save:fit=>api('save',fit),onSaved:record=>{libraryFits.unshift(record);refreshLibraryRows();libraryMessage('已导入新装配：'+record.name)}});
let libraryFits=[],pageMode=null,navigationVersion=0,lastWorkPage='library',libraryLoaded=false;const pageScrollStates=new Map();let editorFitDeleted=false;let fitClipboard=null;try{fitClipboard=JSON.parse(sessionStorage.getItem('fitlab-fit-clipboard'))}catch{}
const libraryTree=createLibraryTree($('#library-nav'),catalog,drawFitLibrary);
function drawFitLibrary(){
 const selectedHull=libraryTree.selectedHull();$('#new-fit').hidden=!selectedHull;$('#new-fit').disabled=false;$('#new-fit').title=selectedHull?'为'+selectedHull.name+'新建装配':'';

 const q=$('#library-search').value.trim().toLowerCase(),fits=libraryFits.filter(f=>libraryTree.matches(f)&&((q&&matchesName(byId(f.shipId),q))||(f.name+' '+byId(f.shipId)?.name+' '+(f.tags||[]).join(' ')+' '+(f.notes||'')).toLowerCase().includes(q)));
 $('#library-count').textContent=fits.length+' / '+libraryFits.length+' 份装配';
 $('#library-list').innerHTML=fits.length?fits.map(f=>`<button class="library-row" data-fit="${libraryFits.indexOf(f)}">${img(byId(f.shipId))}<span class="library-fit-identity"><b><span translate="no">${esc(f.name)}</span>${f._workingDraft?' · 草稿':''}</b><small>${esc(byId(f.shipId)?.name||'未知舰船')} · <span translate="no">${esc(f.characterName||'无技能')}</span></small><span class="library-tags">${(f.tags||[]).map(t=>`<span>${esc(t)}</span>`).join('')}</span></span><span class="library-fit-notes ${f.notes?'':'is-empty'}" title="${esc(f.notes||'右键添加备注')}">${esc(f.notes||'暂无备注')}</span><span class="library-fit-price" data-fit-price="${libraryFits.indexOf(f)}"><small>参考估价</small><b>读取中…</b></span></button>`).join(''):'<div class="library-empty">'+(libraryFits.length?'此分类下没有匹配装配，可调整分类或搜索条件。':'装配库还没有配置，请在左侧选择船型后新建装配。')+'</div>';
 renderLibraryPrices();
 $('#library-list').insertAdjacentHTML('beforeend','<div class="library-paste-space" tabindex="0" aria-label="装配库空白区域，可右键粘贴"></div>');
 $('#library-list').querySelectorAll('[data-fit]').forEach(b=>{const f=libraryFits[Number(b.dataset.fit)];b.onclick=()=>{const record={...f};delete record._workingDraft;restoreFit(record);location.hash='fitting'};b.oncontextmenu=e=>libraryMenu(e,f);b.onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')libraryMenu(e,f)}});
 $('#library-list').oncontextmenu=e=>{if(!e.target.closest('[data-fit]'))libraryMenu(e)};
 $('#library-list').querySelector('.library-paste-space').onkeydown=e=>{if(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10')libraryMenu(e)};
}
const characterManager=installCharacterManager({api,catalog,onReturn:()=>{location.hash=lastWorkPage==='editor'&&!editorFitDeleted?'fitting':'library'}});
$('#manage-characters').onclick=()=>{location.hash='characters'};
async function navigateFitPage(){
 const version=++navigationVersion,next=location.hash.startsWith('#characters')?'characters':location.hash==='#fitting'?'editor':'library',previous=pageMode;
 if(previous&&previous!==next)capturePageScroll(previous);
 if(next==='editor'&&editorFitDeleted){location.hash='library';return}
 if(next==='library'&&previous==='editor'){try{await persistFit()}catch(e){say('草稿已保留，保存失败：'+e.message)}}
 if(version!==navigationVersion)return;
 pageMode=next;if(next!=='characters')lastWorkPage=next;$('#nav-library').href=next==='characters'&&lastWorkPage==='editor'?'#fitting':'#library';if(next==='characters')characterManager.show();else characterManager.hide();if(next==='characters')$('#manage-characters').setAttribute('aria-current','page');else $('#manage-characters').removeAttribute('aria-current');if(next==='library'||next==='editor')$('#nav-library').setAttribute('aria-current','page');else $('#nav-library').removeAttribute('aria-current');$('#editor-page').hidden=next!=='editor';$('#library-page').hidden=next!=='library';
 for(const id of ['fit-library','save-fit','undo','redo'])$('#'+id).hidden=next!=='editor';
 closeMenu(false);$('#info-window').hidden=true;if(flow.open)flow.close();
 document.title=t(next==='characters'?'EVE FitLab · 角色管理':next==='library'?'EVE FitLab · 装配库':'EVE FitLab · 装配工作台');
 restorePageScroll(next);
 if(next==='library'&&!(previous==='characters'&&libraryLoaded)){
  $('#library-list').innerHTML='<div class="library-empty">正在读取装配库…</div>';
  try{const fits=await api('library');if(version!==navigationVersion)return;libraryFits=fits.sort((a,b)=>(b.updatedAt||'').localeCompare(a.updatedAt||''));
 const draft=JSON.parse(localStorage.getItem('fitlab-working-draft')||'null');
 if(draft&&byId(draft.shipId)){const stored=libraryFits.find(f=>f.id===draft.id),fields=['name','notes','shipId','slots','tags','skills','damageProfile','damageLocks','defenseMode','drones','cargo','scenario','scenarios','activeScenarioId'];if(!stored||fields.some(k=>JSON.stringify(stored[k])!==JSON.stringify(draft[k]))){libraryFits=libraryFits.filter(f=>!draft.id||f.id!==draft.id);libraryFits.unshift({...draft,_workingDraft:true})}}
 libraryTree.update(libraryFits);drawFitLibrary();libraryLoaded=true;restorePageScroll(next)}
  catch(e){if(version===navigationVersion)$('#library-list').textContent='读取失败：'+e.message}
 }
}
$('#library-search').oninput=drawFitLibrary;
$('#fit-library').onclick=()=>{location.hash='library'};
window.addEventListener('hashchange',navigateFitPage);

$('#new-fit').onclick=()=>{
 const hull=libraryTree.selectedHull();if(!hull)return;
 openFlow('新建装配 · '+hull.name,`<form id="create-fit-form"><label>名称<input id="create-fit-name" aria-label="装配名称" maxlength="120" required value="${esc(hull.name+' · 新装配')}"></label><label>标签<input id="create-fit-tags" aria-label="装配标签" placeholder="用逗号分隔，例如：深渊、舰队" maxlength="500"></label><button type="submit">创建装配</button></form>`);
 $('#create-fit-name').focus();$('#create-fit-name').select();
 $('#create-fit-form').onsubmit=e=>{e.preventDefault();const name=$('#create-fit-name').value.trim();if(!name){$('#flow-error').textContent='请输入装配名称';return}const tags=[...new Set($('#create-fit-tags').value.split(/[,，]/).map(t=>t.trim()).filter(Boolean))];restoreFit({name,tags,shipId:hull.id,skills:[],characterName:'无技能 · 基础对照',slots:[]});flow.close();location.hash='fitting'};
};

installPilotPicker($('#pilot'),{api,catalog,current:()=>fitRecord.characterName,select:c=>{fitRecord.skills=structuredClone(c.skills);fitRecord.characterName=c.name;updateShip();save()}});

$('#rename-fit').onclick=()=>{
 const heading=$('.title h1'),button=$('#rename-fit');
 if($('.fit-name-input'))return;
 const input=document.createElement('input');input.className='fit-name-input';input.setAttribute('aria-label','装配名称');input.maxLength=120;input.value=fitRecord.name;
 heading.hidden=true;button.hidden=true;heading.after(input);input.focus();input.select();
 let finished=false;
 const finish=commit=>{if(finished)return;finished=true;const value=input.value.trim();if(commit&&value&&value!==fitRecord.name){fitRecord.name=value;storeWorkingDraft()}heading.textContent=fitRecord.name;heading.hidden=false;button.hidden=false;input.remove()};
 input.onblur=()=>finish(true);
 input.onkeydown=e=>{if(e.isComposing)return;if(e.key==='Enter'||e.key==='Escape'){e.preventDefault();e.stopPropagation();finish(e.key==='Enter');button.focus()}};
};
function renderFitTags(){
 const root=$('#fit-tags');root.innerHTML=(fitRecord.tags||[]).map((t,i)=>`<button class="fit-tag" data-tag-index="${i}" title="点击编辑标签">${esc(t)}</button>`).join('')+'<button class="add-fit-tag" aria-label="添加标签" title="添加标签">＋</button>';
 const edit=index=>{
  if(root.querySelector('input'))return;
  const existing=index!==null,source=existing?root.querySelector(`[data-tag-index="${index}"]`):root.querySelector('.add-fit-tag');
  const input=document.createElement('input');input.className='fit-tag-input';input.setAttribute('aria-label',existing?'编辑标签':'新标签');input.placeholder='输入标签，回车添加';input.maxLength=40;input.value=existing?fitRecord.tags[index]:'';
  source.hidden=true;source.after(input);input.focus();let finished=false;
  const finish=commit=>{if(finished)return;finished=true;const text=input.value.trim();if(commit){const tags=[...(fitRecord.tags||[])];if(existing){if(text)tags[index]=text;else tags.splice(index,1)}else if(text)tags.push(text);fitRecord.tags=[...new Set(tags)];storeWorkingDraft()}renderFitTags()};
  input.onblur=()=>finish(true);
  input.onkeydown=e=>{if(e.isComposing)return;if(e.key==='Enter'||e.key==='Escape'){e.preventDefault();e.stopPropagation();finish(e.key==='Enter');root.querySelector('.add-fit-tag').focus()}};
 };
 root.querySelector('.add-fit-tag').onclick=()=>edit(null);
 root.querySelectorAll('[data-tag-index]').forEach(b=>b.onclick=()=>edit(Number(b.dataset.tagIndex)));
}

try{const draft=JSON.parse(localStorage.getItem('fitlab-working-draft'));if(draft&&byId(draft.shipId))restoreFit(draft);else{updateShip();scheduleAnalysis()}}catch{updateShip();scheduleAnalysis()}

function renderEngineStats(){const scroll=$('.inspector').scrollTop;mountEngineStats($('#ship-stats'),report,ship,fitRecord.characterName,fitRecord.defenseMode||'hp',mode=>{fitRecord.defenseMode=mode;storeWorkingDraft();renderEngineStats()},catalog,fitRecord.damageProfile,editDamageProfile);$('.inspector').scrollTop=scroll;renderBayConfig();renderScenario();renderFitPrice()}

function editDamageProfile(){
 let amounts=damageTenths(fitRecord.damageProfile);
 const locked=damageNames.map((_,i)=>fitRecord.damageLocks?.[i]===true);
 openFlow('针对抗 · 来伤比例',`<p class="profile-note">拖动调整 · 未锁定项按比例分摊变化 · 锁定项保持不变</p><div class="damage-profile-editor">${damageNames.map((name,i)=>`<div class="damage-profile-row"><span>${name}</span><div class="mini-bar damage-profile-bar damage-${i}" role="slider" tabindex="0" aria-label="${name}比例" aria-valuemin="0" aria-valuemax="100" aria-valuenow="${amounts[i]}"><i></i><b></b></div><button class="damage-lock" aria-label="锁定${name}比例" aria-pressed="false"></button></div>`).join('')}</div><div id="damage-profile-preview"></div><div class="damage-profile-actions"><button id="damage-uniform">均匀分布</button><button id="damage-apply">应用</button></div>`);
 let editorMode='bars',point=damageSplits(amounts);
 const modeSwitch=document.createElement('div');modeSwitch.className='damage-editor-switch';modeSwitch.setAttribute('role','group');modeSwitch.setAttribute('aria-label','来伤调整方式');
 modeSwitch.innerHTML='<button data-editor="bars" aria-pressed="true">条形</button><button data-editor="square" aria-pressed="false">二维图</button>';
 flow.querySelector('.flow-head').insertBefore(modeSwitch,flow.querySelector('.flow-head > button'));
 const squareView=document.createElement('div');squareView.className='damage-square-view';squareView.hidden=true;
 squareView.innerHTML=`<div class="damage-square" aria-label="二维伤害面积图">${damageNames.map((n,i)=>`<div class="damage-quadrant damage-${i}"><span>${n}<b></b></span></div>`).join('')}<button class="damage-divider divider-top" data-axis="topX" aria-label="上方竖分割线" title="左右拖动：电磁 / 热能"></button><button class="damage-divider divider-bottom" data-axis="bottomX" aria-label="下方竖分割线" title="左右拖动：动能 / 爆炸"></button><button class="damage-divider divider-horizontal" data-axis="y" aria-label="横分割线" title="上下拖动：上下两组伤害"></button></div><div class="damage-square-legend"></div><p class="damage-square-note"></p><button class="damage-unlock-all">解锁全部以调整</button>`;
 flow.querySelector('.damage-profile-editor').after(squareView);
 const square=squareView.querySelector('.damage-square'),handles=[...squareView.querySelectorAll('.damage-divider')];
 const syncPoint=()=>{point=damageSplits(amounts)};
 const drawSquare=()=>{
  const hasLocks=locked.some(Boolean);
  square.style.setProperty('--top-x',point.topX*100+'%');square.style.setProperty('--bottom-x',point.bottomX*100+'%');square.style.setProperty('--split-y',point.y*100+'%');
  squareView.querySelectorAll('.damage-quadrant b').forEach((b,i)=>b.textContent=(amounts[i]/10).toFixed(1)+'%');
  squareView.querySelector('.damage-square-legend').innerHTML=damageNames.map((n,i)=>`<span class="damage-${i}"><i></i>${n} <b>${(amounts[i]/10).toFixed(1)}%</b></span>`).join('');
  squareView.querySelector('.damage-square-note').textContent=hasLocks?'已锁定部分比例；解锁后可拖动分割线。':'横线调整上下两组总量；上下竖线各自调整组内比例。';
  squareView.querySelector('.damage-unlock-all').hidden=!hasLocks;
  handles.forEach(h=>h.disabled=hasLocks);
 };
 modeSwitch.querySelectorAll('button').forEach(b=>b.onclick=()=>{editorMode=b.dataset.editor;modeSwitch.querySelectorAll('button').forEach(t=>t.setAttribute('aria-pressed',t===b));flow.querySelector('.damage-profile-editor').hidden=editorMode!=='bars';squareView.hidden=editorMode!=='square';flow.querySelector('.flow-body > .profile-note').textContent=editorMode==='bars'?'拖动调整 · 未锁定项按比例分摊变化 · 锁定项保持不变':'拖动三条分割线 · 四块面积准确对应当前比例';syncPoint();drawSquare()});
 squareView.querySelector('.damage-unlock-all').onclick=()=>{locked.fill(false);preview()};
 const moveSplit=(axis,value)=>{if(locked.some(Boolean))return;point[axis]=Math.max(0,Math.min(1,value));amounts=splitDamage(point.topX,point.bottomX,point.y);preview()};
 handles.forEach(handle=>{
  const axis=handle.dataset.axis;
  let start=null;
  const drag=e=>{if(!start)return;const r=square.getBoundingClientRect(),delta=axis==='y'?(e.clientY-start.position)/r.height:(e.clientX-start.position)/r.width;moveSplit(axis,start.value+delta)};
  handle.onpointerdown=e=>{if(e.button!==0||handle.disabled)return;e.preventDefault();handle.focus();start={value:point[axis],position:axis==='y'?e.clientY:e.clientX};handle.setPointerCapture(e.pointerId)};
  handle.onpointermove=e=>{if(handle.hasPointerCapture(e.pointerId))drag(e)};
  handle.onpointerup=e=>{if(handle.hasPointerCapture(e.pointerId)){drag(e);handle.releasePointerCapture(e.pointerId)}};
  handle.onlostpointercapture=()=>start=null;
  handle.onkeydown=e=>{const d=e.shiftKey?.05:.005,delta=(axis==='y'?{ArrowUp:-d,ArrowDown:d}:{ArrowLeft:-d,ArrowRight:d})[e.key];if(delta!==undefined||e.key==='Home'||e.key==='End'){e.preventDefault();moveSplit(axis,e.key==='Home'?0:e.key==='End'?1:point[axis]+delta)}};
 });
 const bars=[...flow.querySelectorAll('.damage-profile-bar')];
 const lockButtons=[...flow.querySelectorAll('.damage-lock')];
 const movable=i=>!locked[i]&&locked.filter(v=>!v).length>1;
 const lockIcon=isLocked=>`<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true"><rect x="5" y="10" width="14" height="11" rx="2"/><path d="${isLocked?'M8 10V7a4 4 0 0 1 8 0v3':'M8 10V7a4 4 0 0 1 8 0'}"/><path d="M12 14v3"/></svg>`;
 const preview=()=>{
  bars.forEach((bar,i)=>{bar.querySelector('i').style.width=amounts[i]/10+'%';bar.querySelector('b').textContent=(amounts[i]/10).toFixed(1)+'%';bar.setAttribute('aria-valuenow',amounts[i]/10);bar.setAttribute('aria-disabled',!movable(i));lockButtons[i].innerHTML=lockIcon(locked[i]);lockButtons[i].setAttribute('aria-pressed',locked[i]);lockButtons[i].setAttribute('aria-label',(locked[i]?'解锁':'锁定')+damageNames[i]+'比例')});
  $('#damage-profile-preview').textContent=locked.filter(Boolean).length>=3?'合计 100% · 无可分摊项，请先解锁':'合计 100%';
  $('#damage-uniform').disabled=locked.every(Boolean);
  drawSquare();
 };
 lockButtons.forEach((button,i)=>button.onclick=()=>{locked[i]=!locked[i];preview()});
 bars.forEach((bar,i)=>{
  let dragStart=null;
  const set=value=>{amounts=redistributeDamage(dragStart||amounts,locked,i,value*10);syncPoint();preview()};
  const drag=e=>{const rect=bar.getBoundingClientRect();set((e.clientX-rect.left)/rect.width*100)};
  bar.onpointerdown=e=>{if(e.button!==0||!movable(i))return;e.preventDefault();dragStart=[...amounts];bar.focus();bar.setPointerCapture(e.pointerId);bar.classList.add('dragging');drag(e)};
  bar.onpointermove=e=>{if(bar.hasPointerCapture(e.pointerId))drag(e)};
  bar.onpointerup=e=>{if(bar.hasPointerCapture(e.pointerId)){drag(e);bar.releasePointerCapture(e.pointerId)}};
  bar.onlostpointercapture=()=>{dragStart=null;bar.classList.remove('dragging')};
  bar.onkeydown=e=>{const step=e.shiftKey?10:1;const value=({ArrowLeft:amounts[i]/10-step,ArrowDown:amounts[i]/10-step,ArrowRight:amounts[i]/10+step,ArrowUp:amounts[i]/10+step,Home:0,End:100})[e.key];if(value!==undefined){e.preventDefault();set(value)}};
 });
 $('#damage-uniform').onclick=()=>{const free=amounts.map((_,i)=>i).filter(i=>!locked[i]),budget=free.reduce((sum,i)=>sum+amounts[i],0);free.forEach((i,j)=>amounts[i]=Math.floor(budget/free.length)+(j<budget%free.length?1:0));syncPoint();preview()};
 $('#damage-apply').onclick=()=>{fitRecord.damageProfile=amounts.map(v=>v/1000);fitRecord.damageLocks=[...locked];fitRecord.defenseMode='targeted';storeWorkingDraft();renderEngineStats();flow.close()};
 preview();
}

function storeWorkingDraft(){try{localStorage.setItem('fitlab-working-draft',JSON.stringify(currentFit()));$('#save').textContent='草稿已保留 · 待保存到装配库'}catch{$('#save').textContent='草稿存储失败，请保存装配'}}

navigateFitPage();

function renderPilot(){
 const full=fitRecord.characterName||'选择角色技能',name=full==='全技能 V · 模拟角色'?'全技能 V':full;
 const button=$('#pilot');button.classList.add('pilot-identity');button.setAttribute('aria-label','切换驾驶员：'+name);button.setAttribute('aria-haspopup','dialog');button.title=full;
 button.innerHTML=`<span class="pilot-portrait"><svg viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.3" aria-hidden="true"><path d="M10 12a6 6 0 0 1 12 0v3a6 6 0 0 1-12 0Z"/><path d="M10 12h12M12 21l4 3 4-3M5 29v-3c0-3 4-5 7-5m8 0c3 0 7 2 7 5v3"/></svg></span><span class="pilot-copy"><span class="pilot-caption">驾驶员</span><span class="pilot-name">${esc(name)}</span></span><svg class="pilot-chevron" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.3" aria-hidden="true"><path d="m4 6 4 4 4-4"/></svg>`;
}

installWorkspaceResize(document.querySelector('#editor-page .workspace'));

function syncEngineSlots(){
 for(const use of report.attributes.slotUsage||[]){const kind=use.kind.toLowerCase();if(kind in counts&&kind!=='subsystem')counts[kind]=use.available}
 const next=fresh().map(s=>slots.find(x=>x.key===s.key)||s);
 for(const s of slots)if(s.item&&!next.some(x=>x.key===s.key))next.push(s);
 const changed=JSON.stringify(next)!==JSON.stringify(slots);slots=next;
 if(changed||ship.group===963){renderSlots();renderTree()}
 localStorage.setItem('fitlab-working-draft',JSON.stringify(currentFit()));
}


let marketPricePromise=null;
async function renderFitPrice(){
 const anchor=document.createElement('div');anchor.className='stat-block';anchor.textContent='估价读取中…';$('#ship-stats').insertAdjacentHTML('beforeend','<div class="panel-title">装配估价</div>');$('#ship-stats').append(anchor);
 const fit=currentFit();
 try{
  marketPricePromise??=api('prices').catch(e=>{marketPricePromise=null;throw e});
  const data=await marketPricePromise;if(!anchor.isConnected)return;
  const {total,missing}=estimateFitPrice(fit,data);
  anchor.innerHTML='<div class="stat-row"><span>'+(missing?'已知部分':'参考总价')+'</span><b>'+total.toLocaleString(getLocale(),{maximumFractionDigits:0})+' ISK</b></div><p class="profile-note">ESI 市场均价 · '+new Date(data.updatedAt*1000).toLocaleString()+(missing?' · '+missing+' 种物品无报价':'')+'<br>含船体、装备、满弹夹、无人机与普通货舱；不是即时采购价。</p>';
 }catch{if(anchor.isConnected)anchor.textContent='市场均价暂不可用'}
}


function acceptsBay(t,kind){return !!t&&(kind==='drones'?t.kind==='drone':!['ship','skill'].includes(t.kind))}
function addToBay(t,kind){
 if(!acceptsBay(t,kind)){say('此物品不能放入'+(kind==='drones'?'无人机库':'货舱'));return}
 if(kind==='drones'){
  if(!report||reportVersion!==analysisVersion){say('正在计算机库容量，请稍后再添加');return}
  const used=(fitRecord.drones||[]).reduce((s,e)=>s+(byId(e.item)?.volume||0)*e.quantity,0);
  if(!(t.volume>0)||used+t.volume>report.attributes.droneBayAvailable+1e-8){say('无人机库容量不足');return}
 }
 mutate(()=>{const entries=fitRecord[kind]??=[];const e=entries.find(e=>e.item===t.id&&(kind!=='drones'||e.quantity<5));if(e){if(e.quantity>=100000)return;e.quantity++}else entries.push({item:t.id,quantity:1,...(kind==='drones'?{active:0}:{})})},'已放入'+t.name);
}
function selectBay(kind){filter=filter?.bay===kind?null:{bay:kind};$('#search').value='';renderSlots();renderTree()}
function renderBayConfig(){
 const root=$('#bay-config');if(!root)return;
 root.innerHTML=['drones','cargo'].map(kind=>{
  const drones=kind==='drones',entries=fitRecord[kind]||[],title=drones?'无人机库':'货舱';
  const ready=report&&reportVersion===analysisVersion,capacity=ready?report.attributes.attributeSnapshot.capacity:null;
  const used=entries.reduce((sum,e)=>sum+(byId(e.item)?.volume||0)*e.quantity,0);
  const fmt=n=>Number(n).toLocaleString(getLocale(),{maximumFractionDigits:1});
  const droneLimit=(label,value,max,unit)=>'<span class="bay-capacity" aria-label="'+label+'">'+label+' <b class="'+(!ready?'':value>max?'limit-over':value===max?'limit-full':'limit-free')+'">'+(ready?fmt(value):'—')+'</b> / '+(ready?fmt(max):'—')+' '+unit+'</span>';
  const droneSummary='<span class="drone-bay-resources">'+droneLimit('带宽',report?.attributes.droneBandwidthUsed,report?.attributes.droneBandwidthAvailable,'Mbit/s')+droneLimit('机库',report?.attributes.droneBayUsed,report?.attributes.droneBayAvailable,'m³')+'</span>';
  const cargoSummary='<span class="bay-capacity" aria-label="货舱占用"><b class="'+(capacity==null?'':used>capacity?'limit-over':used===capacity?'limit-full':'limit-free')+'">'+fmt(used)+'</b> / '+(capacity==null?'—':fmt(capacity))+' m³</span>';
  const special=ready&&!drones?[['舰队机库','fleetHangarCapacity'],['舰船维护舱','shipMaintenanceBayCapacity'],['燃料舱','specialFuelBayCapacity'],['矿石舱','specialOreHoldCapacity'],['采集舱','generalMiningHoldCapacity'],['冰矿舱','specialIceHoldCapacity'],['气云舱','specialGasHoldCapacity'],['弹药舱','specialAmmoHoldCapacity']].filter(([n,k])=>report.attributes.attributeSnapshot[k]>0).map(([n,k])=>'<small>'+n+' '+fmt(report.attributes.attributeSnapshot[k])+' m³</small>').join(''):'';
  return '<section class="bay-config-section '+(filter?.bay===kind?'bay-selected':'')+'" data-bay="'+kind+'"><div class="slot-heading"><button class="bay-filter" data-select-bay="'+kind+'">'+title+'</button>'+(drones?droneSummary:cargoSummary)+'</div>'+(special?'<div class="special-bay-capacities">'+special+'</div>':'')+entries.map((e,i)=>'<div class="bay-config-item" tabindex="0" data-bay-index="'+i+'">'+img(byId(e.item))+'<span>'+esc(byId(e.item).name)+'</span>'+(drones?'<span class="drone-quantity">× <button data-edit-drone-quantity aria-label="编辑'+esc(byId(e.item).name)+'数量">'+e.quantity+'</button></span><span class="drone-launch-boxes" role="group" aria-label="出动数量">'+Array.from({length:5},(_,j)=>'<button class="drone-launch-box '+(j<(e.active||0)?'lit':'')+'" data-launch="'+(j+1)+'" aria-label="出动 '+(j+1)+' 架'+esc(byId(e.item).name)+'" aria-pressed="'+(j<(e.active||0))+'" '+(j>=e.quantity?'disabled':'')+' title="'+(j+1)+' 架；再次点击当前数量收回全部"></button>').join('')+'</span>':'<label>携带<input aria-label="'+esc(byId(e.item).name)+'携带数量" data-bay-field="quantity" type="number" min="1" max="100000" value="'+e.quantity+'"></label>')+'</div>').join('')+'<button class="bay-config-empty" data-select-bay="'+kind+'"><span>＋</span> 拖入'+(drones?'无人机':'物品')+' · 点击筛选浏览器</button></section>';
 }).join('');
 root.querySelectorAll('[data-select-bay]').forEach(b=>b.onclick=()=>selectBay(b.dataset.selectBay));
 root.querySelectorAll('[data-bay]').forEach(section=>{
  const kind=section.dataset.bay;
  section.ondragover=e=>{if(slotDrag)return;if(!acceptsBay(byId(dragged),kind))return;e.preventDefault();e.dataTransfer.dropEffect='copy';section.classList.add('compatible')};
  section.ondragleave=e=>{if(!section.contains(e.relatedTarget))section.classList.remove('compatible')};
  section.ondrop=e=>{e.preventDefault();e.stopPropagation();if(slotDrag){clearDrag();return;}addToBay(byId(e.dataTransfer.getData('text/plain')),kind);clearDrag()};
  section.querySelectorAll('[data-bay-index]').forEach(row=>{
   const index=+row.dataset.bayIndex;
   if(kind==='drones'){
    row.querySelector('[data-edit-drone-quantity]').onclick=()=>{
     const e=fitRecord.drones[index],button=row.querySelector('[data-edit-drone-quantity]');
     if(!report||reportVersion!==analysisVersion){say('正在计算机库容量，请稍后编辑');return}
     const max=maximumDroneQuantity(fitRecord.drones,index,report.attributes.droneBayAvailable,id=>byId(id)?.volume||0);
     const input=document.createElement('input');input.type='number';input.min='1';input.max=String(max);input.value=e.quantity;input.setAttribute('aria-label','无人机携带数量');button.replaceWith(input);input.focus();input.select();
     let done=false;const finish=commit=>{if(done)return;done=true;const value=Number(input.value);if(!commit){renderBayConfig();return}if(!Number.isInteger(value)||value<1||value>max){say('数量必须在 1–'+max+' 之间，不能超过机库容量');renderBayConfig();return}
      mutate(()=>{e.quantity=value;e.active=Math.min(e.active||0,value);fitRecord.drones=splitDroneStacks(fitRecord.drones)},'已调整无人机数量')};
     input.onblur=()=>finish(true);input.onkeydown=e=>{if(e.isComposing)return;if(e.key==='Enter'||e.key==='Escape'){e.preventDefault();e.stopPropagation();finish(e.key==='Enter')}};
    };
    row.querySelectorAll('[data-launch]').forEach(b=>b.onclick=()=>{
     const e=fitRecord.drones[index],count=+b.dataset.launch,next=e.active===count?0:count;
     if(next>e.active){
      if(!report||reportVersion!==analysisVersion){say('正在计算无人机带宽，请稍后出动');return}
      const others=fitRecord.drones.filter((_,i)=>i!==index),total=others.reduce((s,e)=>s+(e.active||0),0)+next;
      const bandwidth=others.reduce((s,e)=>s+(e.active||0)*(byId(e.item)?.attrs[1271]||0),0)+next*(byId(e.item)?.attrs[1271]||0);
      if(total>(report.attributes.attributeSnapshot.maxActiveDrones??5)||bandwidth>report.attributes.droneBandwidthAvailable){say('出动数量或带宽已达上限');return}
     }
     mutate(()=>e.active=next,next?'已出动 '+next+' 架无人机':'已收回本组无人机');
    });
   }

   row.querySelectorAll('input').forEach(input=>input.onchange=()=>{const field=input.dataset.bayField,e=fitRecord[kind][index],value=Number(input.value);if(!Number.isInteger(value)||value<(field==='active'?0:1)||value>100000||field==='active'&&value>e.quantity){say('数量无效，出动数不能超过携带数');renderBayConfig();return}mutate(()=>{e[field]=value;if(field==='quantity'&&e.active>value)e.active=value},'已调整数量')});
   const open=e=>{e.preventDefault();e.stopPropagation();const item=byId(fitRecord[kind][index].item);menuOrigin=row;const menu=$('#menu');menu.innerHTML='<div class="menu-title">'+esc(item.name)+'</div>';
    for(const [text,action] of [['查看信息',()=>showInfo(item,null,kind==='drones'?index:null)],['移出'+(kind==='drones'?'无人机库':'货舱'),()=>mutate(()=>fitRecord[kind].splice(index,1),'已移出物品')]]){const b=document.createElement('button');b.role='menuitem';b.textContent=text;b.onclick=()=>{closeMenu(false);action()};menu.append(b)}
    menu.hidden=false;const rect=row.getBoundingClientRect();menu.style.left=Math.max(8,Math.min(e.clientX||rect.left,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(e.clientY||rect.bottom,innerHeight-menu.offsetHeight-8))+'px';menu.querySelector('button').focus()};
   row.oncontextmenu=open;row.onkeydown=e=>{if(e.target===row&&(e.key==='ContextMenu'||e.shiftKey&&e.key==='F10'))open(e)};
  });
 });
}
function editSnapshot(){return {slots:structuredClone(slots),drones:structuredClone(fitRecord.drones||[]),cargo:structuredClone(fitRecord.cargo||[])}}
function applyEditSnapshot(s){slots=s.slots;fitRecord.drones=s.drones;fitRecord.cargo=s.cargo}

function renderShipBadge(){
 const host=$('#ship');host.querySelector('.ship-tech-badge')?.remove();
 const tech=ship.attrs[422],kind=tech===3?'t3':tech===2?'t2':ship.meta==='势力'?'faction':null;
 host.classList.toggle('has-tech-badge',!!kind);if(!kind)return;
 const badge=document.createElement('span');badge.className='ship-tech-badge '+kind;badge.setAttribute('aria-label',kind==='faction'?'势力舰船':kind==='t2'?'二级科技舰船':'三级科技舰船');badge.title=badge.getAttribute('aria-label');badge.style.backgroundImage='url(assets/ship-corner-'+kind+'.png)';host.append(badge);
}

function renderScenario(){
 updateScenarioStatus();
 $('#scenario-controls')?.remove();
 const state=scenarioPresets(fitRecord);
 $('#ship-stats').insertAdjacentHTML('beforeend','<section id="scenario-controls"><div class="panel-title scenario-heading"><span>情景设置</span><button id="edit-scenario">设置</button></div><div class="scenario-inline"><select id="active-scenario" aria-label="应用情景"><option value="">不应用情景</option>'+state.scenarios.map(s=>'<option value="'+esc(s.id)+'">'+esc(s.name)+'</option>').join('')+'</select><small>仅本地 · 静态估算</small></div></section>');
 $('#active-scenario').value=state.activeScenarioId||'';
 async function commit(fields){
  const owner=fitRecord,isNew=!owner.id;
  const saved=await api(isNew?'save':'fit/scenarios',isNew?{...currentFit(),...fields}:{id:owner.id,revision:owner.revision,...fields});
  if(fitRecord!==owner)return;
  Object.assign(fitRecord,fields,{id:saved.id,revision:saved.revision,updatedAt:saved.updatedAt});
  const index=libraryFits.findIndex(f=>f.id===saved.id);if(index>=0)libraryFits[index]=saved;else libraryFits.unshift(saved);
  storeWorkingDraft();if(isNew)$('#save').textContent='装配与情景已保存';
  scheduleAnalysis();renderScenario();say(isNew?'已保存新装配与情景':'情景已保存并应用 · 装配修改仍独立保存');
 }
 $('#active-scenario').onchange=async e=>{const input=e.target;input.disabled=true;try{await commit(scenarioFields({...state,activeScenarioId:input.value||null}))}catch(error){say(error.message);input.value=state.activeScenarioId||'';input.disabled=false}};
 $('#scenario-context').onclick=()=>openScenarioQuickMenu($('#scenario-context'),{state:scenarioPresets(fitRecord),onSelect:id=>commit(scenarioFields({...scenarioPresets(fitRecord),activeScenarioId:id})),onEdit:()=>$('#edit-scenario')?.click()});
 $('#edit-scenario').onclick=async()=>{try{
  const fits=await api('library');openScenarioEditor({fit:currentFit(),fits,shipName:id=>byId(id)?.name||'未知舰船',calculate:getCalculation,onSave:commit});
 }catch(e){say(e.message)}};
}

function selectSlot(e,key){cancelInstallPreview();
 if(e.shiftKey){
  const order=slots.map(s=>s.key),a=order.indexOf(selectionAnchor),b=order.indexOf(key);
  if(a>=0)order.slice(Math.min(a,b),Math.max(a,b)+1).forEach(k=>selectedSlots.add(k));else{selectedSlots.add(key);selectionAnchor=key}
  renderSlots();say('已选 '+selectedSlots.size+' 个槽位 · 右键批量操作');return;
 }
 if(e.ctrlKey||e.metaKey){if(selectedSlots.has(key))selectedSlots.delete(key);else selectedSlots.add(key);selectionAnchor=key;renderSlots();return}
 selectedSlots.clear();selectedSlots.add(key);selectionAnchor=key;toggleFilter(key);
}
function stateEligible(s,state){
 const t=byId(s.item);return t&&['high','mid','low'].includes(s.kind)&&(state!=='Active'||t.canActivate)&&(state!=='Overload'||t.canActivate&&t.canOverload);
}
function openBatchMenu(e,keys,title){
 e.preventDefault();e.stopPropagation();menuOrigin=e.target.closest('.rack-operations,.module,.slot')||e.currentTarget;
 const targets=()=>slots.filter(s=>keys.includes(s.key)&&s.item),menu=$('#menu');menu.innerHTML='<div class="menu-title">'+esc(title)+'</div>';
 const actions=['Active','Online','Offline','Overload'].map(state=>[stateLabels[state],()=>{const affected=targets().filter(s=>stateEligible(s,state)&&moduleState(s)!==state);if(affected.length)mutate(()=>affected.forEach(s=>{s.state=state;s.online=state!=='Offline'}),'已对 '+affected.length+' 件装备'+stateLabels[state])},targets().some(s=>stateEligible(s,state)&&moduleState(s)!==state)]);
 actions.push(['卸载装备',()=>mutate(()=>targets().forEach(s=>{s.item=null;s.ammo=null}),'已批量卸载装备'),targets().length>0],['卸载弹药',()=>mutate(()=>targets().forEach(s=>s.ammo=null),'已批量卸载弹药'),targets().some(s=>s.ammo)]);
 for(const [label,fn,enabled] of actions){const b=document.createElement('button');b.role='menuitem';b.setAttribute('aria-label',label);b.innerHTML=menuIcon(label)+'<span>'+label+'</span>';b.disabled=!enabled;b.onclick=()=>{closeMenu(false);fn()};menu.append(b)}
 menu.hidden=false;const r=menuOrigin.getBoundingClientRect();menu.style.left=Math.max(8,Math.min(e.clientX||r.left,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(e.clientY||r.bottom,innerHeight-menu.offsetHeight-8))+'px';menu.querySelector('button:not(:disabled)')?.focus();
}
function cancelInstallPreview(){
 clearTimeout(previewTimer);previewTimer=null;previewToken++;previewKey=null;
 if(previewRestore){previewRestore();previewRestore=null;}
 document.querySelector('.fit-install-preview')?.remove();$('.inspector')?.classList.remove('install-preview-active');
 $('#slots')?.querySelectorAll('.preview-target').forEach(e=>e.classList.remove('preview-target'));
}
function applyPreviewValues(result,fit){
 const stats=document.createElement('div'),resources=document.createElement('div');
 mountEngineStats(stats,result,ship,fitRecord.characterName,fitRecord.defenseMode||'hp',()=>{},catalog,fitRecord.damageProfile,()=>{});
 renderResources(result,resources);
 const restore=[];
 previewRestore=()=>{document.dispatchEvent(new CustomEvent('fitlab-calculation-invalidated'));restore.reverse().forEach(fn=>fn());};
 function patch(old,next){
  if(!old)return;
  if(!next){const hidden=old.hidden;old.hidden=true;restore.push(()=>old.hidden=hidden);return;}
  const html=old.innerHTML,attributes=[...old.attributes].map(a=>[a.name,a.value]);
  restore.push(()=>{old.innerHTML=html;for(const a of [...old.attributes])old.removeAttribute(a.name);for(const [key,value] of attributes)old.setAttribute(key,value)});
  old.innerHTML=next.innerHTML;
  if(old.classList.contains('meter'))old.classList.toggle('over',next.classList.contains('over'));
  if(next.hasAttribute('data-explain'))old.setAttribute('data-explain',next.getAttribute('data-explain'));else old.removeAttribute('data-explain');
  const previous=document.createElement('div');previous.innerHTML=html;
  const before=previous.querySelector('b')||previous.querySelector('label>span'),after=old.querySelector('b')||old.querySelector('label>span');
  if(before&&after&&before.textContent!==after.textContent){
   old.classList.add('preview-changed');old.title='当前 '+before.textContent+' → 预览 '+after.textContent;
   if(old.classList.contains('stat-row')||old.classList.contains('meter')){
    const was=document.createElement('small');was.className='preview-old';was.textContent=(old.classList.contains('meter')?before.textContent.split(' / ')[0]:before.textContent)+' → ';after.prepend(was);
   }
  }
 }
 const pair=(root,next,selector,key)=>{
  const pool=new Map();for(const el of next.querySelectorAll(selector)){const k=key(el);if(!pool.has(k))pool.set(k,[]);pool.get(k).push(el)}
  for(const el of root.querySelectorAll(selector))patch(el,pool.get(key(el))?.shift());
 };
 pair($('#resources'),resources,'.meter',e=>e.querySelector('progress')?.getAttribute('aria-label'));
 pair($('#ship-stats'),stats,'.stat-row',e=>e.querySelector(':scope>span')?.textContent);
 pair($('#ship-stats'),stats,'.section-summary',e=>e.getAttribute('aria-label'));
 pair($('#ship-stats'),stats,'.attack-total',()=> 'total');
 pair($('#ship-stats'),stats,'.damage-bars',()=> 'bars');
 for(const el of $('#slots').querySelectorAll('[data-module-metrics]')){
  const slot=fit.slots.find(s=>s.key===el.dataset.moduleMetrics);if(!slot)continue;
  const next=document.createElement('div');next.innerHTML=slotMetrics(slot,el.dataset.metricGroup||'details',result);patch(el,next);
 }
 // Original nodes, handlers, scroll position and scenario controls stay in place.
}
function queueInstallPreview(key,id=dragged,source='drag'){
 const version=analysisVersion,identity=id+':'+key+':'+version+':'+source;
 if(previewKey===identity)return;cancelInstallPreview();previewKey=identity;const token=previewToken;
 const target=key?$('#slots [data-key="'+key+'"]'):null;target?.classList.add('preview-target');
 previewTimer=setTimeout(async()=>{
  const item=byId(id),fit=currentFit(),slot=fit.slots.find(s=>s.key===key);if(!item)return;
  const host=document.createElement('div');host.className='fit-install-preview';host.setAttribute('role','status');$('.fit-actions').append(host);
  const title=slot?labels[slot.kind]+' '+(Number(slot.key.split('-')[1])+1)+' · '+(byId(item.kind==='ammo'?slot.ammo:slot.item)?.name||'空槽')+' → '+item.name:item.name;
  const hint=source==='drag'?'移开取消 · 松开安装':'移开恢复 · 双击或 Enter 安装';
  const banner=(status)=>{host.innerHTML='<b>'+esc(title)+'</b><small>'+esc(status)+'</small>';};
  if(!slot||!canInstall(item,key)){banner(selectedSlots.size>1?'请选择单个目标槽位，或拖入指定槽位':'没有兼容目标，请先选择槽位或腾出空槽');return;}
  if(reportVersion!==version||analysisState!=='complete'){banner('等待当前装配计算完成，再悬停预览');return;}
  const limitReason=installLimit(item,key);if(limitReason){banner('无法安装：'+limitReason);return;}
  const previousAmmo=slot.ammo;applyCandidate(slot,item);
  const ammoNote=item.kind!=='ammo'&&previousAmmo&&!slot.ammo?' · 原弹药不兼容，将卸下':'';
  banner('预览计算中…'+ammoNote);
  try{
   const result=await getCalculation(fit);
   if(token!==previewToken||version!==analysisVersion||!host.isConnected||(source==='drag'&&dragged!==id))return;
   document.dispatchEvent(new CustomEvent('fitlab-calculation-invalidated'));
   applyPreviewValues(result,fit);$('.inspector').classList.add('install-preview-active');
   banner((result.isValid?hint:'装配受限：'+result.issues.map(i=>i.message).join('；'))+ammoNote);
  }catch(e){if(token===previewToken&&host.isConnected){if(previewRestore){previewRestore();previewRestore=null;}banner('预览不可用：'+e.message);}}
 },source==='drag'?180:350);
}
document.addEventListener('dragover',e=>{if(previewKey&&!e.target.closest('#slots .slot'))cancelInstallPreview()});
document.addEventListener('dragend',cancelInstallPreview);
window.addEventListener('hashchange',()=>{cancelInstallPreview();selectedSlots.clear()});

document.addEventListener('dragleave',e=>{if(!e.relatedTarget&&!e.clientX&&!e.clientY)cancelInstallPreview()});
window.addEventListener('blur',cancelInstallPreview);

function libraryMessage(message){let el=$('#library-notice');if(!el){el=document.createElement('span');el.id='library-notice';el.role='status';$('#library-count').after(el)}el.textContent=message}
function refreshLibraryRows(){libraryTree.update(libraryFits);drawFitLibrary();libraryLoaded=true;restorePageScroll(next)}
function libraryMenu(e,fit=null){
 e.preventDefault();e.stopPropagation();menuOrigin=e.target.closest('[data-fit],.library-paste-space')||$('#library-list');const menu=$('#menu');menu.innerHTML='<div class="menu-title">'+esc(fit?.name||'装配库')+'</div>';
 const entries=fit?[
  ['生成图片',()=>exportFitImage(fit)],
  ['编辑备注',()=>{openFlow('装配备注 · '+fit.name,'<form id="library-notes-form"><label>备注<textarea name="notes" aria-label="装配备注" maxlength="4000" rows="7" placeholder="用途、操作要点、适用场景…">'+esc(fit.notes||'')+'</textarea></label><button>保存备注</button></form>');$('#library-notes-form textarea').focus();$('#library-notes-form').onsubmit=async e=>{e.preventDefault();const button=e.currentTarget.querySelector('button');button.disabled=true;try{const source=structuredClone(fit);delete source._workingDraft;source.notes=$('#library-notes-form textarea').value.trim();const saved=await api('save',source);libraryFits=libraryFits.filter(f=>f!==fit&&(!fit.id||f.id!==fit.id));libraryFits.unshift(saved);if(fit.id&&fitRecord.id===fit.id){fitRecord.notes=saved.notes;fitRecord.revision=saved.revision;fitRecord.updatedAt=saved.updatedAt;localStorage.setItem('fitlab-working-draft',JSON.stringify(currentFit()))}refreshLibraryRows();flow.close();libraryMessage('备注已保存')}catch(error){$('#flow-error').textContent=error.message;button.disabled=false}}}],
  ['重命名',()=>{openFlow('重命名装配','<form id="library-rename-form"><label>名称<input name="name" maxlength="120" required value="'+esc(fit.name)+'"></label><button>保存</button></form>');const input=$('#library-rename-form input');input.focus();input.select();$('#library-rename-form').onsubmit=async e=>{e.preventDefault();const name=input.value.trim();if(!name)return;try{const source=structuredClone(fit);delete source._workingDraft;source.name=name;const saved=await api('save',source);libraryFits=libraryFits.filter(f=>f!==fit&&(!fit.id||f.id!==fit.id));libraryFits.unshift(saved);if(fit.id&&fitRecord.id===fit.id){fitRecord.name=name;fitRecord.revision=saved.revision;fitRecord.updatedAt=saved.updatedAt;updateShip();localStorage.setItem('fitlab-working-draft',JSON.stringify(currentFit()))}refreshLibraryRows();flow.close();libraryMessage('名称已更新')}catch(error){$('#flow-error').textContent=error.message}}}],
  ['复制',()=>{fitClipboard=structuredClone(fit);for(const k of ['id','revision','updatedAt','_workingDraft'])delete fitClipboard[k];try{sessionStorage.setItem('fitlab-fit-clipboard',JSON.stringify(fitClipboard))}catch{}libraryMessage('已复制「'+fit.name+'」，在列表底部空白处右键粘贴。')}],
  ['删除',()=>{openFlow('删除装配','<p>删除「'+esc(fit.name)+'」？</p><button id="library-confirm-delete">删除装配</button>');$('#library-confirm-delete').onclick=async()=>{try{if(fit.id&&fit.revision)await api('fit/delete',{id:fit.id,revision:fit.revision});libraryFits=libraryFits.filter(f=>f!==fit&&(!fit.id||f.id!==fit.id));const draft=JSON.parse(localStorage.getItem('fitlab-working-draft')||'null');if(draft&&(fit.id?draft.id===fit.id:!draft.id&&draft.name===fit.name))localStorage.removeItem('fitlab-working-draft');if(fitRecord.id===fit.id)editorFitDeleted=true;refreshLibraryRows();flow.close();libraryMessage('装配已删除')}catch(error){$('#flow-error').textContent=error.message}}}]
 ]:[['粘贴',async()=>{if(!fitClipboard)return;const copy=structuredClone(fitClipboard);const names=new Set(libraryFits.map(f=>f.name));let suffix=' · 副本',n=2;while(names.has(copy.name.slice(0,110)+suffix))suffix=' · 副本 '+n++;copy.name=copy.name.slice(0,110)+suffix;const saved=await api('save',copy);libraryFits.unshift(saved);$('#library-search').value='';libraryTree.update(libraryFits);libraryTree.revealHull(saved.shipId);drawFitLibrary();$('#library-list').scrollTop=0;libraryMessage('已粘贴「'+saved.name+'」')},!!fitClipboard]];
 for(const [name,fn,enabled=true] of entries){const b=document.createElement('button');b.role='menuitem';b.textContent=name;b.disabled=!enabled;if(name==='删除')b.className='danger';b.onclick=async()=>{closeMenu(false);try{await fn()}catch(error){libraryMessage(error.message)}};menu.append(b)}
 menu.hidden=false;const r=menuOrigin.getBoundingClientRect();menu.style.left=Math.max(8,Math.min(e.type==='keydown'?r.left:e.clientX,innerWidth-menu.offsetWidth-8))+'px';menu.style.top=Math.max(8,Math.min(e.type==='keydown'?r.bottom:e.clientY,innerHeight-menu.offsetHeight-8))+'px';menu.querySelector('button:not(:disabled)')?.focus();
}

function estimateFitPrice(fit,data){
  const quantities=new Map([[fit.shipId,1]]),add=(id,n)=>quantities.set(id,(quantities.get(id)||0)+n);
  for(const s of fit.slots||[]){if(s.item)add(s.item,1);if(s.ammo){const n=magazine(byId(s.item),byId(s.ammo));if(Number.isFinite(n))add(s.ammo,n)}}
  for(const e of [...(fit.drones||[]),...(fit.cargo||[])])add(e.item,e.quantity);
  let total=0,missing=0;for(const [id,n] of quantities){const price=data.prices[id];if(price)total+=price*n;else missing++}
  return {total,missing};
}

async function renderLibraryPrices(){
 const rows=[...$('#library-list').querySelectorAll('[data-fit-price]')].map(el=>({el,fit:libraryFits[Number(el.dataset.fitPrice)]}));
 if(!rows.length)return;
 try{marketPricePromise??=api('prices').catch(e=>{marketPricePromise=null;throw e});const data=await marketPricePromise;
 for(const {el,fit} of rows){if(!el.isConnected)continue;const {total,missing}=estimateFitPrice(fit,data);el.innerHTML='<small>'+(missing?'已知部分估价':'参考估价')+'</small><b>'+total.toLocaleString(getLocale(),{maximumFractionDigits:0})+' <small class="isk-unit">ISK</small></b>';el.title='ESI 市场均价 · '+new Date(data.updatedAt*1000).toLocaleString()+'；含船体、装备、满弹夹、无人机与货舱'+(missing?'；'+missing+' 种物品暂无报价':'')}
 }catch{for(const {el} of rows)if(el.isConnected)el.innerHTML='<small>参考估价</small><b>暂不可用</b>'}
}

function capturePageScroll(page){const selectors=page==='editor'?['.fitting','.inspector','#tree']:page==='library'?['#library-list','.library-tree-scroll']:['#character-list','#character-skills'];pageScrollStates.set(page,{windowY:window.scrollY,positions:selectors.map(selector=>({selector,top:$(selector)?.scrollTop||0,left:$(selector)?.scrollLeft||0}))})}
function restorePageScroll(page){const state=pageScrollStates.get(page);if(!state)return;for(const {selector,top,left} of state.positions){const el=$(selector);if(el){el.scrollTop=top;el.scrollLeft=left}}window.scrollTo(0,state.windowY)}
function scenarioStatusMarkup(){return '<small id="scenario-calculation-status" role="status" class="scenario-calculation-status '+analysisState+'">'+(analysisState==='pending'?'计算中<span class="calculation-dots" aria-hidden="true">...</span>':analysisState==='complete'?'计算完成':'计算失败')+'</small>'}
function updateScenarioStatus(){
 const status=$('#scenario-calculation-status');if(status)status.outerHTML=scenarioStatusMarkup();
 $('.workspace').dataset.analysisState=analysisState;
 let badge=$('#scenario-context');if(!badge){badge=document.createElement('button');badge.id='scenario-context';badge.className='scenario-context';badge.setAttribute('aria-haspopup','menu');badge.setAttribute('aria-expanded','false');badge.setAttribute('aria-controls','scenario-quick-menu');$('.fit-actions').append(badge)}
 const state=scenarioPresets(fitRecord),name=state.scenarios.find(s=>s.id===state.activeScenarioId)?.name||'不应用情景';
 badge.textContent=(analysisState==='pending'?'计算中 · ':analysisState==='failed'?'计算失败 · ':'')+name+' ▾';
 badge.title='快速切换情景';
}


$('#app-settings').onclick=async e=>{e.preventDefault();openFlow('设置','<div class="app-settings-row"><span>界面语言</span><select id="settings-language" aria-label="界面语言" translate="no"><option value="zh-CN">简体中文</option><option value="zh-TW">繁體中文</option><option value="en">English</option><option value="ja">日本語</option></select></div><div class="app-settings-row"><span>界面主题</span><select id="settings-theme" aria-label="界面主题"><option value="dark">夜间</option><option value="light">日间</option></select></div><section class="storage-settings"><h3>数据存储目录</h3><p class="profile-note">装配、角色、授权配置和计算引擎状态存放于此。浏览器缓存由浏览器管理。</p><form id="storage-settings-form"><input id="storage-directory" aria-label="数据存储目录" placeholder="正在读取…" required disabled><div class="storage-actions"><button type="button" id="storage-choose" hidden>选择文件夹</button><button type="button" id="storage-open" disabled>在文件浏览器中打开</button><button id="storage-migrate" disabled>迁移到此目录</button></div></form><p id="storage-status" role="status"></p></section>');$('#settings-language').value=getLocale();$('#settings-language').onchange=e=>setLocale(e.target.value);$('#settings-theme').value=document.body.classList.contains('light')?'light':'dark';$('#settings-theme').onchange=e=>{document.body.classList.toggle('light',e.target.value==='light');$('#theme').textContent=e.target.value==='light'?'☾ 夜间':'☼ 日间'};
 const input=$('#storage-directory'),status=$('#storage-status'),migrate=$('#storage-migrate'),open=$('#storage-open'),form=$('#storage-settings-form');let current='';
 try{const data=await api('storage');if(!form.isConnected)return;current=data.directory;input.value=current;input.disabled=false;open.disabled=false}catch(error){status.textContent=error.message}
 if(window.fitlabDesktop){$('#storage-choose').hidden=false;$('#storage-choose').onclick=async()=>{const directory=await window.fitlabDesktop.chooseDataFolder();if(directory){input.value=directory;input.dispatchEvent(new Event('input'))}}}
 input.oninput=()=>{migrate.disabled=!input.value.trim()||input.value.trim()===current};
 open.onclick=async()=>{try{await api('storage/open',{});status.textContent='已打开当前存储目录'}catch(error){status.textContent=error.message}};
 form.onsubmit=async e=>{e.preventDefault();migrate.disabled=open.disabled=input.disabled=true;status.textContent='正在迁移数据，请稍候…';try{const data=await api('storage',{directory:input.value.trim()});current=data.directory;input.value=current;status.textContent='数据已迁移，新目录立即生效。'+(data.backup?' 原目录备份：'+data.backup:'')}catch(error){status.textContent='迁移未完成：'+error.message}finally{input.disabled=open.disabled=false;migrate.disabled=input.value.trim()===current}};
};

$('#developer-about').onclick=e=>{e.preventDefault();const address='TYDiRLFWukWdHpiZKQdGLoX7ivH2PFtPBS';openFlow('开发者介绍','<section class="developer-intro"><p id="developer-version" class="developer-version" translate="no">EVE FitLab</p><p class="developer-download"><a href="https://imfishman.com/" target="_blank" rel="noopener noreferrer"><span>官网下载</span> · <strong translate="no">imfishman.com</strong> ↗</a></p><h2>深海的鱼</h2><p class="developer-game-id">EVE 游戏 ID · ImFishMan</p><p>感谢你使用 EVE FitLab。希望它能让装配构思与搭配尝试变得更轻松。</p><div class="developer-contact"><p>欢迎建议，也欢迎来聊聊你的装配想法。</p><dl><div><dt>邮箱</dt><dd><a href="mailto:wzx2377951590@gmail.com">wzx2377951590@gmail.com</a></dd></div><div><dt>QQ</dt><dd>2377951590</dd></div><div><dt>开发工具</dt><dd>GPT-6-Astra</dd></div></dl></div><div class="developer-plea"><svg viewBox="0 0 180 140" role="img" aria-label="二次元小人跪拜求投喂"><ellipse cx="91" cy="124" rx="70" ry="8" fill="#86cbd0" opacity=".1"/><g stroke="#37445c" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M55 94Q40 105 48 119L75 121L84 109L98 122L121 119Q131 105 111 92" fill="#b6a5df"/><path d="M61 78Q48 88 46 108L70 114L89 98L109 114L133 106Q124 83 110 78" fill="#e6dcff"/><path d="M60 101L38 115Q30 124 44 125L66 122M115 101L135 115Q145 123 132 125L109 122" fill="#ffe5dc"/><path d="M48 56Q42 21 78 18Q120 10 131 46L130 79L113 96L63 90Z" fill="#8dcad7"/><path d="M55 59Q51 97 88 106Q123 99 125 59" fill="#ffe5dc"/><path d="M48 62L61 34L65 66L80 36L86 67L96 35L108 66L119 37L129 69Q137 28 108 19Q58 4 48 40Z" fill="#a8e0e5"/><path d="M48 48L32 34L39 15L63 26M116 24L141 13L149 34L130 48" fill="#b6a5df"/><path d="M66 79l10 5l-10 3M110 79l-10 5l10 3" fill="none"/><path d="M81 96q7-8 14 0" fill="none"/><path d="M69 87q-8 13-3 15q7 0 8-13M108 87q8 13 3 15q-7 0-8-13" fill="#93d9ef" stroke="#6caac9"/><path d="M133 120h36q-2 13-18 13t-18-13Z" fill="#f5dab1"/><path d="M143 118v-8m8 8v-12m8 12v-7" stroke="#e8b967"/><path d="M23 63l-5-8m6 20l-10 1M152 60l7-7m-4 19l11 1" stroke="#e8b967"/></g><ellipse cx="59" cy="88" rx="7" ry="3" fill="#f3a5b4" opacity=".65"/><ellipse cx="118" cy="88" rx="7" ry="3" fill="#f3a5b4" opacity=".65"/></svg><p>Token太贵了，救救孩子吧！<small>拜托拜托，求投喂～</small></p></div><div class="developer-support"><div><h3>支持开发</h3><p>如果这个工具对你有帮助，欢迎自愿赞助，支持后续开发与维护。感谢每一份支持。</p><p class="support-network">USDT · TRON（TRC20）</p><label for="developer-address">赞助地址</label><input id="developer-address" readonly value="'+address+'" spellcheck="false"><button id="copy-developer-address" type="button">复制地址</button><span id="developer-copy-status" role="status"></span></div><figure><img src="assets/developer-tron-qr.png" alt="深海的鱼的 TRON 赞助地址二维码"><figcaption>扫码赞助 · USDT / TRON</figcaption></figure></div></section>');const versionLabel=$('#developer-version');api('version').then(data=>{if(versionLabel.isConnected)versionLabel.textContent='EVE FitLab · v'+data.version}).catch(()=>{});$('#copy-developer-address').onclick=async()=>{try{await navigator.clipboard.writeText(address);$('#developer-copy-status').textContent='已复制'}catch{$('#developer-address').focus();$('#developer-address').select();$('#developer-copy-status').textContent='已选中地址，请手动复制'}}};
