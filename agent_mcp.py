"""Small agent-facing MCP facade over public NEngine tools. No game formulas.

Run: python agent_mcp.py --engine PATH --state PATH
Native MCP remains unchanged. All actions use its public requests; no session-file reads.
"""
import argparse
import hashlib
import json
import sys
import tempfile
from pathlib import Path
from nengine_bridge import NEngineBridge, NEngineError


def obj(properties, required=()):
    return {'type':'object','properties':properties,'required':list(required),'additionalProperties':False}


S={'type':'string'}
I={'type':'integer'}
ANY={'type':'object'}
TOOLS=[
 {'name':'fitlab_battle','description':'Run battles directly from fitting sessions; never browse or construct scenario graphs. prepare: id,seed,seconds,ships[{sessionId,revision,id,team,position:{x,y,z},reservePerWeapon}]. Engine loads each weapon to its native magazine capacity, adds the explicitly requested reserve, starts at full HP/capacitor and zero velocity unless overridden. Returns verified draftId, conditions and ability roster. start: draftId,jobId,policyPreset="stationary-weapons-v1" (locks enemy, fires conventional weapons; no movement/repair/EWAR/drones), OR policies:{team:JavaScript source}. status/result/events/cancel: jobId; status can waitSeconds:10 to avoid frequent polling. Custom tick returns {memory,intents}; use fitlab_tools for advanced policies only. Identical jobId/request retries never start a second job.','inputSchema':obj({'action':{'type':'string','enum':['prepare','start','status','result','events','cancel']},'id':S,'seed':{'type':'integer','minimum':0},'seconds':{'type':'integer','minimum':1,'maximum':3600},'ships':{'type':'array','minItems':2,'maxItems':32,'items':obj({'sessionId':S,'revision':I,'id':S,'team':S,'position':obj({'x':{'type':'number'},'y':{'type':'number'},'z':{'type':'number'}},['x','y','z']),'velocity':ANY,'reservePerWeapon':I,'supplies':ANY,'options':ANY},['sessionId','revision','id','team','position'])},'draftId':S,'jobId':S,'waitSeconds':{'type':'number','minimum':0,'maximum':10},'policyPreset':{'type':'string','enum':['stationary-weapons-v1']},'policies':{'type':'object','additionalProperties':S},'offset':I,'limit':I},['action'])},
 {'name':'fitlab_status','description':'Start here. Returns source version, workflow and supported capability boundaries. UI does not need to be running.','inputSchema':obj({})},
 {'name':'fitlab_search','description':'Batch resolve up to 30 item names. Returns candidates, never silently picks an ambiguous match. Use returned typeId.','inputSchema':obj({'names':{'type':'array','items':S,'minItems':1,'maxItems':30},'categoryId':I},['names'])},
 {'name':'fitlab_fit','description':'Create/read/edit a durable fitting by sessionId. Read needs only sessionId. Create needs shipTypeId; optionally skillPreset="all5" resolves all published skills from the engine catalog, or supply explicit skills (default untrained). Edit needs revision, stable requestId and commands. Example remove: {kind:"remove",instanceId:"gun1"}; install: {kind:"install",item:{id:"gun1",typeId:484,slotIndex:0,active:true}}. Retry uncertain writes with the SAME revision/requestId/commands. No automatic save. For combat pass these sessions to fitlab_battle prepare; do not construct scenario graphs.','inputSchema':obj({'action':{'type':'string','enum':['create','read','edit','undo','redo','save']},'sessionId':S,'shipTypeId':I,'name':S,'skillPreset':{'type':'string','enum':['all5','untrained']},'skills':{'type':'object','additionalProperties':{'type':'integer','minimum':0,'maximum':5}},'revision':I,'requestId':S,'commands':{'type':'array','items':ANY},'context':ANY},['action','sessionId'])},
 {'name':'fitlab_tools','description':'Discover native tool descriptions on demand. No name lists all native tools; name returns description and a reference to its inputSchema. Large schema objects are explored with fitlab_result JSON pointers. Battle simulation exists; fullCombatSupported=false means incomplete EVE coverage, not absence of battle tools.','inputSchema':obj({'name':S})},
 {'name':'fitlab_call','description':'Invoke an advanced native tool using its discovered schema. Arguments are forwarded unchanged, including revision/requestId. Results are bounded and stored by reference, never silently truncated. For ordinary fits prefer fitlab_fit.','inputSchema':obj({'name':S,'arguments':ANY},['name','arguments'])},
 {'name':'fitlab_result','description':'Read a stored result by opaque resultId and JSON pointer (for example /analysis/weapons). Large objects return paged keys and child pointers; arrays return paged entries. References are content-addressed and persist across restarts.','inputSchema':obj({'resultId':S,'pointer':S,'offset':{'type':'integer','minimum':0},'limit':{'type':'integer','minimum':1,'maximum':30}},['resultId'])}
]

