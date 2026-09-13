
export function installWorkspaceResize(workspace){
 const key='fitlab-workspace-widths',minimum={left:200,center:300,right:200};
 let saved=null;try{const value=JSON.parse(localStorage.getItem(key));if(value&&Number.isFinite(value.left)&&Number.isFinite(value.right)&&value.left>0&&value.right>0)saved=value}catch{}
 let widths={left:260,right:220},drag=null;
 const handles=['left','right'].map((side,i)=>{const h=document.createElement('div');h.className='workspace-splitter';h.tabIndex=0;h.role='separator';h.setAttribute('aria-orientation','vertical');h.setAttribute('aria-label',i?'调整装配与属性列宽':'调整装备浏览器与装配列宽');h.title='拖动调整列宽 · 双击恢复默认';workspace.append(h);return h});
 const total=()=>workspace.clientWidth;
 const clamp=(v,min,max)=>Math.max(min,Math.min(max,v));
 function layout(){
  const enabled=innerWidth>820&&total()>=700;
  handles.forEach(h=>h.hidden=!enabled);
  if(!enabled){workspace.style.removeProperty('grid-template-columns');return}
  const desired=saved||(innerWidth<=1100?{left:260,right:220}:{left:300,right:260}),available=total()-minimum.center;
  let left=Math.max(minimum.left,desired.left),right=Math.max(minimum.right,desired.right);
  if(left+right>available){const extra=left+right-minimum.left-minimum.right,room=available-minimum.left-minimum.right;left=minimum.left+(left-minimum.left)*room/extra;right=minimum.right+(right-minimum.right)*room/extra}
  widths={left,right};workspace.style.gridTemplateColumns=left+'px minmax(300px,1fr) '+right+'px';
  handles[0].style.left=left+'px';handles[1].style.left=(total()-right)+'px';
  handles.forEach((h,i)=>{h.setAttribute('aria-valuemin',200);h.setAttribute('aria-valuemax',Math.floor(total()-300-(i?left:right)));h.setAttribute('aria-valuenow',Math.round(i?right:left))});
 }
 const persist=()=>{try{localStorage.setItem(key,JSON.stringify(saved))}catch{}};
 function adjust(i,value){
  const other=i?widths.left:widths.right,valueClamped=clamp(value,200,total()-300-other);
  saved={...widths,[i?'right':'left']:valueClamped};layout();
 }
 handles.forEach((h,i)=>{
  h.onpointerdown=e=>{if(e.button!==0)return;e.preventDefault();drag={x:e.clientX,value:i?widths.right:widths.left};h.setPointerCapture(e.pointerId);h.classList.add('dragging');document.body.classList.add('resizing-workspace')};
  h.onpointermove=e=>{if(drag&&h.hasPointerCapture(e.pointerId))adjust(i,drag.value+(e.clientX-drag.x)*(i?-1:1))};
  h.onpointerup=e=>{if(h.hasPointerCapture(e.pointerId))h.releasePointerCapture(e.pointerId)};
  h.onlostpointercapture=()=>{drag=null;h.classList.remove('dragging');document.body.classList.remove('resizing-workspace');persist()};
  h.ondblclick=()=>{saved=null;try{localStorage.removeItem(key)}catch{}layout()};
  h.onkeydown=e=>{if(!['ArrowLeft','ArrowRight'].includes(e.key))return;e.preventDefault();adjust(i,(i?widths.right:widths.left)+(e.key==='ArrowRight'?1:-1)*(i?-1:1)*(e.shiftKey?30:10));persist()};
 });
 new ResizeObserver(layout).observe(workspace);window.addEventListener('resize',layout);layout();
}
