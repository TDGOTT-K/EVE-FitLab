import {getLocale,translateFor,gameName} from './i18n.js';
import {qrCanvas,scanFitImage} from './fit-image-code.js';
import {planShareText,parsePlanText,encodePlanCodes,decodePlanCodes,parsePlanCode} from './plan-share-code.js';
const filename=name=>(name||'脑插方案').replace(/[<>:"/\\|?*\x00-\x1f]/g,'_').slice(0,80);
function dialog(title,body){const d=document.createElement('dialog');d.className='share-dialog plan-share-dialog';d.innerHTML='<div class="flow-head"><b></b><button type="button" aria-label="关闭分享窗口">×</button></div>'+body;d.querySelector('b').textContent=title;d.querySelector('button').onclick=()=>d.close();d.addEventListener('close',()=>d.remove(),{once:true});document.body.append(d);d.showModal();return d}
function download(blob,name){const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),10000)}
const number=x=>(x>0?'+':'')+x.toLocaleString(getLocale(),{maximumFractionDigits:3});
const affixValue=r=>r.state!=='available'?'—':[r.percent?number(r.percent)+'%':'',r.additive?number(r.additive)+(r.unit?' '+r.unit:''):''].filter(Boolean).join(' · ')||'0';
function icon(id){return new Promise(resolve=>{const im=new Image();im.crossOrigin='anonymous';let done=false;const end=value=>{if(done)return;done=true;clearTimeout(timer);resolve(value)};const timer=setTimeout(()=>end(null),5000);im.onload=()=>end(im);im.onerror=()=>end(null);im.src='https://images.evetech.net/types/'+id+'/icon?size=64'})}
export async function renderPlanShareImage(data,find){
 const plan=data.document.plan,codes=await encodePlanCodes(data.document),qrs=await Promise.all(codes.map(qrCanvas));
 const entries=[...plan.implants.map(i=>({...i,kind:'implants'})),...plan.boosters.map(i=>({...i,kind:'boosters'}))];
 const pictures=await Promise.all(entries.map(i=>icon(i.typeId)));
 const canvas=document.createElement('canvas');canvas.width=Math.max(1000,...qrs.map(q=>q.width+84));const ctx=canvas.getContext('2d'),commands=[];let y=34;
 function text(value,x,top,size=23,color='#dcebf1',max=900,raw=false){value=raw?String(value):translateFor(getLocale(),String(value));commands.push(()=>{ctx.font=size+'px "Microsoft YaHei",sans-serif';ctx.fillStyle=color;let s=String(value);while(s.length&&ctx.measureText(s).width>max)s=s.slice(0,-1);if(s!==String(value))s=s.slice(0,-1)+'…';ctx.fillText(s,x,top)})}
 function section(name){y+=22;text(name,42,y,25,'#91d7db');y+=44}
 text('EVE FITLAB · 脑插与增效剂方案',42,y,26,'#91d7db');y+=55;text(plan.name,42,y,34,'#dcebf1',900,true);y+=54;
 text('技能快照 '+(plan.pilot?.skills.length||0)+' 项 · SDE '+data.document.source.buildNumber,42,y,19,'#99aeb9');y+=36;
 text('保留当前副作用选择 · 分享码可还原完整方案',42,y,19,'#99aeb9');y+=25;
 for(const kind of ['implants','boosters']){
  section(kind==='implants'?'脑插':'增效剂');
  const rows=entries.map((e,i)=>({e,i})).filter(x=>x.e.kind===kind);
  if(!rows.length){text('未安装',42,y,22,'#778e9b');y+=38}
  for(const {e,i} of rows){const top=y,t=find(kind,e.typeId);commands.push(()=>{ctx.fillStyle='#172a35';ctx.fillRect(36,top-8,928,90);if(pictures[i])ctx.drawImage(pictures[i],48,top+4,56,56)});text(String(e.slot).padStart(2,'0')+'  '+gameName(t||e.typeId),120,y+3,24,'#dcebf1',818);text('Type ID '+e.typeId,120,y+40,17,'#7d9aa9',240);
   if(kind==='boosters'){const names=(e.enabledSideEffects||[]).map(id=>t?.sideEffects?.find(s=>s.id===id)?.name||String(id));text(names.length?'副作用：'+names.join('、'):'无副作用生效',350,y+40,17,names.length?'#e0a188':'#8db6b8',580)}y+=100}
 }
 section('方案加成');
 for(const r of data.summary.items){text(r.name+' · '+r.scope,42,y,21,'#c0d4df',730);text(affixValue(r),790,y,22,r.direction==='penalty'?'#e8a187':'#91d7db',170);y+=37}
 if(!data.summary.items.length){text(data.summary.complete?'暂无加成':'加成暂不可用',42,y,22);y+=37}
 if(!data.summary.complete){text('部分效果未计入，请导入软件查看原因',42,y,20,'#e8a187');y+=37}
 if(!data.analysis.pilotPrerequisitesSatisfied){text('当前计算角色条件未满足；以上为方案词条，不代表装配准入',42,y,18,'#e8a187');y+=32}
 section('方案导入码');
 text('方案库 → 导入分享方案；请保留全部二维码和白边',42,y,21);y+=45;
 for(let i=0;i<qrs.length;i++){const qr=qrs[i],left=(canvas.width-qr.width)/2;text('方案码 '+(i+1)+' / '+qrs.length,left,y,22);y+=38;const top=y;commands.push(()=>{ctx.imageSmoothingEnabled=false;ctx.drawImage(qr,left,top)});y+=qr.height+26}
 text('imfishman.com · '+data.document.source.engineVersion,42,y,18,'#819aa8');y+=42;
 if(y>30000)throw Error('分享图过长，请改用文字分享');
 canvas.height=y;ctx.fillStyle='#101d26';ctx.fillRect(0,0,canvas.width,canvas.height);ctx.textBaseline='top';for(const command of commands)command();
 const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));if(!blob)throw Error('图片生成失败');
 return {blob,width:canvas.width,height:canvas.height,missingImages:pictures.filter(i=>!i).length};
}
export async function showPlanShare(plan,{api,find,image=false}){
 const source=structuredClone(plan),d=dialog(image?'分享方案图片':'分享方案文字','<p role="status" class="share-status">正在核对方案…</p><div class="plan-share-body"></div><div class="share-actions"></div>');
 const status=d.querySelector('[role=status]'),body=d.querySelector('.plan-share-body'),actions=d.querySelector('.share-actions');let url;
 d.addEventListener('close',()=>{if(url)URL.revokeObjectURL(url)},{once:true});
 try{
  const data=await api('plan-share/export',{plan:source});if(!d.open)return;
  const save=document.createElement('button'),copy=document.createElement('button');save.type=copy.type='button';actions.append(copy,save);
  if(!image){
   const text=planShareText(data.document),area=document.createElement('textarea');area.readOnly=true;area.value=text;area.setAttribute('aria-label','方案分享文本');body.append(area);
   copy.textContent='复制文字';save.textContent='保存文本';
   copy.onclick=async()=>{try{await navigator.clipboard.writeText(text);status.textContent='分享文字已复制'}catch{area.focus();area.select();status.textContent='请按 Ctrl+C 复制选中的分享文字'}};
   save.onclick=()=>download(new Blob([text],{type:'text/plain;charset=utf-8'}),filename(source.name)+'.fitlab-plan.txt');
   status.textContent='自定义 FITLAB-PLAN/1 格式 · 包含技能快照，不含账号身份和本地分组';
  }else{
   status.textContent='正在生成方案图片…';copy.disabled=save.disabled=true;
   const result=await renderPlanShareImage(data,find);if(!d.open)return;url=URL.createObjectURL(result.blob);
   const im=new Image();im.src=url;im.alt='脑插与增效剂方案分享图';body.append(im);save.textContent='保存 PNG';copy.textContent='复制图片';save.disabled=false;copy.disabled=!navigator.clipboard?.write||!window.ClipboardItem;
   save.onclick=()=>download(result.blob,filename(source.name)+'-FitLab方案.png');
   copy.onclick=async()=>{try{await navigator.clipboard.write([new ClipboardItem({'image/png':result.blob})]);status.textContent='图片已复制'}catch{status.textContent='图片复制不可用，请保存 PNG'}};
   status.textContent=result.width+' × '+result.height+(result.missingImages?' · 部分图标未加载，物品名称与分享码完整':' · 含可导入方案码');
  }
 }catch(e){if(d.open)status.textContent=e.message}
}
export function showPlanImport({api,onImported}){
 const d=dialog('导入分享方案','<div class="plan-share-body"><textarea aria-label="粘贴方案分享文本" placeholder="粘贴 FITLAB-PLAN/1 分享文字"></textarea><input type="file" multiple accept=".txt,.json,.png,.jpg,.jpeg,.webp" aria-label="选择方案文本或分享图片"><p>也可拖入图片或粘贴截图；多张方案码可分批补充。</p><button type="button" data-check>校验文本</button><button type="button" data-clear>清空</button><p role="status">等待导入</p><button type="button" data-import disabled>导入为新方案</button></div>');
 const area=d.querySelector('textarea'),status=d.querySelector('[role=status]'),save=d.querySelector('[data-import]');let plan=null,version=0,codes=new Set();
 const invalidate=()=>{plan=null;save.disabled=true;return ++version};
 async function verify(document,token){const result=await api('plan-share/import',{document});if(token!==version||!d.open)return;plan=result.plan;status.textContent=plan.name+' · '+plan.implants.length+' 个脑插 / '+plan.boosters.length+' 个增效剂'+(!result.summary.complete?' · 部分效果不可计算':'')+'。将保存为最外层新方案。';save.disabled=false}
 d.querySelector('[data-check]').onclick=async()=>{const token=invalidate();try{await verify(parsePlanText(area.value),token)}catch(e){if(token===version)status.textContent=e.message}};
 area.oninput=invalidate;
 async function files(list){const token=invalidate(),collected=new Set(codes);if(list.length>24){status.textContent='每次最多24张图片';return}status.textContent='正在读取…';try{for(const file of list){if(file.size>25*1024*1024)throw Error('文件超过25MB');if(file.type.startsWith('image/')){const scanned=await scanFitImage(file,parsePlanCode);if(token!==version||!d.open)return;for(const code of scanned)collected.add(code)}else{if(file.size>250000)throw Error('文本过大');const text=await file.text();if(token!==version||!d.open)return;area.value=text;await verify(parsePlanText(text),token);return}if(token!==version||!d.open)return}codes=collected;await verify(await decodePlanCodes([...codes]),token)}catch(e){if(token===version)status.textContent=e.message}}
 d.querySelector('input').onchange=e=>files([...e.target.files]);d.ondragover=e=>e.preventDefault();d.ondrop=e=>{e.preventDefault();files([...e.dataTransfer.files])};d.onpaste=e=>{if(e.clipboardData.files.length){e.preventDefault();files([...e.clipboardData.files])}};
 d.querySelector('[data-clear]').onclick=()=>{invalidate();codes.clear();area.value='';d.querySelector('input').value='';status.textContent='已清空'};
 save.onclick=async()=>{if(!plan)return;save.disabled=true;try{const saved=await api('loadout-plan',plan);onImported(saved);d.close()}catch(e){status.textContent=e.message;save.disabled=false}};
 d.addEventListener('close',()=>version++,{once:true});
}
