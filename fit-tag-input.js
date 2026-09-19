export function mountFitTagInput(root,suggestions=[]){
 const tags=new Set();root.classList.add('fit-tag-editor');
 const chips=document.createElement('div');chips.className='fit-tag-chips';
 const input=document.createElement('input');input.placeholder='输入标签，按 Enter 添加';input.setAttribute('aria-label','添加装配标签');input.maxLength=80;
 const choices=document.createElement('div');choices.className='fit-tag-suggestions';
 const add=value=>{for(const text of value.split(/[,，]/).map(s=>s.trim()).filter(Boolean)){if([...tags].join(',').length+text.length<=500)tags.add(text);}input.value='';draw()};
 function draw(){
  chips.replaceChildren();choices.replaceChildren();
  for(const tag of tags){const chip=document.createElement('span');chip.className='filter-tag';const text=document.createElement('span');text.textContent=tag;const remove=document.createElement('button');remove.type='button';remove.textContent='×';remove.setAttribute('aria-label','移除标签：'+tag);remove.onclick=()=>{tags.delete(tag);draw()};chip.append(text,remove);chips.append(chip)}
  const query=input.value.trim().toLowerCase();
  for(const tag of [...new Set(suggestions)].filter(t=>!tags.has(t)&&t.toLowerCase().includes(query)).slice(0,8)){const b=document.createElement('button');b.type='button';b.className='fit-tag';b.textContent=tag;b.onclick=()=>{add(tag);input.focus()};choices.append(b)}
 }
 input.onkeydown=e=>{if(e.isComposing)return;if(e.key==='Enter'||e.key===','){e.preventDefault();e.stopPropagation();add(input.value)}else if(e.key==='Backspace'&&!input.value&&tags.size){tags.delete([...tags].at(-1));draw()}};
 input.oninput=draw;root.append(chips,input,choices);draw();
 return {values(){add(input.value);return [...tags]}};
}
