"""EVE native-app SSO: one-shot skill import using PKCE, no stored tokens."""
import base64, hashlib, json, secrets, threading, time, urllib.parse, urllib.request
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

def finish(query,browser):
 state=query.get('state',[''])[0]
 with _lock:
  entry=_pending.get(state)
  if not entry or entry['expires']<time.time() or not secrets.compare_digest(entry['browser'],browser):raise ValueError('授权已过期或浏览器不匹配，请重新登录。')
  del _pending[state]
 if query.get('error'):raise ValueError('已取消官网授权。')
 code=query.get('code',[''])[0]
 if not code:raise ValueError('官网未返回授权码，请重新登录。')
 try:
  tokens=request_json('https://login.eveonline.com/v2/oauth/token',{'grant_type':'authorization_code','code':code,'client_id':entry['client'],'code_verifier':entry['verifier'],'redirect_uri':CALLBACK})
  token=tokens['access_token']
  # Ask the issuing authority to validate the token; never trust decoded JWT claims.
  identity=request_json('https://login.eveonline.com/oauth/verify',token=token)
  if SCOPE not in identity.get('Scopes','').split():raise ValueError('未授予读取技能权限。')
  cid=int(identity['CharacterID'])
  data=request_json('https://esi.evetech.net/latest/characters/'+str(cid)+'/skills/?datasource=tranquility',token=token)
  return {'id':'eve:'+str(cid),'eveCharacterId':cid,'name':identity['CharacterName'],'source':'EVE 官网','skills':[{'skillTypeId':r['skill_id'],'level':r['active_skill_level']} for r in data['skills']]}
 except ValueError:raise
 except Exception:raise ValueError('官网授权或技能读取失败，请稍后重新登录。') from None
