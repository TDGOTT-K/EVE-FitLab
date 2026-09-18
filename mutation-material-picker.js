import {escapeHtml as esc} from './scenario-display.js';
const descriptions=new Map();
let priceRequest,priceRequestedAt=0;
const icon=id=>'https://images.evetech.net/types/'+id+'/icon?size=64';
function plainDescription(value){
 const doc=new DOMParser().parseFromString(String(value||'').replace(/<br\s*\/?\s*>/gi,'\n'),'text/html');
 doc.querySelectorAll('script,style').forEach(n=>n.remove());
 return doc.body.textContent.trim();
}
export function mountMutationMaterialPicker(host,{options,value,api,onChange}){
 let selected=options.find(o=>o.id===Number(value))||options[0],hovered=null,revision=0;
 const uid='mutation-material-'+crypto.randomUUID();
 host.innerHTML='<button type="button" class="mutation-material-trigger" aria-haspopup="listbox" aria-expanded="false" aria-controls="'+uid+'" aria-label="选择突变质体"></button>';
 const trigger=host.firstElementChild,popup=document.createElement('div');
 popup.id=uid;popup.className='mutation-material-popup';popup.setAttribute('popover','auto');
 popup.innerHTML='<div class="mutation-material-options" role="listbox" aria-label="突变质体"></div><aside class="mutation-material-description" role="tooltip" id="'+uid+'-description" aria-live="polite"></aside>';
 host.append(popup);
 const list=popup.firstElementChild,detail=popup.lastElementChild;
 function paint(){
  trigger.innerHTML=selected?'<img class="mutation-plasmid-icon" src="'+icon(selected.id)+'" alt="" draggable="false"><span><small>突变质体</small><b>'+esc(selected.name)+'</b></span><span class="mutation-material-chevron">⌄</span>':'暂无适用材料';
  list.innerHTML=options.map(o=>'<button type="button" role="option" aria-selected="'+(o.id===selected?.id)+'" tabindex="-1" data-material="'+o.id+'"><img src="'+icon(o.id)+'" alt="" draggable="false"><span>'+esc(o.name)+'</span><i>'+(o.id===selected?.id?'✓':'')+'</i></button>').join('');
  list.querySelectorAll('button').forEach((button,index)=>{
   const item=options[index];
   button.onpointerenter=()=>showDetails(item,button);
   button.onfocus=()=>showDetails(item,button);
   button.onclick=()=>{const changed=selected?.id!==item.id;selected=item;close();paint();trigger.focus();if(changed)onChange(item.id)};
   button.onkeydown=e=>{let next;if(e.key==='ArrowDown')next=(index+1)%options.length;else if(e.key==='ArrowUp')next=(index+options.length-1)%options.length;else if(e.key==='Home')next=0;else if(e.key==='End')next=options.length-1;if(next!==undefined){e.preventDefault();list.children[next].focus()}};
  });
 }
 async function showDetails(item,button){
  hovered=item.id;const token=++revision;
  list.querySelectorAll('button').forEach(b=>{b.classList.toggle('previewing',b===button);b.removeAttribute('aria-describedby')});button.setAttribute('aria-describedby',detail.id);
  detail.innerHTML='<div class="mutation-material-detail-head"><img src="'+icon(item.id)+'" alt=""><b>'+esc(item.name)+'</b></div><p class="material-description-text">正在读取介绍…</p><div class="material-price"><small>参考价格</small><b>读取中…</b><small class="material-price-source">ESI 市场均价</small></div>';
  if(!descriptions.has(item.id))descriptions.set(item.id,api('items/'+item.id).catch(e=>{descriptions.delete(item.id);throw e}));
  if(Date.now()-priceRequestedAt>3600000){priceRequest=null;priceRequestedAt=Date.now()}
  priceRequest??=api('prices').catch(e=>{priceRequest=null;throw e});
  const current=()=>token===revision&&hovered===item.id&&popup.isConnected;
  descriptions.get(item.id).then(data=>{if(!current())return;const raw=data.description;detail.querySelector('.material-description-text').textContent=plainDescription(typeof raw==='string'?raw:raw?.zh||raw?.en)||'暂无物品介绍';}).catch(()=>{if(current())detail.querySelector('.material-description-text').textContent='介绍暂时不可用，重新悬停可重试';});
  priceRequest.then(data=>{if(!current())return;const price=data.prices?.[item.id];detail.querySelector('.material-price b').textContent=Number.isFinite(price)&&price>0?price.toLocaleString('zh-CN',{maximumFractionDigits:2})+' ISK':'暂无参考价格';detail.querySelector('.material-price-source').textContent='ESI 市场均价 · 非实时成交价'+(data.updatedAt?'\n获取于 '+new Date(data.updatedAt*1000).toLocaleString('zh-CN'):'');}).catch(()=>{if(current())detail.querySelector('.material-price b').textContent='价格暂时不可用';});
 }
 function position(){
  if(!popup.matches(':popover-open'))return;
  const r=trigger.getBoundingClientRect(),width=Math.min(620,innerWidth-24);
  popup.style.width=width+'px';popup.style.maxHeight=Math.max(150,innerHeight-24)+'px';
  popup.style.left=Math.max(12,Math.min(r.right-width,innerWidth-width-12))+'px';
  popup.style.top=Math.max(12,Math.min(r.bottom+7,innerHeight-popup.offsetHeight-12))+'px';
 }
 function close(){if(popup.matches(':popover-open'))popup.hidePopover();revision++;}
 function open(){if(trigger.disabled||!options.length)return;popup.showPopover();position();list.querySelector('[aria-selected="true"]')?.focus({preventScroll:true});}
 trigger.onclick=()=>popup.matches(':popover-open')?close():open();
 trigger.onkeydown=e=>{if(e.key==='ArrowDown'||e.key==='ArrowUp'){e.preventDefault();open()}};
 popup.onkeydown=e=>{if(e.key==='Escape'){e.preventDefault();e.stopPropagation();close();trigger.focus()}if(e.key==='Tab'){close();trigger.focus()}};
 popup.addEventListener('toggle',e=>{trigger.setAttribute('aria-expanded',String(e.newState==='open'));if(e.newState==='closed'){revision++;hovered=null}});
 window.addEventListener('resize',position);
 paint();
 return {get value(){return selected?.id},set disabled(v){trigger.disabled=v;if(v)close()},destroy(){revision++;window.removeEventListener('resize',position);popup.remove()}};
}
