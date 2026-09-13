from pathlib import Path
p=Path('app.js');s=p.read_text(encoding='utf-8');s="import {mountStats} from './stats.js';\n"+s
s=s.replace("const $=", "const marketIcons=await fetch('./data/market-icons.json').then(r=>r.json());\nlet defenseEhp=false;\nconst $=")
s=s.replace("low:'低槽'", "low:'低槽',rig:'改装件'");s=s.replace("low:ship.attrs['12']", "low:ship.attrs['12'],rig:ship.attrs['1137']")
a=s.index('try{const saved=');b=s.index('const img=',a)
s=s[:a]+"try{const saved=JSON.parse(localStorage.getItem('fitlab-prototype-v1'));if(Array.isArray(saved))slots=slots.map(s=>{const old=saved.find(v=>v.key===s.key&&v.kind===s.kind);return old&&(!old.item||byId(old.item))?{...s,...old}:s})}catch{}\n"+s[b:]
s=s.replace("${kind==='high'?hardpoints():''}","${kind==='high'?hardpoints():kind==='rig'?`<span title=\"校准值：已使用 / 上限\">校准 ${limit(slots.filter(s=>s.kind==='rig'&&s.item).reduce((n,s)=>n+(byId(s.item).attrs[1153]||0),0),ship.attrs[1132])}</span>`:''}")
s=s.replace("${slots.filter(s=>s.kind===kind&&s.item).length} / ${counts[kind]}","${limit(slots.filter(s=>s.kind===kind&&s.item).length,counts[kind])}")
s=s.replace("${used} / ${total}</span>","${limit(used,total)}</span>")
s=s.replace("${magazine(t,a)} 发", "X ${magazine(t,a)}")
s=s.replace("t.kind===s.kind))", "t.kind===s.kind&&(t.kind!=='rig'||t.attrs[1547]===ship.attrs[1547])))")
s=s.replace("s.kind===t.kind&&!s.item", "canInstall(t,s.key)&&!s.item")
s=s.replace("t.kind===selected.kind", "t.kind===selected.kind&&(t.kind!=='rig'||t.attrs[1547]===ship.attrs[1547])")
s=s.replace("t.name+' '+t.en+' '+t.path.join(' ')", "t.name+' '+t.en+' '+t.id+' '+t.path.join(' ')")
s=s.replace('<summary>${esc(k)}</summary>', '<summary>${marketIcons[path+\'/\'+k]?`<img class="tree-icon" src="${marketIcons[path+\'/\'+k]}" alt="">`:k.startsWith(\'科技\')?\'<span class="tech-icon">◇</span>\':\'\'}${esc(k)}</summary>')
s=s.replace("${s.online?'在线':'离线'} · ${t.attrs['50']||0} tf / ${t.attrs['30']||0} MW", "${kind==='rig'?`校准 ${t.attrs[1153]||0} · 尺寸 ${t.attrs[1547]}`:`${s.online?'在线':'离线'} · ${t.attrs['50']||0} tf / ${t.attrs['30']||0} MW`}")
s=s.replace("if(t)entries.push([s.online?", "if(t&&s.kind!=='rig')entries.push([s.online?")
s=s.replace("['卸载装备',()=>remove(id),true,'danger']);}", "['卸载装备',()=>remove(id),true,'danger']);if(t&&s.kind==='rig')entries.push(['卸载改装件',()=>remove(id),true,'danger']);}")
a=s.index('function renderShipStats(){');b=s.index('let infoOrigin',a)
s=s[:a]+'''function limit(used,max){return `<b class="limit-${used<max?'free':used===max?'full':'over'}">${used}</b> / ${max}`}
function renderShipStats(){const scroll=$('.inspector').scrollTop;mountStats($('#ship-stats'),ship,slots,catalog,defenseEhp,()=>{defenseEhp=!defenseEhp;renderShipStats()});$('.inspector').scrollTop=scroll}
'''+s[b:]
s += '''
const explain=document.createElement('div');explain.id='stat-explanation';explain.role='tooltip';explain.hidden=true;document.body.append(explain);let explanationTarget=null;
function hideExplain(){explain.hidden=true;explanationTarget?.removeAttribute('aria-describedby');explanationTarget=null}
function showExplain(target){hideExplain();explanationTarget=target;explain.textContent=target.dataset.explain;target.setAttribute('aria-describedby',explain.id);explain.hidden=false;const r=target.getBoundingClientRect();explain.style.left=Math.max(8,Math.min(r.left-explain.offsetWidth-12,innerWidth-explain.offsetWidth-8))+'px';explain.style.top=Math.max(8,Math.min(r.top,innerHeight-explain.offsetHeight-8))+'px'}
document.addEventListener('pointerover',e=>{const t=e.target.closest('[data-explain]');if(t&&t!==explanationTarget)showExplain(t)});
document.addEventListener('pointerout',e=>{if(explanationTarget&&!explanationTarget.contains(e.relatedTarget)&&!explain.contains(e.relatedTarget))hideExplain()});
document.addEventListener('focusin',e=>{const t=e.target.closest('[data-explain]');if(t)showExplain(t)});
document.addEventListener('focusout',hideExplain);document.addEventListener('scroll',hideExplain,true);window.addEventListener('resize',hideExplain);document.addEventListener('keydown',e=>{if(e.key==='Escape')hideExplain()});
'''
p.write_text(s,encoding='utf-8')
p=Path('index.html');s=p.read_text(encoding='utf-8').replace('placeholder="搜索装备或弹药…"','placeholder="使用名称、分类、ID 搜索"');p.write_text(s,encoding='utf-8')
