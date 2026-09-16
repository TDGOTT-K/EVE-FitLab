import {scenarioPresets,scenarioFields,withoutScenario} from './scenario-presets.js';
import {mountTargetPlane} from './target-plane.js';
import {installTargetMapControls} from './target-map-controls.js';

export function openScenarioEditor({fit,fits,shipName,calculate,onSave}){
 const state=scenarioPresets(fit),dialog=document.createElement('dialog');dialog.className='scenario-dialog';
 const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
 let plane,controls,form,revision=0,busy=false,dirty=false;
 dialog.innerHTML='<div class="flow-head"><b>情景设置</b><button type="button" data-close aria-label="关闭情景设置">×</button></div><div class="scenario-toolbar"><select aria-label="当前情景"></select><button type="button" data-new>＋ 新建</button><button type="button" data-copy>复制</button><button type="button" data-delete>删除</button></div><div class="scenario-editor-body"></div><div class="scenario-footer"><span role="status">仅本地保存 · 不随图片分享</span><button type="button" data-save>保存并应用</button></div><p class="scenario-error" role="alert"></p>';
 document.body.append(dialog);const $=s=>dialog.querySelector(s),select=$('select'),status=$('[role=status]');
 function capture(){const item=state.scenarios.find(s=>s.id===state.activeScenarioId);if(!item||!form)return;const data=new FormData(form);item.name=form.elements.scenarioName.value.trim()||'未命名情景';item.value={...item.value,...Object.fromEntries(['targetFitId','supportFitId','hostileFitId','targetLayer'].map(k=>[k,data.get(k)])),...Object.fromEntries(['supportDistance','hostileDistance'].map(k=>[k,Number(data.get(k))])),...plane.value(),targetHealth:controls.health()};}
 function changed(){dirty=true;status.textContent='情景修改未保存 · 关闭可放弃';}
 function draw(){
  plane?.destroy();revision++;form=null;
  select.innerHTML='<option value="">不应用情景</option>'+state.scenarios.map(s=>'<option value="'+esc(s.id)+'">'+esc(s.name)+'</option>').join('');select.value=state.activeScenarioId||'';
  const item=state.scenarios.find(s=>s.id===state.activeScenarioId);$('[data-copy]').disabled=$('[data-delete]').disabled=!item;
  if(!item){$('.scenario-editor-body').innerHTML='<div class="scenario-empty"><strong>装配自身数值</strong><p>新建一个情景，右键放置目标、传电或毁电来源。</p><p>已保存情景保留在上方列表中。</p></div>';return;}
  const value=item.value;
  $('.scenario-editor-body').innerHTML='<form><input class="scenario-name" name="scenarioName" aria-label="情景名称" maxlength="80" value="'+esc(item.name)+'">'+['targetFitId','supportFitId','hostileFitId','targetLayer','supportDistance','hostileDistance'].map(k=>'<input type="hidden" name="'+k+'" value="'+esc(value[k]??(k==='targetLayer'?'shield':k.endsWith('Distance')?10000:''))+'">').join('')+'<div class="scenario-map"></div></form><p class="scenario-scope">静态条件估算，供装配参考。当前传电/毁电按来源持续运转计算，尚未联算来源缺电停机。</p>';
  form=$('form');form.onsubmit=e=>e.preventDefault();form.oninput=()=>{changed();select.selectedOptions[0].textContent=form.elements.scenarioName.value.trim()||'未命名情景'};
  const root=$('.scenario-map');plane=mountTargetPlane(root,value);const currentPlane=plane;
  async function speedLimit(){const request=++revision,id=form.elements.targetFitId.value;currentPlane.setSpeedLimit(null,id?'正在读取速度上限…':'右键空地放置目标');if(!id)return;
   try{const enemy=fits.find(f=>f.id===id);if(!enemy)throw Error('目标装配已删除，请重新选择');const [a,b]=await Promise.all([calculate(withoutScenario(fit)),calculate(withoutScenario(enemy))]);if(request!==revision||!dialog.open)return;const limit=a.attributes.maxVelocity+b.attributes.maxVelocity;if(!Number.isFinite(limit))throw Error('速度数据缺失');currentPlane.setSpeedLimit(limit,'相对速度上限 '+limit.toFixed(1)+' m/s');}
   catch(e){if(request===revision&&dialog.open)currentPlane.setSpeedLimit(null,'速度上限不可用；已有矢量保留，位置仍可调整。');}
  }
  controls=installTargetMapControls(root,{form,fits,ownShipId:fit.shipId,shipName,onTargetChange:()=>{changed();speedLimit()},health:value.targetHealth,plane});
  root.addEventListener('pointerup',changed);root.addEventListener('keydown',e=>{if(e.key.startsWith('Arrow'))changed()});speedLimit();
 }
 select.onchange=()=>{capture();state.activeScenarioId=select.value||null;changed();draw()};
 $('[data-new]').onclick=()=>{capture();const item={id:crypto.randomUUID(),name:'情景 '+(state.scenarios.length+1),value:{}};state.scenarios.push(item);state.activeScenarioId=item.id;changed();draw();$('.scenario-name').select()};
 $('[data-copy]').onclick=()=>{capture();const source=state.scenarios.find(s=>s.id===state.activeScenarioId);if(!source)return;const item={...structuredClone(source),id:crypto.randomUUID(),name:(source.name+' 副本').slice(0,80)};state.scenarios.push(item);state.activeScenarioId=item.id;changed();draw()};
 $('[data-delete]').onclick=()=>{const item=state.scenarios.find(s=>s.id===state.activeScenarioId);if(!item||!confirm('删除情景“'+item.name+'”？'))return;state.scenarios=state.scenarios.filter(s=>s.id!==item.id);state.activeScenarioId=null;changed();draw()};
 function close(){if(busy)return;if(dirty&&!confirm('放弃尚未保存的情景修改？'))return;dialog.close()}
 $('[data-close]').onclick=close;dialog.oncancel=e=>{e.preventDefault();close()};dialog.onclose=()=>{revision++;plane?.destroy();dialog.remove()};
 $('[data-save]').onclick=async()=>{capture();busy=true;$('[data-save]').disabled=true;$('.scenario-error').textContent='';try{await onSave(scenarioFields(state));dirty=false;dialog.close()}catch(e){$('.scenario-error').textContent=e.message}finally{busy=false;if(dialog.isConnected)$('[data-save]').disabled=false}};
 dialog.showModal();draw();
}
