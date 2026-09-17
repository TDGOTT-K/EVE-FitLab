"""Generate hover benefit text from SDE descriptions and explicit base attributes."""
import html,json,re
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent
BASE=ROOT/'desktop/build/sde'
def read(name):return {x['_key']:x for x in map(json.loads,(BASE/(name+'.jsonl')).read_text(encoding='utf-8').splitlines())}
TYPES=read('types');DOGMA=read('typeDogma');ATTRS=read('dogmaAttributes')
CHAR={175:'魅力',176:'智力',177:'记忆',178:'感知',179:'意志'}
LABELS={548:'护盾提升量',895:'装甲维修量',2457:'装甲维修量',314:'电容回充时间',315:'最大速度',1079:'电容容量',337:'护盾容量',3015:'护盾容量',335:'装甲容量',327:'结构容量',767:'炮台跟踪速度',554:'信号半径',847:'导弹爆炸速度',848:'导弹爆炸半径',349:'炮台失准距离',351:'最佳射程',566:'扫描分辨率',151:'惯性修正',313:'能量栅格输出',424:'CPU 输出'}
SKIP={182,183,184,277,278,279,330,331,422,633,1087,1089,1090,1091,1092,1093,1647,1692,1890,1916,2422}
def plain(text):
 text=re.sub(r'<br\s*/?>','\n',text,flags=re.I);return html.unescape(re.sub(r'<[^>]*>','',text)).replace('\r','')
def summary(t):
 desc=plain(TYPES[t['id']].get('description',{}).get('zh',''))
 lines=[x.strip() for x in desc.splitlines() if x.strip()]
 explicit=[x for x in lines if re.match(r'^(主要效果|次要效果|套件效果|套装效果|植入体套装效果)\s*[：:]',x)]
 if explicit:
  # Keep individual bonuses ahead of set amplification; preserve source wording.
  result=sorted(explicit,key=lambda x:0 if x.startswith('次要') else 1 if x.startswith('主要') else 2)
 else:
  result=[]
  for line in lines:
   if re.search(r'过期|失效|小时|分钟|持续时间为|YC\d|开发|研究人员',line):continue
   if len(line)<=160 and re.search(r'\d+(?:\.\d+)?\s*[%％]|[感知记忆智力魅力意志毅力]+\s*[+＋]\s*\d',line):result.append(line)
 values={a['attributeID']:a['value'] for a in DOGMA[t['id']].get('dogmaAttributes',[]) if a['value']}
 if ['训练与其他','白板'] in t.get('benefitPaths',[]):result=[CHAR[id]+' +'+format(v,'g') for id,v in values.items() if id in CHAR]
 if not explicit and 'Omega' in t.get('en',''):result=['套装效果：'+line for line in result]
 if not result:
  for id,v in values.items():
   a=ATTRS.get(id,{});name=a.get('name','');unit=a.get('unitID')
   if id in SKIP or 'penalty' in name.lower() or 'implantset' in name.lower() or 'setbonus' in name.lower():continue
   if id in CHAR:result.append(CHAR[id]+' +'+format(v,'g'));continue
   if id not in LABELS and not ('bonus' in name.lower() or 'modifier' in name.lower()):continue
   label=LABELS.get(id,a.get('displayName',{}).get('zh'))
   if not label:continue
   if unit in [105,121]:value=format(v,'+g')+'%'
   elif unit==127:value=format(v*100,'+g')+'%'
   elif unit==104:value='× '+format(v,'g')
   elif unit in [108,111]:value=format((1-v)*100,'g')+'%'
   elif unit==109:value=format((v-1)*100,'+g')+'%'
   elif unit in [None,120]:value=format(v,'+g')
   else:continue
   result.append(label+' '+value)
 if not result:result=['作用：'+'、'.join(t.get('benefitLabels',[])),'具体效果见右键 → 详细信息']
 penalties=[]
 for id,v in values.items():
  a=ATTRS.get(id,{});
  if 'penalty' in a.get('name','').lower():
   label=a.get('displayName',{}).get('zh')
   if label:penalties.append(label+(' '+format(v,'+g')+'%' if a.get('unitID') in [105,121] else ''))
 if penalties:result+=['可选副作用：'+'；'.join(penalties)]
 return '\n'.join(dict.fromkeys(result))
if __name__=='__main__':
 for name in ['implant','booster']:
  p=ROOT/(name+'-catalog.js');s=p.read_text(encoding='utf-8');items=json.loads(s[s.index('['):].rstrip(';\n'))
  for t in items:t['benefitTooltip']=summary(t)
  p.write_text(s[:s.index('[')]+json.dumps(items,ensure_ascii=True,separators=(',',':'))+';\n',encoding='utf-8')
