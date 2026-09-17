export const SLOT_METRIC_LIMIT=4;
const damageAttributes=[114,118,117,116];
const positive=(type,id)=>(type?.attrs?.[id]||0)>0;
// Eligibility describes the item's capability, not its current on/off output.
// New metrics can be added here without changing the slot renderer.
export const SLOT_METRIC_PRIORITY=[
 {id:'cpu',priority:10,label:'CPU',unit:'tf',title:'CPU 占用',available:t=>positive(t,50),value:(m,s)=>s==='Offline'?0:m.cpuUsage},
 {id:'power',priority:20,label:'栅格',unit:'MW',title:'能量栅格占用',available:t=>positive(t,30),value:(m,s)=>s==='Offline'?0:m.powergridUsage},
 {id:'capacitor',priority:30,label:'耗电',unit:'GJ/s',title:'每秒电容消耗',available:t=>positive(t,6),value:(m,s)=>['Active','Overload'].includes(s)?m.capacitorUsagePerSecond:0},
 {id:'dps',priority:40,label:'DPS',unit:'',title:'当前输出伤害 / 秒',available:t=>t.effects?.some(e=>e===40||e===42)||damageAttributes.some(id=>positive(t,id)),value:(m,s)=>['Active','Overload'].includes(s)?(m.damagePerSecond||0)+(m.charge?.damagePerSecondBonus||0):0},
 {id:'range',priority:50,label:'最佳射程',unit:'m',title:'最佳射程 / 作用距离',available:t=>positive(t,54),value:m=>m.optimalRangeMeters},
 {id:'falloff',priority:60,label:'失准',unit:'m',title:'失准距离',available:t=>positive(t,158),value:m=>m.falloffRangeMeters},
 {id:'repair',priority:70,label:'修量',unit:'HP/s',title:'启用时每秒修复量',available:t=>[68,84,83].some(id=>positive(t,id)),value:(m,s)=>['Active','Overload'].includes(s)?(m.shieldRepairPerSecond||0)+(m.armorRepairPerSecond||0)+(m.structureRepairPerSecond||0):0},
 {id:'cycle',priority:80,label:'单轮',unit:'s',title:'单轮运行时间',available:t=>positive(t,51)||positive(t,73),value:m=>m.cycleTimeSeconds},
];
export function selectSlotMetrics(type,computed,state,group='details'){
 if(!type)return [];
 return SLOT_METRIC_PRIORITY.filter(metric=>(group==='resources'?['cpu','power'].includes(metric.id):!['cpu','power'].includes(metric.id))&&metric.available(type)).sort((a,b)=>a.priority-b.priority).slice(0,SLOT_METRIC_LIMIT).map(metric=>{
  const scenario=metric.id==='dps'?computed?.scenarioMetrics?.damagePerSecond:null;
  let value=scenario?scenario.value:computed?metric.value(computed,state):null,unit=metric.unit;
  if(!Number.isFinite(value))value=null;
  if(unit==='m'&&value>1000){value/=1000;unit='km'}
  return {id:metric.id,label:metric.label,title:metric.title,unit,value,baseline:scenario?.baseline,conditions:computed?.scenarioMetrics?.conditions};
 });
}
