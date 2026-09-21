const crypto=require('node:crypto');
const fs=require('node:fs');
const semver=require('semver');
const bindingKeys=['engineVersion','revision','staticRule','buildNumber','indexSha256'];
function sameBinding(a,b){return bindingKeys.every(k=>a?.[k]!==undefined&&a[k]===b?.[k]);}
function verifyManifest(envelope,key,channel='stable'){
 if(!envelope||typeof envelope.payload!=='string'||typeof envelope.signature!=='string'||envelope.payload.length>65536)throw Error('更新清单格式无效');
 const bytes=Buffer.from(envelope.payload,'base64');
 if(!crypto.verify(null,bytes,key,Buffer.from(envelope.signature,'base64')))throw Error('更新清单签名校验失败');
 const m=JSON.parse(bytes.toString('utf8'));
 if(m.schema!==1||m.channel!==channel||!semver.valid(m.version)||channel==='stable'&&semver.prerelease(m.version)||!Array.isArray(m.notes)||m.notes.length>30||m.notes.some(n=>typeof n!=='string'||n.length>2000))throw Error('更新版本或通道无效');
 const expected=`https://github.com/TDGOTT-K/EVE-FitLab/releases/download/v${m.version}/EVE-FitLab-${m.version}-Windows-x64-Setup.exe`;
 if(m.installer?.url!==expected||!/^[A-Za-z0-9+/]{86}==$/.test(m.installer?.sha512||'')||!Number.isSafeInteger(m.installer.size)||m.installer.size<=0||m.installer.size>2**31)throw Error('更新下载来源无效');
 if(!bindingKeys.every(k=>m.engineSource?.[k]!==undefined))throw Error('更新引擎来源缺失');
 return m;
}
function verifyInfo(info,m){
 const files=info?.files;
 if(info?.packages||info?.version!==m.version||!Array.isArray(files)||files.length!==1||files[0].url!==m.installer.url||files[0].sha512!==m.installer.sha512||files[0].size!==m.installer.size)throw Error('下载描述与已签名版本清单不一致，请稍后重试');
}
async function verifyInstaller(file,m){
 const stat=await fs.promises.stat(file);if(stat.size!==m.installer.size)throw Error('更新文件大小校验失败');
 const hash=crypto.createHash('sha512');for await(const chunk of fs.createReadStream(file))hash.update(chunk);
 if(hash.digest('base64')!==m.installer.sha512)throw Error('更新文件内容校验失败');
}
module.exports={sameBinding,verifyManifest,verifyInfo,verifyInstaller};
