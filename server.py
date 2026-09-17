from abyssal_instances import save_instance
from loadout_plans import save_plan, save_layout
from workspace_view import attach_workspace_view
from scenario_presets import validate_presets
from linked_scenario import resolve_fit_scenario
from sustained_tank import sustained_tank
from fitting_scenario import calculate_scenario,validate_scenario
from calculation_graph import attach_calculations
from market_prices import prices
from capacitor import calculate_capacitor
"""FitLab local storage and fitting-engine adapter. Run: python server.py"""
import json, os, re, threading, uuid, urllib.request, urllib.error, urllib.parse
from http.cookies import SimpleCookie
import eve_sso
import storage_location
from pathlib import Path
from datetime import datetime, timezone
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler

ROOT=Path(os.environ.get('FITLAB_WEB_ROOT',Path(__file__).resolve().parent))
STATE=storage_location.read_location(Path(os.environ.get('FITLAB_DEFAULT_STATE',ROOT/'state'))); STATE.mkdir(parents=True,exist_ok=True)
os.environ.setdefault('FITLAB_NENGINE_STATE',str(STATE/'nengine-ui'))
CATALOG=json.loads((ROOT/'data/full-catalog.json').read_text(encoding='utf-8'))
if os.environ.get('FITLAB_CALCULATOR','nengine')=='nengine':
 from nengine_catalog import refresh_catalog
 CATALOG=refresh_catalog(CATALOG)
TYPES={t['id']:t for t in CATALOG}
METADATA=json.loads((ROOT/'data/item-metadata.json').read_text(encoding='utf-8'))
LOCK=threading.RLock()
ENGINE=os.environ.get('FITLAB_ENGINE_URL','http://127.0.0.1:5210')
SOURCE=Path(os.environ.get('FITLAB_CHARACTER_SOURCE',STATE/'external-characters-disabled.json'))
WORKSPACE='workspace_c49c0a7aa1704ea086941346a0366418'
def now():return datetime.now(timezone.utc).isoformat()
def read_library():
 p=STATE/'library.json'
 return json.loads(p.read_text(encoding='utf-8')) if p.exists() else {'fits':[],'characters':[]}
def write_library(data):
 p=STATE/'library.json';tmp=p.with_suffix('.tmp');tmp.write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8');os.replace(tmp,p)
def engine(path,body=None):
 req=urllib.request.Request(ENGINE+'/api/'+path,data=json.dumps(body).encode() if body is not None else None,headers={'Content-Type':'application/json','Origin':ENGINE,'X-FitLab-Key':os.environ.get('FITLAB_ENGINE_KEY','')})
 try:
  with urllib.request.urlopen(req,timeout=60) as r:d=json.load(r)
 except urllib.error.HTTPError as e:
  try:d=json.load(e)
  except Exception:raise ValueError('计算服务拒绝请求')
  raise ValueError('; '.join([str(d.get('summary','计算失败'))]+[str(x) for x in d.get('errors',[])]))
 if isinstance(d,dict) and d.get('isSuccess') is False:raise ValueError(d.get('summary','计算失败'))
 return d.get('data',d) if isinstance(d,dict) else d
def characters():
 all5=[{'skillTypeId':t['id'],'level':5} for t in CATALOG if t['kind']=='skill']
 result=[{'id':'none','name':'无技能 · 基础对照','skills':[],'source':'内置'}, {'id':'all5','name':'全技能 V · 模拟角色','skills':all5,'source':'内置'}]
 if SOURCE.exists():
  data=json.loads(SOURCE.read_text(encoding='utf-8-sig'));workspaces=data.get('state',data).get('workspaces',[])
  selected=next((w for w in workspaces if w['workspace']['workspace_id']==WORKSPACE),None)
  if selected:
   skill_map={x['skillKey'].lower():x['typeId'] for x in engine('catalog/skills')['skills']}
   for c in selected.get('characters',{}).values():
    p=c.get('capability_profile',{});rows=p.get('industry',{}).get('skill_levels',[])
    if p.get('is_unlimited_skill_simulation'):skills=all5
    else:
     skills=[{'skillTypeId':skill_map.get(x['skill_key'].lower()),'level':x['level']} for x in rows]
     if not skills or any(x['skillTypeId'] is None for x in skills):continue
    result.append({'id':c['character_id'],'name':c['display_name'],'skills':skills,'source':'EdenOS 已保存快照'})
 return result+read_library()['characters']
