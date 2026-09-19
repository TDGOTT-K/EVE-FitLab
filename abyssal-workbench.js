import {mountMutationMaterialPicker} from './mutation-material-picker.js';
import {escapeHtml as esc} from './scenario-display.js';
const display=(v,id)=>id===101?v/1000:[108,111].includes(id)?(1-v)*100:id===109?(v-1)*100:id===127?v*100:v;
const unit=(id,label)=>id===101?'s':[108,109,111,127].includes(id)?'%':id===104?'×':label||'';
const number=v=>Number(v).toLocaleString('zh-CN',{maximumSignificantDigits:8,notation:v!==0&&(Math.abs(v)<0.0001||Math.abs(v)>=1e12)?'scientific':'standard'});
export function openAbyssalWorkbench({type,record,copy=false,onSave,api,options}){
 let receipt=structuredClone(record?.generationReceipt||null),edit=structuredClone(record?.editReceipt||null),rule=null,metadata={},values={},busy=false,running=false,stop=false,stream=null,lastRequest=null,rolls=0,editing=false,candidate=null;
 const targets=new Map();
 const dialog=document.createElement('dialog');dialog.className='abyssal-workbench';dialog.setAttribute('aria-label','深渊变异工作台');
 dialog.innerHTML=`<div class="mutation-head"><div><b>深渊变异</b><span class="mutation-mock">编辑 · 随机试验</span><div class="mutation-mode" role="group" aria-label="变异工作台模式"><button type="button" data-mode="random" aria-pressed="true">随机</button><button type="button" data-mode="edit" aria-pressed="false">编辑</button></div></div><button data-close aria-label="关闭变异工作台">×</button></div><div class="mutation-body"><div class="mutation-materials"><div class="mutation-base"><img src="https://images.evetech.net/types/${type.id}/icon?size=64" alt=""><div><small>原装备</small><b>${esc(type.name)}</b></div></div><span class="mutation-link">＋</span><div class="mutation-plasmid"></div></div><label class="mutation-name-label">实例名称<input data-name maxlength="100" aria-label="深渊实例名称"></label><div class="mutation-attributes"></div><div class="mutation-summary" role="status"></div><section class="mutation-goals"><h3>合格条件</h3><p>拖动两个滑块选择合格范围，勾选的属性须同时达标。红线为编辑值参考，白点为随机候选。</p><div data-targets></div><div class="mutation-trial-actions"><button data-estimate>计算期望次数</button><button data-trial>Roll 至合格</button><button data-stop hidden>停止</button></div><p data-chance role="status"></p><p data-progress role="status"></p><small>基于独立均匀随机模型，不代表 CCP 官方概率。期望次数不是保底次数。</small></section><details class="mutation-notes"><summary>备注</summary><textarea aria-label="深渊实例备注" maxlength="1000" rows="2"></textarea></details></div><div class="mutation-foot"><small>模拟实例 · 非游戏内实物</small><div><button data-apply>应用编辑</button><button data-roll>随机一次</button><button data-adopt>应用结果</button><button data-save>保存到深渊库</button></div></div><p class="mutation-error" role="alert"></p>`;
 document.body.append(dialog);const $=s=>dialog.querySelector(s);$('[data-name]').value=record?record.name+(copy?' · 副本':''):type.name+' · 深渊';$('textarea').value=record?.notes||'';
 const picker=mountMutationMaterialPicker($('.mutation-plasmid'),{options,value:(receipt||edit)?.rule.mutaplasmidTypeId,api,onChange:()=>load()});
 const request=operation=>({operation,baseTypeId:type.id,mutaplasmidTypeId:picker.value});
 function controls(){
  dialog.classList.toggle('mutation-editing',editing);
  dialog.querySelectorAll('[data-mode]').forEach(b=>{b.setAttribute('aria-pressed',String((b.dataset.mode==='edit')===editing));b.disabled=busy||running;});
  dialog.querySelectorAll('[role=slider]').forEach(slider=>{const a=rule.attributes.find(a=>a.attributeId===+slider.dataset.attr),enabled=!busy&&!running&&a.minimumValue!==a.maximumValue;slider.setAttribute('aria-disabled',String(!enabled));slider.tabIndex=enabled?0:-1;});
  $('[data-apply]').hidden=!editing;$('[data-save]').hidden=!editing;$('[data-roll]').hidden=editing;$('[data-adopt]').hidden=editing;$('.mutation-goals').hidden=editing;
  dialog.querySelectorAll('[data-apply],[data-roll],[data-save],[data-estimate],[data-trial],[data-adopt],[data-enabled]').forEach(e=>e.disabled=busy||running||!rule);
  $('[data-adopt]').disabled=busy||running||!candidate;$('[data-save]').disabled=busy||running||!rule?.nativeInstanceSupported;picker.disabled=busy||running;$('[data-close]').disabled=busy;$('[data-stop]').hidden=!running;$('[data-stop]').disabled=stop||!running;$('[data-trial]').textContent=stream?'继续试验':'Roll 至合格';
 }
 function resetTrial(){stream=null;lastRequest=null;$('[data-progress]').textContent='';$('[data-chance]').textContent='';}
 function invalidate(){receipt=null;edit=null;dialog.querySelectorAll('[data-change]').forEach(e=>e.textContent='');$('.mutation-summary').textContent='数值已修改，保存时将由引擎校验';controls();}
 function paint(){
  $('.mutation-attributes').innerHTML=(rule?.attributes||[]).map(a=>{
   const id=a.attributeId,meta=metadata[id]||{},u=unit(a.unitId,meta.unit),fmt=x=>number(display(x,a.unitId))+(u?' '+u:''),label=meta.label||a.name;
   const low=Math.min(a.minimumValue,a.baseValue),high=Math.max(a.maximumValue,a.baseValue),position=x=>100*(x-low)/(high-low||1);
   const t=targets.get(id)||{enabled:true,low:a.minimumValue,high:a.maximumValue};targets.set(id,t);
   const v=editing?values[id]:candidate?.mutation.attributes[id];
   const comparison=editing?(receipt?.rolls.find(r=>r.range.attributeId===id)?.comparison||edit?.rows.find(r=>r.attributeId===id)?.comparison):candidate?.rolls.find(r=>r.range.attributeId===id)?.comparison;
   const aria=(value,name)=>`role="slider" data-attr="${id}" aria-label="${esc(label+' '+name)}" aria-valuemin="${a.minimumValue}" aria-valuemax="${a.maximumValue}" aria-valuenow="${value}" aria-valuetext="${esc(fmt(value))}"`;
   return `<div class="mutation-edit-row" data-row="${id}"><label><b>${editing?'':`<input type="checkbox" data-enabled="${id}" aria-label="筛选${esc(label)}" ${t.enabled?'checked':''}> `}${esc(label)}</b><small>原值 ${esc(fmt(a.baseValue))} · 允许 ${esc(fmt(a.minimumValue))} ～ ${esc(fmt(a.maximumValue))}</small></label><div class="mutation-reading"><strong data-value="${id}">${v==null?'—':esc(number(display(v,a.unitId)))}</strong><span>${esc(u)}</span></div><small data-change>${comparison?.relativeChange!=null?(comparison.relativeChange>0?'+':'')+number(comparison.relativeChange*100)+'%':''}</small><div class="mutation-range ${editing?'':'mutation-dual'}" data-track="${id}" style="grid-column:1/-1" ${editing?aria(values[id],'编辑值'):''}>
    <i class="mutation-allowed" style="left:${position(a.minimumValue)}%;width:${position(a.maximumValue)-position(a.minimumValue)}%"></i><i class="mutation-baseline" style="left:${position(a.baseValue)}%"></i>
    ${editing?`<i class="mutation-point" style="left:${position(values[id])}%"></i>`:`<i class="mutation-selection" style="left:${position(t.low)}%;width:${position(t.high)-position(t.low)}%"></i><i class="mutation-reference" style="left:${position(values[id])}%" title="编辑值参考"></i>${candidate?`<i class="mutation-point mutation-candidate-point" style="left:${position(v)}%" title="随机候选"></i>`:''}<span class="mutation-bound" data-bound="low" ${aria(t.low,'合格下限')} style="left:${position(t.low)}%"></span><span class="mutation-bound" data-bound="high" ${aria(t.high,'合格上限')} style="left:${position(t.high)}%"></span>`}
   </div>${editing?'':`<small class="mutation-range-label" data-range-label="${id}">合格范围 ${esc(fmt(t.low))} ～ ${esc(fmt(t.high))}</small>`}</div>`;
  }).join('');
  $('.mutation-summary').textContent=editing?(receipt?'已应用随机结果':edit?'手动编辑 · 已通过引擎范围校验':'拖动滑条编辑装备数值'):(candidate?'随机候选 · 点击“应用结果”才会覆盖编辑值':'拖动双滑块设置范围 · 红线为编辑值参考');
  dialog.querySelectorAll('[role=slider]').forEach(slider=>{
   const a=rule.attributes.find(a=>a.attributeId===+slider.dataset.attr),id=a.attributeId,t=targets.get(id),track=$('[data-track="'+id+'"]'),low=Math.min(a.minimumValue,a.baseValue),high=Math.max(a.maximumValue,a.baseValue),bound=slider.dataset.bound;let pointer=null,dragOffset=0;
   const enabled=()=>!busy&&!running&&a.minimumValue!==a.maximumValue;
   const position=x=>100*(x-low)/(high-low||1),fmt=x=>number(display(x,a.unitId))+' '+unit(a.unitId,metadata[id]?.unit);
   const update=value=>{if(!enabled())return;value=Math.max(a.minimumValue,Math.min(a.maximumValue,value));
    if(bound){value=bound==='low'?Math.min(value,t.high):Math.max(value,t.low);if(value===t[bound])return;t[bound]=value;slider.style.left=position(value)+'%';track.querySelector('.mutation-selection').style.left=position(t.low)+'%';track.querySelector('.mutation-selection').style.width=position(t.high)-position(t.low)+'%';$('[data-range-label="'+id+'"]').textContent='合格范围 '+fmt(t.low)+' ～ '+fmt(t.high);resetTrial();}
    else{if(value===values[id])return;values[id]=value;$('[data-value="'+id+'"]').textContent=number(display(value,a.unitId));track.querySelector('.mutation-point').style.left=position(value)+'%';invalidate();}
    slider.setAttribute('aria-valuenow',String(value));slider.setAttribute('aria-valuetext',fmt(value));
   };
   const move=e=>{const rect=track.getBoundingClientRect();if(!rect.width)return;const value=low+(high-low)*Math.max(0,Math.min(1,(e.clientX-dragOffset-rect.left)/rect.width)),span=a.maximumValue-a.minimumValue;update(a.minimumValue+span*Math.round(1000*Math.max(0,Math.min(1,(value-a.minimumValue)/span)))/1000);};
   slider.onpointerdown=e=>{if(!enabled()||e.button!==0)return;e.preventDefault();e.stopPropagation();pointer=e.pointerId;const rect=track.getBoundingClientRect();dragOffset=bound?e.clientX-(rect.left+position(t[bound])*rect.width/100):0;slider.setPointerCapture(pointer);slider.focus({preventScroll:true});move(e);};
   slider.onpointermove=e=>{if(pointer===e.pointerId)move(e)};
   const finish=e=>{if(pointer!==e.pointerId)return;pointer=null;if(slider.hasPointerCapture(e.pointerId))slider.releasePointerCapture(e.pointerId);};
   slider.onpointerup=finish;slider.onpointercancel=finish;slider.onlostpointercapture=()=>{pointer=null};
   slider.onkeydown=e=>{if(!enabled())return;const step=(a.maximumValue-a.minimumValue)/(e.shiftKey?100:1000),v=bound?t[bound]:values[id];const next={ArrowLeft:v-step,ArrowDown:v-step,ArrowRight:v+step,ArrowUp:v+step,Home:a.minimumValue,End:a.maximumValue}[e.key];if(next!==undefined){e.preventDefault();update(next)}};
  });
  dialog.querySelectorAll('[data-enabled]').forEach(input=>input.onchange=()=>{targets.get(+input.dataset.enabled).enabled=input.checked;resetTrial();});controls();
 }
 function criteria(){return [...targets].filter(([,t])=>t.enabled).map(([attributeId,t])=>({attributeId,mode:'range',minimum:t.low,maximum:t.high}));}
 function checkInputs(){if(!rule||rule.attributes.some(a=>!Number.isFinite(values[a.attributeId])||values[a.attributeId]<a.minimumValue||values[a.attributeId]>a.maximumValue))throw Error('请将编辑值保持在允许范围内')}

 async function apply(){checkInputs();const result=await api('mutation-workbench',{...request('edit'),values});edit=result.edit;receipt=null;rule=result.rule;}
 function chanceText(c){$('[data-chance]').textContent=c.state==='impossible'?'合格概率为 0，当前条件不可能随机达成。':`单次合格概率 ${c.probability==null?'低于数值表示范围':number(c.probability*100)+'%'} · 平均需要 ${c.expectedAttempts==null?'超出数值表示范围':number(c.expectedAttempts)} 次`;}
 async function action(fn){if(busy||running)return;busy=true;controls();$('.mutation-error').textContent='';try{await fn()}catch(e){$('.mutation-error').textContent=e.message}finally{busy=false;paint()}}
 async function load(){receipt=null;edit=null;candidate=null;rule=null;targets.clear();resetTrial();await action(async()=>{const r=await api('mutation-rule',request('edit'));rule=r.data;metadata=r.metadata;values=Object.fromEntries(rule.attributes.map(a=>[a.attributeId,Math.max(a.minimumValue,Math.min(a.maximumValue,a.baseValue))]));});}
 dialog.querySelectorAll('[data-mode]').forEach(b=>b.onclick=()=>{if(busy||running)return;editing=b.dataset.mode==='edit';paint();});
 $('[data-adopt]').onclick=()=>{if(busy||running||!candidate)return;receipt=structuredClone(candidate);edit=null;values={...receipt.mutation.attributes};editing=true;paint();};
 $('[data-apply]').onclick=()=>action(apply);
 $('[data-roll]').onclick=()=>action(async()=>{const r=await api('mutation-roll',request('edit'));candidate=r.data;metadata=r.metadata;rolls++;resetTrial();});
 $('[data-estimate]').onclick=()=>action(async()=>{checkInputs();chanceText((await api('mutation-workbench',{...request('estimate'),criteria:criteria()})).chance)});
 $('[data-save]').onclick=()=>action(async()=>{if(!receipt&&!edit)await apply();await onSave({name:$('[data-name]').value,notes:$('textarea').value,...(receipt?{generationReceipt:receipt}:{editReceipt:edit})});dialog.close()});
 $('[data-trial]').onclick=async()=>{
  if(busy||running)return;let goal;try{checkInputs();goal={...request('trial'),criteria:criteria()};}catch(e){$('.mutation-error').textContent=e.message;return;}
  if(JSON.stringify(goal)!==lastRequest){stream=null;lastRequest=JSON.stringify(goal);}stream??={seed:crypto.randomUUID(),nextAttempt:0};running=true;stop=false;controls();$('[data-progress]').textContent='正在随机试验…';$('.mutation-error').textContent='';
  try{while(!stop&&dialog.isConnected){
   const r=await api('mutation-workbench',{...goal,attempts:500,...(stream?{seed:stream.seed,startAttempt:stream.nextAttempt}:{})});chanceText(r.chance);stream=r.trial;
   $('[data-progress]').textContent=`已实际随机 ${number(stream.nextAttempt)} 次 · ${stream.found?'找到合格实例':stream.state==='impossible'?'当前条件不可达':'未命中'}`;
   if(stream.found){candidate=stream.match;rolls=stream.nextAttempt;stream=null;break;}
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
