// Check browser business modules without executing them or requiring a server.
const fs=require('node:fs'),path=require('node:path');
const parser=require('@babel/parser'),traverse=require('@babel/traverse').default,types=require('@babel/types');
const root=path.resolve(__dirname,'..');
const globals=new Set((
 'undefined NaN Infinity globalThis Object Function Boolean Symbol Error AggregateError EvalError RangeError ReferenceError SyntaxError TypeError URIError Number BigInt Math Date String RegExp Array Int8Array Uint8Array Uint8ClampedArray Int16Array Uint16Array Int32Array Uint32Array Float32Array Float64Array BigInt64Array BigUint64Array Map Set WeakMap WeakSet WeakRef FinalizationRegistry ArrayBuffer SharedArrayBuffer Atomics DataView JSON Promise Reflect Proxy Intl WebAssembly parseInt parseFloat isNaN isFinite decodeURI decodeURIComponent encodeURI encodeURIComponent '+
 'window document navigator location localStorage sessionStorage Image ImageData createImageBitmap DOMParser XMLSerializer MutationObserver ResizeObserver IntersectionObserver CustomEvent MouseEvent PointerEvent KeyboardEvent Event Node Element NodeFilter DOMPoint HTMLElement HTMLInputElement HTMLTextAreaElement HTMLSelectElement HTMLButtonElement HTMLCanvasElement HTMLDialogElement FileReader File Blob FormData ClipboardItem Path2D CSS getComputedStyle requestAnimationFrame cancelAnimationFrame innerWidth innerHeight devicePixelRatio alert confirm prompt matchMedia console performance crypto fetch URL URLSearchParams TextEncoder TextDecoder CompressionStream DecompressionStream AbortController AbortSignal structuredClone setTimeout clearTimeout setInterval clearInterval queueMicrotask btoa atob'
).split(/\s+/));
function unresolved(source,file){
 const found=[],ast=parser.parse(source,{sourceType:'module'});
 const check=(p,name)=>{if(!globals.has(name)&&!p.scope.hasBinding(name))found.push(file+':'+p.node.loc.start.line+' 未定义变量 '+name)};
 traverse(ast,{
  ReferencedIdentifier(p){if(!(p.parent.type==='UnaryExpression'&&p.parent.operator==='typeof'))check(p,p.node.name)},
  AssignmentExpression(p){for(const name of Object.keys(types.getBindingIdentifiers(p.node.left)))check(p,name)},
  UpdateExpression(p){if(p.node.argument.type==='Identifier')check(p,p.node.argument.name)},
  'ForInStatement|ForOfStatement'(p){if(p.node.left.type!=='VariableDeclaration')for(const name of Object.keys(types.getBindingIdentifiers(p.node.left)))check(p,name)}
 });
 return [...new Set(found)];
}
// These exact classes previously broke the loadout picker and library refresh.
if(unresolved('function picker(){ extraFolders=[] }','fixture').length!==1||
 unresolved('function refresh(){ restorePageScroll(next) } function restorePageScroll(){}','fixture').length!==1||
 unresolved('const x=1; function good(x){return x+window.innerWidth}','fixture').length)throw Error('Binding checker self-check failed');
const files=fs.readdirSync(root).filter(f=>f.endsWith('.js')&&!f.endsWith('-vendor.js')&&!f.endsWith('-catalog.js')&&f!=='loadout-item-info.js');
const problems=files.flatMap(f=>unresolved(fs.readFileSync(path.join(root,f),'utf8'),f));
if(problems.length){console.error(problems.join('\n'));process.exitCode=1;}
else require('esbuild').build({absWorkingDir:root,entryPoints:['app.js'],bundle:true,write:false,platform:'browser',format:'esm',logLevel:'silent'}).then(()=>{
 console.log(files.length+' 个浏览器业务模块：变量绑定与应用模块导入检查通过');
}).catch(error=>{console.error(error.message);process.exitCode=1});