def sso_client():
 return eve_sso.CLIENT_ID

def save_character(body,lib):
 existing=next((c for c in lib['characters'] if c['id']==body.get('id')),None)
 if body.get('id') and not existing:raise ValueError('角色不存在，内置和外部快照请先复制。')
 if existing and existing.get('source')=='EVE 官网':raise ValueError('官网角色请重新授权更新，或复制为自定义角色。')
 name=body.get('name','')
 if not isinstance(name,str) or not name.strip() or len(name)>80:raise ValueError('请输入 1～80 字的角色名称')
 skills=body.get('skills')
 if not isinstance(skills,list) or len(skills)>2000:raise ValueError('技能列表无效')
 validate_fit({'shipId':587,'name':'skill-check','slots':[],'skills':skills})
 ids=[x['skillTypeId'] for x in skills]
 if len(ids)!=len(set(ids)):raise ValueError('技能列表中有重复项')
 if existing and body.get('revision')!=existing.get('revision',0):raise ValueError('角色已在另一窗口修改，请重新打开。')
 c={'id':existing['id'] if existing else str(uuid.uuid4()),'name':name.strip(),'skills':[{'skillTypeId':x['skillTypeId'],'level':x['level']} for x in skills if x['level']>0],'source':'自定义角色','updatedAt':now(),'revision':existing.get('revision',0)+1 if existing else 1}
 lib['characters']=[x for x in lib['characters'] if x['id']!=c['id']]+[c];write_library(lib);return c

def module_state(slot):
 t=TYPES.get(slot.get('item'),{})
 return slot.get('state') or ('Offline' if slot.get('online') is False else 'Active' if t.get('canActivate') else 'Online')
def validate_fit(f):
 if f.get('shipId') not in TYPES or TYPES[f['shipId']]['kind']!='ship':raise ValueError('请选择有效舰船')
 if not str(f.get('name','')).strip():raise ValueError('请输入装配名称')
 if not isinstance(f.get('notes',''),str) or len(f.get('notes',''))>4000:raise ValueError('备注必须为不超过 4000 字的文字')
 if len(f['name'])>120:raise ValueError('名称不能超过 120 字')
 if not isinstance(f.get('slots'),list) or len(f['slots'])>100:raise ValueError('槽位数据无效')
 keys=set()
 for s in f['slots']:
  if not re.fullmatch(r'(high|mid|low|rig|subsystem)-\d+',s.get('key','')) or s['key'] in keys:raise ValueError('槽位标识无效')
  keys.add(s['key'])
  if s.get('kind')!=s['key'].split('-')[0]:raise ValueError('槽位标识与槽型不一致')
  if s.get('item') and (s['item'] not in TYPES or TYPES[s['item']]['kind']!=s['kind']):raise ValueError('槽型不匹配')
  if s.get('item') and s['kind']=='subsystem':
   t=TYPES[s['item']]
   if t['attrs'].get('1380')!=f['shipId'] or t['attrs'].get('1366')!=125+int(s['key'].split('-')[1]):raise ValueError('子系统与舰船或子系统类型不匹配')
  if s.get('item'):
   state=module_state(s);t=TYPES[s['item']]
   if s['kind']=='subsystem' and state!='Online':raise ValueError('子系统必须在线')
   if state not in ['Offline','Online','Active','Overload']:raise ValueError('无效装备状态')
   if state in ['Active','Overload'] and not t.get('canActivate'):raise ValueError('此装备不能启用')
   if state=='Overload' and not t.get('canOverload'):raise ValueError('此装备不支持超载')
  if s.get('ammo') and (s['ammo'] not in TYPES or TYPES[s['ammo']]['kind']!='ammo'):raise ValueError('弹药数据无效')
 validate_scenario(f.get('scenario',{}))
 if 'scenarios' in f:validate_presets(f)
 for kind in ['drones','cargo']:
  entries=f.get(kind,[])
  if not isinstance(entries,list) or len(entries)>200:raise ValueError('舱内物品列表无效')
  for e in entries:
   if e.get('item') not in TYPES or type(e.get('quantity')) is not int or not 1<=e['quantity']<=100000:raise ValueError('舱内物品数量无效')
   if kind=='drones' and (TYPES[e['item']]['kind']!='drone' or type(e.get('active',0)) is not int or not 0<=e.get('active',0)<=e['quantity']):raise ValueError('无人机出动数量无效')
 for s in f.get('skills',[]):
  if s.get('skillTypeId') not in TYPES or TYPES[s['skillTypeId']]['kind']!='skill' or type(s.get('level')) is not int or not 0<=s['level']<=5:raise ValueError('技能等级或类型无效')
 return f
