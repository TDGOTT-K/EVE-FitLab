from pathlib import Path
p=Path('explanations.js');s=p.read_text(encoding='utf-8');s=s.replace('clearTimeout(node.timer);','cancelAnimationFrame(node.frame);').replace('node.bridge.remove()','node.bridge.remove();node.aura?.remove()')
s=s.replace(";node.panel.querySelector('.explain-lock').textContent", ";node.aura.classList.add('complete');node.panel.querySelector('.explain-lock').textContent")
s=s.replace("if(!owner&&chain[0]?.locked)return;",'')
s=s.replace('<button aria-label="关闭此层说明">×</button>','')
s=s.replace('locked:false,timer:null','locked:false,frame:null,aura:null')
s=s.replace("node.timer=setTimeout(()=>lock(node),delay);panel.querySelector('button').onclick=()=>{closeFrom(depth);};panel.onpointerenter=bridge.onpointerenter=()=>clearTimeout(leaveTimer);", "animateLock(node);")
a=s.index(" document.addEventListener('pointerover'");b=s.index(" document.addEventListener('focusin'",a)
s=s[:a]+''' function track(target){clearTimeout(leaveTimer);let keep=0;for(let i=0;i<chain.length;i++){const n=chain[i];if(target instanceof Node&&(n.anchor.contains(target)||n.panel.contains(target)||n.bridge.contains(target)))keep=i+1}if(keep<chain.length)leaveTimer=setTimeout(()=>closeFrom(keep),120)}
 document.addEventListener('pointerover',e=>{track(e.target);const anchor=e.target.closest('[data-explain]');if(anchor)show(anchor)});
 document.addEventListener('pointerout',e=>track(e.relatedTarget));
'''+s[b:]
s=s.replace('if(node)lock(node)', 'if(node){cancelAnimationFrame(node.frame);lock(node)}')
p.write_text(s,encoding='utf-8')
