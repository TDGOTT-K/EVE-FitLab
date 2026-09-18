"""EFT input translation only; fitting rules and results remain engine-owned."""
import re
import unicodedata
from functools import lru_cache
from nengine_catalog import index_metadata,refresh_catalog,effective_module_state

def key(text):return unicodedata.normalize('NFKC',text).strip().casefold()

@lru_cache(maxsize=1)
def names():
    data=index_metadata();result={}
    for ident,t in data['types'].items():
        if not t.get('published'):continue
        for name in t.get('name',{}).values():result.setdefault(key(name),set()).add(ident)
    return result

def parse_eft(text):
    if not isinstance(text,str) or not text.strip() or len(text)>100000:raise ValueError('请粘贴不超过 100000 字的 EFT 装配文本')
    lines=text.lstrip('\ufeff').splitlines()
    if len(lines)>1000:raise ValueError('装配文本最多 1000 行')
    catalog={t['id']:t for t in refresh_catalog([])};data=index_metadata();issues=[];notes=[];fit=None;counters={k:0 for k in ('low','mid','high','rig','subsystem')}
    def error(number,line,message):issues.append({'line':number,'text':line,'message':message})
    def resolve(name):
        ids=names().get(key(name),set())
        if len(ids)!=1:raise ValueError(('无法识别' if not ids else '名称对应多个物品')+'：'+name)
        return next(iter(ids))
    def attrs(ident):return {r['attributeID']:r['value'] for r in data['typeDogma'].get(ident,{}).get('dogmaAttributes',[])}
    for number,raw in enumerate(lines,1):
        line=raw.strip()
        if not line:continue
        try:
            if fit is None:
                match=re.fullmatch(r'\[(.+?),\s*(.+)\]',line)
                if not match:raise ValueError('首行应为 [舰船名称, 装配名称]；当前支持 EFT 文本')
                ship=resolve(match[1]);title=match[2].strip()
                if catalog.get(ship,{}).get('kind')!='ship':raise ValueError('首行不是可用舰船')
                if not title or len(title)>120:raise ValueError('装配名称须为 1–120 字')
                fit={'shipId':ship,'name':title,'slots':[],'drones':[],'cargo':[],'skills':[],'characterName':'无技能 · 基础对照','scenario':{}}
                continue
            empty=re.fullmatch(r'\[Empty (Low|Med|Mid|High|Rig|Subsystem) slot\]',line,re.I)
            if empty:
                kind=empty[1].lower();kind='mid' if kind=='med' else kind
                i=counters[kind];counters[kind]+=1;fit['slots'].append({'key':kind+'-'+str(i),'kind':kind,'item':None,'ammo':None});continue
            if line.startswith('['):raise ValueError('当前一次导入一份装配；不支持带深渊变异附表的 EFT 扩展')
            offline=bool(re.search(r'\s*/offline$',line,re.I));content=re.sub(r'\s*/offline$','',line,flags=re.I).strip()
            quantity=re.fullmatch(r'(.+?)\s+[x×](\d+)',content,re.I)
            if quantity:
                ident=resolve(quantity[1]);count=int(quantity[2]);ammo=None
                if not 1<=count<=100000:raise ValueError('物品数量须为 1–100000')
            else:
                count=1;ammo=None
                try:ident=resolve(content)
                except ValueError as original:
                    candidates=[]
                    for split in re.finditer(',',content):
                        try:candidates.append((resolve(content[:split.start()]),resolve(content[split.end():])))
                        except ValueError:pass
                    if len(candidates)!=1:raise original
                    ident,ammo=candidates[0]
            t=catalog.get(ident);a=attrs(ident);category=data['groups'][data['types'][ident]['groupID']]['categoryID']
            if offline and (quantity or not t or t['kind'] not in counters):raise ValueError('/offline 只用于已安装装备')
            if ammo and (not t or t['kind'] not in counters or catalog.get(ammo,{}).get('kind')!='ammo'):raise ValueError('逗号后的物品必须为装备弹药')
            if category==18:
                fit['drones'].append({'item':ident,'quantity':count,'active':0})
            elif category==87:
                maximum=a.get(2215)
                if not maximum or maximum!=int(maximum) or not 1<=maximum<=12:raise ValueError('该舰载机缺少可用的中队数量定义')
                reserve=fit.setdefault('fighterLoadout',{'tubes':[],'reserve':[]})['reserve']
                while count:
                    n=min(count,int(maximum));reserve.append({'typeId':ident,'quantity':n,'active':False});count-=n
                    if len(reserve)>200:raise ValueError('舰载机库存超过当前接口上限')
            elif not quantity and category==20 and (331 in a or 1087 in a):
                plan=fit.setdefault('loadoutPlan',{'name':'导入的脑插与增效剂','customized':True,'implants':[],'boosters':[]})
                kind='implants' if 331 in a else 'boosters';slot=int(a[331 if kind=='implants' else 1087])
                if any(e['slot']==slot for e in plan[kind]):raise ValueError('脑插或增效剂槽位重复')
                plan[kind].append({'typeId':ident,'slot':slot,**({'enabledSideEffects':[]} if kind=='boosters' else {})})
            elif quantity or t and t['kind']=='ammo':
                if not t:raise ValueError('当前装配货舱尚不支持此类物品：'+data['types'][ident]['name'].get('zh',str(ident)))
                fit['cargo'].append({'item':ident,'quantity':count})
            elif t and t['kind'] in counters:
                kind=t['kind'];i=counters[kind]
                if kind=='subsystem':
                    if a.get(1366) not in (125,126,127,128):raise ValueError('子系统没有可识别的槽位定义')
                    i=int(a[1366])-125
                if any(s['key']==kind+'-'+str(i) for s in fit['slots']):raise ValueError('槽位重复')
                counters[kind]=max(counters[kind],i+1)
                slot={'key':kind+'-'+str(i),'kind':kind,'item':ident,'ammo':ammo}
                if offline:slot['state']='Offline'
                slot['state']=effective_module_state(slot);slot['online']=slot['state']!='Offline';fit['slots'].append(slot)
            else:raise ValueError('此行不是已支持的装配条目')
        except ValueError as ex:error(number,raw,str(ex))
    if fit:
        if len(fit['slots'])>100 or len(fit['drones'])>200 or len(fit['cargo'])>200:error(0,'','槽位或库存条目超过当前装配接口上限')
        notes=['EFT 不包含角色技能：请选择计算角色。','未声明弹仓数量、晶体损伤与启用状态；普通主动装备默认启用，/offline 保留离线。']
        if fit['drones'] or fit.get('fighterLoadout'):notes.append('无人机与舰载机先放入备用库存；请在工作台选择出动数量。')
        if fit.get('loadoutPlan',{}).get('boosters'):notes.append('EFT 不记录副作用抽取结果；导入药剂暂不选择副作用。')
    return {'format':'EFT','fit':fit if not issues else None,'issues':issues,'notes':notes,'source':data['source']}
