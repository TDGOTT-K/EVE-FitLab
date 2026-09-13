from pathlib import Path
p=Path('app.js');s=p.read_text(encoding='utf-8');s="import {requestCache} from './request-cache.js';\n"+s
# initialize before any boot-time requests
pos=s.index('const treeOpen=');s=s[:pos]+'''const cachedItem=requestCache(id=>api('items/'+id),128);
const cachedCalculation=requestCache(fit=>api('analyze',fit),4);
function getCalculation(fit){return cachedCalculation(JSON.stringify({shipId:fit.shipId,slots:fit.slots,skills:fit.skills}),fit)}
'''+s[pos:]
s=s.replace("const result=await api('analyze',currentFit());", "const result=await getCalculation(currentFit());")
a=s.index(' try{const [data,calculation]=',s.index('async function showInfo'));b=s.index('\nfunction clampInfo()',a)
s=s[:a]+''' const calculationRequest=selected?getCalculation(captured):Promise.resolve(null);
 // Attach rejection handling immediately, even while item metadata is in flight.
 const settledCalculation=calculationRequest.then(value=>({value}),error=>({error}));
 try{const data=await cachedItem(t.id,t.id);if(request!==infoRequest||panel.hidden)return;
 renderItemInfo($('#info-content'),t,data,null,catalog,'');clampInfo();
 if(!selected){say('已读取物品详情');return}
 const status=document.createElement('p');status.className='profile-note';status.textContent='装配参数计算中 · 可先查看基础属性';$('#info-content').append(status);
 let touched=false;const remember=()=>{touched=true};$('#info-content').querySelector('.info-tabs').addEventListener('click',remember,{once:true});
 const outcome=await settledCalculation;if(request!==infoRequest||panel.hidden)return;
 if(outcome.error){status.textContent='装配参数暂不可用：'+outcome.error.message;return}
 const calculation=outcome.value,group=selected.kind==='rig'?calculation.snapshot.rigs:calculation.snapshot.modules;
 const siblings=captured.slots.filter(s=>s.kind===selected.kind&&s.item===selected.item),index=siblings.findIndex(s=>s.key===selected.key);
 let computed=group.filter(m=>m.dogmaTypeId===selected.item)[index];if(t.kind==='ammo')computed=computed?.charge||null;
 const selectedTab=touched?$('#info-content [aria-pressed="true"]')?.dataset.infoTab:null,scroll=panel.scrollTop;
 renderItemInfo($('#info-content'),t,data,computed,catalog,`${captured.characterName||'无技能'} · ${stateLabels[moduleState(selected)]||'被动'}`);
 if(selectedTab)$('#info-content').querySelector(`[data-info-tab="${selectedTab}"]`)?.click();clampInfo();if(touched)panel.scrollTop=scroll;say('已读取物品详情');
 }catch(e){if(request===infoRequest&&!panel.hidden)$('#info-content').innerHTML=`<p class="notice">${esc(e.message)}</p>`}
}
'''+s[b:];p.write_text(s,encoding='utf-8')
