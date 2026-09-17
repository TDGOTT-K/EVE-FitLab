export function installPlanResize(workspace){
 const library=workspace.querySelector('.plan-library'),body=workspace.querySelector('#plan-library-body'),key='fitlab-plan-workspace-size';
 let preferred={width:260,height:205};try{const saved=JSON.parse(localStorage.getItem(key));for(const k of ['width','height'])if(Number.isFinite(saved?.[k])&&saved[k]>0)preferred[k]=saved[k]}catch{}
 let current={...preferred},drag=null;
 const handles=['height','width'].map(axis=>{const h=document.createElement('div');h.className='plan-splitter plan-splitter-'+axis;h.tabIndex=0;h.role='separator';h.setAttribute('aria-label',axis==='height'?'调整方案库高度':'调整物品浏览器宽度');h.setAttribute('aria-orientation',axis==='height'?'horizontal':'vertical');h.title='拖动调整大小 · 双击恢复默认';workspace.append(h);return h;});
 const clamp=(v,min,max)=>Math.max(min,Math.min(max,v));
 function layout(){
  if(!workspace.clientWidth||!workspace.clientHeight)return;
  const maxWidth=Math.max(160,workspace.clientWidth-300),minWidth=Math.min(200,maxWidth),maxHeight=Math.max(100,workspace.clientHeight-workspace.querySelector('.plan-library-head').offsetHeight-220);
  current={width:clamp(preferred.width,minWidth,maxWidth),height:clamp(preferred.height,100,maxHeight)};
  workspace.style.gridTemplateColumns=current.width+'px minmax(0,1fr)';body.style.height=current.height+'px';
  const top=library.offsetHeight;handles[0].hidden=body.hidden;handles[0].style.top=top+'px';handles[1].style.top=top+'px';handles[1].style.left=current.width+'px';
  handles.forEach((h,i)=>{h.setAttribute('aria-valuemin',i?minWidth:100);h.setAttribute('aria-valuemax',Math.floor(i?maxWidth:maxHeight));h.setAttribute('aria-valuenow',Math.round(i?current.width:current.height));});
 }
 const persist=()=>{try{localStorage.setItem(key,JSON.stringify(preferred))}catch{}};
 handles.forEach((h,i)=>{
  const axis=i?'width':'height',coord=e=>i?e.clientX:e.clientY;
  h.onpointerdown=e=>{if(e.button!==0)return;e.preventDefault();drag={start:coord(e),value:current[axis]};h.setPointerCapture(e.pointerId);h.classList.add('dragging');document.body.classList.add('resizing-plan-'+axis)};
  h.onpointermove=e=>{if(!drag||!h.hasPointerCapture(e.pointerId))return;preferred[axis]=drag.value+coord(e)-drag.start;layout();preferred[axis]=current[axis]};
  h.onpointerup=h.onpointercancel=e=>{if(h.hasPointerCapture(e.pointerId))h.releasePointerCapture(e.pointerId)};
  h.onlostpointercapture=()=>{drag=null;h.classList.remove('dragging');document.body.classList.remove('resizing-plan-'+axis);persist()};
  h.ondblclick=()=>{preferred[axis]=i?260:205;layout();persist()};
  h.onkeydown=e=>{const keys=i?['ArrowLeft','ArrowRight']:['ArrowUp','ArrowDown'];if(!keys.includes(e.key))return;e.preventDefault();preferred[axis]=current[axis]+(e.key===keys[1]?1:-1)*(e.shiftKey?30:10);layout();preferred[axis]=current[axis];persist()};
 });
 const observer=new ResizeObserver(layout);observer.observe(workspace);observer.observe(library);new MutationObserver(layout).observe(body,{attributes:true,attributeFilter:['hidden']});layout();
}
