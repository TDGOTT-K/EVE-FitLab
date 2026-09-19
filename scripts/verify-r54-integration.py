"""Read-only r54 checks using FitLab mapping and public MCP."""
import sys,json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from nengine_adapter import analyze,bridge
b=bridge()
try:
 assert b.discover()['engineVersion']=='0.210.0'
 def fit(ship,rows):
  return {'name':'r54 integration check','shipId':ship,'skills':[],'slots':[],
          'fighterLoadout':{'tubes':rows,'reserve':[]}}
 def squad(id,t,active=True):return {'id':id,'typeId':t,'quantity':6,'active':active}
 report=analyze(fit(23911,[squad('torpedo',40563),squad('bomb',40566)]))
 torpedo=next(i for i in report['native']['outputContributions']['items'] if i['source']['officialAbilityId']==19)
 assert torpedo['metrics']['loadedCycleDps']['state']=='available'
 bomb=next(i for i in report['native']['outputContributions']['items'] if i['kind']=='fighter_bomb')
 assert bomb['metrics']['loadedCycleDps']['reason']=='BOMB_LAUNCH_RATE_AND_SUPPLY_POLICY_REQUIRED'
 query={'fit':report['nativeFit'],'context':{'output':{'fighterBombReferencePolicy':'one-bomb-per-fitted-member-loaded-cycle-v1'}}}
 explicit=b.call('fit_analyze',query)['result']
 assert next(i for i in explicit['outputContributions']['items'] if i['kind']=='fighter_bomb')['metrics']['loadedCycleDps']['state']=='available'
 four=fit(23911,[squad('light-'+str(i),23055,False) for i in range(4)])
 occupied=analyze(four)
 assert occupied['native']['fighterOccupancy']['loadedClasses']['light']['totalCount']==4
 assert occupied['native']['fighterOccupancy']['deployedClasses']['light']['totalCount']==0
 assert any(i['code']=='FIGHTER_LOADED_LIMIT_EXCEEDED' for i in occupied['issues'])
 direction=b.call('mutation_rule',{'baseTypeId':4393,'mutaplasmidTypeId':60476})['result']
 assert next(a for a in direction['attributes'] if a['attributeId']==1255)['highIsGood'] is True
 discovery=b.call('catalog_item',{'typeId':92822})['result']
 assert discovery['capabilities']['hullConfiguration'] is not None
 result={'engine':'0.210.0/r54','torpedo':'available','bombDefault':'requires_policy',
         'bombExplicit':'available','loadedStandby':4,'deployed':0,'overloadDiagnosed':True,
         'mutationDirection':True,'hullDiscovery':True}
 out=Path('output/integration-r54-ui-001');out.mkdir(parents=True,exist_ok=True)
 (out/'verification.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
 print(json.dumps(result))
finally:b.close()
