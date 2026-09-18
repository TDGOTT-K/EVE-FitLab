const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
export async function showTextImport({api,calculate,onSaved}){
 const dialog=document.createElement('dialog');dialog.className='share-dialog text-import-dialog';
 dialog.innerHTML='<div class="flow-head"><b>导入游戏装配文本</b><button aria-label="关闭文本导入">×</button></div><div class="image-import-body"><p>粘贴游戏或 Pyfa 导出的 EFT 文本，首行为 [舰船, 装配名]。支持中英文物品名；一次导入一份。</p><textarea aria-label="EFT 装配文本" spellcheck="false" maxlength="100000" placeholder="[Rifter, 我的装配]&#10;Gyrostabilizer II&#10;&#10;1MN Afterburner II&#10;&#10;200mm AutoCannon II, EMP S&#10;&#10;EMP S x1000"></textarea><div class="text-import-options"><label>打开文本文件 <input aria-label="选择 EFT 文件" type="file" accept=".txt,.eft,.cfg,.fit,text/plain"></label><label>计算角色 <select aria-label="导入装配计算角色"></select></label></div><button data-parse>解析并检查</button><p role="status" data-status>只读取文本；导入时创建新装配。</p><div data-preview></div><button data-save disabled>导入为新装配</button></div>';
 document.body.append(dialog);dialog.showModal();let token=0,fit=null,pilots=[];
 const text=dialog.querySelector('textarea'),role=dialog.querySelector('select'),status=dialog.querySelector('[data-status]'),preview=dialog.querySelector('[data-preview]'),save=dialog.querySelector('[data-save]'),parse=dialog.querySelector('[data-parse]');
 dialog.querySelector('.flow-head button').onclick=()=>dialog.close();dialog.onclose=()=>{token++;dialog.remove()};
 const reset=()=>{token++;fit=null;save.disabled=true;preview.replaceChildren();status.textContent='文本或角色已变化，请重新解析。'};
 text.oninput=role.onchange=reset;
 dialog.querySelector('input').onchange=async e=>{const file=e.target.files[0];if(!file)return;if(file.size>400000){status.textContent='文件过大，请使用单份 EFT 装配文本';return;}text.value=await file.text();reset();};
 role.innerHTML='<option value="">无技能 · 基础对照</option>';
 try{pilots=await api('characters');if(dialog.open){role.innerHTML=pilots.map((p,i)=>'<option value="'+i+'">'+esc(p.name)+'</option>').join('');const all=pilots.findIndex(p=>p.id==='all5');role.value=String(all>=0?all:0)}}catch(e){status.textContent='角色列表不可用，使用无技能角色：'+e.message}
 parse.onclick=async()=>{const version=++token;fit=null;save.disabled=true;parse.disabled=true;status.textContent='正在解析并检查…';preview.replaceChildren();
  try{
   const result=await api('import/eft',{text:text.value});if(version!==token||!dialog.open)return;
   if(result.issues.length){preview.innerHTML='<ul>'+result.issues.map(i=>'<li>第 '+i.line+' 行：'+esc(i.message)+'<pre>'+esc(i.text)+'</pre></li>').join('')+'</ul>';status.textContent='未导入。请修正这些行，不会跳过无法识别的装备。';return;}
   const pilot=pilots[Number(role.value)];fit={...result.fit,skills:structuredClone(pilot?.skills||[]),characterName:pilot?.name||'无技能 · 基础对照'};
   const candidate=fit;let report,reason='';try{report=await calculate(candidate)}catch(e){reason=e.message}
   if(version!==token||!dialog.open)return;
   preview.innerHTML='<h3>'+esc(candidate.name)+'</h3><p>'+candidate.slots.filter(s=>s.item).length+' 件装备 · '+candidate.drones.reduce((n,d)=>n+d.quantity,0)+' 架无人机 · '+candidate.cargo.length+' 项货舱物品</p>'+result.notes.map(n=>'<p class="profile-note">'+esc(n)+'</p>').join('');
   if(reason||report?.issues?.length)preview.innerHTML+='<details open><summary>计算与校验提示</summary>'+[...(reason?[reason]:[]),...(report?.issues||[]).map(i=>i.message)].map(s=>'<p>'+esc(s)+'</p>').join('')+'</details>';
   status.textContent=report?.isValid?'解析完成，装配校验通过。':'解析完成，可导入为草稿；部分结果或装配条件尚未满足。';save.disabled=false;
  }catch(e){if(version===token)status.textContent=e.message}finally{parse.disabled=false}
 };
 save.onclick=async()=>{if(!fit)return;save.disabled=parse.disabled=true;text.disabled=role.disabled=true;status.textContent='正在保存新装配…';
  try{const record=await api('save',fit);onSaved(record);dialog.close()}catch(e){status.textContent='保存失败：'+e.message;save.disabled=parse.disabled=false;text.disabled=role.disabled=false}
 };text.focus();
}
