from pathlib import Path
p=Path('app.js');s=p.read_text(encoding='utf-8');i=s.index('function openMenu(');s=s[:i]+'''const menuIconPaths={
 active:'<path d="m8 5 10 7-10 7Z"/>',
 stop:'<rect x="6" y="6" width="12" height="12" rx="1"/>',
 offline:'<path d="M12 3v8M6.4 5.8a8 8 0 1 0 11.2 0"/>',
 overload:'<path d="m13 2-8 12h6l-1 8 9-13h-6Z"/>',
 remove:'<path d="M10 5H5v14h5M9 12h12m-4-4 4 4-4 4"/>',
 info:'<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7v.5"/>',
 install:'<path d="M4 15v5h16v-5M12 3v12m-4-4 4 4 4-4"/>'
};
function menuIcon(label){const key=label==='启用'?'active':label==='关闭'?'stop':label==='离线'?'offline':label==='超载'?'overload':label==='查看信息'?'info':label.includes('卸载')?'remove':label==='在线 · 被动'?'offline':'install';return `<svg class="menu-action-icon" viewBox="0 0 24 24" aria-hidden="true">${menuIconPaths[key]}</svg>`}
'''+s[i:];s=s.replace('b.textContent=label;', '''const checked=label.startsWith('✓ '),text=checked?label.slice(2):label;b.setAttribute('aria-label',text);b.innerHTML=menuIcon(text)+`<span class="menu-action-label">${esc(text)}</span>${checked?'<svg class="menu-selected-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6"/></svg>':''}`;if(checked)b.setAttribute('aria-current','true');''');p.write_text(s,encoding='utf-8')
