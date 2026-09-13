from pathlib import Path
p=Path('explanations.js');s=p.read_text(encoding='utf-8');s=s.replace('function show(anchor){clearTimeout(leaveTimer);','function show(anchor){').replace("if(chain[depth]?.anchor===anchor)return;closeFrom(depth);", "if(chain[depth]?.anchor===anchor)return;clearTimeout(leaveTimer);closeFrom(depth);");p.write_text(s,encoding='utf-8')
