// This is a read-only preflight, never a write timeout or an automatic retry.
export async function validateFighterLoadout(read,{timeoutMs=30000}={}){
 const controller=new AbortController();let timer;
 const deadline=new Promise((_,reject)=>{timer=setTimeout(()=>{
  reject(new Error('舰载机校验超时，配置未更改，请重试。'));
  controller.abort();
 },timeoutMs)});
 try{return await Promise.race([Promise.resolve().then(()=>read(controller.signal)),deadline]);}
 finally{clearTimeout(timer);}
}
