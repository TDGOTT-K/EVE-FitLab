export function openAfterHours(){
 if(document.querySelector('.after-hours-overlay'))return;
 const previous=document.activeElement,host=document.createElement('dialog');host.className='after-hours-overlay';host.setAttribute('aria-label','下班跃迁小游戏');
 host.innerHTML='<div class="after-hours-bar"><span>下班跃迁 · 街机彩蛋</span><button type="button">返回 FitLab</button></div><iframe title="下班跃迁" src="after-hours/index.html" allow="autoplay"></iframe>';
 const close=()=>host.close();host.querySelector('button').onclick=close;
 const nav=()=>close();window.addEventListener('hashchange',nav);
 host.onclose=()=>{window.removeEventListener('hashchange',nav);host.querySelector('iframe').src='about:blank';host.remove();previous?.focus();};
 document.body.append(host);host.showModal();host.querySelector('iframe').onload=()=>host.querySelector('iframe').focus();
}
