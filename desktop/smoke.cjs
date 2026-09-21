// Runs only with --smoke-test against FITLAB_TEST_ROOT, never the user's library.
module.exports=String.raw`(async()=>{
 const api=async(path,body)=>{const response=await fetch('/api/'+path,body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});const data=await response.json();if(!response.ok){const error=Error(data.error?.message||data.error||'Request failed');error.code=data.error?.code;error.status=response.status;throw error;}return data;};
 const chars=await api('characters');
 const before={id:crypto.randomUUID(),shipId:587,name:'Desktop native smoke',slots:[],skills:chars.find(c=>c.id==='all5').skills};
 const after={...before,slots:[{key:'high-0',kind:'high',item:2881,ammo:185,state:'Active',loadedCharges:10}],cargo:[]};
 const preview=await api('preview',{before,after});
 const {createNativeEditHistory}=await import('/native-edit-history.js');
 const history=createNativeEditHistory(api,{workbench:true});
 const applied=await history.apply(before,after);
 if(applied.result.analysis.fitHash!==preview.editPreview.candidateHash)throw Error('Preview/commit hash mismatch');
 const saved=await history.save(after,input=>api('save',{...input,_saveRequestId:crypto.randomUUID()}));
 if(saved.nativeSession.id!==history.state().id)throw Error('Save did not retain the edit session');
 const undo=await history.undo();if(undo.report.nativeFit.items.length!==0)throw Error('Native undo failed');
 const redo=await history.redo();if(redo.result.analysis.fitHash!==preview.editPreview.candidateHash)throw Error('Native redo failed');
 const exports=await api('native-session/export',{sessionId:history.state().id,snapshot:'saved'});
 if(exports.result.fitHash!==preview.editPreview.candidateHash)throw Error('Saved document differs from preview');
 const replay=await import('/sandbox-preview-data.js');if(replay.default.ships.length!==6||replay.default.duration!==100)throw Error('Sandbox replay missing');
 document.querySelector('#nav-sandbox-preview').click();
 const frame=document.querySelector('#sandbox-preview-overlay iframe');
 await new Promise((resolve,reject)=>{let tries=0;const timer=setInterval(()=>{try{if(frame.contentDocument?.querySelector('#play:not([disabled])')){clearInterval(timer);resolve();}else if(++tries>120){clearInterval(timer);reject(Error('Packaged sandbox frame did not load'));}}catch(e){clearInterval(timer);reject(e)}},250);});
 const sandboxShips=frame.contentDocument.querySelectorAll('#roster button').length;
 if(sandboxShips!==6)throw Error('Sandbox roster incomplete');
 document.querySelector('#manage-characters').click();if(document.querySelector('#sandbox-preview-overlay'))throw Error('Sandbox navigation stuck');
 const updater=await window.fitlabDesktop.updates.state();if(updater.currentVersion!=='0.1.3')throw Error('Updater version mismatch');
 const backup=await api('update-backup',{});if(!backup.files)throw Error('Update backup missing');
 return {updater,updateBackup:backup,sandboxShips,sandboxNavigation:true,title:document.title,characters:chars.length,cpu:preview.attributes.cpuAvailable,desktop:!!window.fitlabDesktop,
  engineVersion:preview.engineVersion,source:preview.source,previewHash:preview.editPreview.candidateHash,
  saved:saved.nativeSession,workingRevision:history.state().revision,undoRedo:true,artifactModules:true};
})()`;
