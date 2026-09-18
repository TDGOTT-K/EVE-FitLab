// Save the snapshot selected by the user, even when the editor changes in flight.
const canonical=value=>Array.isArray(value)?value.map(canonical):value&&typeof value==='object'?Object.fromEntries(Object.keys(value).sort().map(key=>[key,canonical(value[key])])):value;
export function sameSavedContent(a,b){
 const content=fit=>{const {id,revision,updatedAt,_workingDraft,...rest}=fit;return JSON.stringify(canonical(rest));};
 return content(a)===content(b);
}
export function createFitSaver({read,write,accept}){
 let owner={},tail=Promise.resolve();
 return {
  reset(){owner={};},
  save(){
   const selected=owner,input=structuredClone(read());
   const task=tail.catch(()=>{}).then(async()=>{
    const receipt=selected.receipt;
    // Serial saves of a new fit keep the identity allocated by the first save.
    if(receipt&&(!input.id||input.id===receipt.id)&&(!input.revision||input.revision<receipt.revision)){
     input.id=receipt.id;input.revision=receipt.revision;
    }
    const saved=await write(input);selected.receipt=saved;
    if(selected===owner)accept(saved,{input,unchanged:sameSavedContent(input,read())});
    return saved;
   });
   tail=task;return task;
  }
 };
}
