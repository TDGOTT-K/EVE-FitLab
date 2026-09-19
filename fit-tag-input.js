export function mountFitTagInput(root,initialTags=[],onChange=()=>{}){
 let tags=[...initialTags],finishEditing=null;root.classList.add('fit-tags-row');
 function draw(){
  root.replaceChildren();
  tags.forEach((tag,index)=>{const b=document.createElement('button');b.type='button';b.className='fit-tag';b.textContent=tag;b.title='点击编辑标签';b.onclick=()=>edit(index,b);root.append(b)});
  const add=document.createElement('button');add.type='button';add.className='add-fit-tag';add.textContent='＋';add.setAttribute('aria-label','添加标签');add.title='添加标签';add.onclick=()=>edit(null,add);root.append(add);
 }
 function edit(index,source){
  if(finishEditing)return;
  const existing=index!==null,input=document.createElement('input');input.className='fit-tag-input';input.setAttribute('aria-label',existing?'编辑标签':'新标签');input.placeholder='输入标签，回车添加';input.maxLength=40;input.value=existing?tags[index]:'';
  source.hidden=true;source.after(input);input.focus();let finished=false;
  const finish=commit=>{if(finished)return;finished=true;finishEditing=null;const text=input.value.trim();if(commit){if(existing){if(text)tags[index]=text;else tags.splice(index,1)}else if(text)tags.push(text);tags=[...new Set(tags)]}draw();if(commit)onChange([...tags])};
  finishEditing=finish;input.onblur=()=>finish(true);
  input.onkeydown=e=>{if(e.isComposing)return;if(e.key==='Enter'||e.key==='Escape'){e.preventDefault();e.stopPropagation();finish(e.key==='Enter');root.querySelector('.add-fit-tag')?.focus()}};
 }
 draw();return {values(){finishEditing?.(true);return [...tags]}};
}
