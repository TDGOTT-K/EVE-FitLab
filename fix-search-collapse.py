from pathlib import Path
p=Path('app.js');s=p.read_text(encoding='utf-8');s=s.replace('const treeOpen=new Set();','const treeOpen=new Set(),searchCollapsed=new Set();\nlet treeSearchQuery="";')
s=s.replace("const q=$('#search').value.trim().toLowerCase();const items=", "const q=$('#search').value.trim().toLowerCase();if(q!==treeSearchQuery){searchCollapsed.clear();treeSearchQuery=q}const items=")
s=s.replace('open=!!q||treeOpen.has(key)', 'open=q?!searchCollapsed.has(key):treeOpen.has(key)')
s=s.replace('if(treeOpen.has(key))treeOpen.delete(key);else treeOpen.add(key);renderTree()', 'if(q){if(searchCollapsed.has(key))searchCollapsed.delete(key);else searchCollapsed.add(key)}else{if(treeOpen.has(key))treeOpen.delete(key);else treeOpen.add(key)}renderTree()')
p.write_text(s,encoding='utf-8')