def analyze(f,resolve_links=True):
 if os.environ.get("FITLAB_CALCULATOR", "nengine")=="nengine":
  validate_fit(f)
  from nengine_adapter import analyze as native_analyze
  return native_analyze(f)
 return analyze_legacy(f,resolve_links)

def analyze_legacy(f,resolve_links=True):
 validate_fit(f);h=TYPES[f['shipId']];lines=[f"[{h['en']}, FitLab]"]
 fitted=[s for s in f['slots'] if s.get('item')]
 for s in fitted:
  t=TYPES[s['item']];line=t['en']
  if s.get('ammo'):line+=', '+TYPES[s['ammo']]['en']
  lines.append(line)
 imported=engine('actions/ImportFitText',{'text':'\n'.join(lines),'locale':'en'})
 snapshot=imported['snapshot'];snapshot['skills']=f.get('skills',[])
 snapshot['droneBay']={'drones':[{'droneTypeId':'type:'+str(e['item']),'dogmaTypeId':e['item'],'name':TYPES[e['item']]['en'],'quantity':e.get('active',0),'bayQuantity':e['quantity'],'state':'Active' if e.get('active',0)>0 else 'Online'} for e in f.get('drones',[])]}
 snapshot['cargo']=[{'typeId':'type:'+str(e['item']),'dogmaTypeId':e['item'],'name':TYPES[e['item']]['en'],'quantity':e['quantity']} for e in f.get('cargo',[])]
 # Preserve the user's selected module state; EFT imports otherwise activate everything.
 queues={k:[s for s in fitted if s['kind']==k] for k in ['high','mid','low','rig','subsystem']}
 slot_keys={}
 for m in snapshot['modules']:
  k=m['slotKind'].lower();s=queues[k].pop(0);m['state']=module_state(s);slot_keys[m['slotId']]=s['key']
 result=engine('actions/ValidateFit',{'snapshot':snapshot,'simulationMode':'SingleShip'})
 for m in result['snapshot']['modules']:m['workspaceSlotKey']=slot_keys.get(m['slotId'])
 result['skillCount']=len(snapshot['skills'])
 # Projected module repair describes its output; classify remote output separately.
 a=result['attributes']
 for group,field,remote in [(41,'shieldRepairPerSecond','remoteShieldRepairPerSecond'),(325,'armorRepairPerSecond','remoteArmorRepairPerSecond'),(585,'structureRepairPerSecond','remoteStructureRepairPerSecond')]:
  total=sum(m.get(field,0) for m in result['snapshot']['modules'] if m['state'] in ['Active','Overload'] and TYPES.get(m.get('dogmaTypeId'),{}).get('group')==group)
  a[remote]=total
  a[field]=max(0,a[field]-total)
 a['remoteCapacitorTransferPerSecond']=sum(m.get('capacitorTransferPerSecond',0) for m in result['snapshot']['modules'] if m['state'] in ['Active','Overload'] and TYPES.get(m.get('dogmaTypeId'),{}).get('group')==67)
 scenario,links=resolve_fit_scenario(f.get('scenario',{}),read_library()['fits'],lambda other:analyze(other,False),TYPES,result['attributes']['maxVelocity']) if resolve_links else (validate_scenario({}),{})
 result['scenarioLinks']=links
 result['capacitorAnalysis']=calculate_capacitor(result,TYPES,scenario)
 attach_calculations(result)
 result['sustainedTank']=sustained_tank(result,TYPES,scenario)
 result['scenarioAnalysis']=calculate_scenario(result,scenario,TYPES)
 result['cargoUsed']=sum((TYPES[e['item']].get('volume') or 0)*e['quantity'] for e in f.get('cargo',[]))
 limit=result['attributes']['attributeSnapshot'].get('maxActiveDrones')
 if limit is not None and sum(e.get('active',0) for e in f.get('drones',[]))>limit:
  result['isValid']=False;result['issues'].append({'message':'出动无人机数量超过当前角色操控上限'})
 capacity=result['attributes']['attributeSnapshot'].get('capacity')
 if capacity is not None and result['cargoUsed']>capacity:
  result['isValid']=False;result['issues'].append({'message':'普通货舱体积超过容量'})
 if h['group']==963:
  selected={TYPES[s['item']]['attrs'].get('1366') for s in fitted if s['kind']=='subsystem'}
  if len(selected)!=4:
   result['isValid']=False;result['issues'].append({'message':'请先选齐核心、防御、攻击、推进四类子系统'})
  for slot in result['attributes'].get('slotUsage',[]):
   if slot['kind']=='Subsystem':slot['available']=4
 if resolve_links:attach_workspace_view(result,f,TYPES)
 return result
