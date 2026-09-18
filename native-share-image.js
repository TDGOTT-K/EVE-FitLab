import {mountNativeStats} from './nengine-view.js';
import {encodeFitCodes,qrCanvas} from './fit-image-code.js';
import {implantCatalog,boosterCatalog} from './loadout-catalog.js';
import {effectiveModuleState} from './module-state.js';
import {OFFICIAL_SITE_HOST} from './site-config.js';
import {translateFor,getLocale} from './i18n.js';

const labels={high:'高槽',mid:'中槽',low:'低槽',rig:'改装件',subsystem:'子系统'};
const states={Online:'在线',Offline:'离线',Active:'启用',Overload:'超载'};
function picture(url){return new Promise(resolve=>{const im=new Image();im.crossOrigin='anonymous';const timer=setTimeout(()=>resolve(null),5000);im.onload=()=>{clearTimeout(timer);resolve(im)};im.onerror=()=>{clearTimeout(timer);resolve(null)};im.src=url})}

// Consume the same native presenter as the workspace. Only text/layout changes.
export function nativeShareStats(report,catalog,options){
 const root=document.createElement('div');mountNativeStats(root,report,{mode:options.mode||'hp',catalog});
 const rows=[];
 for(const el of root.querySelectorAll('.panel-title,.stat-row,.damage-cell,.native-output-item,.profile-note,.native-cap-window,.native-output-metric')){
  if(el.classList.contains('panel-title'))rows.push({heading:el.querySelector('span')?.textContent||el.firstChild.textContent,summary:el.querySelector('b')?.textContent||''});
  else if(el.matches('select'))rows.push({note:'输出口径：'+el.selectedOptions[0]?.textContent});
  else if(el.classList.contains('native-cap-window'))rows.push({note:'电容观察窗口：'+el.querySelector('select').selectedOptions[0]?.textContent});
  else if(el.classList.contains('profile-note'))rows.push({note:el.textContent});
  else if(options.details!==false){
   if(el.classList.contains('native-output-item'))rows.push({label:el.querySelector('span').textContent,value:el.querySelector('b').textContent+' · '+el.querySelector('small').textContent});
   else rows.push({label:el.querySelector('span')?.textContent||'',value:el.querySelector('b')?.textContent||''});
  }
 }
 return rows;
}

