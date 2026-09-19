"""EVE native-app SSO: one-shot skill snapshot import using PKCE, no stored tokens."""
import base64, hashlib, json, secrets, threading, time, urllib.parse, urllib.request, urllib.error
from datetime import datetime, timezone
CLIENT_ID='4799765cb7bc470fa6477b18a64c4df2'
SCOPE='esi-skills.read_skills.v1'
CALLBACK='http://127.0.0.1:5207/api/eve/callback'
_pending={}
_lock=threading.Lock()

def begin(client_id):
 state=secrets.token_urlsafe(32);browser=secrets.token_urlsafe(32);verifier=secrets.token_urlsafe(48)
 challenge=base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip('=')
 with _lock:
  for k,v in list(_pending.items()):
   if v['expires']<time.time():del _pending[k]
  _pending[state]={'browser':browser,'verifier':verifier,'client':client_id,'expires':time.time()+600}
 return 'https://login.eveonline.com/v2/oauth/authorize/?'+urllib.parse.urlencode({'response_type':'code','redirect_uri':CALLBACK,'client_id':client_id,'scope':SCOPE,'code_challenge':challenge,'code_challenge_method':'S256','state':state}),browser

def request_json(url, data=None, token=None):
 headers={'User-Agent':'EVE-FitLab/1.0','Accept':'application/json'}
 if token:headers['Authorization']='Bearer '+token
 if data is not None:headers['Content-Type']='application/x-www-form-urlencoded';data=urllib.parse.urlencode(data).encode()
 with urllib.request.urlopen(urllib.request.Request(url,data=data,headers=headers),timeout=25) as r:return json.load(r)

def request_skills(cid,token):
 url='https://esi.evetech.net/latest/characters/'+str(cid)+'/skills/?datasource=tranquility'
 request=urllib.request.Request(url,headers={'User-Agent':'EVE-FitLab/1.0','Accept':'application/json','Authorization':'Bearer '+token})
 with urllib.request.urlopen(request,timeout=25) as response:
  raw=response.read(4*1024*1024+1)
  if len(raw)>4*1024*1024:raise ValueError('官网技能响应过大，未导入。')
  return json.loads(raw),{'provider':'ESI Tranquility','url':url,'fetchedAt':datetime.now(timezone.utc).isoformat(),'contentSha256':hashlib.sha256(raw).hexdigest(),'providerDate':response.headers.get('Date'),'expiresAt':response.headers.get('Expires')}

def finish(query,browser):
 state=query.get('state',[''])[0]
 with _lock:
  entry=_pending.get(state)
  if not entry or entry['expires']<time.time() or not secrets.compare_digest(entry['browser'],browser):raise ValueError('授权已过期或浏览器不匹配，请从当前 FitLab 重新授权。')
  del _pending[state]
 if query.get('error'):raise ValueError('已取消官网授权。')
 code=query.get('code',[''])[0]
 if not code:raise ValueError('官网未返回授权码，请重新登录。')
 try:
  tokens=request_json('https://login.eveonline.com/v2/oauth/token',{'grant_type':'authorization_code','code':code,'client_id':entry['client'],'code_verifier':entry['verifier'],'redirect_uri':CALLBACK})
  token=tokens['access_token']
  # Validate with the issuer instead of trusting decoded JWT claims.
  identity=request_json('https://login.eveonline.com/oauth/verify',token=token)
  if SCOPE not in identity.get('Scopes','').split():raise ValueError('未授予读取技能权限。')
  cid=int(identity['CharacterID'])
  if cid<=0:raise ValueError('官网返回的角色 ID 无效。')
  data,source=request_skills(cid,token)
  rows=[{'skillTypeId':r['skill_id'],'activeLevel':r['active_skill_level'],'trainedLevel':r['trained_skill_level'],'skillPoints':r.get('skillpoints_in_skill')} for r in data['skills']]
  snapshot={'characterId':cid,'name':identity['CharacterName'],'source':source,'skills':rows,'totalSp':data.get('total_sp'),'unallocatedSp':data.get('unallocated_sp')}
  return {'id':'eve:'+str(cid),'eveCharacterId':cid,'name':identity['CharacterName'],'source':'EVE 官网','skills':[{'skillTypeId':r['skillTypeId'],'level':r['activeLevel']} for r in rows],'skillSnapshot':snapshot}
 except urllib.error.HTTPError as error:raise ValueError('官网授权或 ESI 技能读取失败（HTTP '+str(error.code)+'），请重新授权。') from None
 except ValueError:raise
 except Exception:raise ValueError('官网授权或技能读取失败，请稍后重新登录。') from None

if __name__=='__main__':
 import argparse
 from pathlib import Path
 parser=argparse.ArgumentParser(description='Export a saved ESI snapshot for character_skill_snapshot / sde-character-skills; no tokens.')
 parser.add_argument('--library',required=True);parser.add_argument('--character',required=True);parser.add_argument('--out',required=True);parser.add_argument('--build',type=int,default=3503375)
 args=parser.parse_args();library=json.loads(Path(args.library).read_text(encoding='utf-8-sig'))
 character=next((c for c in library['characters'] if c['id']==args.character),None)
 if not character or not character.get('skillSnapshot'):parser.error('Character has no imported ESI skill snapshot.')
 Path(args.out).write_text(json.dumps({'buildNumber':args.build,'snapshot':character['skillSnapshot']},ensure_ascii=False,indent=2),encoding='utf-8')
