import {saveDownload} from './download-center.js';
export async function showFitTextExport(api,fit){
 const dialog=document.createElement('dialog');dialog.className='share-dialog';dialog.innerHTML='<div class="flow-head"><b>导出装配文本</b><button data-close aria-label="关闭文本导出">×</button></div><div class="image-import-body"><p>EVE 标准装配文本（EFT）。复制后可粘贴到游戏装配窗口导入，物品使用标准英文名。</p><textarea readonly aria-label="装配导出文本" rows="14" spellcheck="false"></textarea><div><button data-copy disabled>复制文本</button><button data-download disabled>下载文件</button></div><p role="status">正在生成…</p></div>';document.body.append(dialog);dialog.querySelector('[data-close]').onclick=()=>dialog.close();dialog.onclose=()=>dialog.remove();dialog.showModal();
 const status=dialog.querySelector('[role=status]');
 try{const data=await api('export/eft',{fit}),text=data.text;if(!dialog.isConnected)return;dialog.querySelector('textarea').value=text;status.textContent=data.notes.join(' ');for(const b of dialog.querySelectorAll('[data-copy],[data-download]'))b.disabled=false;
 dialog.querySelector('[data-copy]').onclick=async()=>{try{await navigator.clipboard.writeText(text);status.textContent='已复制'}catch{dialog.querySelector('textarea').select();status.textContent='请按 Ctrl+C 复制'}};
 dialog.querySelector('[data-download]').onclick=()=>saveDownload(new Blob([text],{type:'text/plain;charset=utf-8'}),(fit.name||'装配').replace(/[<>:"/\\|?*]/g,'_')+'.txt');
 }catch(error){if(dialog.isConnected)status.textContent=error.message}
}