# Task-oriented read/state actions; the native game contract remains r60.
TOOLS.append({'name':'fitlab_item','description':'Read compact official item traits, activation configuration and common BASE attributes. typeId is required. Optional names accepts exact/internal/localized attribute names or numeric IDs as strings (for example shieldCapacity, maxVelocity, CPU). Ambiguous names return candidates. For skilled/fitted values use fitlab_fit attributes; for overall fitted stats use fitlab_fit read.','inputSchema':obj({'typeId':I,'names':{'type':'array','items':S,'minItems':1,'maxItems':30},'locale':S},['typeId'])})
fit_tool=next(t for t in TOOLS if t['name']=='fitlab_fit')
fit_tool['inputSchema']['properties']['action']['enum']+=['attributes','state']
fit_tool['inputSchema']['properties'].update({'names':{'type':'array','items':S,'minItems':1,'maxItems':30},'itemId':S,'locale':S,'instanceIds':{'type':'array','items':S,'minItems':1,'maxItems':100},'active':{'type':'boolean'},'online':{'type':'boolean'},'overheated':{'type':'boolean'}})
fit_tool['description']+=' Create returns calculated summary immediately. read returns fitted stats WITHOUT JSON-pointer browsing. attributes: sessionId,names,optional itemId (ship by default or module.<id>) gives named fitted values. state: sessionId,revision,requestId,instanceIds and explicit active/online/overheated changes states directly. Passive modules must have active:false; do not unload/reinstall just to change state.'
battle_tool=next(t for t in TOOLS if t['name']=='fitlab_battle')
battle_tool['inputSchema']['properties'].update({'kinds':{'type':'array','items':S,'maxItems':20},'sourceId':S,'targetId':S})
battle_tool['description']+=' result includes destruction times, last-damage snapshots and end-time HP separately. End-time HP is NOT victory-time HP. events supports kinds (e.g. ShipDestroyed, MissileApplicationResolved), sourceId,targetId; no manual offset guessing needed. Pending results return ready:false and the next status call. Unarmed targets need no reservePerWeapon.'


def encoded(value):
    return json.dumps(value,ensure_ascii=False,separators=(',',':'),allow_nan=False)


