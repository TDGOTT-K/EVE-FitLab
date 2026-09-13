from pathlib import Path
p=Path('app.js');s=p.read_text(encoding='utf-8')
s=s.replace("menu.querySelector('button:not(:disabled)')?.focus();", "menu.querySelector('button:not(:disabled)')?.focus({preventScroll:true});")
s=s.replace("document.addEventListener('scroll',e=>{if(!$('#menu').contains(e.target))closeMenu(false)},true);", "document.addEventListener('wheel',e=>{if(!e.target.closest('#menu'))closeMenu(false)},{passive:true});")
s=s.replace("install(byId(e.dataTransfer.getData('text/plain')),key);clearDrag()", "const dropped=byId(e.dataTransfer.getData('text/plain'));if(e.target.closest('.ammo')&&dropped?.kind!=='ammo')say('弹药子槽只接受兼容弹药');else install(dropped,key);clearDrag()")
p.write_text(s,encoding='utf-8')
p=Path('index.html');s=p.read_text(encoding='utf-8').replace('<title>', '<link rel="icon" href="data:image/svg+xml,%3Csvg xmlns=%27http://www.w3.org/2000/svg%27 viewBox=%270 0 64 64%27%3E%3Ctext y=%2750%27 font-size=%2750%27%3E◈%3C/text%3E%3C/svg%3E"><title>');p.write_text(s,encoding='utf-8')
