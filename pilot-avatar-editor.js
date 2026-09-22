import {builtinAvatars,avatarMarkup,savePilotAvatar} from './pilot-portrait.js';
export function editPilotAvatar(character,onChange){
 const dialog=document.createElement('dialog');dialog.className='pilot-avatar-dialog';
 dialog.innerHTML='<form method="dialog"><div class="flow-head"><b>选择角色头像</b><button aria-label="关闭">×</button></div></form><div class="avatar-grid"></div><div class="avatar-actions"><label class="avatar-upload">上传图片<input type="file" accept="image/png,image/jpeg,image/webp,image/gif" hidden></label><button type="button" data-reset>恢复默认</button></div><p class="avatar-note">头像保存在本机。图片会裁切为正方形。</p><p role="status"></p>';
 const status=dialog.querySelector('[role=status]');let closed=false;
 const choose=value=>{if(closed)return;try{savePilotAvatar(character,value);onChange();dialog.close();}catch{status.textContent='头像保存失败，请检查本机存储空间。';}};
 for(const avatar of builtinAvatars){const b=document.createElement('button');b.type='button';b.title=avatar.name;b.setAttribute('aria-label',avatar.name);b.innerHTML=avatarMarkup(avatar.id)+'<span>'+avatar.name+'</span>';b.onclick=()=>choose(avatar.id);dialog.querySelector('.avatar-grid').append(b);}
 dialog.querySelector('[data-reset]').onclick=()=>choose(null);
 dialog.querySelector('input').onchange=async e=>{
  const file=e.target.files[0];if(!file)return;
  if(file.size>10*1024*1024){status.textContent='请选择不超过 10 MB 的图片。';return;}
  status.textContent='正在处理图片…';
  try{const bitmap=await createImageBitmap(file);const canvas=document.createElement('canvas');canvas.width=canvas.height=256;const size=Math.min(bitmap.width,bitmap.height);canvas.getContext('2d').drawImage(bitmap,(bitmap.width-size)/2,(bitmap.height-size)/2,size,size,0,0,256,256);bitmap.close();choose(canvas.toDataURL('image/png'));}catch{status.textContent='无法读取这张图片，请选择 PNG、JPEG、WebP 或 GIF 图片。';}
 };
 dialog.onclose=()=>{closed=true;dialog.remove();};document.body.append(dialog);dialog.showModal();
}
