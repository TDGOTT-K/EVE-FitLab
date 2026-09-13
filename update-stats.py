from pathlib import Path
p=Path('extract.py');s=p.read_text(encoding='utf-8');s=s.replace("attrs=dogma.get", "capacity=t.get('capacity'),volume=t.get('volume'),attrs=dogma.get");p.write_text(s,encoding='utf-8')
p=Path('app.js');s=p.read_text(encoding='utf-8')
s=s.replace('''${t?'<button class="more" aria-label="装备操作菜单">···</button><button class="remove" aria-label="卸载装备" title="卸载装备">×</button>':''}''','')
s=s.replace(''''<button class="unload" aria-label="卸载弹药">卸载</button>'+img(a)''','img(a)')
s=s.replace("${a?esc(a.name):'弹药槽 · 点击筛选 / 拖入装填'}</span>","${a?esc(a.name):'弹药槽 · 点击筛选 / 拖入装填'}</span>${a?`<span class=\"ammo-count\" title=\"满弹夹容量：模块容量 ÷ 单发弹药体积\">${magazine(t,a)} 发</span>`:''}")
s=s.replace("if(e.target.closest('.remove'))remove(key);else if(e.target.closest('.more'))openMenu(e,key,'slot');else toggleFilter(key)","toggleFilter(key)")
s=s.replace("e.target.closest('.unload')?unload(key):toggleFilter(key,true)","toggleFilter(key,true)")
a=s.index('const hardpointIcons=');b=s.index('function hardpoints',a);s=s[:a]+s[b:]
s=s.replace('<svg viewBox="0 0 24 24" aria-hidden="true">${hardpointIcons[icon]}</svg>','<img src="assets/${icon}.png" alt="" width="26" height="26">')
s=s.replace("function renderResources(){", "function magazine(t,a){return t.capacity!=null&&a.volume>0?Math.floor(t.capacity/a.volume+1e-8):'—'}\nfunction renderResources(){")
s=s.replace("let infoOrigin=null;", '''function renderShipStats(){
+ const a=ship.attrs;let dps=0;
+ for(const s of slots){const t=byId(s.item),ammo=byId(s.ammo);if(!s.online||!t?.effects?.includes(42)||!ammo||!t.attrs['51'])continue;dps+=[114,116,117,118].reduce((sum,id)=>sum+(ammo.attrs[id]||0),0)*(t.attrs['64']||1)/(t.attrs['51']/1000)}
+ const row=(label,value)=>`<div class="stat-row"><span>${label}</span><b>${value}</b></div>`;
+ const resist=(name,hp,ids)=>`<div class="defense-layer">${row(name,`${hp} HP`)}<div class="resists">${ids.map((id,i)=>`<span title="${['电磁','热能','动能','爆炸'][i]}抗性"><small>${['电','热','动','爆'][i]}</small>${((1-a[id])*100).toFixed(0)}%</span>`).join('')}</div></div>`;
+ $('#ship-stats').innerHTML=`<div class="panel-title">攻击 <small>未加成 · 不含换弹</small></div><div class="stat-block">${row('炮塔 DPS',dps.toFixed(1))}</div><div class="panel-title">防御 <small>裸船基础值</small></div><div class="stat-block">${resist('护盾',a['263'],[271,270,273,272])}${resist('装甲',a['265'],[267,2700,269,268]).replace('NaN%',((1-a['270'])*100).toFixed(0)+'%')}${resist('结构',a['9'],[113,110,109,111])}</div><div class="panel-title">机动与锁定 <small>裸船基础值</small></div><div class="stat-block">${row('最大速度',`${a['37']} m/s`)}${row('信号半径',`${a['552']} m`)}${row('锁定距离',`${(a['76']/1000).toFixed(1)} km`)}${row('锁定目标数',a['192'])}</div>`;
+}
+let infoOrigin=null;'''.replace('\n+','\n'))
# correct shield thermal resonance is 274; armor thermal is 270
s=s.replace("[271,270,273,272]", "[271,274,273,272]")
s=s.replace("${resist('装甲',a['265'],[267,2700,269,268]).replace('NaN%',((1-a['270'])*100).toFixed(0)+'%')}","${resist('装甲',a['265'],[267,270,269,268])}")
s=s.replace("renderResources();", "renderResources();renderShipStats();")
p.write_text(s,encoding='utf-8')
p=Path('index.html');s=p.read_text(encoding='utf-8');s=s.replace('未计入角色技能与模块加成，不作为装配有效性结论。</div>','未计入角色技能与模块加成，不作为装配有效性结论。</div><div id="ship-stats"></div>');p.write_text(s,encoding='utf-8')
p=Path('style.css');s=p.read_text(encoding='utf-8');s+='''
.ammo-count{margin-left:auto;color:var(--accent);font-variant-numeric:tabular-nums;white-space:nowrap}.hardpoint img{object-fit:contain}.stat-block{padding:8px 16px 12px}.stat-row{display:flex;justify-content:space-between;align-items:center;padding:7px 0;gap:10px;font-size:11px}.stat-row>span{color:var(--muted)}.stat-row b{font-weight:500;font-variant-numeric:tabular-nums}.defense-layer+.defense-layer{border-top:1px solid var(--line);margin-top:9px;padding-top:4px}.resists{display:grid;grid-template-columns:repeat(4,1fr);gap:4px;font-size:11px}.resists>span{display:flex;justify-content:space-between;gap:2px;flex-wrap:wrap}.resists small{color:var(--muted)}.inspector{scrollbar-width:thin;scrollbar-color:var(--line) transparent}
''';p.write_text(s,encoding='utf-8')
