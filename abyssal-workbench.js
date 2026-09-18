import {escapeHtml as esc} from './scenario-display.js';
const rift='<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.3" aria-hidden="true"><path d="m12 2 8 7-2 10-9 3-7-9Z"/><path d="m14 4-6 8 5 1-3 7 8-10-5-1Z" fill="currentColor" stroke="none"/></svg>';
const format=(n,u)=>new Intl.NumberFormat('zh-CN',{maximumFractionDigits:u==='×'?4:2}).format(n)+(u?' '+u:'');
export function openAbyssalWorkbench({type,record,copy=false,onSave,api,options}){
 let receipt=record?.generationReceipt?structuredClone(record.generationReceipt):null,rule=receipt?.rule||null,metadata={},busy=false,roll=0;
 const request=()=>({baseTypeId:type.id,mutaplasmidTypeId:Number($('select').value)});
 const dialog=document.createElement('dialog');dialog.className='abyssal-workbench';dialog.setAttribute('aria-label','深渊变异工作台');
 dialog.innerHTML='<div class="mutation-head"><div>'+rift+'<b>深渊变异</b><span class="mutation-mock">引擎随机变异</span></div><button data-close aria-label="关闭变异工作台">×</button></div><div class="mutation-body"><div class="mutation-materials"><div class="mutation-base"><img src="https://images.evetech.net/types/'+type.id+'/icon?size=64" alt=""><div><small>原装备</small><b>'+esc(type.name)+'</b></div></div><span class="mutation-link">＋</span><label class="mutation-plasmid">'+rift+'<span><small>突变质体</small><select aria-label="突变质体"></select></span></label></div><div class="mutation-result-title"><h2></h2><button class="edit-name-icon" data-rename aria-label="编辑深渊实例名称">✎</button><span class="mutation-roll-count"></span></div><div class="mutation-table-head"><span>变异属性</span><span>原值 → 结果</span><small>色带为变异范围</small></div><div class="mutation-attributes"></div><div class="mutation-summary" role="status"></div><details class="mutation-notes"><summary>备注</summary><textarea aria-label="深渊实例备注" maxlength="1000" rows="2" placeholder="记录用途或偏好"></textarea></details></div><div class="mutation-foot"><small>模拟生成 · 非游戏内实物</small><div><button data-roll>'+rift+'<span>变异一次</span></button><button data-save>保存到深渊库</button></div></div><p class="mutation-error" role="alert"></p>';
 document.body.append(dialog);const $=s=>dialog.querySelector(s);let name=record?record.name+(copy?' · 副本':''):type.name+' · 深渊';$('.mutation-result-title h2').textContent=name;$('textarea').value=record?.notes||'';$('select').innerHTML=options.map(o=>'<option value="'+o.id+'">'+esc(o.name)+'</option>').join('');if(receipt)$('select').value=receipt.rule.mutaplasmidTypeId;
 function paint(){
  let good=0,bad=0;
  $('.mutation-attributes').innerHTML=(rule?.attributes||[]).map(a=>{
   const meta=metadata[a.attributeId]||{},base=a.baseValue,value=receipt?.mutation.attributes[a.attributeId],low=Math.min(a.minimumValue,base),high=Math.max(a.maximumValue,base),position=v=>100*(v-low)/(high-low||1),direction=a.highIsGood??meta.highIsGood;
   const changed=value!=null&&value!==base,better=typeof direction==='boolean'&&changed?((value>base)===direction):null;
   if(better===true)good++;if(better===false)bad++;
   const unit=meta.unit||'',fmt=v=>formatAttribute(v,a.unitId,unit),delta=base?(value-base)/Math.abs(base)*100:null;
   return '<div class="mutation-attribute '+(better===true?'better':better===false?'worse':'')+'"><div class="mutation-attribute-label"><b>'+esc(meta.label||a.name)+'</b><small>'+esc(fmt(base))+' → <strong>'+(value==null?'—':esc(fmt(value)))+'</strong></small></div><div class="mutation-range" title="可变异范围：'+esc(fmt(a.minimumValue))+' ～ '+esc(fmt(a.maximumValue))+'；虚线为原值"><i class="mutation-allowed" style="left:'+position(a.minimumValue)+'%;width:'+(position(a.maximumValue)-position(a.minimumValue))+'%"></i><i class="mutation-baseline" style="left:'+position(base)+'%"></i>'+(value==null?'':'<i class="mutation-point" style="left:'+position(value)+'%"></i>')+'</div><span class="mutation-delta">'+(value==null?'待变异':delta==null?esc(fmt(value-base)):(delta>0?'+':'')+delta.toFixed(1)+'%')+'</span></div>';
  }).join('');
  $('.mutation-roll-count').textContent=roll?'第 '+roll+' 次':'';
  $('.mutation-summary').textContent=busy?'正在读取引擎…':receipt?good+' 项改善 · '+bad+' 项下降':rule&&!rule.nativeInstanceSupported?'此类型可生成，但当前引擎尚不支持安装':record?.uiMock?'旧演示实例：重新变异后才可安装':'虚线为原值 · 亮点为结果';
  $('[data-save]').disabled=!receipt||busy||!rule?.nativeInstanceSupported;
  $('[data-roll]').disabled=!rule||busy;
  $('select').disabled=busy;$('[data-close]').disabled=busy;
 }
 async function loadRule(){busy=true;receipt=null;rule=null;paint();$('.mutation-error').textContent='';try{const result=await api('mutation-rule',request());rule=result.data;metadata=result.metadata;}catch(e){$('.mutation-error').textContent=e.message}finally{busy=false;paint()}}
 $('[data-rename]').onclick=()=>{if($('.mutation-name-input')||busy)return;const h=$('h2'),input=document.createElement('input');input.className='mutation-name-input';input.setAttribute('aria-label','深渊实例名称');input.maxLength=100;input.value=name;h.hidden=true;h.after(input);input.focus();input.select();let done=false;const finish=commit=>{if(done)return;done=true;if(commit&&input.value.trim())name=input.value.trim();h.textContent=name;h.hidden=false;input.remove()};input.onblur=()=>finish(true);input.onkeydown=e=>{if(e.isComposing)return;if(e.key==='Enter'||e.key==='Escape'){e.preventDefault();e.stopPropagation();finish(e.key==='Enter')}};};
 $('select').onchange=loadRule;
 $('[data-roll]').onclick=async()=>{if(busy)return;busy=true;paint();$('.mutation-error').textContent='';try{const result=await api('mutation-roll',request());receipt=result.data;rule=receipt.rule;metadata=result.metadata;roll++;}catch(e){$('.mutation-error').textContent=e.message}finally{busy=false;paint()}};
 $('[data-save]').onclick=async()=>{if(!receipt||busy)return;busy=true;paint();try{await onSave({name,notes:$('textarea').value,generationReceipt:receipt});dialog.close()}catch(e){$('.mutation-error').textContent=e.message}finally{busy=false;if(dialog.isConnected)paint()}};
 $('[data-close]').onclick=()=>dialog.close();dialog.oncancel=e=>{if(busy)e.preventDefault()};dialog.onclose=()=>dialog.remove();paint();dialog.showModal();if(receipt){busy=true;paint();api('mutation-rule',request()).then(r=>{metadata=r.metadata}).catch(e=>{$('.mutation-error').textContent=e.message}).finally(()=>{busy=false;paint()})}else loadRule();
}


function formatAttribute(value,id,unit){
 if(id===101)return format(value/1000,'s');
 if([108,111].includes(id))return format((1-value)*100,'%');
 if(id===109)return format((value-1)*100,'%');
 if(id===127)return format(value*100,'%');
 return format(value,id===104?'×':unit);
}
