import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {encodeFitCodes,decodeFitCodes,parseCode} from './fit-image-code.js';
import {shareFitExtension} from './share-fit-extension.js';
const catalog=JSON.parse(execFileSync('python',['-c','import json;from nengine_catalog import refresh_catalog;print(json.dumps(refresh_catalog([])))'],{encoding:'utf8',maxBuffer:32*1024*1024}));
const base={name:'Native share',shipId:587,skills:[],slots:[{key:'high-0',kind:'high',item:455,ammo:247,state:'Active'}],drones:[],cargo:[],
 crystals:[{id:'crystal-mounted',typeId:247,damage:0,moduleId:'high-0'}],
 loadoutPlan:{id:'private-plan-id',folder:'private-folder',revision:42,name:'My plan',implants:[{typeId:27147,slot:10}],boosters:[{typeId:9950,slot:1,enabledSideEffects:[2737]}],pilot:{name:'Private pilot',skills:[]},rollReceipt:{secret:'omit'}},
 outputMetric:'loadedCycleDps',capacitorHorizon:900,damageProfile:[25,25,25,25],defenseMode:'targeted',scenario:{targetFitId:'private-target'},scenarios:[{id:'private-scenario'}],activeScenarioId:'private-scenario',id:'private-fit'};
const codes=await encodeFitCodes(base,{pilot:false});assert.equal(parseCode(codes[0]).version,3);
const decoded=await decodeFitCodes(codes,catalog);
assert.deepEqual(decoded.crystals,base.crystals);assert.deepEqual(decoded.loadoutPlan.implants,base.loadoutPlan.implants);assert.deepEqual(decoded.loadoutPlan.boosters,base.loadoutPlan.boosters);
assert.equal(decoded.outputMetric,base.outputMetric);assert.equal(decoded.capacitorHorizon,900);assert.equal(decoded.defenseMode,'targeted');assert.deepEqual(decoded.damageProfile,base.damageProfile);
const text=JSON.stringify(decoded);for(const value of ['private-plan-id','private-folder','private-target','private-scenario','private-fit','Private pilot','rollReceipt'])assert(!text.includes(value));
const unknown={...base};delete unknown.cargo;delete unknown.crystals;
const unknownDecoded=await decodeFitCodes(await encodeFitCodes(unknown),catalog);assert(!Object.hasOwn(unknownDecoded,'cargo'));assert(!Object.hasOwn(unknownDecoded,'crystals'));
const extended={...base,tacticalModeTypeId:34319,slots:[{...base.slots[0],loadedCharges:0,abyssalName:'Custom',mutation:{baseTypeId:455,mutaplasmidTypeId:1,ruleVersion:'test',attributes:{54:1000}}}],
 fighterLoadout:{tubes:[{id:'squad-1',typeId:23055,quantity:6,active:true,excludedAbilities:[1],includedSecondaryAbilities:[2]}],reserve:[null]}};
const extendedDecoded=await decodeFitCodes(await encodeFitCodes(extended),catalog);
assert.equal(extendedDecoded.slots[0].loadedCharges,0);assert.deepEqual(extendedDecoded.slots[0].mutation,extended.slots[0].mutation);assert.deepEqual(extendedDecoded.fighterLoadout,extended.fighterLoadout);assert.equal(extendedDecoded.tacticalModeTypeId,34319);
assert.throws(()=>shareFitExtension({...base,slots:[{...base.slots[0],mutation:{attributes:{54:null}}}]}),/深渊属性/);
// Public analysis before/after must preserve all gameplay inputs and results.
execFileSync('python',['-c',[
 'import json,sys',
 'from nengine_adapter import analyze,bridge',
 'a,b=json.load(sys.stdin)',
 "a.pop('id',None);a.pop('scenario',None);a.pop('scenarios',None);a.pop('activeScenarioId',None)",
 'x,y=analyze(a),analyze(b)',
 "assert x['nativeFit']==y['nativeFit']",
 "assert x['native']==y['native']",
 'bridge().close()'
].join(';')],{input:JSON.stringify([base,decoded]),encoding:'utf8'});
console.log('Native share: complete input roundtrip, unknown inventory, privacy, identities and native result parity passed');
