"""Single operation registry shared by MCP discovery, CLI help and validation."""
S={'type':'string'}
I={'type':'integer'}
N={'type':'number'}
B={'type':'boolean'}
O={'type':'object'}

def obj(p,required=()):return {'type':'object','properties':p,'required':list(required),'additionalProperties':False}
def arr(item,maximum=100,minimum=0):return {'type':'array','items':item,'minItems':minimum,'maxItems':maximum}
def enum(*v):return {'type':'string','enum':list(v)}
def integer(low,high):return {'type':'integer','minimum':low,'maximum':high}

IDENTITY={'typeId':integer(1,2147483647),'name':S}
QUERY=obj({'text':S,'groupId':I,'categoryId':I,'metaGroupId':I,'familyOfTypeId':I,'publishedOnly':B,
    'filters':arr(obj({'attribute':S,'min':N,'max':N},['attribute']),20),'attributes':arr(S,20),'sortBy':S,'descending':B,
    'attributeSearch':S,'limit':integer(1,30),'cursor':S})
ITEM=obj({'id':S,'typeId':integer(1,2147483647),'slotIndex':integer(0,31),'chargeTypeId':{'type':['integer','null']},
          'online':B,'active':B,'overheated':B,'mutation':O},['id','typeId','slotIndex'])
CHANGE=obj({'kind':enum('install','remove','setCharge','setOnline','setActive','setOverheated','setSkills','setName','setTags'),
    'item':ITEM,'instanceId':S,'chargeTypeId':{'type':['integer','null']},'online':B,'active':B,'overheated':B,
    'skills':{'type':'object','additionalProperties':integer(0,5)},'name':S,'tags':arr(S,30)},['kind'])
# Collection setters replace the named collection, preserving all other fit fields.
COLLECTIONS={
 'fighters':arr(obj({'id':S,'typeId':I,'memberIds':arr(S,12,1),'deployed':B,'location':enum('reserve','tube'),'tubeIndex':integer(0,11)},['id','typeId','memberIds','deployed']),100),
 'drones':arr(obj({'id':S,'typeId':I,'deployed':B,'mutation':O},['id','typeId','deployed']),100),
 'subsystems':arr(obj({'id':S,'typeId':I},['id','typeId']),4),
 'implants':arr(obj({'id':S,'typeId':I},['id','typeId']),10),
 'boosters':arr(obj({'id':S,'typeId':I,'enabledSideEffects':arr(I,20)},['id','typeId']),32),
}
CHANGE['properties']['kind']['enum'] += ['set'+k[0].upper()+k[1:] for k in COLLECTIONS]
CHANGE['properties'].update(COLLECTIONS)
CHANGE_FIELDS={'install':['item'],'remove':['instanceId'],'setCharge':['instanceId','chargeTypeId'],
 'setOnline':['instanceId','online'],'setActive':['instanceId','active'],'setOverheated':['instanceId','overheated'],
 'setSkills':['skills'],'setName':['name'],'setTags':['tags'],**{'set'+k[0].upper()+k[1:]:[k] for k in COLLECTIONS}}
CHANGE['oneOf']=[obj({'kind':{'const':kind},**{field:CHANGE['properties'][field] for field in fields}},['kind',*fields]) for kind,fields in CHANGE_FIELDS.items()]
VEC=obj({'x':N,'y':N,'z':N},['x','y','z'])
TARGET=obj({'id':S,'distanceMeters':N,'signatureMeters':N,'speedMetersPerSecond':N,'angularRadiansPerSecond':N,'layer':O},
           ['distanceMeters','signatureMeters','speedMetersPerSecond','angularRadiansPerSecond'])
SHIP=obj({'sessionId':S,'revision':I,'id':S,'team':S,'position':VEC,'velocity':VEC,'reservePerWeapon':integer(0,1000000),'supplies':O,'options':O},
         ['sessionId','revision','id','team','position'])
PREPARE={'id':S,'seed':integer(0,2147483647),'seconds':integer(1,3600),'ships':arr(SHIP,32,2)}
POLICY={'policyPreset':enum('stationary-weapons-v1','stationary-weapons-defense-v1'),'policies':{'type':'object','additionalProperties':S}}
EDIT={'sessionId':S,'revision':integer(0,2147483647),'changes':arr(CHANGE,128,1)}
WAIT={'jobId':S,'waitSeconds':integer(0,60)}

