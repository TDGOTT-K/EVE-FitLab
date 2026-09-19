// One editor's native history. UI snapshots/layout remain outside this controller.
// An uncertain response keeps the exact operation for retry; no new request ID.
export function createNativeEditHistory(api){
 let current=fresh();
 function fresh(){return {id:'edit-'+crypto.randomUUID(),revision:0,opened:false,undo:[],redo:[],pending:null,busy:false};}
 async function call(action,args){const response=await api('native-session/'+action,['preview-input','execute','inspect'].includes(action)?{...args,receiptOnly:true}:args);return response.result;}
 function conflict(message){const error=new Error(message);error.code='EDIT_STATE_CONFLICT';return error;}
 async function ensure(owner,fit,hash){
  if(owner.opened)return;
  try{const created=await call('create',{sessionId:owner.id,fit,allowIncompleteDraft:true});owner.revision=created.revision;}
  catch(error){
   if(error.code!=='OUTPUT_EXISTS')throw error;
   const inspected=await call('inspect',{sessionId:owner.id});
   if(inspected.session.revision!==0||inspected.analysis.fitHash!==hash||!inspected.session.allowIncompleteDraft)throw conflict('编辑会话与待恢复的初始装配不一致');
   owner.revision=0;
  }
  owner.opened=true;
 }
 async function execute(owner,operation,requestId,expectedHash,commands){
  const args={sessionId:owner.id,revision:owner.revision,requestId,operation};
  if(commands)args.commands=commands;
  return {args,finish:result=>{
   if(result.appliedFitHash!==expectedHash||result.analysis.fitHash!==expectedHash)throw conflict('编辑回执与当前装配不同，请检查会话状态');
   owner.revision=result.revision;return result;
  }};
 }
 function run(owner,operation){
  if(owner.busy)return Promise.reject(conflict('编辑仍在提交中'));
  owner.busy=true;
  const task=operation().finally(()=>{owner.busy=false;});owner.running=task;return task;
 }
 function begin(owner,prepare){
  if(owner.pending||owner.savePending)return Promise.reject(conflict('上次编辑或保存结果尚未确认，请先重试'));
  const operation={prepare,ready:null};owner.pending=operation;
  return resume(owner);
 }
 function resume(owner){return run(owner,async()=>{
  const pending=owner.pending;if(!pending)throw conflict('没有待重试的编辑');
  if(!pending.ready)pending.ready=await pending.prepare();
  const result=await pending.ready();owner.pending=null;return result;
 });}
 return {
  reset(){current=fresh();return current;},
  capture(){return current;},
  state(owner=current){return {id:owner.id,revision:owner.revision,opened:owner.opened,busy:owner.busy,pending:!!owner.pending||!!owner.savePending,pendingSave:!!owner.savePending,canUndo:!!owner.undo.length,canRedo:!!owner.redo.length};},
  apply(before,after,owner=current){
   before=structuredClone(before);after=structuredClone(after);const requestId=crypto.randomUUID();
   return begin(owner,async()=>{
    const prepared=await call('prepare',{before,after});
    if(!prepared.commands.length)return async()=>{owner.undo.push({changed:false});owner.redo=[];return {changed:false};};
    const preview=await call('preview-input',{fit:prepared.fit,commands:prepared.commands});
    await ensure(owner,prepared.fit,preview.baselineAnalysis.fitHash);
    const change={changed:true,beforeHash:preview.baselineAnalysis.fitHash,afterHash:preview.candidateHash};
    const tx=await execute(owner,'apply',requestId,change.afterHash,prepared.commands);
    return async()=>{const result=tx.finish(await call('execute',tx.args));owner.undo.push(change);owner.redo=[];return {changed:true,result};};
   });
  },
  undo(owner=current){return begin(owner,async()=>{
   const change=owner.undo.at(-1);if(!change)throw conflict('没有可撤销编辑');
   const tx=change.changed?await execute(owner,'undo',crypto.randomUUID(),change.beforeHash):null;
   return async()=>{const result=tx?tx.finish(await call('execute',tx.args)):null;owner.undo.pop();owner.redo.push(change);return {changed:change.changed,result};};
  });},
  redo(owner=current){return begin(owner,async()=>{
   const change=owner.redo.at(-1);if(!change)throw conflict('没有可重做编辑');
   const tx=change.changed?await execute(owner,'redo',crypto.randomUUID(),change.afterHash):null;
   return async()=>{const result=tx?tx.finish(await call('execute',tx.args)):null;owner.redo.pop();owner.undo.push(change);return {changed:change.changed,result};};
  });},
  retry(owner=current){return resume(owner);},
  async save(input,write,owner=current){
   if(owner.busy)await owner.running;
   if(owner.pending)throw conflict('编辑尚未同步，不能保存');
   return run(owner,async()=>{
    let saved;
    try{
     if(!owner.opened){
      const prepared=await call('prepare',{before:input,after:input});
      const initial=await call('preview-input',{fit:prepared.fit,commands:[]});
      await ensure(owner,prepared.fit,initial.candidateHash);
     }
     saved=await write({...input,_editSession:{id:owner.id,revision:owner.revision}});
    }
    catch(error){owner.savePending=!error.status||error.status>=500;throw error;}
    if(owner.opened){if(saved.nativeSession?.id!==owner.id)throw conflict('保存回执不属于当前编辑会话');owner.revision=saved.nativeSession.revision;}
    owner.savePending=false;return saved;
   });
  },
  // Only a confirmed non-committing engine error may be abandoned by the host.
  rejectPending(owner=current){if(owner.busy)throw conflict('编辑仍在提交中');owner.pending=null;},
 };
}
