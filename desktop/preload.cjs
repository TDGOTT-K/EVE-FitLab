const {contextBridge,ipcRenderer}=require('electron');
contextBridge.exposeInMainWorld('fitlabDesktop',{platform:'windows',chooseDataFolder:()=>ipcRenderer.invoke('choose-data-folder')});
window.addEventListener('DOMContentLoaded',()=>{document.documentElement.classList.add('desktop-app');const sync=()=>ipcRenderer.send('desktop-theme',document.body.classList.contains('light'));new MutationObserver(sync).observe(document.body,{attributes:true,attributeFilter:['class']});sync()});