class Handler(SimpleHTTPRequestHandler):
 def __init__(self,*args,**kw):super().__init__(*args,directory=str(ROOT),**kw)
 def end_headers(self):
  self.send_header('Cache-Control','no-store')
  super().end_headers()
 def log_message(self,*args):pass
 def reply(self,data,status=200):
  b=json.dumps(data,ensure_ascii=False).encode();self.send_response(status);self.send_header('Content-Type','application/json; charset=utf-8');self.send_header('Cache-Control','no-store');self.send_header('Content-Length',str(len(b)));self.end_headers();self.wfile.write(b)
 def do_GET(self):
  if os.environ.get('FITLAB_API_KEY') and self.headers.get('X-FitLab-Key')!=os.environ['FITLAB_API_KEY']:return self.reply({'error':'未授权'},403)
  try:
   if self.path=='/api/catalog':return self.reply(CATALOG)
   if self.path=='/api/engine-status':
    from nengine_adapter import bridge
    return self.reply({'provider':os.environ.get('FITLAB_CALCULATOR','nengine'),'status':bridge().discover()})
   if self.path=='/api/fighters':
    from nengine_adapter import fighter_catalog
    return self.reply(fighter_catalog())
   if re.fullmatch(r'/api/items/\d+',self.path):
    item=TYPES.get(int(self.path.rsplit('/',1)[1]))
    if item is None and os.environ.get('FITLAB_CALCULATOR','nengine')=='nengine':
     from nengine_catalog import indexed_item
     item=indexed_item(int(self.path.rsplit('/',1)[1]))
    if not item:return self.reply({'error':'物品不存在'},404)
    if os.environ.get('FITLAB_CALCULATOR','nengine')=='nengine':
     from nengine_catalog import item_metadata
     return self.reply(item_metadata(item))
    attrs=[dict(METADATA['attributes'].get(str(k),{}),value=v) for k,v in item['attrs'].items()]
    return self.reply({'attributes':attrs,'units':METADATA['units'],'effects':[METADATA['effects'].get(str(i),{}) for i in item['effects']],'description':METADATA['descriptions'].get(str(item['id']),{})})
   if self.path=='/api/health':return self.reply({'ready':True})
   if self.path=='/api/version':
    manifest=ROOT/('app-version.json' if (ROOT/'app-version.json').exists() else 'package.json')
    return self.reply({'version':json.loads(manifest.read_text(encoding='utf-8'))['version']})
   if self.path=='/api/storage':return self.reply({'directory':str(STATE.resolve())})
   if self.path=='/api/abyssal-instances':return self.reply(read_library().get('abyssalInstances',[]))
   if self.path=='/api/loadout-layout':return self.reply(read_library().get('loadoutLayout',{'revision':0,'folders':[],'order':[]}))
   if self.path=='/api/loadout-plans':return self.reply(read_library().get('loadoutPlans',[]))
   if self.path=='/api/library':return self.reply(read_library()['fits'])
   if self.path=='/api/prices':return self.reply(prices())
   if self.path=='/api/characters':return self.reply(characters())
   if self.path=='/api/eve/config':return self.reply({'configured':bool(sso_client()),'callback':eve_sso.CALLBACK,'scope':eve_sso.SCOPE})
   if urllib.parse.urlsplit(self.path).path=='/api/eve/callback':
    cookies=SimpleCookie(self.headers.get('Cookie',''));browser=cookies.get('fitlab-sso')
    try:
     c=eve_sso.finish(urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query),browser.value if browser else '')
     validate_fit({'shipId':587,'name':'skill-check','slots':[],'skills':c['skills']})
     with LOCK:
      lib=read_library();old=next((x for x in lib['characters'] if x['id']==c['id']),{});c.update(updatedAt=now(),revision=old.get('revision',0)+1)
      lib['characters']=[x for x in lib['characters'] if x['id']!=c['id']]+[c];write_library(lib)
     destination='/#characters?imported='+urllib.parse.quote(c['id'])
    except Exception:destination='/#characters?authError=1'
    self.send_response(303);self.send_header('Location',destination);self.send_header('Set-Cookie','fitlab-sso=; Path=/api/eve; HttpOnly; SameSite=Lax; Max-Age=0');self.send_header('Referrer-Policy','no-referrer');self.end_headers();return
   if self.path.startswith('/api/'):return self.reply({'error':'接口不存在'},404)
   path=self.path.split('?')[0]
   if not(path=='/' or re.fullmatch(r'/[\w-]+\.(html|css|js)',path) or re.fullmatch(r'/data/(full-catalog|market-icons|locale-game)\.json',path) or re.fullmatch(r'/locales/(source|en|zh-TW|ja)\.json',path) or re.fullmatch(r'/assets/[\w-]+\.png',path)):return self.reply({'error':'资源不存在'},404)
   return super().do_GET()
  except Exception as e:self.reply({'error':str(e)},503)
 def do_POST(self):
  global STATE
  if os.environ.get('FITLAB_API_KEY') and self.headers.get('X-FitLab-Key')!=os.environ['FITLAB_API_KEY']:return self.reply({'error':'未授权'},403)
  if self.headers.get('Origin')!='http://'+self.headers.get('Host',''):return self.reply({'error':'请求来源不匹配'},403)
  try:
   size=int(self.headers.get('Content-Length',0))
   if not 0<size<=2000000:raise ValueError('请求大小无效')
   body=json.loads(self.rfile.read(size))
   if self.path=='/api/storage/open':
    with LOCK:storage_location.open_directory(STATE)
    return self.reply({'opened':True})
   if self.path=='/api/storage':
    with LOCK:
     directory,backup=storage_location.migrate(STATE,body.get('directory'));STATE=directory
    return self.reply({'directory':str(directory),'backup':str(backup) if backup else None})
   if self.path=='/api/analyze':
    with LOCK:return self.reply(analyze(body))
   if self.path=='/api/eve/login':
    client=sso_client()
    url,browser=eve_sso.begin(client)
    data=json.dumps({'url':url}).encode();self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(data)));self.send_header('Set-Cookie','fitlab-sso='+browser+'; Path=/api/eve; HttpOnly; SameSite=Lax; Max-Age=600');self.end_headers();self.wfile.write(data);return
   with LOCK:
    lib=read_library()
    if self.path=='/api/fit/scenarios':
     previous=next((x for x in lib['fits'] if x['id']==body.get('id')),None)
     if previous is None:return self.reply({'error':'请先保存装配，再保存情景。'},404)
     if body.get('revision')!=previous['revision']:return self.reply({'error':'装配已在另一窗口更新，请重新打开后再保存情景。'},409)
     fields=validate_presets(body)
     f=dict(previous,**fields);f['revision']=previous['revision']+1;f['updatedAt']=now()
     lib['fits']=[f if x['id']==f['id'] else x for x in lib['fits']];write_library(lib);return self.reply(f)
    if self.path=='/api/abyssal-instance':
     result=save_instance(body,lib,TYPES,now());write_library(lib);return self.reply(result)
    if self.path=='/api/abyssal-instance/delete':
     previous=next((x for x in lib.get('abyssalInstances',[]) if x['id']==body.get('id')),None)
     if not previous or previous['revision']!=body.get('revision'):raise ValueError('实例已修改或删除，请重新打开')
     lib['abyssalInstances']=[x for x in lib['abyssalInstances'] if x['id']!=previous['id']];write_library(lib);return self.reply({'deleted':True})
    if self.path=='/api/loadout-layout':
     result=save_layout(body,lib,now());write_library(lib);return self.reply(result)
    if self.path=='/api/loadout-plan':
     plan=save_plan(body,lib,now());write_library(lib);return self.reply(plan)
    if self.path=='/api/loadout-plan/delete':
     previous=next((p for p in lib.get('loadoutPlans',[]) if p['id']==body.get('id')),None)
     if previous is None:raise ValueError('方案不存在')
     if body.get('revision')!=previous['revision']:raise ValueError('方案已修改，请重新加载')
     lib['loadoutPlans']=[p for p in lib['loadoutPlans'] if p['id']!=previous['id']];write_library(lib);return self.reply({'deleted':True})
    if self.path=='/api/save':
     f=validate_fit(body);previous=next((x for x in lib['fits'] if x['id']==f.get('id')),None)
     if previous and f.get('revision')!=previous['revision']:return self.reply({'error':'此装配已在另一窗口修改，请重新打开后再编辑。'},409)
     if 'scenarios' in f:f.update(validate_presets(f))
     if not previous:f['id']=str(uuid.uuid4())
     f['revision']=(previous['revision'] if previous else 0)+1;f['updatedAt']=now();f['name']=f['name'].strip()
     lib['fits']=[x for x in lib['fits'] if x['id']!=f['id']]+[f];write_library(lib);return self.reply(f)
    if self.path=='/api/fit/delete':
     target=next((f for f in lib['fits'] if f['id']==body.get('id')),None)
     if not target:return self.reply({'error':'装配不存在或已经删除'},404)
     if body.get('revision')!=target.get('revision'):return self.reply({'error':'装配已在另一窗口修改，请刷新后重试。'},409)
     lib['fits']=[f for f in lib['fits'] if f['id']!=target['id']];write_library(lib);return self.reply({'deleted':True})
    if self.path=='/api/character':
     return self.reply(save_character(body,lib))
    if self.path=='/api/character/delete':
     c=next((x for x in lib['characters'] if x['id']==body.get('id')),None)
     if not c:raise ValueError('该角色不能删除')
     lib['characters']=[x for x in lib['characters'] if x['id']!=c['id']];write_library(lib);return self.reply({'deleted':True})
   self.reply({'error':'接口不存在'},404)
  except (ValueError,KeyError,TypeError) as e:self.reply({'error':str(e)},400)
  except Exception:self.reply({'error':'计算服务不可用或本机存储失败，请检查服务日志后重试。'},503)
if __name__=='__main__':
 print('FitLab: http://127.0.0.1:5207',flush=True)
 ThreadingHTTPServer(('127.0.0.1',5207),Handler).serve_forever()
