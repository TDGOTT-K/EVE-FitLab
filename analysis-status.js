// Display the transport classification; preserve admission and completeness separately.
export function analysisStatusText(report){
 const s=report.analysisStatus;
 if(!s)return report.isValid?'装配校验通过':'装配存在校验问题';
 const legal={valid:'装配合法',invalid:'装配不合法',unknown:'合法性未确定'}[s.legality];
 const result={complete:'当前结果完整',partial:'当前结果不完整',unsupported:'当前计算不支持'}[s.completeness];
 return legal+' · '+result;
}
export const scaleReading=(value,scale=1)=>Number.isFinite(value)?value/scale:null;
