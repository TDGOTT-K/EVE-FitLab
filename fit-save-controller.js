// Save the snapshot selected by the user, even when the editor changes in flight.
const canonical=value=>Array.isArray(value)?value.map(canonical):value&&typeof value==='object'?Object.fromEntries(Object.keys(value).sort().map(key=>[key,canonical(value[key])])):value;
export function sameSavedContent(a,b){
 const content=fit=>{const {id,revision,updatedAt,_workingDraft,_saveRequestId,nativeSession,saveReceipt,...rest}=fit;return JSON.stringify(canonical(rest));};
 return content(a)===content(b);
}
export function createFitSaver({read,write,accept,capture=()=>null}){
 let owner={},tail=Promise.resolve();
 return {
  reset(){owner={};},
  save(){
   const selected=owner,input=structuredClone(read()),context=capture();
   if(!input.id)input.id=selected.draftId??=crypto.randomUUID();
   input._saveRequestId=selected.failed&&sameSavedContent(input,selected.failed)?selected.failed._saveRequestId:crypto.randomUUID();
   const task=tail.catch(()=>{}).then(async()=>{
    const receipt=selected.receipt;
    // Serial saves of a new fit keep the identity allocated by the first save.
    if(receipt&&(!input.id||input.id===receipt.id)&&(!input.revision||input.revision<receipt.revision)){
     input.id=receipt.id;input.revision=receipt.revision;
    }
    let saved;try{saved=await write(input,context);}catch(error){selected.failed=structuredClone(input);throw error;}
    selected.failed=null;selected.receipt=saved;
    if(selected===owner)accept(saved,{input,unchanged:sameSavedContent(input,read())});
    return saved;
   });
   tail=task;return task;
  }
 };
}
