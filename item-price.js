import {getLocale,formatMetric} from './i18n.js';
let cached=null,expires=0;
function snapshot(){if(!cached||Date.now()>expires){expires=Date.now()+60000;cached=fetch('/api/prices').then(async r=>{if(!r.ok)throw Error('报价读取失败');return r.json();}).catch(e=>{cached=null;throw e;});}return cached;}
export function mountItemPrice(identity,item){
 if(!identity)return;
 const row=document.createElement('p');row.className='profile-note item-info-price';row.setAttribute('aria-label','物品参考估价');row.textContent='参考估价 · 读取中…';identity.append(row);
 if(item.abyssal||item.capabilities?.requiresMutationInstance){row.textContent='参考估价 · 深渊实例需单独估价';return;}
 snapshot().then(data=>{if(!row.isConnected)return;const value=data.prices?.[String(item.id)],source=data.source||{};
 row.textContent='参考估价 · '+(Number.isFinite(value)?formatMetric(value,'ISK',{maximumFractionDigits:2}):'暂无报价');
 row.title=[source.provider||'ESI', '单件市场均价，非即时采购价',source.fetchedAt?new Date(source.fetchedAt).toLocaleString(getLocale()):'无可用快照',source.cacheState==='stale'?'过期缓存':'',source.reason||''].filter(Boolean).join(' · ');
 }).catch(error=>{if(row.isConnected){row.textContent='参考估价 · 暂不可用';row.title=error.message;}});
}