# Operation descriptions explain outcome; schemas carry mechanics.
OPS={
 'catalog':{
  'describe':('Official description/ship skill bonuses and base attributes by name OR typeId. Exact name auto-resolves; ambiguity returns candidates.',obj({**IDENTITY,'attributes':arr(S,20),'locale':S})),
  'search':('Filter/sort explicit base attributes in native units. Follow next action for more. Missing filter values are excluded and counted.',obj({'query':QUERY},['query'])),
  'resolve':('Resolve multiple names without guessing ambiguous matches.',obj({'names':arr(S,30,1),'categoryId':I},['names'])),
  'attributes':('Discover official attribute IDs/names/units before filtering.',obj({'text':S,'limit':integer(1,30),'cursor':S},['text'])),
  'compare':('Compare the same named base attributes across items in one call.',obj({'typeIds':arr(I,10,1),'attributes':arr(S,20,1),'locale':S},['typeIds','attributes'])),
  'variants':('Official variation family, not guessed replacements.',obj({**IDENTITY,'limit':integer(1,30),'cursor':S})),
  'charges':('Discover declared ammunition/script groups; preview verifies the actual pair.',obj(IDENTITY)),
 },
 'fitting':{
  'roster':('Read actual drones/fighters/subsystems/implants/boosters and authoritative fitted bay/primary-output projections. setFighters/setDrones etc replace the whole named collection.',obj({'sessionId':S},['sessionId'])),
  'list':('Find durable fitting sessions and their revisions; working/saved metadata remain distinct. Changed library invalidates cursor.',obj({'text':S,'includeClosed':B,'limit':integer(1,30),'cursor':S})),
  'create':('Create durable fit; default skills untrained, optional all5. Returns calculated summary/revision.',obj({'sessionId':S,'shipTypeId':I,'name':S,'skillPreset':enum('untrained','all5'),'skills':{'type':'object','additionalProperties':integer(0,5)}},['sessionId','shipTypeId'])),
  'read':('Calculated fitted totals, modules and readiness.',obj({'sessionId':S},['sessionId'])),
  'edit':('Atomic changes at revision with stable requestId. install.item includes slotIndex, chargeTypeId and active. Passive modules use active:false. No automatic save.',obj({**EDIT,'requestId':S},['sessionId','revision','requestId','changes'])),
  'preview':('Evaluate the same changes without committing. Returns deltas and diagnostics.',obj(EDIT,['sessionId','revision','changes'])),
  'attributes':('Named fitted values for ship or module.<instanceId>/charge.<instanceId>.',obj({'sessionId':S,'attributes':arr(S,30,1),'itemId':S,'locale':S},['sessionId','attributes'])),
  'outputs':('List output contribution IDs, families and application metadata for curves/research.',obj({'sessionId':S},['sessionId'])),
  'curves':('Engine DPS curves: default online ship weapons or explicit contributionIds. Explicit target geometry or declared ideal reference. No combat or sustained-output claim.',obj({'sessionId':S,'contributionIds':arr(S,100,1),'target':TARGET,'metric':enum('appliedCycleDps','appliedLoadedCycleDps','effectiveCycleDps','effectiveLoadedCycleDps'),'intervals':integer(2,128)},['sessionId'])),
  'save':('Explicitly save the draft at revision.',obj({'sessionId':S,'revision':I,'requestId':S},['sessionId','revision','requestId'])),
 },
 'battle':{
  'policies':('Discover preset coverage and optionally check a prepared draft without changing fitting or starting a battle.',obj({'draftId':S})),
  'prepare':('Validate immutable fitting revisions, conditions and finite ammo; returns draftId and capability limits. Does not start.',obj(PREPARE,['id','seed','seconds','ships'])),
  'run':('Prepare and start an experiment in one call with stable jobId, then bounded wait. Reuse identical input after uncertain outcome. Conditions are retained with the job.',obj({**PREPARE,**POLICY,'jobId':S,'waitSeconds':integer(0,60)},['id','seed','seconds','ships','jobId'])),
  'start':('Start the prepared draft once; default stationary weapons only. No movement/repair/EWAR/drone policy implied.',obj({'draftId':S,'jobId':S,**POLICY,'waitSeconds':integer(0,60)},['draftId','jobId'])),
  'wait':('Wait up to 60 seconds; returns final report or actionable pending state. Failure is not combat defeat.',obj(WAIT,['jobId'])),
  'report':('Experiment-bound result: destruction time, last-damage snapshot, end snapshot and damage application are distinct. expectedDraftId rejects cross-experiment substitution.',obj({'jobId':S,'expectedDraftId':S},['jobId'])),
  'events':('Filtered authoritative events; next preserves filters and cursor.',obj({'jobId':S,'kinds':arr(S,20),'sourceId':S,'targetId':S,'offset':integer(0,100000),'limit':integer(1,100)},['jobId'])),
  'cancel':('Cooperative cancellation; partial checkpoint is not completed battle.',obj({'jobId':S},['jobId'])),
  'resume':('Resume only a compatible resumable checkpoint at expected revision. Keeps attempt identity.',obj({'jobId':S,'revision':I},['jobId','revision'])),
 },
 'jobs':{
  'list':('Discover job IDs/states/bytes and storage, including old results.',obj({'afterId':S,'limit':integer(1,30)})),
  'storage':('Quota, remaining bytes and next actions.',obj({})),
  'compact':('dryRun defaults true. Reclaim redundant recovery only for an inactive job at revision; final results and identities are retained. Apply only when cleanup is requested.',obj({'jobId':S,'revision':I,'dryRun':B},['jobId','revision'])),
 },
 'help':{
  'overview':('Task routes and runnable examples, from ship traits to weapons/curves and combat research.',obj({})),
  'operation':('Exact operation schema and example route.',obj({'domain':S,'operation':S},['domain','operation'])),
  'native':('Advanced native tool discovery. Omit name to list. Ordinary tasks use catalog/fitting/battle/jobs.',obj({'name':S})),
 },
 'status':{'read':('Version/source/coverage and storage health. No browser required.',obj({}))},
 'raw':{'call':('Advanced native escape hatch; arguments follow help.native schema, with native side-effect semantics.',obj({'name':S,'arguments':O},['name','arguments']))},
 'result':{'read':('Page a full detail reference; ordinary results are already task-shaped.',obj({'resultId':S,'pointer':S,'offset':integer(0,10000000),'limit':integer(1,30)},['resultId']))},
}

