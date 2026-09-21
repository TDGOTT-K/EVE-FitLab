import {saveDownload} from './download-center.js';
import {renderNativeShareImage} from './native-share-image.js';
import {effectiveModuleState} from './module-state.js';
import {withoutScenario} from './scenario-presets.js';
import {OFFICIAL_SITE_URL,OFFICIAL_SITE_HOST} from './site-config.js';
import {t,getLocale,gameName,translateFor} from './i18n.js';
import {shareIconPaths,shareIconFor} from './share-art.js';
import {encodeFitCodes,qrCanvas} from './fit-image-code.js';
import {SLOT_METRIC_PRIORITY} from './slot-metrics.js';
import {mountEngineStats} from './engine-view.js';
const number=v=>Number.isFinite(v)?v.toLocaleString(getLocale(),{maximumFractionDigits:1}):'—';
const state=s=>s.state||(s.online===false?'Offline':'Online');
const stateName=s=>({Offline:'离线',Online:'在线',Active:'启动',Overload:'超载'})[state(s)]||state(s);
export function shareGroups(fit,byId,options){
 fit={...fit,slots:(fit.slots||[]).map(s=>({...s,state:effectiveModuleState(s,byId(s.item))}))};
 const groups=[];
 for(const [kind,label] of Object.entries({subsystem:'子系统',high:'高槽',mid:'中槽',low:'低槽',rig:'改装件'})){
  const rows=new Map();for(const s of fit.slots||[]){if(s.kind!==kind||!s.item)continue;const key=JSON.stringify([s.item,s.ammo,state(s)]);const old=rows.get(key);if(old)old.quantity++;else rows.set(key,{id:s.item,ammo:s.ammo,key:s.key,quantity:1,title:byId(s.item)?.name||String(s.item),note:(byId(s.item)?.canActivate?stateName(s):(state(s)==='Offline'?'离线':'被动'))+(s.ammo?' · '+(byId(s.ammo)?.name||s.ammo):'')});}
  if(rows.size)groups.push({title:label,rows:[...rows.values()]});
 }
 for(const kind of ['drones',...(options.cargo?['cargo']:[])]){const rows=new Map();for(const e of fit[kind]||[]){let r=rows.get(e.item);if(!r){r={id:e.item,title:byId(e.item)?.name||String(e.item),quantity:0,active:0};rows.set(e.item,r)}r.quantity+=e.quantity;r.active+=e.active||0;}if(rows.size)groups.push({title:kind==='drones'?'无人机':'货舱',rows:[...rows.values()].map(r=>({...r,note:kind==='drones'?'出动 '+r.active+' 架':''}))});}
 return groups;
}
function loadImage(url){return new Promise(resolve=>{const im=new Image();im.crossOrigin='anonymous';const timer=setTimeout(()=>resolve(null),8000);im.onload=()=>{clearTimeout(timer);resolve(im)};im.onerror=()=>{clearTimeout(timer);resolve(null)};im.src=url})}
export async function renderShareImage(fit,report,catalog,price,options){
 if(report.provider==='nengine')return renderNativeShareImage(fit,report,catalog,options,price);
 const imageLocale=options.language||getLocale(),T=value=>translateFor(imageLocale,value);
 const appLogo=await loadImage('assets/app-icon.png');
 await document.fonts.ready;
 const byId=id=>catalog.find(t=>t.id===Number(id)),ship=byId(fit.shipId),a=report.attributes,groups=shareGroups(fit,byId,options);
 const tier=ship.attrs[422]===3?'t3':ship.attrs[422]===2?'t2':ship.meta==='势力'?'faction':null;const badge=tier?await loadImage('assets/ship-corner-'+tier+'.png'):null;
 const ids=[...new Set(groups.flatMap(g=>g.rows.flatMap(r=>[r.id,...(r.ammo?[r.ammo]:[])])))];const assets=await Promise.all([loadImage(`https://images.evetech.net/types/${fit.shipId}/render?size=512`),...ids.map(id=>loadImage(`https://images.evetech.net/types/${id}/icon?size=64`))]);const icons=new Map(ids.map((id,i)=>[id,assets[i+1]]));
 const canvas=document.createElement('canvas');canvas.width=1080;const ctx=canvas.getContext('2d'),commands=[];let y=48;
 const font=(size,bold=false)=>`${bold?'600':'400'} ${size}px "Segoe UI", "Microsoft YaHei", "Microsoft JhengHei", "Yu Gothic UI", sans-serif`;
 function text(value,x,top,size=25,color='#dce9ee',bold=false,raw=false){value=raw?String(value):T(value);commands.push(()=>{ctx.font=font(size,bold);ctx.fillStyle=color;ctx.fillText(String(value),x,top)})}
 function wrap(value,x,top,width,size=25,color='#dce9ee',bold=false,raw=false){value=raw?String(value):T(value);ctx.font=font(size,bold);let line='',yy=top;for(const ch of String(value)){if(ch==='\n'||ctx.measureText(line+ch).width>width){text(line,x,yy,size,color,bold,true);yy+=size*1.5;line=ch==='\n'?'':ch}else line+=ch}text(line,x,yy,size,color,bold,true);return yy+size*1.5;}
 function rule(top){commands.push(()=>{ctx.fillStyle='#2b414e';ctx.fillRect(48,top,984,1)})}
 function box(x,top,w,h,color='#162732',stroke=null){commands.push(()=>{ctx.fillStyle=color;ctx.beginPath();ctx.roundRect(x,top,w,h,10);ctx.fill();if(stroke){ctx.strokeStyle=stroke;ctx.lineWidth=1;ctx.stroke()}})}
 function icon(kind,x,top,size=28,color='#86cbd0'){commands.push(()=>{ctx.save();ctx.translate(x,top);ctx.scale(size/24,size/24);ctx.strokeStyle=color;ctx.lineWidth=1.6;ctx.lineCap='round';ctx.lineJoin='round';ctx.stroke(new Path2D(shareIconPaths[kind]||shareIconPaths.slots));ctx.restore()})}
 function fitted(value,x,top,width,size=26,color='#e7f1f5',bold=false){value=T(value);ctx.font=font(size,bold);while(ctx.measureText(String(value)).width>width&&size>15){size--;ctx.font=font(size,bold)}text(value,x,top,size,color,bold,true)}
 let sectionIndex=0;
 function section(title){y+=28;rule(y);y+=24;icon(shareIconFor(title),48,y,30);text(title,94,y,28,'#a1dce2',true);text(String(++sectionIndex).padStart(2,'0'),978,y,24,'#567887');y+=58;}
 function pair(label,value,x,top,width=480){box(x,top,width,72);icon(shareIconFor(label),x+14,top+22,24,'#789faf');fitted(label,x+50,top+12,width-72,20,'#93b0bd');fitted(value,x+50,top+38,width-72,24,'#e6f0f5',true);}
 if(appLogo)commands.push(()=>ctx.drawImage(appLogo,48,40,46,46));text('EVE FITLAB',108,y,29,'#86cbd0',true);text(OFFICIAL_SITE_HOST,778,y,22,'#a1dce2',false,true);rule(92);y=126;
 y=wrap(fit.name||ship.name,48,y,612,42,'#f1f6f8',true,true)+12;
 y=wrap(ship.name+(ship.meta?' · '+ship.meta:''),48,y,612,26,'#86cbd0')+10;
 if(fit.tags?.length)y=wrap(fit.tags.join('  /  '),48,y,612,22,'#9cb2bf',false,true)+8;
 if(assets[0])commands.push(()=>ctx.drawImage(assets[0],702,114,330,330));if(badge)commands.push(()=>ctx.drawImage(badge,702,114,38,38));
 y=wrap(price?`参考估价  ${number(price.total)} ISK${price.missing?' · '+price.missing+' 种物品缺价':''}`:'参考估价暂不可用',48,y,612,24,'#e9b479')+8;
 y=wrap(options.pilot?(fit.characterName||'自定义技能'):T('已应用当前技能方案（隐藏驾驶员名称）'),48,y,612,21,'#9cb2bf',false,true);y=Math.max(y+14,466);
 const cap=report.capacitorAnalysis;const capText=cap?.status==='stable'?'稳定 '+number(cap.lowPercent)+'%':cap?.status==='depletes'?number(cap.seconds)+' s':cap?.status==='bounded'?'≥ '+number(cap.checkedSeconds/3600)+' h':'未判定';
 const layers=['shield','armor','structure'].map((key,i)=>({name:['护盾','装甲','结构'][i],hp:a[key+'Hitpoints'],res:['em','thermal','kinetic','explosive'].map(k=>a[key+'Resistances']?.[k+'Percent'])}));
 const ehp=layers.reduce((sum,l)=>sum+l.hp/(1-l.res.reduce((s,r)=>s+r,0)/4),0);
 section('性能概览');
 for(const [i,[label,value]] of [['理论 DPS',number(a.appliedDamagePerSecond)],['均匀来伤 EHP',number(ehp)],['电容',capText],['最大速度',number(a.maxVelocity)+' m/s']].entries()){const x=48+i*250;box(x,y,234,124,'#192e3b','#294452');icon(shareIconFor(label),x+16,y+16,28);fitted(label,x+54,y+19,164,19,'#9cb8c5');fitted(value,x+16,y+65,202,31,'#eaf7fa',true)}y+=146;
 y=wrap('基于下列装备状态、弹药和出动无人机计算；EHP 按四种伤害各 25%。',48,y,984,20,'#9cb2bf');
 for(const g of groups){section(g.title);for(const r of g.rows){
 const top=y,bgIndex=commands.length;const im=icons.get(r.id);box(64,top+16,76,76,'#0b1720');if(im)commands.push(()=>ctx.drawImage(im,70,top+22,64,64));else icon(shareIconFor(g.title),84,top+36,32);
 y=wrap(r.title,158,top+18,704,28,'#e9f1f5',true);box(908,top+16,108,40,'#23424f');fitted('× '+r.quantity,922,top+23,82,23,'#a1e2e4',true);
 const ammo=icons.get(r.ammo);if(ammo){const ay=y+2;commands.push(()=>ctx.drawImage(ammo,158,ay,26,26))}y=wrap(r.note||'已装载',ammo?194:158,y+5,ammo?690:730,21,'#94b5c5')+10;
 const slot=(fit.slots||[]).find(s=>s.key===r.key),sameRack=slot?fit.slots.filter(s=>s.item&&s.kind===slot.kind):[];const computed=slot?report.snapshot.modules.filter(m=>m.slotKind.toLowerCase()===slot.kind)[sameRack.findIndex(s=>s.key===slot.key)]:null;const type=byId(r.id);
 if(computed){const metrics=SLOT_METRIC_PRIORITY.filter(m=>m.available(type)).map(m=>{const value=m.value(computed,computed.state);return Number.isFinite(value)?{label:m.label,value:number(m.unit==='m'&&value>=1000?value/1000:value)+' '+(m.unit==='m'&&value>=1000?'km':m.unit)}:null}).filter(Boolean);
 for(let i=0;i<metrics.length;i++){const m=metrics[i],x=158+(i%3)*282,ty=y+Math.floor(i/3)*64;icon(shareIconFor(m.label),x,ty+5,20,'#698b9c');text(m.label,x+28,ty,18,'#789eaf');fitted(m.value,x+28,ty+25,242,22,'#d1e3ec',true)}y+=Math.ceil(metrics.length/3)*64;
 }
 y=Math.max(y+14,top+112);const height=y-top;commands.splice(bgIndex,0,()=>{ctx.fillStyle='#142530';ctx.beginPath();ctx.roundRect(48,top,984,height,10);ctx.fill();ctx.fillStyle='#315363';ctx.fillRect(48,top+16,3,height-32)});y+=12;
 }}
 section('防御与装配资源');
 for(const l of layers){icon(shareIconFor(l.name),48,y,28);text(l.name,92,y,25,'#f1f6f8',true);text(number(l.hp)+' HP',650,y,26,'#bbd7e4',true);y+=46;for(let i=0;i<4;i++){const x=48+i*246,top=y,colors=['#73b9e4','#de9272','#b4bbc5','#e7c263'];text(['电磁','热能','动能','爆炸'][i]+'  '+number(l.res[i]*100)+'%',x,top,22);commands.push(()=>{ctx.fillStyle='#233642';ctx.fillRect(x,top+32,214,6);ctx.fillStyle=colors[i];ctx.fillRect(x,top+32,214*Math.min(1,Math.max(0,l.res[i]||0)),6)})}y+=66;}
 y=wrap('CPU 剩余 '+number(a.cpuAvailable-a.cpuUsed)+' / '+number(a.cpuAvailable)+' tf     能量栅格剩余 '+number(a.powergridAvailable-a.powergridUsed)+' / '+number(a.powergridAvailable)+' MW',48,y,984,23);
 y=wrap('无人机机库 '+number(a.droneBayUsed)+' / '+number(a.droneBayAvailable)+' m³  ·  带宽 '+number(a.droneBandwidthUsed)+' / '+number(a.droneBandwidthAvailable)+' Mbit/s',48,y,984,23);
 if(options.details){section('详细计算结果');const root=document.createElement('div');mountEngineStats(root,report,ship,fit.characterName,'ehp',()=>{},catalog);let pending=[];
 const flush=()=>{for(let i=0;i<pending.length;i++)pair(pending[i][0],pending[i][1],48+(i%2)*504,y+Math.floor(i/2)*84);y+=Math.ceil(pending.length/2)*84;pending=[];};
 for(const row of root.querySelectorAll('.panel-title,.extended-stat-group>summary,.stat-row')){if(!row.classList.contains('stat-row')){flush();const title=row.firstChild?.textContent||row.textContent;y+=20;icon(shareIconFor(title),48,y,26);y=wrap(title,88,y,924,25,'#86cbd0',true)+14;continue;}const label=row.querySelector('span')?.textContent,value=row.querySelector('b')?.textContent;if(label&&value)pending.push([label,value]);}flush();}
 if(options.notes&&fit.notes){section('装配备注');y=wrap(fit.notes,48,y,984,25,'#dce9ee',false,true)+8;}
 section('装配导入码');
 const codes=await encodeFitCodes(fit,options);
 y=wrap('在 EVE FitLab 装配库选择“从图片导入”，即可还原此装配。',48,y,984,24);
 y=wrap('包含装备、弹药、状态、全部货舱、无人机与技能快照；外部装配关联需重新设置。',48,y,984,20,'#9cb2bf');
 y=wrap('请保留以下全部 '+codes.length+' 张装配码及四周白边。缩小过多或裁掉二维码会无法导入。',48,y,984,20,'#9cb2bf');
 for(let i=0;i<codes.length;i++){y+=24;box(140,y,800,54,'#23404d');icon('qr',160,y+12,28);text('装配码 '+(i+1)+' / '+codes.length,204,y+13,24,'#c4e8ee',true);text('请完整保留白色区域',665,y+17,18,'#94b5c5');y+=70;const qr=await qrCanvas(codes[i]),top=y;commands.push(()=>{ctx.imageSmoothingEnabled=false;ctx.drawImage(qr,140,top,800,800);ctx.imageSmoothingEnabled=true});y+=824;}
 section('官网下载');
 const downloadQr=await qrCanvas(OFFICIAL_SITE_URL),downloadTop=y;box(48,y,984,324,'#192e3b','#315363');commands.push(()=>{ctx.imageSmoothingEnabled=false;ctx.drawImage(downloadQr,64,downloadTop+16,292,292);ctx.imageSmoothingEnabled=true});
 text('EVE FITLAB',390,y+28,32,'#eaf7fa',true);text(OFFICIAL_SITE_HOST,390,y+86,38,'#86cbd0',true,true);
 wrap('扫码访问官网，获取软件与更新',390,y+156,610,24,'#c6dce5');wrap('下载二维码 · 非装配导入码',390,y+252,610,19,'#91aebb');y+=344;
 section('EVE FITLAB');y=wrap('EVE Online 舰船装配模拟器 · 构思、验证、分享你的装配',48,y,984,23,'#9cb2bf');y=wrap('生成于 '+new Date().toLocaleString(imageLocale)+' · SDE 3248221',48,y,984,19,'#8298a6');
 y=wrap(price?'ESI 参考估价 · 含船体、装备、满弹夹、无人机与货舱 · 非实时成交价':'市场价格暂不可用',48,y,984,19,'#8298a6');
 if(assets.some(im=>!im))y=wrap('部分图片未能加载，装备名称与数量已保留。',48,y,984,19,'#e9b479');
 if(Math.ceil(y+40)>30000)throw Error('内容过长，请关闭详细属性或备注后重试。');canvas.height=Math.ceil(y+40);ctx.fillStyle='#101b24';ctx.fillRect(0,0,1080,canvas.height);ctx.textBaseline='top';for(const command of commands)command();
 const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));if(!blob)throw Error('图片生成失败');return {blob,width:1080,height:canvas.height,missingImages:assets.filter(im=>!im).length};
}
export async function showShareImage(source,{calculate,catalog,getPrice}){
 const fit=withoutScenario(structuredClone(source)),dialog=document.createElement('dialog');dialog.className='share-dialog';dialog.innerHTML='<div class="flow-head"><b>生成装配长图</b><button aria-label="关闭图片预览">×</button></div><div class="share-options"><label>导出语言<select name="language" translate="no"><option value="zh-CN">简体中文</option><option value="zh-TW">繁體中文</option><option value="en">English</option><option value="ja">日本語</option><option value="de">Deutsch</option><option value="ru">Русский</option><option value="fr">Français</option></select></label><label><input type="checkbox" name="details" checked>完整属性</label><label><input type="checkbox" name="cargo" checked>货舱</label><label><input type="checkbox" name="notes" checked>备注</label><label><input type="checkbox" name="pilot" checked>驾驶员名称</label></div><p class="share-status" role="status">正在计算装配…</p><div class="share-preview"></div><div class="share-actions"><button class="share-download" disabled>保存 PNG</button><button class="share-copy" disabled>复制图片</button></div>';
 document.body.append(dialog);dialog.querySelector('[name=language]').value=getLocale();dialog.showModal();let url,blob,version=0;const status=dialog.querySelector('.share-status'),download=dialog.querySelector('.share-download'),copy=dialog.querySelector('.share-copy');dialog.querySelector('.flow-head button').onclick=()=>dialog.close();dialog.addEventListener('close',()=>{version++;if(url)URL.revokeObjectURL(url);dialog.remove()},{once:true});
 try{const [report,price]=await Promise.all([calculate(fit),getPrice(fit).catch(()=>null)]);if(!dialog.open)return;
 async function render(){const token=++version;download.disabled=copy.disabled=true;status.textContent='正在生成图片…';try{const options={...Object.fromEntries([...dialog.querySelectorAll('input')].map(e=>[e.name,e.checked])),language:dialog.querySelector('[name=language]').value};const result=await renderShareImage(fit,report,catalog,price,options);if(token!==version||!dialog.open)return;blob=result.blob;if(url)URL.revokeObjectURL(url);url=URL.createObjectURL(blob);const im=new Image();im.src=url;im.alt='装配分享长图预览';dialog.querySelector('.share-preview').replaceChildren(im);status.textContent=result.width+' × '+result.height+' · '+(blob.size/1024/1024).toFixed(2)+' MB'+(result.missingImages?' · 部分图标不可用':'');download.disabled=false;copy.disabled=!navigator.clipboard?.write||!window.ClipboardItem;}catch(e){if(token===version)status.textContent='生成失败：'+e.message;}}
 dialog.querySelectorAll('input,select').forEach(e=>e.onchange=render);download.onclick=()=>saveDownload(blob,(fit.name||'装配').replace(/[<>:"/\\|?*\x00-\x1f]/g,'_').slice(0,100)+'-EVE-FitLab.png');copy.onclick=async()=>{try{await navigator.clipboard.write([new ClipboardItem({'image/png':blob})]);status.textContent='图片已复制，可以粘贴到聊天窗口'}catch{status.textContent='浏览器不支持复制或未授予权限，请保存 PNG'}};await render();
 }catch(e){if(dialog.open)status.textContent='计算失败：'+e.message;}
}
