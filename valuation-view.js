import {getLocale,formatMetric,dateMarkup} from './i18n.js';
import {panelTip} from './native-panel-detail.js';
// Format the public fit_valuation result; no price or quantity arithmetic.
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const reasons={QUANTITY_UNDECLARED:'数量未声明',INVENTORY_UNDECLARED:'库存未声明',PRICE_UNAVAILABLE:'暂无报价',MARKET_UNAVAILABLE:'市场来源不可用',MUTATED_INSTANCE_PRICE_REQUIRED:'深渊实例需要单独估价',VALUATION_OVERFLOW:'金额超出范围',TOTAL_OVERFLOW:'合计超出范围',NO_AVAILABLE_SUBTOTALS:'没有可用报价',INCOMPLETE_VALUATION:'部分数量或价格缺失'};
export const valuationReason=reason=>reasons[reason]||reason||'';
export function createValuationReader(api){
 const entries=new Map();return fit=>{const key=JSON.stringify(fit),now=Date.now(),old=entries.get(key);if(old&&now-old.at<60000)return old.promise;
  const promise=api('fit-valuation',{fit}).then(r=>r.valuation);entries.set(key,{at:now,promise});
  while(entries.size>64)entries.delete(entries.keys().next().value);promise.catch(()=>entries.delete(key));return promise;
 };
}
export function valuationSummary(v,locale=getLocale()){
 const amount=v.complete?v.total:v.knownSubtotal,available=Number.isFinite(amount),source=v.marketSource;
 return {label:available?(v.complete?'参考估价':'已知部分估价'):'估价不可用',value:available?(locale===getLocale()?formatMetric(amount,'ISK',{maximumFractionDigits:0}):amount.toLocaleString(locale,{maximumFractionDigits:0})+' ISK'):'—',
  source:source.provider+' · '+(source.fetchedAt?new Date(source.fetchedAt).toLocaleString(locale):'无可用快照')+(source.cacheState==='stale'?' · 过期缓存':source.cacheState==='cached'?' · 缓存':''),
  scope:'船体、装备、脑插、药剂及已声明库存；不补满弹夹，非即时采购价。',reason:valuationReason(v.reason)};
}
export function valuationMarkup(v,locale=getLocale()){
 const source=v.marketSource,sourceHtml=esc(source.provider)+' · '+(source.fetchedAt?dateMarkup(source.fetchedAt):'无可用快照')+(source.cacheState==='stale'?' · 过期缓存':source.cacheState==='cached'?' · 缓存':'');
 const s=valuationSummary(v,locale),format=n=>Number.isFinite(n)?n.toLocaleString(locale,{maximumFractionDigits:2}):'—';
 const detail={lockable:true,title:'装配估价',result:s.value,terms:v.lines.map(l=>[(l.name||'库存')+' × '+(l.quantity??'未知'),l.state==='available'?format(l.subtotal)+' ISK':valuationReason(l.reason)]),conditions:[['来源',s.source],['范围',s.scope],['完整性',v.complete?'完整':s.reason||'部分报价'],['报价地址',v.marketSource.url],['快照',v.snapshotHash],...(v.marketSource.reason?[['来源状态',v.marketSource.reason]]:[])]};
 return '<div class="stat-row" '+panelTip(detail)+'><span>'+esc(s.label)+'</span><b>'+esc(s.value)+'</b></div><p class="profile-note">'+sourceHtml+'<br>'+esc(s.scope)+'</p>';
}
