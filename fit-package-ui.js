import {analysisStatusText} from './analysis-status.js';

export async function exportFitPackage(api,fit){
 const document=await api('fit-package/export',{fit});
 const url=URL.createObjectURL(new Blob([JSON.stringify(document,null,2)],{type:'application/json'}));
 const link=globalThis.document.createElement('a');link.href=url;link.download=(fit.name||'装配').replace(/[<>:"/\\|?*]/g,'_')+'.fitlab.json';
 globalThis.document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),10000);
}

export function importFitPackage(api,onSaved){
 const dialog=document.createElement('dialog');dialog.className='share-dialog';
 dialog.innerHTML='<div class="flow-head"><b>导入完整装配文件</b><button aria-label="关闭装配文件导入">×</button></div><div class="image-import-body"><p>选择 .fitlab.json 文件。保留技能、实例、库存、情景及报价快照；验证后创建新装配。</p><input type="file" accept=".json,application/json" aria-label="选择完整装配文件"><p role="status">等待选择文件。</p><button data-save disabled>导入为新装配</button></div>';
 document.body.append(dialog);dialog.showModal();
 const input=dialog.querySelector('input'),status=dialog.querySelector('[role=status]'),save=dialog.querySelector('[data-save]');
 let fit=null,token=0;
 dialog.querySelector('.flow-head button').onclick=()=>dialog.close();dialog.onclose=()=>{token++;dialog.remove()};
 input.onchange=async()=>{
  const selected=++token;fit=null;save.disabled=true;const file=input.files[0];if(!file)return;
  if(file.size>1800000){status.textContent='文件过大，未读取';return;}
  status.textContent='正在核对版本并重放计算…';
  try{
   const result=await api('fit-package/import',{document:JSON.parse(await file.text())});
   if(selected!==token||!dialog.open)return;
   fit=result.fit;fit._saveRequestId=crypto.randomUUID();
   status.textContent=fit.name+' · '+analysisStatusText(result.report)+'。原生输入和结果核对一致；情景与报价使用文件内快照。';save.disabled=false;
  }catch(error){if(selected===token&&dialog.open)status.textContent=error.message;}
 };
 save.onclick=async()=>{
  if(!fit)return;save.disabled=input.disabled=true;status.textContent='正在保存新装配…';
  try{const saved=await api('save',fit);onSaved(saved);dialog.close();}
  catch(error){status.textContent=error.message;save.disabled=input.disabled=false;}
 };
}
