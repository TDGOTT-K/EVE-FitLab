import assert from 'node:assert/strict';
import {panelTrace,panelAttributeTerm} from './native-panel-detail.js';
import {outputHtml} from './nengine-output-view.js';
const trace={key:'ship/54',attributeId:54,baseValue:20000,value:25000,steps:[{sourceId:'skill.123',before:20000,after:25000,sourceValue:25,operation:6,penalty:1,effectId:1}]};
const report={engineVersion:'test',native:{attributes:{'ship/54':trace}}};
const detail=panelTrace(trace,'最佳射程','km',report,[{id:123,name:'射程技能'}],1000);
assert.equal(detail.result,'25 km');
assert.equal(detail.terms[1][0],'射程技能');
assert.equal(detail.terms[1][3].terms[0][1],'20 km');
assert.equal(panelAttributeTerm('ship/54',report)[1],'25 km');
assert.equal(panelTrace(null,'未知','',report).result,'—');
assert.equal(panelTrace({...trace,value:0},'真实零','km',report,[],1000).result,'0 km');
// Ship weapons other than turrets and excluded fighter abilities retain their
// correct subtotal group and unavailable state in hover explanations.
const make=(id,kind,value)=>({id,kind,source:{typeId:id,instanceId:String(id)},metrics:{nominalCycleDps:{state:value===null?'unsupported':'available',value,reason:value===null?'MISSING_EFFECT':null}}});
const reading={total:12,groups:[{subtotal:12}],exclusions:[],completeSelection:true};
Object.assign(report,{attackMode:'dps',outputSelection:{...reading,metric:'nominalCycleDps'},outputContext:{selection:{contributionIds:[1,2]}},outputBreakdown:{weapons:reading,drones:{...reading,groups:[]},fighters:{...reading,total:null,groups:[],completeSelection:false,exclusions:[{reason:'MISSING_EFFECT'}]}}});
report.native.outputContributions={items:[make(1,'ship_smartbomb',12),make(2,'fighter_rocket',null)],staticBlockers:[]};
report.native.fighterEntities={};
const html=outputHtml(report,[{id:1,name:'智能炸弹'},{id:2,name:'舰载机'}]);
assert(html.includes('智能炸弹'));assert(html.includes('含换弹 DPS'));assert(html.includes('齐射伤害 · DPH'));assert(!html.includes('舰载机已选武器'));
assert(!html.includes('data-native-attack'));
assert(!html.includes('<details'));assert(!html.includes('native-output-metric'));
report.native.inspector={reloadDps:{value:7.25,reason:null},damagePerSecond:{em:{value:0},thermal:{value:12}},damageFractions:{em:{value:0},thermal:{value:1}}};
const connected=outputHtml(report,[]);
assert(connected.includes('7.3'));assert(connected.includes('100%'));assert(connected.includes('0%'));
report.native.inspector.reloadDps={value:null,reason:'INSTALLED_CRYSTAL_NOT_CONSUMABLE_MAGAZINE'};
assert(outputHtml(report,[]).includes('当前晶体不是消耗式弹仓'));
console.log('Panel explanations: scaled source traces, null/zero, output groups and partial states passed');
