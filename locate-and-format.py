from pathlib import Path
p=Path('app.js');s=p.read_text(encoding='utf-8');s=s.replace("$('#info-title').title=t.path.join(' › ');", "$('#info-title').title='在装备浏览器中定位此物品';$('#info-title').onclick=e=>{e.preventDefault();locateItem(t)};")
s=s.replace("if(e.target.closest('button')||e.button!==0)return", "if(e.target.closest('button,a')||e.button!==0)return")
pos=s.index('function clampInfo()');s=s[:pos]+'''function locateItem(t){
 if(['ship','skill'].includes(t.kind)){say('此物品不属于装备浏览器');return}
 $('#search').value='';filter=null;let path='';for(const p of [...t.path,...(t.kind==='ammo'?[]:[t.meta||'科技 I'])]){path+='/'+p;treeOpen.add(path)}renderSlots();renderTree();const item=$(`#tree [data-id="${t.id}"]`);if(item){item.classList.add('located');item.scrollIntoView({block:'center'});item.focus({preventScroll:true});say('已定位：'+t.name)}
}
'''+s[pos:];p.write_text(s,encoding='utf-8')
p=Path('index.html');s=p.read_text(encoding='utf-8').replace('<span id="info-title">物品信息</span>','<a id="info-title" href="#">物品信息</a>');p.write_text(s,encoding='utf-8')
p=Path('engine-view.js');s=p.read_text(encoding='utf-8').replace("pilot='',scale=1){", "pilot='',scale=1,baseLabel='舰船基础'){")
s=s.replace("const terms=[['舰船基础',", "const terms=[[baseLabel,")
s=s.replace("const base=(name,value,initial,unit)=>{const attribute=", "const base=(name,value,initial,unit)=>{if(Math.abs(value-initial)<0.001)return `<div class=\"stat-row\"><span>${name}</span><b>${Number(value).toFixed(1)} ${unit}</b></div>`;const attribute=")
p.write_text(s,encoding='utf-8')
