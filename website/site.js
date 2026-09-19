const imageDialog=document.querySelector('#image-dialog'),sponsorDialog=document.querySelector('#sponsor-dialog');
document.querySelector('#preview')?.addEventListener('click',()=>imageDialog.showModal());
document.querySelector('#sponsor').onclick=()=>sponsorDialog.showModal();
for(const dialog of document.querySelectorAll('dialog')){dialog.querySelector('.close').onclick=()=>dialog.close();dialog.addEventListener('click',event=>{if(event.target===dialog){const r=dialog.getBoundingClientRect();if(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom)dialog.close();}});}
let toastTimer;
function showCopyStatus(message){
 const toast=document.querySelector('#toast');toast.textContent=message;toast.classList.add('visible');
 clearTimeout(toastTimer);toastTimer=setTimeout(()=>toast.classList.remove('visible'),4500);
}
function legacyCopy(text){
 const focus=document.activeElement,area=document.createElement('textarea');
 area.value=text;area.readOnly=true;area.setAttribute('aria-label',window.siteT('复制内容'));
 area.style.cssText='position:fixed;left:0;top:0;width:1px;height:1px;opacity:0;pointer-events:none';
 (document.querySelector('dialog[open]')||document.body).append(area);
 let success=false;
 try{area.focus({preventScroll:true});area.select();area.setSelectionRange(0,area.value.length);success=document.execCommand('copy')===true;}catch{}
 finally{area.remove();focus?.focus({preventScroll:true});}
 return success;
}
function manualCopy(text){
 document.querySelector('#manual-copy-dialog')?.remove();
 const dialog=document.createElement('dialog');dialog.id='manual-copy-dialog';
 const title=document.createElement('h2');title.id='manual-copy-title';title.textContent=window.siteT('请手动复制');dialog.setAttribute('aria-labelledby',title.id);
 const message=document.createElement('p');message.textContent=window.siteT('浏览器未允许自动复制。请复制下面的内容，或选中后按 Ctrl+C；手机可长按复制。');
 const input=document.createElement('input');input.value=text;input.readOnly=true;input.setAttribute('aria-label',window.siteT('复制内容'));
 const close=document.createElement('button');close.className='primary';close.textContent=window.siteT('关闭');close.onclick=()=>dialog.close();
 dialog.append(title,message,input,close);document.body.append(dialog);dialog.showModal();input.focus();input.select();
 dialog.addEventListener('close',()=>dialog.remove(),{once:true});
}
async function copy(text){
 clearTimeout(toastTimer);document.querySelector('#toast').classList.remove('visible');
 // Run the synchronous fallback during the click gesture, without requiring Clipboard API permission.
 let success=legacyCopy(text);
 if(!success&&navigator.clipboard?.writeText){try{await navigator.clipboard.writeText(text);success=true;}catch{}}
 if(success)showCopyStatus(window.siteT('已复制')+' · '+text);
 else manualCopy(text);
 return success;
}
document.querySelector('#copy-qq')?.addEventListener('click',()=>copy('2377951590'));document.querySelector('#copy-address').onclick=()=>copy('TYDiRLFWukWdHpiZKQdGLoX7ivH2PFtPBS');
document.querySelectorAll('.copy-group').forEach(button=>button.addEventListener('click',()=>copy('704164562')));

document.querySelectorAll('.copy-discord').forEach(button=>button.addEventListener('click',()=>copy('https://discord.gg/WY8DG8Tqj5')));
