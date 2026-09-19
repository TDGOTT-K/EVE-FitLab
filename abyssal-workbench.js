import {mountMutationMaterialPicker} from './mutation-material-picker.js';
import {escapeHtml as esc} from './scenario-display.js';
const display=(v,id)=>id===101?v/1000:[108,111].includes(id)?(1-v)*100:id===109?(v-1)*100:id===127?v*100:v;
const raw=(v,id)=>id===101?v*1000:[108,111].includes(id)?1-v/100:id===109?1+v/100:id===127?v/100:v;
const unit=(id,label)=>id===101?'s':[108,109,111,127].includes(id)?'%':id===104?'×':label||'';
const number=v=>Number(v).toLocaleString('zh-CN',{maximumSignificantDigits:8,notation:v!==0&&(Math.abs(v)<0.0001||Math.abs(v)>=1e12)?'scientific':'standard'});
export function openAbyssalWorkbench({type,record,copy=false,onSave,api,options}){
 let receipt=structuredClone(record?.generationReceipt||null),edit=structuredClone(record?.editReceipt||null),rule=null,metadata={},values={},busy=false,running=false,stop=false,stream=null,lastRequest=null,rolls=0;
 const targets=new Map();
 const dialog=document.createElement('dialog');dialog.className='abyssal-workbench';dialog.setAttribute('aria-label','深渊变异工作台');
 dialog.innerHTML=`<div class="mutation-head"><div><b>深渊变异</b><span class="mutation-mock">编辑 · 随机试验</span></div><button data-close aria-label="关闭变异工作台">×</button></div><div class="mutation-body"><div class="mutation-materials"><div class="mutation-base"><img src="https://images.evetech.net/types/${type.id}/icon?size=64" alt=""><div><small>原装备</small><b>${esc(type.name)}</b></div></div><span class="mutation-link">＋</span><div class="mutation-plasmid"></div></div><label class="mutation-name-label">实例名称<input data-name maxlength="100" aria-label="深渊实例名称"></label><div class="mutation-attributes"></div><div class="mutation-summary" role="status"></div><section class="mutation-goals"><h3>合格条件</h3><p>勾选的属性必须同时达标。默认达到编辑值或更好，也可指定范围；不勾选则不限。</p><div data-targets></div><div class="mutation-trial-actions"><button data-estimate>计算期望次数</button><button data-trial>Roll 至合格</button><button data-stop hidden>停止</button></div><p data-chance role="status"></p><p data-progress role="status"></p><small>基于独立均匀随机模型，不代表 CCP 官方概率。期望次数不是保底次数。</small></section><details class="mutation-notes"><summary>备注</summary><textarea aria-label="深渊实例备注" maxlength="1000" rows="2"></textarea></details></div><div class="mutation-foot"><small>模拟实例 · 非游戏内实物</small><div><button data-apply>应用编辑</button><button data-roll>随机一次</button><button data-save>保存到深渊库</button></div></div><p class="mutation-error" role="alert"></p>`;
 document.body.append(dialog);const $=s=>dialog.querySelector(s);$('[data-name]').value=record?record.name+(copy?' · 副本':''):type.name+' · 深渊';$('textarea').value=record?.notes||'';
 const picker=mountMutationMaterialPicker($('.mutation-plasmid'),{options,value:(receipt||edit)?.rule.mutaplasmidTypeId,api,onChange:()=>load()});
 const request=operation=>({operation,baseTypeId:type.id,mutaplasmidTypeId:picker.value});
 function controls(){dialog.querySelectorAll('[data-apply],[data-roll],[data-save],[data-estimate],[data-trial],.mutation-value,[data-targets] input,[data-targets] select').forEach(e=>e.disabled=busy||running||!rule);$('[data-save]').disabled=busy||running||!rule?.nativeInstanceSupported;picker.disabled=busy||running;$('[data-close]').disabled=busy;$('[data-stop]').hidden=!running;$('[data-stop]').disabled=stop||!running;$('[data-trial]').textContent=stream?'继续试验':'Roll 至合格';}
 function resetTrial(){stream=null;lastRequest=null;$('[data-progress]').textContent='';$('[data-chance]').textContent='';}
 function invalidate(){receipt=null;edit=null;resetTrial();$('.mutation-summary').textContent='数值已修改，保存时将由引擎校验';controls();}
 function paint(){
  $('.mutation-attributes').innerHTML=(rule?.attributes||[]).map(a=>{
   const v=values[a.attributeId],meta=metadata[a.attributeId]||{},u=unit(a.unitId,meta.unit),fmt=x=>number(display(x,a.unitId))+(u?' '+u:''),bounds=[display(a.minimumValue,a.unitId),display(a.maximumValue,a.unitId)].sort((a,b)=>a-b);
   const low=Math.min(a.minimumValue,a.baseValue),high=Math.max(a.maximumValue,a.baseValue),position=x=>100*(x-low)/(high-low||1);
   const comparison=receipt?.rolls.find(r=>r.range.attributeId===a.attributeId)?.comparison||edit?.rows.find(r=>r.attributeId===a.attributeId)?.comparison;
   return `<div class="mutation-edit-row"><label><b>${esc(meta.label||a.name)}</b><small>原值 ${esc(fmt(a.baseValue))} · 允许 ${esc(fmt(a.minimumValue))} ～ ${esc(fmt(a.maximumValue))}</small></label><div><input class="mutation-value" data-attr="${a.attributeId}" type="number" step="any" min="${bounds[0]}" max="${bounds[1]}" value="${display(v,a.unitId)}" aria-label="${esc(meta.label||a.name)}编辑值"><span>${esc(u)}</span></div><small>${comparison?.relativeChange!=null?(comparison.relativeChange>0?'+':'')+number(comparison.relativeChange*100)+'%':''}</small><div class="mutation-range" style="grid-column:1/-1" title="虚线为原值，亮点为当前值"><i class="mutation-allowed" style="left:${position(a.minimumValue)}%;width:${position(a.maximumValue)-position(a.minimumValue)}%"></i><i class="mutation-baseline" style="left:${position(a.baseValue)}%"></i><i class="mutation-point" style="left:${position(v)}%"></i></div></div>`;
  }).join('');
  $('.mutation-summary').textContent=receipt?'随机生成'+(rolls?' · 本次试验 '+rolls+' 次':''):edit?'手动编辑 · 已通过引擎范围校验':busy?'正在读取规则…':'输入目标数值，或随机生成一次';
  $('[data-targets]').innerHTML=(rule?.attributes||[]).map(a=>{
   const t=targets.get(a.attributeId)||{enabled:true,mode:a.highIsGood==null?'range':'atLeastAsGood',low:a.minimumValue,high:a.maximumValue};targets.set(a.attributeId,t);
   return `<div class="mutation-target-row" data-target="${a.attributeId}"><label><input type="checkbox" ${t.enabled?'checked':''}>${esc(metadata[a.attributeId]?.label||a.name)}</label><select aria-label="合格条件方式"><option value="atLeastAsGood" ${a.highIsGood==null?'disabled':''}>达到编辑值或更好</option><option value="range">指定范围</option></select><span class="mutation-target-bounds" ${t.mode==='range'?'':'hidden'}><input data-low type="number" step="any" value="${Math.min(display(t.low,a.unitId),display(t.high,a.unitId))}" aria-label="合格下限"> ～ <input data-high type="number" step="any" value="${Math.max(display(t.low,a.unitId),display(t.high,a.unitId))}" aria-label="合格上限"></span></div>`;
  }).join('');
  dialog.querySelectorAll('.mutation-value').forEach(input=>input.onchange=()=>{
   const a=rule.attributes.find(a=>a.attributeId===+input.dataset.attr);if(!input.checkValidity()||input.value===''){input.reportValidity();return;}
   const value=raw(input.valueAsNumber,a.unitId);values[a.attributeId]=Math.min(a.maximumValue,Math.max(a.minimumValue,value));invalidate();
  });
  dialog.querySelectorAll('[data-target]').forEach(row=>{
   const id=+row.dataset.target,a=rule.attributes.find(a=>a.attributeId===id),t=targets.get(id);row.querySelector('select').value=t.mode;
   row.onchange=()=>{t.enabled=row.querySelector('[type=checkbox]').checked;t.mode=row.querySelector('select').value;row.querySelector('.mutation-target-bounds').hidden=t.mode!=='range';const lo=row.querySelector('[data-low]').valueAsNumber,hi=row.querySelector('[data-high]').valueAsNumber;if(Number.isFinite(lo)&&Number.isFinite(hi)&&lo<=hi){const b=[raw(lo,a.unitId),raw(hi,a.unitId)].sort((x,y)=>x-y);t.low=b[0];t.high=b[1];}resetTrial();};
  });controls();
 }
 function criteria(){
  return [...dialog.querySelectorAll('[data-target]')].flatMap(row=>{
   const id=+row.dataset.target,a=rule.attributes.find(a=>a.attributeId===id),t=targets.get(id);if(!t.enabled)return [];
   if(t.mode==='atLeastAsGood')return [{attributeId:id,mode:t.mode,value:values[id]}];
   const low=row.querySelector('[data-low]'),high=row.querySelector('[data-high]');if(low.value===''||high.value===''||!Number.isFinite(low.valueAsNumber)||!Number.isFinite(high.valueAsNumber)||low.valueAsNumber>high.valueAsNumber)throw Error('请填写有效的合格上下限');
   const bounds=[raw(low.valueAsNumber,a.unitId),raw(high.valueAsNumber,a.unitId)].sort((a,b)=>a-b);t.low=bounds[0];t.high=bounds[1];return [{attributeId:id,mode:'range',minimum:t.low,maximum:t.high}];
  });
 }
 function checkInputs(){for(const input of dialog.querySelectorAll('.mutation-value'))if(input.value===''||!input.checkValidity()){input.reportValidity();throw Error('请将编辑值保持在允许范围内')}}
 async function apply(){checkInputs();const result=await api('mutation-workbench',{...request('edit'),values});edit=result.edit;receipt=null;rule=result.rule;}
 function chanceText(c){$('[data-chance]').textContent=c.state==='impossible'?'合格概率为 0，当前条件不可能随机达成。':`单次合格概率 ${c.probability==null?'低于数值表示范围':number(c.probability*100)+'%'} · 平均需要 ${c.expectedAttempts==null?'超出数值表示范围':number(c.expectedAttempts)} 次`;}
 async function action(fn){if(busy||running)return;busy=true;controls();$('.mutation-error').textContent='';try{await fn()}catch(e){$('.mutation-error').textContent=e.message}finally{busy=false;paint()}}
 async function load(){receipt=null;edit=null;rule=null;targets.clear();resetTrial();await action(async()=>{const r=await api('mutation-rule',request('edit'));rule=r.data;metadata=r.metadata;values=Object.fromEntries(rule.attributes.map(a=>[a.attributeId,Math.max(a.minimumValue,Math.min(a.maximumValue,a.baseValue))]));});}
 $('[data-apply]').onclick=()=>action(apply);
 $('[data-roll]').onclick=()=>action(async()=>{const r=await api('mutation-roll',request('edit'));receipt=r.data;edit=null;rule=receipt.rule;metadata=r.metadata;values={...receipt.mutation.attributes};rolls++;resetTrial();});
 $('[data-estimate]').onclick=()=>action(async()=>{checkInputs();chanceText((await api('mutation-workbench',{...request('estimate'),criteria:criteria()})).chance)});
 $('[data-save]').onclick=()=>action(async()=>{if(!receipt&&!edit)await apply();await onSave({name:$('[data-name]').value,notes:$('textarea').value,...(receipt?{generationReceipt:receipt}:{editReceipt:edit})});dialog.close()});
 $('[data-trial]').onclick=async()=>{
  if(busy||running)return;let goal;try{checkInputs();goal={...request('trial'),criteria:criteria()};}catch(e){$('.mutation-error').textContent=e.message;return;}
  if(JSON.stringify(goal)!==lastRequest){stream=null;lastRequest=JSON.stringify(goal);}stream??={seed:crypto.randomUUID(),nextAttempt:0};running=true;stop=false;controls();$('[data-progress]').textContent='正在随机试验…';$('.mutation-error').textContent='';
  try{while(!stop&&dialog.isConnected){
   const r=await api('mutation-workbench',{...goal,attempts:500,...(stream?{seed:stream.seed,startAttempt:stream.nextAttempt}:{})});chanceText(r.chance);stream=r.trial;
   $('[data-progress]').textContent=`已实际随机 ${number(stream.nextAttempt)} 次 · ${stream.found?'找到合格实例':stream.state==='impossible'?'当前条件不可达':'未命中'}`;
   if(stream.found){receipt=stream.match;edit=null;rule=receipt.rule;values={...receipt.mutation.attributes};rolls=stream.nextAttempt;stream=null;break;}
   if(stream.state==='impossible'){stream=null;break;}
   await new Promise(resolve=>setTimeout(resolve,0));
  }}catch(e){$('.mutation-error').textContent=e.message;}finally{running=false;if(dialog.isConnected){paint();if(stop)$('[data-progress]').textContent+=' · 已停止，可继续';}}
 };
 $('[data-stop]').onclick=()=>{stop=true;$('[data-stop]').disabled=true;$('[data-progress]').textContent+=' · 正在停止当前批次';};
 $('[data-close]').onclick=()=>{stop=true;dialog.close()};dialog.oncancel=e=>{if(busy)e.preventDefault();else stop=true};dialog.onclose=()=>{stop=true;picker.destroy();dialog.remove()};
 paint();dialog.showModal();
 if(receipt||edit){const originalReceipt=receipt,originalEdit=edit;action(async()=>{
  const info=await api('mutation-rule',request('edit'));rule=info.data;metadata=info.metadata;
  if(originalReceipt){const verified=await api('mutation-review',{baseTypeId:type.id,generationReceipt:originalReceipt});receipt=verified.data;}else edit=(await api('mutation-workbench',{...request('edit'),values:originalEdit.mutation.attributes})).edit;
  values={...(receipt||edit).mutation.attributes};
 });}else load();
}
