const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('fitlabDesktop',{platform:'windows',chooseDataFolder:()=>ipcRenderer.invoke('choose-data-folder'),downloads:{
 list:()=>ipcRenderer.invoke('downloads:list'),action:(id,action)=>ipcRenderer.invoke('downloads:action',id,action),
 subscribe:handler=>{const listener=(_event,rows)=>handler(rows);ipcRenderer.on('downloads:state',listener);return ()=>ipcRenderer.removeListener('downloads:state',listener);}
},updates:{
 state:()=>ipcRenderer.invoke('update:action','state'),check:()=>ipcRenderer.invoke('update:action','check'),download:()=>ipcRenderer.invoke('update:action','download'),install:()=>ipcRenderer.invoke('update:action','install'),cancel:()=>ipcRenderer.invoke('update:action','cancel'),
 subscribe:handler=>{const listener=(_event,state)=>handler(state);ipcRenderer.on('update:state',listener);return ()=>ipcRenderer.removeListener('update:state',listener);},
 onPrepare:handler=>{ipcRenderer.on('update:prepare',async(_event,nonce)=>{try{await handler();await ipcRenderer.invoke('update:prepared',{nonce,ok:true});}catch(e){await ipcRenderer.invoke('update:prepared',{nonce,ok:false,error:e.message});}});}
}});
window.addEventListener('DOMContentLoaded',()=>{document.documentElement.classList.add('desktop-app');const sync=()=>ipcRenderer.send('desktop-theme',document.body.classList.contains('light'));new MutationObserver(sync).observe(document.body,{attributes:true,attributeFilter:['class']});sync()});
