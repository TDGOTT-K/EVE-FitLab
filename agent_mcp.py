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
 {'name':'fitlab_status','description':'Start here. Returns source version, workflow and supported capability boundaries. UI does not need to be running.','inputSchema':obj({})},
 {'name':'fitlab_search','description':'Batch resolve up to 30 item names. Returns candidates, never silently picks an ambiguous match. Use returned typeId.','inputSchema':obj({'names':{'type':'array','items':S,'minItems':1,'maxItems':30},'categoryId':I},['names'])},
 {'name':'fitlab_fit','description':'Create/read/edit a durable fitting by sessionId. Read needs only sessionId. Create needs shipTypeId; omitted skills are explicitly untrained. Edit needs revision, stable requestId and commands. Example remove: {kind:"remove",instanceId:"gun1"}; install: {kind:"install",item:{id:"gun1",typeId:484,slotIndex:0,active:true}}. Retry uncertain writes with the SAME revision/requestId/commands. Returns engine summary and full-result reference; no automatic save. Use fitlab_tools for advanced command schema.','inputSchema':obj({'action':{'type':'string','enum':['create','read','edit','undo','redo','save']},'sessionId':S,'shipTypeId':I,'name':S,'skills':{'type':'object','additionalProperties':{'type':'integer','minimum':0,'maximum':5}},'revision':I,'requestId':S,'commands':{'type':'array','items':ANY},'context':ANY},['action','sessionId'])},
 {'name':'fitlab_tools','description':'Discover native tool descriptions on demand. No name lists all native tools; name returns description and a reference to its inputSchema. Large schema objects are explored with fitlab_result JSON pointers. Battle simulation exists; fullCombatSupported=false means incomplete EVE coverage, not absence of battle tools.','inputSchema':obj({'name':S})},
 {'name':'fitlab_call','description':'Invoke an advanced native tool using its discovered schema. Arguments are forwarded unchanged, including revision/requestId. Results are bounded and stored by reference, never silently truncated. For ordinary fits prefer fitlab_fit.','inputSchema':obj({'name':S,'arguments':ANY},['name','arguments'])},
 {'name':'fitlab_result','description':'Read a stored result by opaque resultId and JSON pointer (for example /analysis/weapons). Large objects return paged keys and child pointers; arrays return paged entries. References are content-addressed and persist across restarts.','inputSchema':obj({'resultId':S,'pointer':S,'offset':{'type':'integer','minimum':0},'limit':{'type':'integer','minimum':1,'maximum':30}},['resultId'])}
]


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
        if payload is None:payload=json.loads(next(c['text'] for c in raw['content'] if c['type']=='text'))
        if raw.get('isError') or not payload.get('ok'):raise NEngineError(payload)
        return payload['result']

    def store(self,value):
        data=encoded(value).encode('utf-8');identity=hashlib.sha256(data).hexdigest()
        path=self.results/(identity+'.json')
        if not path.exists():
            with tempfile.NamedTemporaryFile(dir=self.results,suffix='.tmp',delete=False) as stream:
                stream.write(data);tmp=Path(stream.name)
            try:tmp.replace(path)
            finally:tmp.unlink(missing_ok=True)
        path.touch()
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

    def read_result(self,args):
        identity=args['resultId']
        if len(identity)!=64 or any(c not in '0123456789abcdef' for c in identity):raise ValueError('Invalid resultId')
        path=self.results/(identity+'.json')
        if not path.exists():raise ValueError('Result reference expired; repeat the read, never repeat a write with a new requestId.')
        value=json.loads(path.read_text(encoding='utf-8'));pointer=args.get('pointer','')
        if pointer and not pointer.startswith('/'):raise ValueError('Use an RFC 6901 JSON pointer.')
        for part in pointer.split('/')[1:]:
            key=part.replace('~1','/').replace('~0','~');value=value[int(key)] if isinstance(value,list) else value[key]
        offset=args.get('offset',0);limit=args.get('limit',20)
        if type(offset)!=int or offset<0 or type(limit)!=int or not 1<=limit<=30:raise ValueError('Invalid pagination')
        if len(encoded(value))<=10000 and not offset:return {'pointer':pointer,'value':value}
        def child(k):return pointer+'/'+str(k).replace('~','~0').replace('/','~1')
        if isinstance(value,dict):
            keys=list(value);return {'pointer':pointer,'total':len(keys),'entries':[{'key':k,'pointer':child(k)} for k in keys[offset:offset+limit]],'nextOffset':offset+limit if offset+limit<len(keys) else None}
        if isinstance(value,list):
            entries=[]
            for i in range(offset,min(len(value),offset+limit)):
                v=value[i];entries.append({'pointer':child(i),'value':v} if len(encoded(v))<=1000 else {'pointer':child(i),'detail':'Read this pointer'})
            return {'pointer':pointer,'total':len(value),'entries':entries,'nextOffset':offset+limit if offset+limit<len(value) else None}
        if isinstance(value,str):return {'pointer':pointer,'text':value[offset:offset+4000],'nextOffset':offset+4000 if offset+4000<len(value) else None}
        return {'pointer':pointer,'value':value}

    def summary(self,result,session_id,revision):
        analysis=result.get('analysis',result);fit=result.get('fit',{})
        fields=['fitHash','buildNumber','scope','staticCoverageComplete','resources','slotUsage','nominalDps','nominalDpsUnavailableReason','defense','capacitor','motion','motionUnsupportedReason']
        summary={k:analysis[k] for k in fields if k in analysis}
        for kind in ('errors','warnings'):
            issues=analysis.get(kind,[])
            summary[kind]={'count':len(issues),'first':issues[:5],'remaining':max(0,len(issues)-5),'fullPointer':'/analysis/'+kind}
        summary['unsupported']=[c for c in analysis.get('coverage',[]) if c.get('status')=='unsupported_static']
        summary['outputSelection']=(analysis.get('outputContributions') or {}).get('selection')
        summary['modules']=[{k:i.get(k) for k in ('id','typeId','slotIndex','chargeTypeId','online','active','overheated')} for i in fit.get('items',[])]
        return {'sessionId':session_id,'revision':revision,'appliedRevision':result.get('appliedRevision'),
                'replayed':result.get('replayed',False),'summary':self.bounded(summary),'fullResultId':self.store(result),
                'detailPolicy':'Engine values only; full result available by reference. Missing values are not zero. Nominal DPS is not actual applied or capacitor-sustainable DPS.'}

    def call(self,name,args):
        if name=='fitlab_status':
            s=self.native('engine_status',{})
            return {'engineVersion':s['engineVersion'],'source':s['source']['source']['buildNumber'],'contractRevision':s['publicContract']['revision'],
                    'workflow':['fitlab_search (batch names)','fitlab_fit create','fitlab_fit edit (commands batch)','fitlab_fit read','fitlab_fit save'],
                    'capabilities':{'fitting':'supported subsets; inspect errors and coverage','battle':'native battle_start/status/result available via fitlab_tools and fitlab_call; not full EVE coverage','imageExport':'not available through this MCP'},
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
            allowed={'action','sessionId'}|({'shipTypeId','name','skills'} if action=='create' else {'context'} if action=='read' else {'revision','requestId','commands','context'} if action=='edit' else {'revision','requestId'} if action=='save' else {'revision','requestId','context'})
            if set(args)-allowed:raise ValueError('Unexpected fields for '+action)
            if action=='create':
                status=self.native('engine_status',{})
                fit={'id':sid,'name':args.get('name',sid),'buildNumber':status['source']['source']['buildNumber'],'shipTypeId':args['shipTypeId'],'omittedSkills':'untrained','skills':args.get('skills',{}),'items':[]}
                result=self.native('fit_create',{'sessionId':sid,'fit':fit,'allowIncompleteDraft':True})
                return {'sessionId':sid,'revision':result['revision'],'fit':fit,'skillPolicy':'Only explicitly supplied skills are trained. Read to see calculated values.'}
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
                if args['name']=='battle_start':result['scriptExample']='function tick({observation,memory}) { return {memory,intents:[]}; }'
                return result
            return [{'name':n,'description':s.get('description','')} for n,s in self.schemas.items()]
        if name=='fitlab_call':return self.bounded(self.native(args['name'],args['arguments']))
        if name=='fitlab_result':return self.read_result(args)
        raise ValueError('Unknown facade tool')


def serve(agent):
    for line in sys.stdin:
        try:
            message=json.loads(line)
            if 'id' not in message:continue
            method=message.get('method');params=message.get('params',{})
            if method=='initialize':
                result={'protocolVersion':params.get('protocolVersion','2025-03-26'),'capabilities':{'tools':{}},'serverInfo':{'name':'fitlab-agent','version':'1.0'},'instructions':'Use fitlab_status then batch search and session-based fitlab_fit. Inspect errors and coverage. Full details are referenced, not truncated. Discover advanced native schemas only when needed.'}
            elif method=='ping':result={}
            elif method=='tools/list':result={'tools':TOOLS}
            elif method=='tools/call':
                try:payload={'ok':True,'result':agent.call(params['name'],params.get('arguments',{}))}
                except NEngineError as e:payload={**e.payload,'recovery':'For uncertain writes retry the identical requestId/revision/commands; read session for current revision. Validation errors require correcting the input.'}
                except Exception as e:payload={'ok':False,'error':{'code':'AGENT_REQUEST','message':str(e)},'recovery':'Check fitlab_tools schema. If a write may have started, reuse its requestId; do not invent a new retry ID.'}
                # Text is canonical for broad client support. JS signed zero is normalized here too.
                result={'isError':not payload['ok'],'content':[{'type':'text','text':encoded(payload)}]}
            else:
                print(encoded({'jsonrpc':'2.0','id':message['id'],'error':{'code':-32601,'message':'Method not found'}}),flush=True);continue
            print(encoded({'jsonrpc':'2.0','id':message['id'],'result':result}),flush=True)
        except (ValueError,KeyError,TypeError) as e:
            print(encoded({'jsonrpc':'2.0','id':None,'error':{'code':-32700,'message':str(e)}}),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--engine',required=True);parser.add_argument('--state',required=True);parser.add_argument('--mcp-dll');args=parser.parse_args()
    sys.stdin.reconfigure(encoding='utf-8');sys.stdout.reconfigure(encoding='utf-8')
    agent=AgentMcp(args.engine,args.state,args.mcp_dll)
    try:serve(agent)
    finally:agent.close()
