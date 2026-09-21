const assert=require('node:assert/strict'),crypto=require('node:crypto'),fs=require('node:fs'),os=require('node:os'),path=require('node:path'),{EventEmitter}=require('node:events');
const {createUpdateController}=require('./desktop/update-controller.cjs');
const {verifyManifest,verifyInstaller}=require('./desktop/update-security.cjs');
const {publicKey,privateKey}=crypto.generateKeyPairSync('ed25519');
const dir=fs.mkdtempSync(path.join(os.tmpdir(),'fitlab-update-test-')),file=path.join(dir,'setup.exe');fs.writeFileSync(file,'verified test artifact');
const binding={engineVersion:'test',revision:62,staticRule:'test',buildNumber:1,indexSha256:'test'};
const base={schema:1,channel:'stable',version:'0.1.4',notes:['test'],engineSource:binding,installer:{url:'https://github.com/TDGOTT-K/EVE-FitLab/releases/download/v0.1.4/EVE-FitLab-0.1.4-Windows-x64-Setup.exe',size:fs.statSync(file).size,sha512:crypto.createHash('sha512').update(fs.readFileSync(file)).digest('base64')}};
const sign=m=>{const bytes=Buffer.from(JSON.stringify(m));return {payload:bytes.toString('base64'),signature:crypto.sign(null,bytes,privateKey).toString('base64')};};
function fixture(m=base,overrides={}){
 const updater=new EventEmitter();let checks=0,downloads=0,prepares=0,installs=0;
 updater.setFeedURL=f=>assert.equal(f.url,'https://imfishman.com/updates/stable/');
 updater.checkForUpdates=async()=>{checks++;return {updateInfo:{version:m.version,files:[m.installer]}};};
 updater.downloadUpdate=async()=>{downloads++;return [file];};
 const c=createUpdateController({updater,currentVersion:'0.1.3',binding,publicKey,readManifest:async()=>sign(m),prepare:async()=>prepares++,install:async()=>installs++,...overrides});
 return {c,updater,counts:()=>({checks,downloads,prepares,installs})};
}
(async()=>{
 try{
  const a=fixture();assert.equal((await a.c.check()).phase,'available');assert.equal(a.updater.autoInstallOnAppQuit,false);assert.equal(a.updater.autoDownload,false);
  assert.equal((await a.c.download()).phase,'downloaded');assert.equal((await a.c.install()).phase,'installing');assert.deepEqual(a.counts(),{checks:1,downloads:1,prepares:1,installs:1});
  const same=fixture({...base,version:'0.1.3',installer:{...base.installer,url:base.installer.url.replaceAll('0.1.4','0.1.3')}});assert.equal((await same.c.check()).phase,'current');assert.equal(same.counts().checks,0);
  const incompatible=fixture({...base,engineSource:{...binding,revision:63}});assert.equal((await incompatible.c.check()).phase,'blocked');assert.equal(incompatible.counts().checks,0);
  const unsigned=sign(base);unsigned.signature='bad';assert.throws(()=>verifyManifest(unsigned,publicKey),/签名/);
  assert.throws(()=>verifyManifest(sign({...base,installer:{...base.installer,url:'https://evil.example/setup.exe'}}),publicKey),/来源/);
  const mismatch=fixture();mismatch.updater.checkForUpdates=async()=>({updateInfo:{version:'0.1.4',files:[{...base.installer,sha512:'bad'}]}});assert.equal((await mismatch.c.check()).phase,'error');assert.equal(mismatch.counts().downloads,0);
  const network=fixture();network.updater.downloadUpdate=async()=>{throw Error('offline')};await network.c.check();assert.equal((await network.c.download()).phase,'error');network.updater.downloadUpdate=async()=>[file];await network.c.check();assert.equal((await network.c.download()).phase,'downloaded');
  let failSave=true;const guarded=fixture(base,{prepare:async()=>{if(failSave)throw Error('save failed');}});await guarded.c.check();await guarded.c.download();assert.equal((await guarded.c.install()).phase,'downloaded');assert.equal(guarded.counts().installs,0);failSave=false;await guarded.c.install();assert.equal(guarded.counts().installs,1);
  let resume;const serial=fixture(base,{readManifest:()=>new Promise(r=>resume=r)});const inFlight=serial.c.check();await assert.rejects(serial.c.check(),/进行/);resume(sign(base));assert.equal((await inFlight).phase,'available');
  const tampered=fixture();await tampered.c.check();await tampered.c.download();fs.appendFileSync(file,'tamper');assert.equal((await tampered.c.install()).phase,'downloaded');assert.equal(tampered.counts().installs,0);assert.equal(tampered.counts().prepares,0);
  await assert.rejects(verifyInstaller(file,base));
  const env=createUpdateController({updater:new class extends EventEmitter{setFeedURL(){}},currentVersion:'0.1.3',binding,publicKey,readManifest:async()=>{throw Error('offline')},prepare:async()=>{},install:async()=>{throw Error('must not install')}});assert.equal((await env.check()).phase,'error');
  const cancelled=fixture();await cancelled.c.check();cancelled.updater.downloadUpdate=token=>new Promise((_resolve,reject)=>token.onCancel(()=>reject(Error('cancelled'))));const pending=cancelled.c.download();cancelled.c.cancel();assert.equal((await pending).phase,'cancelled');assert.equal(cancelled.counts().installs,0);
  console.log('Update tests passed: signature, source, binding, no downgrade/current, metadata identity, explicit download/install, retry, cancellation and tamper rejection.');
 }finally{fs.rmSync(dir,{recursive:true,force:true});}
})().catch(e=>{console.error(e);process.exitCode=1});
