// UI prototype mapping from NEngine native-tactical-modes.md (SDE 3503375).
// Selection only: the legacy calculator does not apply these mode effects.
export const tacticalModes={
 34317:[34319,34323,34321],
 34562:[34564,34566,34570],
 34828:[35676,35677,35678],
 35683:[35686,35687,35688],
};
export function mountTacticalMode(root,fit,onChange){
 root.querySelector('.tactical-mode-row')?.remove();
 const modes=tacticalModes[fit.shipId];if(!modes)return;
 const row=document.createElement('div');row.className='tactical-mode-row';
 row.innerHTML='<label for="tactical-mode-select"><svg viewBox="0 0 20 20" aria-hidden="true"><path d="m10 2 7 4v8l-7 4-7-4V6Z"/><path d="m3 6 7 4 7-4M10 10v8"/></svg>舰体模式</label><div class="tactical-mode-choice"><select id="tactical-mode-select" aria-label="舰体模式"><option value="">请选择模式</option>'+modes.map((id,i)=>'<option value="'+id+'">'+['防御模式','推进模式','精确模式'][i]+'</option>').join('')+'</select><span class="tactical-mode-chevron" aria-hidden="true">⌄</span></div><small title="本轮仅预览模式选择交互；当前计算尚未应用模式加成">加成待接入</small>';
 const select=row.querySelector('select');select.value=modes.includes(fit.tacticalModeTypeId)?String(fit.tacticalModeTypeId):'';
 select.onchange=()=>onChange(select.value?Number(select.value):null);
 root.prepend(row);
}