class AgentMcp:
    def __init__(self,engine,state,mcp_dll=None):
        self.bridge=NEngineBridge(root=engine,state=state,mcp_dll=mcp_dll)
        self.results=Path(state)/'agent-results'
        self.results.mkdir(parents=True,exist_ok=True)
        self.schemas=None

    def close(self):
        self.bridge.close()

    def native(self,name,args):
        if self.schemas is None:
            with self.bridge.lock:
                self.bridge.discover()
                self.schemas={t['name']:t for t in self.bridge.tools}
        if name not in self.schemas:raise ValueError('Unknown native tool; use fitlab_tools.')
        # No mutation caching. The public engine remains responsible for all locking/receipts.
        with self.bridge.lock:
            self.bridge._start()
            raw=self.bridge._rpc('tools/call',{'name':name,'arguments':args})
        payload=raw.get('structuredContent')
        if payload is None:
            text='\n'.join(c['text'] for c in raw.get('content',[]) if c['type']=='text')
            try:payload=json.loads(text)
            except json.JSONDecodeError:
                raise NEngineError({'ok':False,'error':{'code':'NATIVE_TOOL_ERROR','message':text or 'Native tool returned no payload'}})
        if raw.get('isError') or not payload.get('ok'):raise NEngineError(payload)
        return payload['result']

    def store(self,value,durable=False):
        data=encoded(value).encode('utf-8');identity=hashlib.sha256(data).hexdigest()
        directory=self.results.parent/'agent-battle-drafts' if durable else self.results
        directory.mkdir(parents=True,exist_ok=True)
        path=directory/(identity+'.json')
        if not path.exists():
            with tempfile.NamedTemporaryFile(dir=directory,suffix='.tmp',delete=False) as stream:
                stream.write(data);tmp=Path(stream.name)
            try:tmp.replace(path)
            finally:tmp.unlink(missing_ok=True)
        path.touch()
        if durable:return identity
        # Only facade-owned result snapshots; native sessions are never removed.
        files=[]
        for candidate in self.results.glob('*.json'):
            try:files.append((candidate.stat().st_mtime,candidate))
            except FileNotFoundError:pass
        for _,old in sorted(files,reverse=True)[128:]:
            if old!=path:old.unlink(missing_ok=True)
        return identity

    def bounded(self,value):
        ref=self.store(value)
        if len(encoded(value))<=10000:return {'resultId':ref,'value':value}
        return {'resultId':ref,'detail':'Use fitlab_result; complete result retained, not truncated.',
                'keys':list(value) if isinstance(value,dict) else None,'items':len(value) if isinstance(value,list) else None}

    def load_result(self,identity):
        if len(identity)!=64 or any(c not in '0123456789abcdef' for c in identity):raise ValueError('Invalid resultId')
        path=self.results/(identity+'.json')
        if not path.exists():path=self.results.parent/'agent-battle-drafts'/(identity+'.json')
        if not path.exists():raise ValueError('Result reference expired; repeat the read, never repeat a write with a new requestId.')
        raw=path.read_bytes()
        if hashlib.sha256(raw).hexdigest()!=identity:raise ValueError('Result integrity check failed')
        return json.loads(raw)

    def read_result(self,args):
        value=self.load_result(args['resultId']);pointer=args.get('pointer','')
        if pointer and not pointer.startswith('/'):raise ValueError('Use an RFC 6901 JSON pointer.')
        for part in pointer.split('/')[1:]:
            key=part.replace('~1','/').replace('~0','~')
            try:value=value[int(key)] if isinstance(value,list) else value[key]
            except (KeyError,IndexError,TypeError,ValueError):
                raise NEngineError({'ok':False,'error':{'code':'RESULT_POINTER','message':'Path does not exist: '+pointer,'availableKeys':list(value)[:30] if isinstance(value,dict) else None,'keyCount':len(value) if isinstance(value,dict) else None},'recovery':'For fitting statistics use fitlab_fit read with sessionId. Raw fit inputs contain shipTypeId, not a ship object.'})
        offset=args.get('offset',0);limit=args.get('limit',20)
        if type(offset)!=int or offset<0 or type(limit)!=int or not 1<=limit<=30:raise ValueError('Invalid pagination')
        if len(encoded(value))<=10000 and not offset:return {'pointer':pointer,'value':value}
        def child(k):return pointer+'/'+str(k).replace('~','~0').replace('/','~1')
        if isinstance(value,dict):
            keys=list(value);entries=[]
            for k in keys[offset:offset+limit]:
                v=value[k];entry={'key':k,'pointer':child(k)}
                if len(encoded(v))<=400:entry['value']=v
                elif isinstance(v,dict):entry['schemaOutline']={n:v[n] for n in ('type','required','enum','description','default') if n in v and len(encoded(v[n]))<=500}
                entries.append(entry)
            return {'pointer':pointer,'total':len(keys),'entries':entries,'nextOffset':offset+limit if offset+limit<len(keys) else None}
        if isinstance(value,list):
            entries=[]
            for i in range(offset,min(len(value),offset+limit)):
                v=value[i];entries.append({'pointer':child(i),'value':v} if len(encoded(v))<=1000 else {'pointer':child(i),'detail':'Read this pointer'})
            return {'pointer':pointer,'total':len(value),'entries':entries,'nextOffset':offset+limit if offset+limit<len(value) else None}
        if isinstance(value,str):return {'pointer':pointer,'text':value[offset:offset+4000],'nextOffset':offset+4000 if offset+4000<len(value) else None}
        return {'pointer':pointer,'value':value}

    def summary(self,result,session_id,revision):
        analysis=result.get('analysis',result);fit=result.get('fit',{})
        fields=['fitHash','buildNumber','scope','staticCoverageComplete','resources','slotUsage','nominalDps','nominalDpsUnavailableReason','defense','capacitor','motion','motionUnsupportedReason','signatureRadiusMeters']
        summary={k:analysis[k] for k in fields if k in analysis}
        for kind in ('errors','warnings'):
            issues=analysis.get(kind,[])
            summary[kind]={'count':len(issues),'first':issues[:5],'remaining':max(0,len(issues)-5),'fullPointer':'/analysis/'+kind}
        summary['unsupported']=[c for c in analysis.get('coverage',[]) if c.get('status')=='unsupported_static']
        summary['outputSelection']=(analysis.get('outputContributions') or {}).get('selection')
        summary['modules']=[{k:i.get(k) for k in ('id','typeId','slotIndex','chargeTypeId','online','active','overheated')} for i in fit.get('items',[])]
        summary['readiness']={'staticValid':not analysis.get('errors') and analysis.get('staticCoverageComplete',False) and not any(w.get('code')=='RESOURCE_EXCEEDED' for w in analysis.get('warnings',[])),'battle':'requires fitlab_battle prepare; static validity alone is not runtime admission'}
        passive=[e['instanceId'] for e in analysis.get('errors',[]) if e.get('code')=='MODULE_ACTIVE_NOT_APPLICABLE' and e.get('instanceId')]
        if passive:summary['suggestedCorrection']={'tool':'fitlab_fit','arguments':{'action':'state','sessionId':session_id,'revision':revision,'instanceIds':passive,'active':False},'note':'Supply a fresh requestId for this correction; no uninstall/reinstall needed.'}
        return {'sessionId':session_id,'revision':revision,'appliedRevision':result.get('appliedRevision'),
                'replayed':result.get('replayed',False),'summary':self.bounded(summary),'fullResultId':self.store(result),
                'detailPolicy':'Engine values only; full result available by reference. Missing values are not zero. Nominal DPS is not actual applied or capacitor-sustainable DPS.'}

    def call(self,name,args):
        if not isinstance(args,dict):raise ValueError('Tool arguments must be an object')
        if name=='fitlab_item':
            from agent_attributes import item
            return item(self,args)
        if name=='fitlab_battle':
            from agent_battle import battle
            return battle(self,args)
        if name=='fitlab_status':
            s=self.native('engine_status',{})
            return {'engineVersion':s['engineVersion'],'source':s['source']['source']['buildNumber'],'contractRevision':s['publicContract']['revision'],
                    'workflow':['fitlab_search (batch names)','fitlab_fit create','fitlab_fit edit (commands batch)','fitlab_fit read','fitlab_fit save'],
                    'capabilities':{'fitting':'supported subsets; inspect errors and coverage','battle':'Use fitlab_battle prepare with fitting sessions, then start/status/result. Engine constructs all graphs. Coverage is incomplete; prepare validates admission.','imageExport':'not available through this MCP'},
                    'references':'Last 128 result snapshots retained. UI need not be running. Unknown write outcome: retry identical requestId/revision/commands.'}
        if name=='fitlab_search':
            names=args['names']
            if not isinstance(names,list) or not 1<=len(names)<=30 or any(not isinstance(n,str) or not n.strip() for n in names):raise ValueError('Provide 1..30 nonempty names')
            rows=[]
            for query in names:
                params={'text':query,'limit':5}
                if 'categoryId' in args:params['categoryId']=args['categoryId']
                page=self.native('catalog_search',{'query':params})
                rows.append({'query':query,'total':page['total'],'nextCursor':page.get('nextCursor'),
                             'candidates':[{'typeId':i['typeId'],'names':{k:v for k,v in i['names'].items() if k in ('en','zh')},'groupId':i['groupId'],'categoryId':i['categoryId']} for i in page['items']]})
            return self.bounded(rows)
        if name=='fitlab_fit':
            action=args['action'];sid=args['sessionId']
            if action=='attributes':
                if set(args)-{'action','sessionId','names','itemId','locale'}:raise ValueError('Unexpected attribute query fields')
                if not isinstance(args.get('names'),list) or not 1<=len(args['names'])<=30:raise ValueError('Provide 1..30 attribute names')
                from agent_attributes import fitted
                return fitted(self,args)
            if action=='state':
                if set(args)-{'action','sessionId','revision','requestId','instanceIds','active','online','overheated'}:raise ValueError('Unexpected state fields')
                ids=args.get('instanceIds');fields=[k for k in ('online','active','overheated') if k in args]
                if not isinstance(ids,list) or not 1<=len(ids)<=100 or not fields or any(type(args[k])!=bool for k in fields):raise ValueError('Provide instanceIds and boolean state fields')
                if len(ids)!=len(set(ids)) or any(not isinstance(i,str) for i in ids):raise ValueError('Use unique instanceIds')
                return self.call('fitlab_fit',{'action':'edit','sessionId':sid,'revision':args['revision'],'requestId':args['requestId'],
                    'commands':[{'kind':'set'+k[0].upper()+k[1:],'instanceId':i,k:args[k]} for i in ids for k in fields]})
            allowed={'action','sessionId'}|({'shipTypeId','name','skills','skillPreset'} if action=='create' else {'context'} if action=='read' else {'revision','requestId','commands','context'} if action=='edit' else {'revision','requestId'} if action=='save' else {'revision','requestId','context'})
            if set(args)-allowed:raise ValueError('Unexpected fields for '+action)
            if action=='create':
                status=self.native('engine_status',{})
                if 'skillPreset' in args and 'skills' in args:raise ValueError('Choose skills or skillPreset, not both.')
                skills=args.get('skills',{})
                if args.get('skillPreset')=='all5':
                    skills={};cursor=None
                    while True:
                        page=self.native('catalog_search',{'query':{'text':'','categoryId':16,'limit':100,'cursor':cursor}})
                        skills.update({str(i['typeId']):5 for i in page['items']});cursor=page.get('nextCursor')
                        if cursor is None:break
                elif args.get('skillPreset','untrained')!='untrained':raise ValueError('Unknown skill preset')
                fit={'id':sid,'name':args.get('name',sid),'buildNumber':status['source']['source']['buildNumber'],'shipTypeId':args['shipTypeId'],'omittedSkills':'untrained','skills':skills,'items':[]}
                result=self.native('fit_create',{'sessionId':sid,'fit':fit,'allowIncompleteDraft':True})
                projected=self.native('fit_workbench',{'request':{'fit':fit}})
                return {**self.summary(projected,sid,result['revision']),'shipTypeId':fit['shipTypeId'],'skillCount':len(skills),'skillPolicy':args.get('skillPreset','explicit_skills' if skills else 'untrained'),
                        'nextCall':{'tool':'fitlab_fit','arguments':{'action':'read','sessionId':sid}},'note':'summary contains calculated stats. fullResultId is analysis; do not guess a /ship pointer. Edit with revision/requestId/commands.'}
            if action=='read':
                snap=self.native('fit_inspect',{'sessionId':sid})
                result=self.native('fit_workbench',{'request':{'fit':snap['session']['working'],'context':args.get('context',{})}})
                return self.summary(result,sid,snap['session']['revision'])
            if action not in ('edit','undo','redo','save'):raise ValueError('Unknown action')
            request={'sessionId':sid,'revision':args['revision'],'requestId':args['requestId'],'operation':'apply' if action=='edit' else action}
            if action=='save':
                result=self.native('fit_execute',request)
                return {'sessionId':sid,**{k:result.get(k) for k in ('revision','appliedRevision','appliedFitHash','replayed')},'fullResultId':self.store(result)}
            if action=='edit':request['commands']=args['commands']
            if 'context' in args:request['context']=args['context']
            result=self.native('fit_workbench',{'request':request})
            return self.summary(result,sid,result['revision'])
        if name=='fitlab_tools':
            if self.schemas is None:self.native('engine_status',{})
            if 'name' in args:
                schema=self.schemas[args['name']]
                result={'name':schema['name'],'description':schema.get('description'),'schema':self.bounded(schema['inputSchema'])}
                if args['name'] in ('battle_start','battle_assemble'):result['recommended']='Use fitlab_battle prepare with sessions, then start. Do not browse or hand-build low-level scenario graphs.'
                return result
            return [{'name':n,'description':s.get('description','')} for n,s in self.schemas.items()]
        if name=='fitlab_call':return self.bounded(self.native(args['name'],args['arguments']))
        if name=='fitlab_result':return self.read_result(args)
        raise ValueError('Unknown facade tool')


