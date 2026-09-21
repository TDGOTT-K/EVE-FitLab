const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const dir=path.join(process.env.LOCALAPPDATA,'EVE-FitLab-Release');
const privatePath=path.join(dir,'update-signing-private.pem'),publicPath=path.resolve(__dirname,'../desktop/update-public-key.pem');
fs.mkdirSync(dir,{recursive:true});
if(!fs.existsSync(privatePath)){
 if(fs.existsSync(publicPath))throw Error('Existing update identity found: restore the private key; do not replace it.');
 const {privateKey,publicKey}=crypto.generateKeyPairSync('ed25519');
 fs.writeFileSync(privatePath,privateKey.export({format:'pem',type:'pkcs8'}),{mode:0o600,flag:'wx'});
 fs.writeFileSync(publicPath,publicKey.export({format:'pem',type:'spki'}),{flag:'wx'});
}else{
 const expected=crypto.createPublicKey(fs.readFileSync(privatePath)).export({format:'pem',type:'spki'});
 if(fs.existsSync(publicPath)&&fs.readFileSync(publicPath,'utf8')!==expected)throw Error('Key mismatch');
 if(!fs.existsSync(publicPath))fs.writeFileSync(publicPath,expected);
}
console.log('Update signing identity ready; private key stays outside the repository.');
