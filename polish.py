from pathlib import Path
p=Path('app.js');s=p.read_text(encoding='utf-8');s=s.replace("const tree={};for(const t of items)","const tree={};for(const t of [...items].sort((a,b)=>(a.kind==='ammo')-(b.kind==='ammo')))")
s=s.replace("for(const p of t.path){n[p]??={};n=n[p]}(n._items", "for(const p of [...t.path,...(t.kind==='ammo'?[]:[t.attrs['422']===2?'科技 II':'科技 I'])]){n[p]??={};n=n[p]}(n._items")
p.write_text(s,encoding='utf-8')
p=Path('style.css');s=p.read_text(encoding='utf-8');s+='\n.ship-caption{bottom:0;padding:25px 5px 13px;background:linear-gradient(transparent,#090e13e6)}\n';p.write_text(s,encoding='utf-8')
