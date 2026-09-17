import {scenarioPresets,scenarioFields,withoutScenario} from './scenario-presets.js';
import {mountTargetPlane} from './target-plane.js';
import {installTargetMapControls} from './target-map-controls.js';

export function openScenarioEditor({fit,fits,shipName,calculate,onSave}){
 const state=scenarioPresets(fit),dialog=document.createElement('dialog');dialog.className='scenario-dialog';
 const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 let plane,controls,form,revision=0,busy=false,dirty=false;
 dialog.innerHTML='<div class="scenario-commandbar"><b>情景</b><span class="scenario-dirty" role="status" aria-label="已保存" title="已保存"></span><div class="scenario-picker"><select aria-label="当前情景"></select><input data-name aria-label="情景名称" maxlength="80" hidden></div><button type="button" data-rename title="重命名情景" aria-label="重命名情景">✎</button><button type="button" data-new title="新建情景" aria-label="新建情景">＋</button><button type="button" data-copy title="复制情景" aria-label="复制情景">⧉</button><button type="button" data-delete title="删除情景" aria-label="删除情景">⌫</button><button type="button" data-save>保存并应用</button><button type="button" data-close aria-label="关闭情景设置" title="关闭">×</button></div><div class="scenario-editor-body"></div><p class="scenario-error" role="alert"></p>';

 const icons={rename:'M4 16l-1 5 5-1L20 8l-4-4Z M14 6l4 4',new:'M12 4v16 M4 12h16',copy:'M8 8h12v12H8Z M16 8V4H4v12h4',delete:'M4 7h16 M9 7V4h6v3 M6 7l1 14h10l1-14 M10 11v6 M14 11v6'};
 for(const [key,path] of Object.entries(icons))dialog.querySelector('[data-'+key+']').innerHTML='<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="'+path+'"/></svg>';
 document.body.append(dialog);const $=s=>dialog.querySelector(s),select=$('select'),status=$('[role=status]');
 function capture(){const item=state.scenarios.find(s=>s.id===state.activeScenarioId);if(!item||!form)return;const data=new FormData(form);item.name=$('[data-name]').value.trim()||'未命名情景';item.value={...item.value,...Object.fromEntries(['targetFitId','supportFitId','hostileFitId','targetLayer'].map(k=>[k,data.get(k)])),...Object.fromEntries(['supportDistance','hostileDistance'].map(k=>[k,Number(data.get(k))])),...plane.value(),targetHealth:controls.health()};}
 function changed(){dirty=true;status.classList.add('unsaved');status.setAttribute('aria-label','情景修改未保存');status.title='情景修改未保存';}
 function draw(){
  plane?.destroy();revision++;form=null;
  select.innerHTML='<option value="">不应用情景</option>'+state.scenarios.map(s=>'<option value="'+esc(s.id)+'">'+esc(s.name)+'</option>').join('');select.value=state.activeScenarioId||'';
  const item=state.scenarios.find(s=>s.id===state.activeScenarioId);$('[data-copy]').disabled=$('[data-delete]').disabled=$('[data-rename]').disabled=!item;$('[data-name]').value=item?.name||'';$('[data-name]').hidden=true;select.hidden=false;
  if(!item){$('.scenario-editor-body').innerHTML='<div class="scenario-empty"><span class="scenario-empty-orbit" aria-hidden="true">＋</span><button type="button" data-create-empty>新建情景</button></div>';$('[data-create-empty]').onclick=()=>$('[data-new]').click();return;}
  const value=item.value;
  $('.scenario-editor-body').innerHTML='<form>'+ ['targetFitId','supportFitId','hostileFitId','targetLayer','supportDistance','hostileDistance'].map(k=>'<input type="hidden" name="'+k+'" value="'+esc(value[k]??(k==='targetLayer'?'shield':k.endsWith('Distance')?10000:''))+'">').join('')+'<div class="scenario-map"></div></form>';
  form=$('form');form.onsubmit=e=>e.preventDefault();form.oninput=changed;
  const root=$('.scenario-map');plane=mountTargetPlane(root,value);const currentPlane=plane;
  async function speedLimit(){const request=++revision,id=form.elements.targetFitId.value;currentPlane.setSpeedLimit(null,id?'正在读取速度上限…':'右键空地放置目标');if(!id)return;
   try{const enemy=fits.find(f=>f.id===id);if(!enemy)throw Error('目标装配已删除，请重新选择');const [a,b]=await Promise.all([calculate(withoutScenario(fit)),calculate(withoutScenario(enemy))]);if(request!==revision||!dialog.open)return;const limit=a.attributes.maxVelocity+b.attributes.maxVelocity;if(!Number.isFinite(limit))throw Error('速度数据缺失');currentPlane.setSpeedLimit(limit,'相对速度上限 '+limit.toFixed(1)+' m/s');}
   catch(e){if(request===revision&&dialog.open)currentPlane.setSpeedLimit(null,'速度上限不可用；已有矢量保留，位置仍可调整。');}
  }
  controls=installTargetMapControls(root,{form,fits,ownShipId:fit.shipId,shipName,onTargetChange:()=>{changed();speedLimit()},health:value.targetHealth,plane});
  root.addEventListener('scenario-change',changed);speedLimit();
 }
 $('[data-rename]').onclick=()=>{select.hidden=true;const input=$('[data-name]');input.hidden=false;input.focus();input.select()};
 $('[data-name]').oninput=()=>{changed();select.selectedOptions[0].textContent=$('[data-name]').value.trim()||'未命名情景'};
 $('[data-name]').onblur=()=>{capture();$('[data-name]').hidden=true;select.hidden=false};
 $('[data-name]').onkeydown=e=>{if(e.key==='Enter'){e.preventDefault();$('[data-name]').blur()}};
 select.onchange=()=>{capture();state.activeScenarioId=select.value||null;changed();draw()};
 $('[data-new]').onclick=()=>{capture();const item={id:crypto.randomUUID(),name:'情景 '+(state.scenarios.length+1),value:{}};state.scenarios.push(item);state.activeScenarioId=item.id;changed();draw();$('[data-rename]').click()};
 $('[data-copy]').onclick=()=>{capture();const source=state.scenarios.find(s=>s.id===state.activeScenarioId);if(!source)return;const item={...structuredClone(source),id:crypto.randomUUID(),name:(source.name+' 副本').slice(0,80)};state.scenarios.push(item);state.activeScenarioId=item.id;changed();draw()};
 $('[data-delete]').onclick=()=>{const item=state.scenarios.find(s=>s.id===state.activeScenarioId);if(!item||!confirm('删除情景“'+item.name+'”？'))return;state.scenarios=state.scenarios.filter(s=>s.id!==item.id);state.activeScenarioId=null;changed();draw()};
 function close(){if(busy)return;if(dirty&&!confirm('放弃尚未保存的情景修改？'))return;dialog.close()}
 $('[data-close]').onclick=close;dialog.oncancel=e=>{e.preventDefault();close()};dialog.onclose=()=>{revision++;plane?.destroy();dialog.remove()};
 $('[data-save]').onclick=async()=>{capture();busy=true;$('[data-save]').disabled=true;$('.scenario-error').textContent='';try{await onSave(scenarioFields(state));dirty=false;dialog.close()}catch(e){$('.scenario-error').textContent=e.message}finally{busy=false;if(dialog.isConnected)$('[data-save]').disabled=false}};
 dialog.showModal();draw();
}
