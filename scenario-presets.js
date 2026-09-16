export function scenarioPresets(fit){
 if(Array.isArray(fit.scenarios))return {scenarios:structuredClone(fit.scenarios),activeScenarioId:fit.activeScenarioId||null};
 if(fit.scenario&&Object.keys(fit.scenario).length)return {scenarios:[{id:'legacy',name:'原有情景',value:structuredClone(fit.scenario)}],activeScenarioId:'legacy'};
 return {scenarios:[],activeScenarioId:null};
}
export function scenarioFields(state){
 return {...structuredClone(state),scenario:structuredClone(state.scenarios.find(s=>s.id===state.activeScenarioId)?.value||{})};
}
export function withoutScenario(fit){
 const {scenarios,activeScenarioId,scenario,...rest}=fit;
 return {...rest,scenario:{}};
}
