// UI prototype mapping from NEngine native-tactical-modes.md (SDE 3503375).
// Selection only: the legacy calculator does not apply these mode effects.
export const tacticalModes={
 34317:[34319,34323,34321],
 34562:[34564,34566,34570],
 34828:[35676,35677,35678],
 35683:[35686,35687,35688],
};
const modeIcons=[
 '<path d="m10 2 6 2.5v5c0 3.8-3 6.7-6 8.5-3-1.8-6-4.7-6-8.5v-5Z"/><path d="M10 5v9"/>',
 '<path d="m7 3 9 7-9 7 3-7Z"/><path d="M3 6h2M2 10h4M3 14h2"/>',
 '<circle cx="10" cy="10" r="5.5"/><circle cx="10" cy="10" r="1"/><path d="M10 1v5m0 8v5M1 10h5m8 0h5"/>'
];
export function mountTacticalMode(root,fit,onChange){
 root.querySelector('.tactical-mode-row')?.remove();
 const modes=tacticalModes[fit.shipId];if(!modes)return;
 const row=document.createElement('div');row.className='tactical-mode-row';
 row.innerHTML='<label for="tactical-mode-select"><svg viewBox="0 0 20 20" aria-hidden="true"><path d="m10 2 7 4v8l-7 4-7-4V6Z"/><path d="m3 6 7 4 7-4M10 10v8"/></svg>舰体模式</label><div class="tactical-mode-choice"><svg class="tactical-mode-icon" viewBox="0 0 20 20" aria-hidden="true"></svg><select id="tactical-mode-select" aria-label="舰体模式"><option value="">请选择模式</option>'+modes.map((id,i)=>'<option value="'+id+'">'+['防御模式','推进模式','精确模式'][i]+'</option>').join('')+'</select><span class="tactical-mode-chevron" aria-hidden="true">⌄</span></div><small title="本轮仅预览模式选择交互；当前计算尚未应用模式加成">加成待接入</small>';
 const select=row.querySelector('select');select.value=modes.includes(fit.tacticalModeTypeId)?String(fit.tacticalModeTypeId):'';
 const icon=row.querySelector('.tactical-mode-icon');
 const updateIcon=()=>{const index=modes.indexOf(Number(select.value));icon.innerHTML=modeIcons[index]||'<path d="m10 2 7 4v8l-7 4-7-4V6Z"/>';icon.dataset.mode=String(index);};
 updateIcon();
 select.onchange=()=>{updateIcon();onChange(select.value?Number(select.value):null)};
 root.prepend(row);
}
