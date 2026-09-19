"""EVE fitting-window (EFT) text exchange; no fitting calculations."""
from collections import Counter
from nengine_catalog import index_metadata

def export_eft(fit):
    if not isinstance(fit,dict):raise ValueError('需要装配快照')
    data=index_metadata()
    def name(ident):
        row=data['types'].get(ident)
        if not row:raise ValueError('未知物品类型：'+str(ident))
        text=row.get('name',{}).get('en')
        if not text or any(c in text for c in '\r\n'):raise ValueError('物品缺少标准英文名称')
        return text
    title=' '.join(str(fit.get('name') or 'FitLab').splitlines()).replace('[','(').replace(']',')')
    sections=['['+name(fit.get('shipId'))+', '+title+']']
    slots=fit.get('slots') or []
    for kind,label in [('low','Low'),('mid','Med'),('high','High'),('rig','Rig'),('subsystem','Subsystem')]:
        rows=[s for s in slots if s.get('kind')==kind]
        if not rows:continue
        indices={int(s['key'].rsplit('-',1)[1]):s for s in rows}
        if any(i<0 or i>127 for i in indices) or len(indices)!=len(rows):raise ValueError('槽位编号无效')
        lines=[]
        for i in range(max(indices)+1):
            s=indices.get(i,{})
            if not s.get('item'):lines.append('[Empty '+label+' slot]');continue
            line=name(s['item'])
            if s.get('ammo'):line+=', '+name(s['ammo'])
            lines.append(line)
        sections.append('\n'.join(lines))
    def stacks(rows):
        counts=Counter()
        for ident,quantity in rows:
            if type(quantity) is not int or not 0<=quantity<=100000:raise ValueError('库存数量无效')
            counts[ident]+=quantity
        return '\n'.join(name(ident)+' x'+str(quantity) for ident,quantity in counts.items() if quantity)
    drones=stacks((r['item'],r['quantity']) for r in fit.get('drones',[]))
    fighters=fit.get('fighterLoadout') or {}
    fighter_lines=stacks((r['typeId'],r['quantity']) for key in ('tubes','reserve') for r in fighters.get(key,[]) if r)
    cargo=stacks([(r['item'],r['quantity']) for r in fit.get('cargo',[])]+[(r['typeId'],1) for r in fit.get('crystals',[]) if not r.get('moduleId')])
    sections.extend(x for x in (drones,fighter_lines,cargo) if x)
    notes=['标准装配文本不包含技能、脑插药剂、模块启用状态、精确装弹量、晶体损伤和出动状态。']
    if any(s.get('mutation') for s in slots) or any(d.get('mutation') for d in fit.get('drones',[])):notes.append('深渊装备仅导出物品类型，不包含变异属性和实物身份。')
    return {'format':'EFT','text':'\n\n'.join(sections)+'\n','notes':notes}
