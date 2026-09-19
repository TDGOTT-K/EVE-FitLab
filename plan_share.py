"""Portable loadout sharing. All numerical analysis remains public NEngine queries."""
import copy
from loadout_plans import save_plan
from nengine_booster_plan import analyze_plan
from nengine_adapter import bridge
from nengine_catalog import index_metadata

FORMAT='EVE-FitLab-Plan'
def source():
 b=bridge().baseline
 return {k:b[k] for k in ('engineVersion','revision','buildNumber','indexSha256','staticRule')}

def clean_plan(value):
 if not isinstance(value,dict):raise ValueError('方案数据格式无效')
 allowed=('name','implants','boosters','pilot','rollReceipt')
 portable={k:copy.deepcopy(value[k]) for k in allowed if k in value}
 if 'pilot' in portable:
  portable['pilot']={'name':'分享技能快照','skills':[{'skillTypeId':r['skillTypeId'],'level':r['level']} for r in portable['pilot'].get('skills',[])]}
 saved=save_plan(portable,{},'share')
 result={k:saved[k] for k in allowed if k in saved}
 # Slot identities are source metadata, never guessed from the displayed name.
 dogma=index_metadata()['typeDogma']
 for kind,attribute in [('implants',331),('boosters',1087)]:
  for row in result[kind]:
   attrs={a['attributeID']:a['value'] for a in dogma.get(row['typeId'],{}).get('dogmaAttributes',[])}
   if attrs.get(attribute)!=row['slot']:raise ValueError('物品类型与方案槽位不匹配：'+str(row['typeId']))
 return result

def export_plan(value):
 plan=clean_plan(value);result=analyze_plan(plan)
 types=index_metadata()['types']
 names={str(row['typeId']):(types[row['typeId']].get('name',{}).get('zh') or types[row['typeId']].get('name',{}).get('en') or str(row['typeId'])) for key in ('implants','boosters') for row in plan[key]}
 document={'format':FORMAT,'version':1,'source':source(),'itemNames':names,'plan':plan,'nativePlanHash':result['analysis']['planHash']}
 return {'document':document,'summary':result['summary'],'analysis':result['analysis']}

def import_plan(document):
 if not isinstance(document,dict) or document.get('format')!=FORMAT or document.get('version')!=1:raise ValueError('不支持的脑插方案分享格式')
 expected=source();provided=document.get('source',{})
 if not isinstance(provided,dict) or any(provided.get(k)!=expected[k] for k in ('buildNumber','indexSha256','staticRule')):raise ValueError('分享方案的 SDE 或规则版本与当前版本不一致')
 result=export_plan(document.get('plan'))
 if result['document']['nativePlanHash']!=document.get('nativePlanHash'):raise ValueError('方案内容校验不一致，分享数据可能被修改或损坏')
 return {'plan':dict(result['document']['plan'],folder=''),'summary':result['summary'],'analysis':result['analysis']}
