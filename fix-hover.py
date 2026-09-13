from pathlib import Path
p=Path('explanations.js');s=p.read_text(encoding='utf-8-sig').replace("document.addEventListener('pointerover',e=>{clearTimeout(leaveTimer);", "document.addEventListener('pointerover',e=>{if(inChain(e.target))clearTimeout(leaveTimer);");p.write_text(s,encoding='utf-8')