# Identity and policy choices are visible before dispatch, not inferred from errors.
for action in ('describe','variants','charges'):
    OPS['catalog'][action][1]['oneOf']=[{'required':['typeId'],'not':{'required':['name']}},{'required':['name'],'not':{'required':['typeId']}}]

def tools():
    # Separate operations make required fields truthful in client/model schemas.
    return [{'name':'fitlab_'+domain+'_'+action,'description':description,'inputSchema':schema}
            for domain,actions in OPS.items() for action,(description,schema) in actions.items()]


def operation_for_tool(name):
    return next(((domain,action) for domain,actions in OPS.items() for action in actions
                 if name=='fitlab_'+domain+'_'+action),None)


def validate(value,schema,path='$'):
    if 'not' in schema:
        try:validate(value,schema['not'],path)
        except ValueError:pass
        else:raise ValueError(f'{path}: conflicting fields')
    if 'const' in schema and value!=schema['const']:raise ValueError(f'{path}: expected {schema["const"]}')
    if 'oneOf' in schema:
        matches=0
        for branch in schema['oneOf']:
            try:validate(value,branch,path);matches+=1
            except ValueError:pass
        if matches!=1:raise ValueError(f'{path}: fields do not match the selected operation kind')
    types=schema.get('type');types=types if isinstance(types,list) else [types]
    checks={'object':lambda v:isinstance(v,dict),'array':lambda v:isinstance(v,list),'string':lambda v:isinstance(v,str),
            'integer':lambda v:type(v)==int,'number':lambda v:type(v) in (float,int) and __import__('math').isfinite(v),'boolean':lambda v:type(v)==bool,'null':lambda v:v is None}
    if types!=[None] and not any(checks[t](value) for t in types):raise ValueError(f'{path}: expected {types}')
    if 'enum' in schema and value not in schema['enum']:raise ValueError(f'{path}: choose {schema["enum"]}')
    if isinstance(value,dict):
        missing=set(schema.get('required',[]))-set(value)
        if missing:raise ValueError(f'{path}: missing {sorted(missing)}')
        for k,v in value.items():
            child=schema.get('properties',{}).get(k,schema.get('additionalProperties',True))
            if child is False:raise ValueError(f'{path}: unknown field {k}')
            if isinstance(child,dict):validate(v,child,path+'.'+k)
    if isinstance(value,list):
        if not schema.get('minItems',0)<=len(value)<=schema.get('maxItems',100000):raise ValueError(f'{path}: invalid array length')
        for n,v in enumerate(value):validate(v,schema['items'],f'{path}[{n}]')
    if type(value) in (int,float):
        if value<schema.get('minimum',float('-inf')) or value>schema.get('maximum',float('inf')):raise ValueError(f'{path}: out of range')
