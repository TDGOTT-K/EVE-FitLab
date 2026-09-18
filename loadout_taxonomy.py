"""Presentation taxonomy, using current source attributes. No gameplay evaluation."""
MAP={}
def tag(ids,group,leaf):
 for i in ids:MAP[i]=(group,leaf)
tag([39],'防御','维修无人机')
tag([292,64],'火力','武器伤害')
tag([293,441],'火力','武器射速')
tag([294,349,351],'火力','武器射程')
tag([767],'火力','炮台跟踪')
tag([547,557,3030],'火力','导弹射程')
tag([847,848,3031],'火力','导弹伤害应用')
tag([459,591,3028,3029,3353],'火力','无人机性能')
tag([2823,2824,2825],'火力','熵能武器增伤')
tag([337,3015,3017],'防御','护盾容量')
tag([338],'防御','护盾回充')
tag([548,838],'防御','护盾维修')
tag([335,1083,864],'防御','装甲容量')
tag([806,895,2457],'防御','装甲维修')
tag([327],'防御','结构容量')
tag([994,995,996,997],'防御','抗性')
tag([3023,3024],'防御','远程维修')
tag([1079],'电容与装配','电容容量')
tag([314],'电容与装配','电容回充')
tag([317],'电容与装配','装备耗电')
tag([319],'电容与装配','跃迁耗电')
tag([310,424,927],'电容与装配','CPU')
tag([313,323],'电容与装配','能量栅格')
tag([2267],'电容与装配','毁电抗性')
tag([20,315,318,1076,1084,2603,802,803],'机动','速度与推进')
tag([151,1282],'机动','惯性')
tag([624,1932],'机动','跃迁速度')
tag([309],'锁定与电子','锁定距离')
tag([566],'锁定与电子','扫描分辨率')
tag([554,863],'锁定与电子','信号半径')
tag([1027,1028,1029,1030,1550,1552,1553,1554,1565,1566,1567,1568,1569,1570,1571,1572],'锁定与电子','感应强度')
tag([1327,1293],'锁定与电子','跃迁扰断距离')
tag([2747],'锁定与电子','停滞缠绕距离')
tag([3134],'锁定与电子','隐形稳定')
tag([846,1284],'扫描与探索','探针强度')
tag([1156],'扫描与探索','扫描精度')
tag([902,1160,1915,1918,5798,5799,5800],'扫描与探索','黑客与分析')
tag([434,1292],'采集与工业','采矿')
tag([780],'采集与工业','冰矿采集')
tag([379],'采集与工业','精炼')
tag([440,453],'采集与工业','制造')
tag([452,468],'采集与工业','科研')
tag([884],'指挥与后勤','指挥脉冲')
tag([1125,1126],'训练与其他','增效剂使用')
tag([362,438],'训练与其他','声望与社交')
tag([447],'训练与其他','走私')
tag([2806],'训练与其他','技能注入')
CHAR={175:'魅力',176:'智力',177:'记忆',178:'感知',179:'意志'}
META={182,183,184,277,278,279,330,331,422,633,1087,1089,1090,1091,1092,1093,1647,1692,1890,1916,2422}
# These set amplifiers inherit the whole family's primary effects, including Omega.
SETS={3027:[('火力','无人机性能'),('火力','导弹射程'),('火力','导弹伤害应用')],3107:[('电容与装配','电容回充')],1291:[('训练与其他','增效剂使用')],799:[('电容与装配','传电与能量战')]}

def classify(t,kind,dogma,attributes,TYPES):
 a={x['attributeID']:x['value'] for x in dogma[t['id']].get('dogmaAttributes',[]) if x['value']!=0}
 desc=TYPES[t['id']].get('description',{}).get('en','').lower()
 tags=[]
 for id,value in a.items():
  name=attributes.get(id,{}).get('name','')
  if id in META or id in CHAR or id==1799 or 'penalty' in name.lower():continue
  if id in SETS:tags.extend(SETS[id]);continue
  if id==66 or id==312:
   if 'capacitor emission' in desc:tags.append(('电容与装配','传电与能量战'))
   elif 'afterburner' in desc:tags.append(('机动','推进器持续时间'))
   elif 'energy pulse' in desc:tags.append(('火力','立体炸弹射速'))
   elif 'salvage' in desc:tags.append(('扫描与探索','打捞与分析速度'))
   elif 'scan' in desc:tags.append(('扫描与探索','扫描速度'))
   elif 'repair' in desc:
    tags.append(('防御','装甲维修'))
    if 'shield' in desc:tags.append(('防御','护盾维修'))
    if 'hull' in desc:tags.append(('防御','结构维修'))
   elif 'mining' in desc or 'gas' in desc:tags.append(('采集与工业','采集速度'))
   elif 'booster' in desc:tags.append(('训练与其他','增效剂持续时间'))
   else:tags.append(('训练与其他','其他特殊效果'))
   continue
  if id in MAP:tags.append(MAP[id])
  else:tags.append(('训练与其他','其他特殊效果'))
 # Serpentis historical smuggling metadata must not distract from Snake's speed bonus.
 if 802 in a or 803 in a:tags=[v for v in tags if v!=('训练与其他','走私')]
 pure=bool(set(a)&CHAR.keys()) and not (set(a)-META-CHAR.keys()) and all(e['effectID'] in {302,304,306,308,310} for e in dogma[t['id']].get('dogmaEffects',[]))
 if kind=='implants' and pure:tags=[('训练与其他','白板')]
 elif not tags and set(a)&CHAR.keys():tags=[('训练与其他','属性与技能训练')]
 if not tags:tags=[('训练与其他','其他特殊效果')]
 t['benefitPaths']=[list(x) for x in dict.fromkeys(tags)]
 t['benefitLabels']=[CHAR[id]+' +'+format(a[id],'g') for id in CHAR if id in a] if pure else list(dict.fromkeys(x[1] for x in tags))
 return t
