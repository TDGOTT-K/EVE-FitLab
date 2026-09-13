// Reuse both pending and completed requests; failed requests remain retryable.
export function requestCache(load,limit=32){
 const entries=new Map();
 return (key,input)=>{
  if(entries.has(key)){const result=entries.get(key);entries.delete(key);entries.set(key,result);return result}
  const result=Promise.resolve().then(()=>load(input));entries.set(key,result);
  while(entries.size>limit)entries.delete(entries.keys().next().value);
  result.catch(()=>{if(entries.get(key)===result)entries.delete(key)});
  return result;
 };
}
