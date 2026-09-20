// Reconcile generated UI without replacing unchanged nodes, focus or image elements.
const key=node=>node.nodeType===1?(node.getAttribute('data-key')||node.id||node.getAttribute('data-rack-limits')):null;
function compatible(a,b){return !!a&&a.nodeType===b.nodeType&&a.nodeName===b.nodeName&&key(a)===key(b);}
function children(parent,next){
 const keyed=new Map([...parent.childNodes].filter(n=>key(n)).map(n=>[key(n),n]));let cursor=parent.firstChild;
 for(const incoming of [...next.childNodes]){
  const candidate=key(incoming)?keyed.get(key(incoming)):cursor;
  if(compatible(candidate,incoming)){
   if(candidate!==cursor)parent.insertBefore(candidate,cursor);
   if(candidate.nodeType===1){
    for(const attr of [...candidate.attributes])if(!incoming.hasAttribute(attr.name))candidate.removeAttribute(attr.name);
    for(const attr of incoming.attributes)if(candidate.getAttribute(attr.name)!==attr.value)candidate.setAttribute(attr.name,attr.value);
    children(candidate,incoming);
   }else if(candidate.nodeValue!==incoming.nodeValue)candidate.nodeValue=incoming.nodeValue;
   cursor=candidate.nextSibling;
  }else parent.insertBefore(incoming.cloneNode(true),cursor);
 }
 while(cursor){const next=cursor.nextSibling;cursor.remove();cursor=next;}
}
export function patchHtml(root,html){const template=document.createElement('template');template.innerHTML=html;children(root,template.content);}