export async function renderNativeShareImage(fit,report,catalog,options){
 if(report.provider!=='nengine'||report.scenarioTarget||fit.activeScenarioId||Object.keys(fit.scenario||{}).length)throw Error('分享图片必须使用无情景原生装配结果');
 const language=options.language||getLocale(),T=text=>translateFor(language,String(text)),fmt=n=>Number.isFinite(n)?n.toLocaleString(language,{maximumFractionDigits:3}):'—';
 const types=new Map([...catalog,...implantCatalog,...boosterCatalog].map(t=>[t.id,t]));
 for(const entity of Object.values(report.native.fighterEntities||{})){
  const m=entity.abilityMetadata;if(m?.typeId&&!types.has(m.typeId))types.set(m.typeId,{id:m.typeId,name:m.name?.zh||m.name?.en});
 }
 const name=id=>types.get(id)?.name||'物品 #'+id;
 const config=[];
 const add=(section,title,detail='',id=null)=>config.push({section,title,detail,id});
 if(fit.tacticalModeTypeId)add('舰体模式',name(fit.tacticalModeTypeId),'类型 #'+fit.tacticalModeTypeId,fit.tacticalModeTypeId);
 for(const s of fit.slots||[]){if(!s.item)continue;
  const status=effectiveModuleState(s,types.get(s.item));
  let detail=states[status]+' · '+s.key;
  if(s.ammo)detail+=' · '+name(s.ammo)+((fit.crystals||[]).some(c=>c.moduleId===s.key&&c.typeId===s.ammo)?' · 晶体已装载':Object.hasOwn(s,'loadedCharges')?' · 已装 '+s.loadedCharges:' · 弹量未声明');
  if(s.mutation)detail+='\n变异声明：'+Object.entries(s.mutation.attributes).map(([id,v])=>'#'+id+' = '+fmt(v)).join('；');
  add(labels[s.kind]||s.kind,s.abyssalName||name(s.item),detail,s.item);
 }
 for(const d of fit.drones||[])add('无人机',name(d.item)+' × '+d.quantity,'出动 '+(d.active||0)+(d.mutation?' · 变异声明 '+Object.entries(d.mutation.attributes).map(([id,v])=>'#'+id+' = '+fmt(v)).join('；'):''),d.item);
 for(const location of ['tubes','reserve'])for(const [i,s] of (fit.fighterLoadout?.[location]||[]).entries())if(s){
  const id=s.id||'fighter-'+location+'-'+i,entity=report.native.fighterEntities?.[id];
  const fighterName=entity?.abilityMetadata?.name?.zh||entity?.abilityMetadata?.name?.en||name(s.typeId);
  add('舰载机',fighterName+' × '+s.quantity,(location==='reserve'?'备用':s.active===false?'未出动':'已出动')+' · '+id+'\n排除主武器 ID：'+(s.excludedAbilities?.join('、')||'无')+' · 计入附加武器 ID：'+(s.includedSecondaryAbilities?.join('、')||'无'),s.typeId);
 }
 const plan=fit.loadoutPlan;
 for(const i of plan?.implants||fit.implantPlan||[])add('脑插',name(i.typeId),'槽位 '+(i.slot??types.get(i.typeId)?.slot??'—'),i.typeId);
 for(const b of plan?.boosters||[]){const effects=types.get(b.typeId)?.sideEffects||[];add('增效剂',name(b.typeId),'槽位 '+b.slot+' · 生效副作用：'+(b.enabledSideEffects?.map(id=>effects.find(e=>e.id===id)?.name||'#'+id).join('、')||'无'),b.typeId)}
 for(const c of fit.crystals||[])if(c.moduleId||options.cargo)add('晶体',name(c.typeId),'损伤声明 '+fmt(c.damage)+' · '+(c.moduleId?'装入 '+c.moduleId:'备用')+' · '+c.id,c.typeId);
 if(options.cargo)for(const c of fit.cargo||[])add('货舱',name(c.item)+' × '+c.quantity,'',c.item);
 const ids=[...new Set([fit.shipId,...config.map(r=>r.id).filter(Boolean)])];
 const loaded=await Promise.all(ids.map(id=>picture('https://images.evetech.net/types/'+id+'/icon?size=64'))),images=new Map(ids.map((id,i)=>[id,loaded[i]]));
 const canvas=document.createElement('canvas');canvas.width=1080;const ctx=canvas.getContext('2d'),commands=[],textLines=[];let y=38;
 const font=(size,bold=false)=>(bold?'600 ':'400 ')+size+'px "Segoe UI", "Microsoft YaHei", sans-serif';
 function line(text,{x=48,width=984,size=23,color='#dce9ee',bold=false,raw=false}={}){
  text=raw?String(text):T(text);textLines.push(text);ctx.font=font(size,bold);let part='';
  const draw=()=>{const value=part,top=y;commands.push(()=>{ctx.font=font(size,bold);ctx.fillStyle=color;ctx.fillText(value,x,top)});y+=size*1.45;part=''};
  for(const ch of text){if(ch==='\n'){draw();continue}if(ctx.measureText(part+ch).width>width)draw();part+=ch}draw();
 }
 function heading(text,summary=''){
  y+=22;const top=y;commands.push(()=>{ctx.fillStyle='#28424e';ctx.fillRect(48,top,984,2)});y+=22;
  line(text+(summary?' · '+summary:''),{size:29,bold:true,color:'#94dce1'});y+=8;
 }
 function pair(label,value){
  const top=y,index=commands.length;y+=10;line(label,{x:64,width:358,size:21,color:'#9eb7c4'});const left=y;y=top+10;
  line(value,{x:450,width:566,size:23,bold:true});y=Math.max(y,left)+10;const height=y-top;
  commands.splice(index,0,()=>{ctx.fillStyle='#172b36';ctx.beginPath();ctx.roundRect(48,top,984,height,7);ctx.fill()});y+=6;
 }
 line('EVE FITLAB · '+OFFICIAL_SITE_HOST,{size:26,bold:true,color:'#94dce1'});y+=16;
 const shipTop=y;if(images.get(fit.shipId))commands.push(()=>ctx.drawImage(images.get(fit.shipId),904,shipTop,112,112));
 line(fit.name||name(fit.shipId),{size:39,bold:true,raw:true,width:812});line(name(fit.shipId),{size:27,color:'#94dce1',width:812});
 line(options.pilot?(fit.characterName||'自定义技能'):'已应用技能快照 · 隐藏驾驶员名称',{size:20,color:'#91aebb',raw:!!options.pilot});
 line('无情景装配 · DPS 不扣目标抗性 · '+(fit.outputMetric==='loadedCycleDps'?'有限弹量周期':'名义周期'),{size:20,color:'#91aebb'});
 line('N '+report.engineVersion+' · SDE '+report.source.buildNumber+' · 本地接口 r'+report.source.revision,{size:19,color:'#91aebb'});
 if(!report.isValid)line('装配存在校验或机制覆盖问题，以下保留可用分项；不代表装配已合法。',{size:21,color:'#edaf83'});
 heading('装配配置');let previous='';
 for(const row of config){
  if(row.section!==previous){line(row.section,{size:24,bold:true,color:'#94dce1'});previous=row.section}
  const top=y,index=commands.length,im=images.get(row.id);y+=12;if(im)commands.push(()=>ctx.drawImage(im,62,top+12,54,54));
  line(row.title,{x:134,width:880,size:24,raw:true});if(row.detail)line(row.detail,{x:134,width:880,size:18,color:'#9eb7c4'});y=Math.max(y+12,top+78);const height=y-top;
  commands.splice(index,0,()=>{ctx.fillStyle='#142530';ctx.beginPath();ctx.roundRect(48,top,984,height,8);ctx.fill()});y+=12;
 }
 for(const row of nativeShareStats(report,catalog,{details:options.details,mode:fit.defenseMode||'hp'})){
  if(row.heading)heading(row.heading,row.summary);else if(row.note)line(row.note,{size:18,color:'#9eb7c4'});else pair(row.label,row.value);
 }
 heading('装配资源');for(const r of report.native.resources){const resource={cpu:['CPU','tf'],powergrid:['能量栅格','MW'],droneBay:['无人机机库','m³'],droneBandwidth:['无人机带宽','Mbit/s'],calibration:['校准','']}[r.id]||[r.id,''];pair(resource[0],'已用 '+fmt(r.used)+' / '+fmt(r.capacity)+' '+resource[1]+' · 剩余 '+fmt(r.remaining));}
 if(!report.native.resources.length)line('资源查询不可用',{color:'#edaf83'});
 if(options.notes&&fit.notes){heading('备注');line(fit.notes,{raw:true})}
 heading('装配导入码');line('保存全部二维码可恢复装配输入。情景和本地关联不包含在图片中。',{size:20});
 line('二维码始终包含全部库存与技能快照；上方货舱开关仅控制可见清单。',{size:18,color:'#9eb7c4'});
 const codes=await encodeFitCodes(fit,options);
 for(const [i,code] of codes.entries()){
  y+=16;line('装配码 '+(i+1)+' / '+codes.length,{bold:true});const qr=await qrCanvas(code),top=y;
  commands.push(()=>{ctx.imageSmoothingEnabled=false;ctx.drawImage(qr,140,top,800,800);ctx.imageSmoothingEnabled=true});y+=830;
 }
 line('生成于 '+new Date().toLocaleString(language)+' · '+OFFICIAL_SITE_HOST,{size:18,color:'#91aebb'});
 if(loaded.some(im=>!im))line('部分图标未加载，物品名称和导入数据保留。',{size:18,color:'#edaf83'});
 if(y+40>30000)throw Error('长图内容过多，请关闭完整属性或备注；输入数据不会被截断');
 canvas.height=Math.ceil(y+40);ctx.fillStyle='#101b24';ctx.fillRect(0,0,1080,canvas.height);ctx.textBaseline='top';for(const draw of commands)draw();
 const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/png'));if(!blob)throw Error('图片生成失败');
 return {blob,width:canvas.width,height:canvas.height,missingImages:loaded.filter(im=>!im).length,textLines};
}
