import {implantCatalog} from './loadout-catalog.js';
import {escapeHtml as esc} from './scenario-display.js';
const catalog=implantCatalog.filter(t=>t.slot>=1&&t.slot<=10);
const byId=id=>catalog.find(t=>t.id===id);
export function openImplantEditor({plan=[],onApply}){
 const selected=new Map(plan.map(x=>[byId(x.typeId)?.slot,x.typeId]).filter(([slot])=>slot));
 let active=1,drag=null;
 const dialog=document.createElement('dialog');dialog.className='implant-dialog';
 dialog.innerHTML='<div class="flow-head"><b>脑插配置</b><button type="button" data-close aria-label="关闭脑插配置">×</button></div><div class="implant-intro"><span>当前装配的脑插方案</span><small>交互预览 · 加成与技能校验待接入</small></div><div class="implant-layout"><section class="implant-slots" aria-label="脑插槽位"></section><section class="implant-browser"><div class="implant-search"><input aria-label="搜索脑插" placeholder="搜索名称、型号"><span class="implant-match-count"></span></div><div class="implant-candidates"></div><div class="implant-hint">双击或拖入安装 · 拖回此处卸下</div></section></div><div class="implant-bottom"><span class="implant-status" role="status"></span><button type="button" data-cancel>取消</button><button type="button" data-apply>应用方案</button></div>';
 document.body.append(dialog);
 const slots=dialog.querySelector('.implant-slots'),list=dialog.querySelector('.implant-candidates'),search=dialog.querySelector('input'),status=dialog.querySelector('.implant-status');
 const icon=t=>'<img draggable="false" loading="lazy" src="https://images.evetech.net/types/'+t.id+'/icon?size=64" alt="">';
 function choose(id){const t=byId(id);if(!t)return;selected.set(t.slot,id);active=t.slot;render();status.textContent='已选择 '+t.name;}
 function renderList(){
  const q=search.value.trim().toLowerCase(),rows=catalog.filter(t=>t.slot===active&&(!q||(t.name+' '+t.en+' '+t.id).toLowerCase().includes(q)));
  dialog.querySelector('.implant-match-count').textContent='槽位 '+active+' · '+rows.length+' 件';
  list.innerHTML=rows.map(t=>'<button type="button" draggable="true" data-type="'+t.id+'" class="implant-candidate '+(selected.get(active)===t.id?'chosen':'')+'">'+icon(t)+'<span>'+esc(t.name)+'<small>'+esc(t.en)+'</small></span>'+(selected.get(active)===t.id?'<b>✓</b>':'')+'</button>').join('')||'<p class="pilot-empty">没有匹配脑插</p>';
  list.querySelectorAll('button').forEach(b=>{
   const id=Number(b.dataset.type);b.ondblclick=()=>choose(id);b.onkeydown=e=>{if(e.key==='Enter'){e.preventDefault();choose(id)}};
   b.ondragstart=e=>{drag={id,fromSlot:false};e.dataTransfer.setData('text/plain',String(id));e.dataTransfer.effectAllowed='copy';};b.ondragend=()=>{drag=null;};
  });
 }
 function render(){
  slots.innerHTML=Array.from({length:10},(_,i)=>{const slot=i+1,t=byId(selected.get(slot));return '<div class="implant-slot '+(slot===active?'active':'')+'" data-slot="'+slot+'"><button type="button" class="implant-slot-main" '+(t?'draggable="true"':'')+' aria-label="脑插槽位 '+slot+'" aria-pressed="'+(slot===active)+'"><span class="implant-slot-number">'+String(slot).padStart(2,'0')+'</span>'+(t?icon(t):'<span class="implant-empty-icon">＋</span>')+'<span class="implant-slot-name">'+esc(t?.name||'选择脑插')+'</span></button>'+(t?'<button type="button" class="implant-remove" aria-label="卸下槽位 '+slot+'">×</button>':'')+'</div>';}).join('');
  slots.querySelectorAll('[data-slot]').forEach(el=>{
   const slot=Number(el.dataset.slot),button=el.querySelector('.implant-slot-main');
   button.onclick=()=>{active=slot;render();};
   el.querySelector('.implant-remove')?.addEventListener('click',()=>{selected.delete(slot);render();status.textContent='已卸下槽位 '+slot;});
   button.ondragstart=e=>{const id=selected.get(slot);if(!id){e.preventDefault();return;}drag={id,fromSlot:true};e.dataTransfer.setData('text/plain',String(id));e.dataTransfer.effectAllowed='move';};button.ondragend=()=>{drag=null;};
   el.ondragover=e=>{if(!drag)return;e.preventDefault();e.dataTransfer.dropEffect=byId(drag.id)?.slot===slot?(drag.fromSlot?'move':'copy'):'none';};
   el.ondrop=e=>{e.preventDefault();if(!drag)return;if(byId(drag.id)?.slot===slot)choose(drag.id);else status.textContent='该脑插只能安装到槽位 '+byId(drag.id)?.slot;drag=null;};
  });
  renderList();
 }
 const browser=dialog.querySelector('.implant-browser');
 browser.ondragover=e=>{if(drag?.fromSlot){e.preventDefault();e.dataTransfer.dropEffect='move';}};
 browser.ondrop=e=>{if(!drag?.fromSlot)return;e.preventDefault();selected.delete(byId(drag.id).slot);drag=null;render();status.textContent='已卸下脑插';};
 search.oninput=renderList;
 dialog.querySelectorAll('[data-close],[data-cancel]').forEach(b=>b.onclick=()=>dialog.close());
 dialog.querySelector('[data-apply]').onclick=()=>{onApply([...selected.values()].map(typeId=>({typeId})));dialog.close();};
 dialog.onclose=()=>dialog.remove();
 render();dialog.showModal();
}
