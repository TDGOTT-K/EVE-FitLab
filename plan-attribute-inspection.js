// Presentation adapter for public booster_plan_analyze traces; no evaluation.
export function planAttributeInspection(response,kind,slot,attributes){
 const analysis=response.analysis,roster=kind==='implants'?response.nativePlan.implants:response.nativePlan.boosters;
 const instance=roster.find(row=>row.id===kind+'-'+slot);
 if(!instance)throw Error('方案物品已不存在');
 const itemId=(kind==='implants'?'implant.':'booster.')+instance.id;
 return {source:analysis.source,ruleVersion:analysis.ruleVersion,planHash:analysis.planHash,
  staticCoverageComplete:analysis.projectionComplete,errors:analysis.issues,
  traces:analysis.attributes,
  items:attributes.map(attribute=>{
   const trace=analysis.attributes[itemId+'/'+attribute.id];
   const available=analysis.projectionComplete&&trace!=null;
   return {query:{itemId,attributeId:attribute.id},typeId:instance.typeId,
    state:available?'available':analysis.projectionComplete?'unavailable':'blocked_static_dependencies',
    reason:available?null:analysis.projectionComplete?'独立方案接口未返回此属性；基础值见基础属性页':'方案包含当前引擎未支持的效果',
    trace:available?trace:null,metadata:{definition:attribute}};
  })};
}
