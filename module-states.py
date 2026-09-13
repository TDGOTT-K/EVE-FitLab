from pathlib import Path
p=Path('build-catalog.py');s=p.read_text(encoding='utf-8');s=s.replace("for t in read('types'):","effect_categories={e['_key']:e.get('effectCategoryID') for e in read('dogmaEffects')}\nfor t in read('types'):")
s=s.replace('attrs=attrs,effects=effects)', 'attrs=attrs,effects=effects,canActivate=any(effect_categories.get(e) in [1,2,3] for e in effects),canOverload=any(effect_categories.get(e)==5 for e in effects))');p.write_text(s,encoding='utf-8')
p=Path('app.js');s=p.read_text(encoding='utf-8');pos=s.index('function renderSlots()');s=s[:pos]+'''const stateLabels={Offline:'离线',Online:'在线 · 未启用',Active:'启用',Overload:'超载'};
function moduleState(s){return s.state||(s.online===false?'Offline':byId(s.item)?.canActivate?'Active':'Online')}
function changeModuleState(key,state){const s=slots.find(s=>s.key===key);mutate(()=>{s.state=state;s.online=state!=='Offline'},'装备状态：'+stateLabels[state])}
'''+s[pos:]
s=s.replace("${s.online?'':'offline'}", "${moduleState(s)==='Offline'?'offline':''} state-${moduleState(s).toLowerCase()}")
s=s.replace("${s.online?'在线':'离线'}", "${moduleState(s)==='Online'&&!t.canActivate?'在线 · 被动':stateLabels[moduleState(s)]}")
s=s.replace('s.item=null;s.ammo=null;s.online=true','s.item=null;s.ammo=null;s.online=true;delete s.state')
s=s.replace('s.item=t.id;s.ammo=null;s.online=true','s.item=t.id;s.ammo=null;s.online=true;s.state=t.canActivate?\'Active\':\'Online\'')
old="if(t&&s.kind!=='rig')entries.push([s.online?'设为离线':'设为在线',()=>mutate(()=>s.online=!s.online,'已切换装备在线状态')],['卸载装备',()=>remove(id),true,'danger']);"
new="if(t&&s.kind!=='rig'){for(const [state,label] of [['Offline','离线'],['Online',t.canActivate?'在线 · 未启用':'在线 · 被动'],...(t.canActivate?[['Active','启用']]:[]),...(t.canOverload&&t.canActivate?[['Overload','超载']]:[])])entries.push([(moduleState(s)===state?'✓ ':'')+label,()=>changeModuleState(id,state),moduleState(s)!==state]);entries.push(['卸载装备',()=>remove(id),true,'danger'])}"
assert old in s;s=s.replace(old,new);p.write_text(s,encoding='utf-8')
p=Path('server.py');s=p.read_text(encoding='utf-8');pos=s.index('def validate_fit(');s=s[:pos]+'''def module_state(slot):
 t=TYPES.get(slot.get('item'),{})
 return slot.get('state') or ('Offline' if slot.get('online') is False else 'Active' if t.get('canActivate') else 'Online')
'''+s[pos:]
s=s.replace("  if s.get('ammo')", "  if s.get('item'):\n   state=module_state(s);t=TYPES[s['item']]\n   if state not in ['Offline','Online','Active','Overload']:raise ValueError('无效装备状态')\n   if state in ['Active','Overload'] and not t.get('canActivate'):raise ValueError('此装备不能启用')\n   if state=='Overload' and not t.get('canOverload'):raise ValueError('此装备不支持超载')\n  if s.get('ammo')",1)
s=s.replace("m['state']='Active' if s.get('online',True) else 'Offline'", "m['state']=module_state(s)");p.write_text(s,encoding='utf-8')
