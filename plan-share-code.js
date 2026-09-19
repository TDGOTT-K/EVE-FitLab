const HEADER='FITLAB-PLAN/1';
const hash=async bytes=>[...new Uint8Array(await crypto.subtle.digest('SHA-256',bytes))].map(x=>x.toString(16).padStart(2,'0')).join('');
async function read(stream,limit){const reader=stream.getReader(),parts=[];let size=0;try{for(;;){const {done,value}=await reader.read();if(done)break;size+=value.length;if(size>limit)throw Error('方案分享数据过大');parts.push(value)}}finally{await reader.cancel()}const result=new Uint8Array(size);let at=0;for(const p of parts){result.set(p,at);at+=p.length}return result}
export const planShareText=document=>HEADER+'\n'+JSON.stringify(document,null,2);
export function parsePlanText(text){if(text.length>250000)throw Error('分享文本过大');if(!text.trim().startsWith(HEADER+'\n')&&!text.trim().startsWith(HEADER+'\r\n'))throw Error('请粘贴以 FITLAB-PLAN/1 开头的方案分享文本');return JSON.parse(text.trim().slice(HEADER.length).trim())}
export function parsePlanCode(text){const m=/^FPLAN1:([a-f0-9]{64}):(\d{1,2}):(\d{1,2}):([A-Za-z0-9+/=]{1,800})$/.exec(text||'');if(!m||+m[2]<1||+m[2]>+m[3]||+m[3]>24)return null;return {hash:m[1],index:+m[2],total:+m[3],data:m[4]}}
export async function encodePlanCodes(document){
 const raw=new TextEncoder().encode(JSON.stringify(document));if(raw.length>200000)throw Error('方案数据过大');
 const bytes=await read(new Blob([raw]).stream().pipeThrough(new CompressionStream('gzip')),12000),digest=await hash(bytes),data=btoa(String.fromCharCode(...bytes)),total=Math.ceil(data.length/600);
 if(total>24)throw Error('方案码超过24张，请改用文字分享');
 return Array.from({length:total},(_,i)=>'FPLAN1:'+digest+':'+(i+1)+':'+total+':'+data.slice(i*600,(i+1)*600));
}
export async function decodePlanCodes(codes){
 const parts=codes.map(parsePlanCode).filter(Boolean);if(!parts.length)throw Error('未识别到脑插方案分享码');
 const first=parts[0],map=new Map();
 for(const p of parts){if(p.hash!==first.hash||p.total!==first.total)throw Error('混入了不同方案的二维码，请分别导入');if(map.has(p.index)&&map.get(p.index)!==p.data)throw Error('二维码分片冲突');map.set(p.index,p.data)}
 if(map.size!==first.total)throw Error('方案码不完整：'+map.size+'/'+first.total+'，请补充其余二维码');
 const bytes=Uint8Array.from(atob(Array.from({length:first.total},(_,i)=>map.get(i+1)).join('')),c=>c.charCodeAt(0));
 if(await hash(bytes)!==first.hash)throw Error('方案码校验失败');
 const raw=await read(new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip')),200000);
 return JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(raw));
}