def error_payload(error,name,args):
    args=args if isinstance(args,dict) else {}
    payload=error.payload if isinstance(error,NEngineError) else {'ok':False,'error':{'code':'AGENT_REQUEST','message':str(error)}}
    if 'recovery' in payload:return payload
    code=payload.get('error',{}).get('code')
    if name=='fitlab_result':recovery='Use the available result keys, or fitlab_fit read for fitted statistics. This read did not change a session.'
    elif name=='fitlab_battle':recovery='Correct the named preparation field or fitting issue. For unpublished results poll status with waitSeconds:10; do not restart the battle.'
    elif code in ('OUTPUT_EXISTS','STALE_REVISION'):recovery='Read this session with fitlab_fit read; use its current revision. An existing session is not recreated.'
    elif name=='fitlab_fit' and args.get('action') in ('edit','state','undo','redo','save'):recovery='If the outcome is uncertain retry identical revision/requestId/commands. Passive module activation errors can be fixed with action:state, instanceIds and active:false; do not reinstall.'
    else:recovery='Correct the named input. This read does not need a transaction retry.'
    return {**payload,'recovery':recovery}


def serve(agent):
    for line in sys.stdin:
        try:
            message=json.loads(line)
            if 'id' not in message:continue
            method=message.get('method');params=message.get('params',{})
            if method=='initialize':
                result={'protocolVersion':params.get('protocolVersion','2025-03-26'),'capabilities':{'tools':{}},'serverInfo':{'name':'fitlab-agent','version':'3.0'},'instructions':'Use fitlab_item for official traits/base attributes, fitlab_fit read for fitted statistics and attributes for named fitted values. Do not browse raw fit pointers for stats. For combat use fitlab_battle prepare/start/status/result; result includes a timeline with destruction, last damage and simulation end distinguished. Never report end HP as HP at victory. Use events kinds/sourceId/targetId filters for details. Passive modules are online but not active; use fitlab_fit state to change state without reinstalling. Do not construct engine graphs. Single stationary experiments are not general ship strength conclusions.'}
            elif method=='ping':result={}
            elif method=='tools/list':result={'tools':TOOLS}
            elif method=='tools/call':
                try:payload={'ok':True,'result':agent.call(params['name'],params.get('arguments',{}))}
                except Exception as e:payload=error_payload(e,params['name'],params.get('arguments',{}))
                # Text is canonical for broad client support. JS signed zero is normalized here too.
                result={'isError':not payload['ok'],'content':[{'type':'text','text':encoded(payload)}]}
            else:
                print(encoded({'jsonrpc':'2.0','id':message['id'],'error':{'code':-32601,'message':'Method not found'}}),flush=True);continue
            print(encoded({'jsonrpc':'2.0','id':message['id'],'result':result}),flush=True)
        except (ValueError,KeyError,TypeError) as e:
            print(encoded({'jsonrpc':'2.0','id':None,'error':{'code':-32700,'message':str(e)}}),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--engine',required=True);parser.add_argument('--state',required=True);parser.add_argument('--mcp-dll');parser.add_argument('--call');parser.add_argument('--arguments');parser.add_argument('--out');args=parser.parse_args()
    sys.stdin.reconfigure(encoding='utf-8');sys.stdout.reconfigure(encoding='utf-8')
    agent=AgentMcp(args.engine,args.state,args.mcp_dll)
    try:
        if args.call:
            request=json.loads(Path(args.arguments).read_text(encoding='utf-8')) if args.arguments else {}
            try:result={'ok':True,'result':agent.call(args.call,request)}
            except Exception as e:result=error_payload(e,args.call,request)
            text=encoded(result)
            if args.out:Path(args.out).write_text(text,encoding='utf-8')
            else:print(text)
            if not result['ok']:raise SystemExit(1)
        else:serve(agent)
    finally:agent.close()
